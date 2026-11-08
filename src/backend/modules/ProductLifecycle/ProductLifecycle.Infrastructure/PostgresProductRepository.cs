using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using ProductLifecycle.Application;
using ProductLifecycle.Domain;

namespace ProductLifecycle.Infrastructure;

public sealed class PostgresProductRepository(NpgsqlDataSource dataSource) : IProductRepository
{
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            CREATE SCHEMA IF NOT EXISTS product_lifecycle;
            CREATE TABLE IF NOT EXISTS product_lifecycle.urun (
                urun_id uuid PRIMARY KEY,
                urun_kodu text NOT NULL UNIQUE,
                urun_adi text NOT NULL,
                urun_tipi text NOT NULL,
                stok_birimi text NOT NULL,
                aktif_mi boolean NOT NULL DEFAULT true
            );
            ALTER TABLE product_lifecycle.urun ADD COLUMN IF NOT EXISTS material_group_code text;
            ALTER TABLE product_lifecycle.urun ADD COLUMN IF NOT EXISTS attributes jsonb NOT NULL DEFAULT '{}'::jsonb;
            """);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Product> CreateAsync(Product product, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO product_lifecycle.urun (urun_id, urun_kodu, urun_adi, urun_tipi, stok_birimi, aktif_mi,material_group_code,attributes)
            VALUES (@id, @code, @name, @type, @unit, true,@material_group,@attributes)
            RETURNING urun_id, urun_kodu, urun_adi, urun_tipi, stok_birimi, aktif_mi,material_group_code,attributes::text;
            """);
        command.Parameters.AddWithValue("id", product.Id);
        command.Parameters.AddWithValue("code", product.Code);
        command.Parameters.AddWithValue("name", product.Name);
        command.Parameters.AddWithValue("type", product.Type);
        command.Parameters.AddWithValue("unit", product.StockUnit);
        AddAttributes(command,product);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return ReadProduct(reader);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DuplicateProductCodeException();
        }
    }

    private static Product ReadProduct(NpgsqlDataReader reader)=>Product.Rehydrate(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetBoolean(5),reader.IsDBNull(6)?null:reader.GetString(6),JsonSerializer.Deserialize<Dictionary<string,string>>(reader.GetString(7)));
    private static void AddAttributes(NpgsqlCommand command,Product product){command.Parameters.Add("material_group",NpgsqlDbType.Text).Value=(object?)product.MaterialGroupCode??DBNull.Value;command.Parameters.Add("attributes",NpgsqlDbType.Jsonb).Value=JsonSerializer.Serialize(product.Attributes);}

    public static string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    public Task<Product?> UpdateAsync(Guid id, Product product, CancellationToken ct) => UpdateCoreAsync(id, product, null, ct);

    public Task<Product?> SetActiveAsync(Guid id, bool isActive, CancellationToken ct) => UpdateCoreAsync(id, null, isActive, ct);

    private async Task<Product?> UpdateCoreAsync(Guid id, Product? product, bool? isActive, CancellationToken ct)
    {
        await using var connection=await dataSource.OpenConnectionAsync(ct);
        await using var transaction=await connection.BeginTransactionAsync(ct);
        await using(var locked=new NpgsqlCommand("SELECT urun_id FROM product_lifecycle.urun WHERE urun_id=@id FOR UPDATE",connection,transaction))
        {
            locked.Parameters.AddWithValue("id",id);
            if(await locked.ExecuteScalarAsync(ct) is null) return null;
        }
        // Inspect declared foreign keys, pairing composite-key columns by ordinal.
        // The parent FOR UPDATE lock also serializes concurrent FK inserts.
        var references=new List<(string Schema,string Table,string[] Child,string[] Parent)>();
        await using(var catalog=new NpgsqlCommand("""
            SELECT ns.nspname,child.relname,
              array_agg(ca.attname::text ORDER BY k.ord),array_agg(pa.attname::text ORDER BY k.ord)
            FROM pg_constraint c
            JOIN pg_class child ON child.oid=c.conrelid
            JOIN pg_namespace ns ON ns.oid=child.relnamespace
            JOIN LATERAL unnest(c.conkey,c.confkey) WITH ORDINALITY AS k(childnum,parentnum,ord) ON true
            JOIN pg_attribute ca ON ca.attrelid=c.conrelid AND ca.attnum=k.childnum
            JOIN pg_attribute pa ON pa.attrelid=c.confrelid AND pa.attnum=k.parentnum
            WHERE c.contype='f' AND c.confrelid='product_lifecycle.urun'::regclass
            GROUP BY c.oid,ns.nspname,child.relname
            """,connection,transaction))
        {
            await using var reader=await catalog.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct)) references.Add((reader.GetString(0),reader.GetString(1),reader.GetFieldValue<string[]>(2),reader.GetFieldValue<string[]>(3)));
        }
        foreach(var reference in references)
        {
            var predicates=reference.Child.Zip(reference.Parent,(child,parent)=>$"r.{QuoteIdentifier(child)}=p.{QuoteIdentifier(parent)}");
            var sql=$"SELECT EXISTS(SELECT 1 FROM {QuoteIdentifier(reference.Schema)}.{QuoteIdentifier(reference.Table)} r JOIN product_lifecycle.urun p ON {string.Join(" AND ",predicates)} WHERE p.urun_id=@id)";
            await using var check=new NpgsqlCommand(sql,connection,transaction);
            check.Parameters.AddWithValue("id",id);
            if(await check.ExecuteScalarAsync(ct) is true) throw new ProductInUseException();
        }
        var updateSql = product is null ? """
            UPDATE product_lifecycle.urun SET aktif_mi=@active
            WHERE urun_id=@id RETURNING urun_id,urun_kodu,urun_adi,urun_tipi,stok_birimi,aktif_mi,material_group_code,attributes::text
            """ : """
            UPDATE product_lifecycle.urun SET urun_kodu=@code,urun_adi=@name,urun_tipi=@type,stok_birimi=@unit,material_group_code=@material_group,attributes=@attributes
            WHERE urun_id=@id RETURNING urun_id,urun_kodu,urun_adi,urun_tipi,stok_birimi,aktif_mi,material_group_code,attributes::text
            """;
        await using var update=new NpgsqlCommand(updateSql,connection,transaction);
        update.Parameters.AddWithValue("id",id);
        if (product is null) update.Parameters.AddWithValue("active",isActive!.Value);
        else
        {
            update.Parameters.AddWithValue("code",product.Code);
            update.Parameters.AddWithValue("name",product.Name); update.Parameters.AddWithValue("type",product.Type); update.Parameters.AddWithValue("unit",product.StockUnit);AddAttributes(update,product);
        }
        try
        {
            Product result;
            await using(var reader=await update.ExecuteReaderAsync(ct))
            {
                await reader.ReadAsync(ct);
                result=ReadProduct(reader);
            }
            await transaction.CommitAsync(ct);
            return result;
        }
        catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.UniqueViolation) {throw new DuplicateProductCodeException();}
    }

    public async Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT urun_id, urun_kodu, urun_adi, urun_tipi, stok_birimi, aktif_mi,material_group_code,attributes::text
            FROM product_lifecycle.urun ORDER BY urun_kodu;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var products = new List<Product>();
        while (await reader.ReadAsync(cancellationToken))
            products.Add(ReadProduct(reader));
        return products;
    }
}
