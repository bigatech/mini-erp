using Npgsql;
using ProductLifecycle.Application;
namespace ProductLifecycle.Infrastructure;
public sealed class PostgresModelImageService(NpgsqlDataSource source,S3ModelImageStore store):IModelImageService
{
    public async Task EnsureCreatedAsync(CancellationToken ct)
    {await using var c=source.CreateCommand("CREATE TABLE IF NOT EXISTS product_lifecycle.model_image(image_id uuid PRIMARY KEY,model_id uuid NOT NULL REFERENCES product_lifecycle.model(model_id),file_name text NOT NULL,content_type text NOT NULL,object_key text NOT NULL UNIQUE,file_size bigint NOT NULL,uploaded_at timestamptz NOT NULL); CREATE INDEX IF NOT EXISTS ix_model_image_model ON product_lifecycle.model_image(model_id,uploaded_at);");await c.ExecuteNonQueryAsync(ct);}
    private async Task Check(Guid id,CancellationToken ct)
    {await using var c=source.CreateCommand("SELECT EXISTS(SELECT 1 FROM product_lifecycle.model WHERE model_id=@id)");c.Parameters.AddWithValue("id",id);if(!(bool)(await c.ExecuteScalarAsync(ct))!)throw new PlmReferenceNotFoundException();}
    private static ModelImageDto Dto(Guid id,Guid model,string name,string type,long size,DateTimeOffset time)=>new(id,model,name,type,size,time,$"/api/v1/plm/models/{model}/images/{id}/content");
    public async Task<IReadOnlyList<ModelImageDto>> ListAsync(Guid modelId,CancellationToken ct)
    {await Check(modelId,ct);await using var c=source.CreateCommand("SELECT image_id,file_name,content_type,file_size,uploaded_at FROM product_lifecycle.model_image WHERE model_id=@id ORDER BY uploaded_at DESC");c.Parameters.AddWithValue("id",modelId);await using var r=await c.ExecuteReaderAsync(ct);var rows=new List<ModelImageDto>();while(await r.ReadAsync(ct))rows.Add(Dto(r.GetGuid(0),modelId,r.GetString(1),r.GetString(2),r.GetInt64(3),r.GetFieldValue<DateTimeOffset>(4)));return rows;}
    public async Task<ModelImageDto> UploadAsync(Guid modelId,string fileName,byte[] bytes,CancellationToken ct)
    {
        var type=ModelImageRules.ContentType(bytes);await Check(modelId,ct);
        var id=Guid.NewGuid();var key=modelId.ToString("N")+"/"+id.ToString("N");var time=DateTimeOffset.UtcNow;
        var name=Path.GetFileName(fileName.Replace('\\','/'));if(string.IsNullOrWhiteSpace(name)||name.Contains('\0'))name="image";
        await store.PutAsync(key,bytes,ct);
        try
        {await using var c=source.CreateCommand("INSERT INTO product_lifecycle.model_image(image_id,model_id,file_name,content_type,object_key,file_size,uploaded_at) VALUES(@id,@model,@name,@type,@key,@size,@time)");c.Parameters.AddWithValue("id",id);c.Parameters.AddWithValue("model",modelId);c.Parameters.AddWithValue("name",name);c.Parameters.AddWithValue("type",type);c.Parameters.AddWithValue("key",key);c.Parameters.AddWithValue("size",(long)bytes.Length);c.Parameters.AddWithValue("time",time);await c.ExecuteNonQueryAsync(ct);}
        catch {try{await store.DeleteAsync(key,CancellationToken.None);}catch(ModelImageStorageException){/* Orphan can be reconciled by metadata/key audit. */}throw;}
        return Dto(id,modelId,name,type,bytes.Length,time);
    }
    public async Task<ModelImageContent?> ContentAsync(Guid modelId,Guid imageId,CancellationToken ct)
    {await using var c=source.CreateCommand("SELECT object_key,content_type FROM product_lifecycle.model_image WHERE model_id=@model AND image_id=@id");c.Parameters.AddWithValue("model",modelId);c.Parameters.AddWithValue("id",imageId);string key,type;await using(var r=await c.ExecuteReaderAsync(ct)){if(!await r.ReadAsync(ct))return null;key=r.GetString(0);type=r.GetString(1);}return new(await store.GetAsync(key,ct),type);}
}
