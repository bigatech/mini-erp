namespace ProductLifecycle.Application;
public sealed record ModelImageDto(Guid Id,Guid ModelId,string FileName,string ContentType,long Size,DateTimeOffset UploadedAt,string Url);
public sealed record ModelImageContent(byte[] Bytes,string ContentType);
public sealed class ModelImageStorageException:Exception {}
public interface IModelImageService
{
    Task<IReadOnlyList<ModelImageDto>> ListAsync(Guid modelId,CancellationToken ct);
    Task<ModelImageDto> UploadAsync(Guid modelId,string fileName,byte[] bytes,CancellationToken ct);
    Task<ModelImageContent?> ContentAsync(Guid modelId,Guid imageId,CancellationToken ct);
}
public static class ModelImageRules
{
    public const int MaximumBytes=5*1024*1024;
    public static string ContentType(byte[] data)
    {
        if(data.Length==0||data.Length>MaximumBytes)throw new ArgumentException("Görsel 1 bayt ile 5 MB arasında olmalıdır.","file");
        if(data.Length>=8&&data.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))return "image/png";
        if(data.Length>=3&&data[0]==255&&data[1]==216&&data[2]==255)return "image/jpeg";
        if(data.Length>=12&&System.Text.Encoding.ASCII.GetString(data,0,4)=="RIFF"&&System.Text.Encoding.ASCII.GetString(data,8,4)=="WEBP")return "image/webp";
        throw new ArgumentException("Yalnız PNG, JPEG ve WebP görselleri kabul edilir.","file");
    }
}
