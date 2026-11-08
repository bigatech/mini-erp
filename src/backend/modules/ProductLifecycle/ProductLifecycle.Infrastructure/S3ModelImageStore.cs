using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ProductLifecycle.Application;
namespace ProductLifecycle.Infrastructure;

// Path-style S3 SigV4. Object keys are generated UUIDs, never caller-supplied URLs.
public sealed class S3ModelImageStore(string endpoint,string accessKey,string secretKey)
{
    private static readonly HttpClient Client=new(){Timeout=TimeSpan.FromSeconds(30)};
    private const string Bucket="minierp-model-images";
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static byte[] Hmac(byte[] key,string value)=>HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(value));
    private async Task<HttpResponseMessage> Send(HttpMethod method,string path,byte[]? bytes,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(accessKey)||string.IsNullOrWhiteSpace(secretKey))throw new ModelImageStorageException();
        if(!Uri.TryCreate(endpoint.TrimEnd('/')+path,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https"))throw new ModelImageStorageException();
        var now=DateTimeOffset.UtcNow;var date=now.ToString("yyyyMMdd",CultureInfo.InvariantCulture);var stamp=now.ToString("yyyyMMdd'T'HHmmss'Z'",CultureInfo.InvariantCulture);
        var payload=Hash(bytes??[]);const string signed="host;x-amz-content-sha256;x-amz-date";
        var canonical=$"{method.Method}\n{uri.AbsolutePath}\n\nhost:{uri.Authority}\nx-amz-content-sha256:{payload}\nx-amz-date:{stamp}\n\n{signed}\n{payload}";
        var scope=$"{date}/us-east-1/s3/aws4_request";var signing=Hmac(Hmac(Hmac(Hmac(Encoding.UTF8.GetBytes("AWS4"+secretKey),date),"us-east-1"),"s3"),"aws4_request");
        var signature=Convert.ToHexStringLower(Hmac(signing,$"AWS4-HMAC-SHA256\n{stamp}\n{scope}\n{Hash(Encoding.UTF8.GetBytes(canonical))}"));
        using var request=new HttpRequestMessage(method,uri);request.Headers.Host=uri.Authority;
        request.Headers.TryAddWithoutValidation("x-amz-date",stamp);request.Headers.TryAddWithoutValidation("x-amz-content-sha256",payload);
        request.Headers.TryAddWithoutValidation("Authorization",$"AWS4-HMAC-SHA256 Credential={accessKey}/{scope}, SignedHeaders={signed}, Signature={signature}");
        if(bytes is not null)request.Content=new ByteArrayContent(bytes);
        try{return await Client.SendAsync(request,ct);}catch(HttpRequestException){throw new ModelImageStorageException();}
    }
    public async Task PutAsync(string key,byte[] bytes,CancellationToken ct)
    {
        using(var head=await Send(HttpMethod.Head,"/"+Bucket,null,ct))
        {
            if(head.StatusCode==HttpStatusCode.NotFound)
            {using var create=await Send(HttpMethod.Put,"/"+Bucket,[],ct);if(!create.IsSuccessStatusCode&&create.StatusCode!=HttpStatusCode.Conflict)throw new ModelImageStorageException();}
            else if(!head.IsSuccessStatusCode)throw new ModelImageStorageException();
        }
        using var response=await Send(HttpMethod.Put,"/"+Bucket+"/"+key,bytes,ct);if(!response.IsSuccessStatusCode)throw new ModelImageStorageException();
    }
    public async Task<byte[]> GetAsync(string key,CancellationToken ct)
    {using var response=await Send(HttpMethod.Get,"/"+Bucket+"/"+key,null,ct);if(!response.IsSuccessStatusCode)throw new ModelImageStorageException();return await response.Content.ReadAsByteArrayAsync(ct);}
    public async Task DeleteAsync(string key,CancellationToken ct)
    {using var response=await Send(HttpMethod.Delete,"/"+Bucket+"/"+key,null,ct);if(!response.IsSuccessStatusCode)throw new ModelImageStorageException();}
}
