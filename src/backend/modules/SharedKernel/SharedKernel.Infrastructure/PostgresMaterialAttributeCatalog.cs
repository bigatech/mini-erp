using Npgsql;
using SharedKernel.Application;
namespace SharedKernel.Infrastructure;

public sealed partial class PostgresMaterialAttributeCatalog(NpgsqlDataSource source):IMaterialAttributeCatalog,IMaterialAttributeWriter
{
    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        await using var command=source.CreateCommand("""
        CREATE TABLE IF NOT EXISTS shared_definitions.material_group(code text PRIMARY KEY,name text NOT NULL);
        CREATE TABLE IF NOT EXISTS shared_definitions.material_attribute_definition(
          group_code text NOT NULL REFERENCES shared_definitions.material_group(code),code text NOT NULL,name text NOT NULL,
          data_type text NOT NULL CHECK(data_type IN ('text','number')),unit text,PRIMARY KEY(group_code,code));
        CREATE TABLE IF NOT EXISTS shared_definitions.material_catalog_seed(seed_key text PRIMARY KEY);
        DO $seed$ BEGIN
        IF NOT EXISTS(SELECT 1 FROM shared_definitions.material_catalog_seed WHERE seed_key='textile-v1') THEN
        INSERT INTO shared_definitions.material_group(code,name) VALUES('KUMAS','Kumaş'),('ETIKET','Etiket') ON CONFLICT DO NOTHING;
        INSERT INTO shared_definitions.material_attribute_definition(group_code,code,name,data_type,unit) VALUES
          ('KUMAS','gramaj','Gramaj','number','g/m²'),('KUMAS','en','En','number','cm'),
          ('KUMAS','karisim','Karışım','text',NULL),('KUMAS','renk','Renk','text',NULL),
          ('ETIKET','ebat','Ebat','text',NULL),('ETIKET','icerik','İçerik','text',NULL) ON CONFLICT DO NOTHING;
        INSERT INTO shared_definitions.material_catalog_seed(seed_key) VALUES('textile-v1') ON CONFLICT DO NOTHING;
        END IF; END $seed$;
        """);
        await command.ExecuteNonQueryAsync(ct);
    }
    public async Task<IReadOnlyList<MaterialGroupDto>> ListAsync(CancellationToken ct)
    {
        await using var command=source.CreateCommand("SELECT g.code,g.name,f.code,f.name,f.data_type,f.unit FROM shared_definitions.material_group g LEFT JOIN shared_definitions.material_attribute_definition f ON f.group_code=g.code ORDER BY g.code,f.code");
        await using var reader=await command.ExecuteReaderAsync(ct);var result=new List<MaterialGroupDto>();var fields=new Dictionary<string,List<MaterialAttributeDefinition>>();
        while(await reader.ReadAsync(ct))
        {
            var code=reader.GetString(0);
            if(!fields.TryGetValue(code,out var list)){list=[];fields[code]=list;result.Add(new(code,reader.GetString(1),list));}
            if(!reader.IsDBNull(2))list.Add(new(reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.IsDBNull(5)?null:reader.GetString(5)));
        }
        return result;
    }
    public async Task ValidateAsync(string? groupCode,IReadOnlyDictionary<string,string>? attributes,CancellationToken ct)
    {var groups=await ListAsync(ct);MaterialAttributeRules.Validate(groups.SingleOrDefault(g=>g.Code==groupCode?.Trim()),groupCode,attributes);}
    public async Task<MaterialGroupDto> CreateGroupAsync(string code,string name,CancellationToken ct)
    {
        code=MaterialAttributeRules.Code(code);name=DefinitionInput.Required(name,"name");
        await using var command=source.CreateCommand("INSERT INTO shared_definitions.material_group(code,name) VALUES(@code,@name)");command.Parameters.AddWithValue("code",code);command.Parameters.AddWithValue("name",name);await Execute(command,ct);return new(code,name,[]);
    }
    public async Task<MaterialAttributeDefinition> CreateFieldAsync(string group,string code,string name,string dataType,string? unit,CancellationToken ct)
    {
        group=MaterialAttributeRules.Code(group,"group");code=MaterialAttributeRules.Code(code);name=DefinitionInput.Required(name,"name");
        dataType=DefinitionInput.Required(dataType,"dataType");if(dataType is not ("text" or "number"))throw new ArgumentException("Veri tipi text veya number olmalıdır.","dataType");
        unit=string.IsNullOrWhiteSpace(unit)?null:DefinitionInput.Required(unit,"unit");
        await using var command=source.CreateCommand("INSERT INTO shared_definitions.material_attribute_definition(group_code,code,name,data_type,unit) VALUES(@group,@code,@name,@type,@unit)");
        command.Parameters.AddWithValue("group",group);command.Parameters.AddWithValue("code",code);command.Parameters.AddWithValue("name",name);command.Parameters.AddWithValue("type",dataType);command.Parameters.Add("unit",NpgsqlTypes.NpgsqlDbType.Text).Value=(object?)unit??DBNull.Value;
        await Execute(command,ct);return new(code,name,dataType,unit);
    }
    private static async Task Execute(NpgsqlCommand command,CancellationToken ct)
    {try{await command.ExecuteNonQueryAsync(ct);}catch(PostgresException ex)when(ex.SqlState==PostgresErrorCodes.UniqueViolation){throw new DuplicateDefinitionException();}catch(PostgresException ex)when(ex.SqlState==PostgresErrorCodes.ForeignKeyViolation){throw new UnknownDefinitionCategoryException();}}
}
