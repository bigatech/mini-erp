using Npgsql;
using SharedKernel.Application;

namespace SharedKernel.Infrastructure;

public sealed partial class PostgresDefinitionRepository(NpgsqlDataSource source) : IDefinitionRepository
{
    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        await using var command = source.CreateCommand("""
            CREATE SCHEMA IF NOT EXISTS shared_definitions;
            CREATE TABLE IF NOT EXISTS shared_definitions.categories (
              code text PRIMARY KEY, name text NOT NULL);
            ALTER TABLE shared_definitions.categories ADD COLUMN IF NOT EXISTS module_code text NOT NULL DEFAULT 'shared-kernel';
            ALTER TABLE shared_definitions.categories ADD COLUMN IF NOT EXISTS feature_code text NOT NULL DEFAULT 'general';
            UPDATE shared_definitions.categories SET module_code='product-lifecycle',feature_code='type'
              WHERE code='product-type' AND module_code='shared-kernel' AND feature_code='general';
            UPDATE shared_definitions.categories SET module_code='product-lifecycle',feature_code='stock-unit'
              WHERE code='stock-unit' AND module_code='shared-kernel' AND feature_code='general';
            CREATE UNIQUE INDEX IF NOT EXISTS ux_definition_target ON shared_definitions.categories(module_code,feature_code)
              WHERE NOT (module_code='shared-kernel' AND feature_code='general');
            CREATE TABLE IF NOT EXISTS shared_definitions.items (
              category text NOT NULL REFERENCES shared_definitions.categories(code),
              code text NOT NULL, name text NOT NULL, active boolean NOT NULL DEFAULT true,
              PRIMARY KEY(category,code));
            INSERT INTO shared_definitions.categories(code,name,module_code,feature_code)
              VALUES ('product-type','Ürün tipi','product-lifecycle','type'),('stock-unit','Stok birimi','product-lifecycle','stock-unit'),
                     ('model-variant','Model varyantları','product-lifecycle','model-variant'),('model-size','Model bedenleri','product-lifecycle','model-size') ON CONFLICT DO NOTHING;
            DO $migration$
            BEGIN
              IF to_regclass('product_lifecycle.urun') IS NOT NULL THEN
                INSERT INTO shared_definitions.items(category,code,name)
                  SELECT DISTINCT 'product-type',btrim(urun_tipi),btrim(urun_tipi)
                  FROM product_lifecycle.urun WHERE btrim(urun_tipi) <> '' ON CONFLICT DO NOTHING;
                INSERT INTO shared_definitions.items(category,code,name)
                  SELECT DISTINCT 'stock-unit',btrim(stok_birimi),btrim(stok_birimi)
                  FROM product_lifecycle.urun WHERE btrim(stok_birimi) <> '' ON CONFLICT DO NOTHING;
              END IF;
              IF to_regclass('product_lifecycle.model_configuration_option') IS NOT NULL THEN
                -- Existing model values are preserved. Only globally unambiguous code/name
                -- pairs enter the shared catalogue; conflicting names need an owner decision.
                INSERT INTO shared_definitions.items(category,code,name)
                  SELECT category.code,legacy.code,min(legacy.name)
                  FROM product_lifecycle.model_configuration_option legacy
                  JOIN shared_definitions.categories category
                    ON category.module_code='product-lifecycle'
                   AND category.feature_code=CASE legacy.kind WHEN 'variant' THEN 'model-variant' WHEN 'size' THEN 'model-size' END
                  WHERE legacy.kind IN ('variant','size')
                  GROUP BY category.code,legacy.code HAVING count(DISTINCT legacy.name)=1
                  ON CONFLICT DO NOTHING;
              END IF;
            END $migration$;
            """);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<DefinitionCategory>> CategoriesAsync(CancellationToken ct)
    {
        await using var command = source.CreateCommand("SELECT code,name,module_code,feature_code FROM shared_definitions.categories ORDER BY code");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<DefinitionCategory>();
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetString(0),reader.GetString(1),true,reader.GetString(2),reader.GetString(3)));
        return result;
    }

    public async Task<DefinitionCategory> CreateCategoryAsync(string code, string name, string moduleCode, string featureCode, CancellationToken ct)
    {
        code=DefinitionInput.Category(code); name=DefinitionInput.Required(name,"name");
        (moduleCode,featureCode)=DefinitionTargets.Validate(moduleCode,featureCode);
        await using var command=source.CreateCommand("INSERT INTO shared_definitions.categories(code,name,module_code,feature_code) VALUES(@code,@name,@module,@feature)");
        command.Parameters.AddWithValue("code",code); command.Parameters.AddWithValue("name",name);
        command.Parameters.AddWithValue("module",moduleCode); command.Parameters.AddWithValue("feature",featureCode);
        try { await command.ExecuteNonQueryAsync(ct); }
        catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.UniqueViolation) { throw new DuplicateDefinitionException(); }
        return new(code,name,true,moduleCode,featureCode);
    }

    public async Task<IReadOnlyList<DefinitionItem>> ItemsAsync(string category, CancellationToken ct)
    {
        category=DefinitionInput.Category(category);
        await using var command=source.CreateCommand("SELECT category,code,name,active FROM shared_definitions.items WHERE category=@category ORDER BY code");
        command.Parameters.AddWithValue("category",category);
        await using var reader=await command.ExecuteReaderAsync(ct);
        var result=new List<DefinitionItem>();
        while(await reader.ReadAsync(ct)) result.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetBoolean(3)));
        return result;
    }

    public async Task<DefinitionItem> CreateItemAsync(string category,string code,string name,bool active,CancellationToken ct)
    {
        category=DefinitionInput.Category(category); code=DefinitionInput.Required(code,"code"); name=DefinitionInput.Required(name,"name");
        await using var command=source.CreateCommand("INSERT INTO shared_definitions.items(category,code,name,active) VALUES(@category,@code,@name,@active)");
        command.Parameters.AddWithValue("category",category); command.Parameters.AddWithValue("code",code);
        command.Parameters.AddWithValue("name",name); command.Parameters.AddWithValue("active",active);
        try { await command.ExecuteNonQueryAsync(ct); }
        catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.UniqueViolation) { throw new DuplicateDefinitionException(); }
        catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.ForeignKeyViolation) { throw new UnknownDefinitionCategoryException(); }
        return new(category,code,name,active);
    }
}
