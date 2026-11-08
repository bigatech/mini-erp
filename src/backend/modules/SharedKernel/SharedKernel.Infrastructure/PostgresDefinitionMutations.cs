using Npgsql;
using SharedKernel.Application;
namespace SharedKernel.Infrastructure;
public sealed partial class PostgresDefinitionRepository
{
    public async Task<DefinitionCategory> UpdateCategoryAsync(string code,string name,CancellationToken ct)
    {
        code=DefinitionInput.Category(code);name=DefinitionInput.Required(name,"name");
        await using var command=source.CreateCommand("UPDATE shared_definitions.categories SET name=@name WHERE code=@code RETURNING module_code,feature_code");command.Parameters.AddWithValue("code",code);command.Parameters.AddWithValue("name",name);
        await using var r=await command.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new UnknownDefinitionCategoryException();return new(code,name,true,r.GetString(0),r.GetString(1));
    }
    public async Task DeleteCategoryAsync(string code,CancellationToken ct)
    {
        code=DefinitionInput.Category(code);await using var connection=await source.OpenConnectionAsync(ct);await using var tx=await connection.BeginTransactionAsync(ct);
        await using(var read=new NpgsqlCommand("SELECT module_code,feature_code FROM shared_definitions.categories WHERE code=@code FOR UPDATE",connection,tx))
        {read.Parameters.AddWithValue("code",code);await using var r=await read.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new UnknownDefinitionCategoryException();if(r.GetString(0)=="product-lifecycle"&&r.GetString(1) is "type" or "stock-unit" or "model-variant" or "model-size")throw new DefinitionInUseException();}
        await using(var check=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM shared_definitions.items WHERE category=@code)",connection,tx)){check.Parameters.AddWithValue("code",code);if((bool)(await check.ExecuteScalarAsync(ct))!)throw new DefinitionInUseException();}
        await using(var delete=new NpgsqlCommand("DELETE FROM shared_definitions.categories WHERE code=@code",connection,tx)){delete.Parameters.AddWithValue("code",code);await delete.ExecuteNonQueryAsync(ct);}await tx.CommitAsync(ct);
    }
    public Task<DefinitionItem> UpdateItemAsync(string category,string code,string name,bool active,CancellationToken ct)=>MutateItem(category,code,name,active,false,ct);
    public async Task DeleteItemAsync(string category,string code,CancellationToken ct)=>await MutateItem(category,code,null,false,true,ct);
    private async Task<DefinitionItem> MutateItem(string category,string code,string? name,bool active,bool delete,CancellationToken ct)
    {
        category=DefinitionInput.Category(category);code=DefinitionInput.Required(code,"code");if(!delete)name=DefinitionInput.Required(name,"name");
        await using var connection=await source.OpenConnectionAsync(ct);await using var tx=await connection.BeginTransactionAsync(ct);DefinitionItem old;string module,feature;
        await using(var read=new NpgsqlCommand("SELECT i.name,i.active,c.module_code,c.feature_code FROM shared_definitions.items i JOIN shared_definitions.categories c ON c.code=i.category WHERE i.category=@category AND i.code=@code FOR UPDATE OF i",connection,tx))
        {read.Parameters.AddWithValue("category",category);read.Parameters.AddWithValue("code",code);await using var r=await read.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new UnknownDefinitionCategoryException();old=new(category,code,r.GetString(0),r.GetBoolean(1));module=r.GetString(2);feature=r.GetString(3);}
        if(delete||name!=old.Name||active!=old.IsActive)
        {
            var sql=module!="product-lifecycle"?"SELECT false":feature switch
            {
                "type"=>"SELECT EXISTS(SELECT 1 FROM product_lifecycle.urun WHERE urun_tipi=@name)",
                "stock-unit"=>"SELECT EXISTS(SELECT 1 FROM product_lifecycle.urun WHERE stok_birimi=@name)",
                "model-variant"=>"SELECT EXISTS(SELECT 1 FROM product_lifecycle.model_configuration_option WHERE kind='variant' AND code=@code)",
                "model-size"=>"SELECT EXISTS(SELECT 1 FROM product_lifecycle.model_configuration_option WHERE kind='size' AND code=@code)",_=>"SELECT false"
            };
            await using var check=new NpgsqlCommand(sql,connection,tx);check.Parameters.AddWithValue("name",old.Name);check.Parameters.AddWithValue("code",code);if((bool)(await check.ExecuteScalarAsync(ct))!)throw new DefinitionInUseException();
        }
        await using(var mutate=new NpgsqlCommand(delete?"DELETE FROM shared_definitions.items WHERE category=@category AND code=@code":"UPDATE shared_definitions.items SET name=@name,active=@active WHERE category=@category AND code=@code",connection,tx))
        {mutate.Parameters.AddWithValue("category",category);mutate.Parameters.AddWithValue("code",code);mutate.Parameters.AddWithValue("name",name??old.Name);mutate.Parameters.AddWithValue("active",active);await mutate.ExecuteNonQueryAsync(ct);}
        await tx.CommitAsync(ct);return delete?old:new(category,code,name!,active);
    }
}
