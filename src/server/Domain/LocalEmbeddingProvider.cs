using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
namespace Learning;

public record LocalEmbeddingConfiguration(string Version,string Endpoint,ModelEmbeddingSpace Space,int TimeoutMilliseconds)
{
    public void Validate()
    {
        if(Version!="embedding-local/1" || Space==null || !Uri.TryCreate(Endpoint,UriKind.Absolute,out var uri) || uri.Scheme!="http" || uri.Host!="127.0.0.1" || uri.AbsolutePath.TrimEnd('/')!="/v1" || uri.Query.Length>0 || uri.Fragment.Length>0 || uri.UserInfo.Length>0 || TimeoutMilliseconds is <1 or >600_000)throw new ApiError(422,"EMBEDDING_CONFIGURATION_INVALID","本机向量配置无效，只允许固定本机接口。");Space.Validate();
    }
}
public interface IModelEmbeddingProvider
{
    Task<ModelEmbeddingResponse> Generate(ModelEmbeddingInput[] inputs,CancellationToken ct);
}
// Transport retains raw returned response and usage; protocol/index validation is separate.
// Not registered in production until durable calls, shared budgets and frozen indexing jobs exist.
public sealed class LocalEmbeddingProvider(LocalEmbeddingConfiguration frozen,string keyFile):IModelEmbeddingProvider
{
    public async Task<ModelEmbeddingResponse> Generate(ModelEmbeddingInput[] inputs,CancellationToken ct)
    {
        frozen.Validate();var texts=ModelEmbeddings.Inputs(frozen.Space,inputs);
        if(string.IsNullOrWhiteSpace(keyFile) || !File.Exists(keyFile))throw new ApiError(422,"LOCAL_PROVIDER_KEY_REQUIRED","请在本机配置模型认证文件。");
        if(!OperatingSystem.IsWindows() && (File.GetUnixFileMode(keyFile)&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.OtherRead|UnixFileMode.OtherWrite))!=0)throw new ApiError(422,"LOCAL_PROVIDER_KEY_NOT_PRIVATE","模型认证文件须仅本人可读写。");
        var key=(await File.ReadAllTextAsync(keyFile,ct)).Trim();if(key.Length is <1 or >4096 || key.Any(char.IsControl))throw new ApiError(422,"LOCAL_PROVIDER_KEY_REQUIRED","本机认证文件无效。");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(frozen.TimeoutMilliseconds);
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false}){Timeout=Timeout.InfiniteTimeSpan};
        using var request=new HttpRequestMessage(HttpMethod.Post,frozen.Endpoint.TrimEnd('/')+"/embeddings");request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        request.Content=JsonContent.Create(new{model=frozen.Space.Model,input=texts,encoding_format="float",truncation=false});
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        if(!response.IsSuccessStatusCode)throw new ApiError(422,response.StatusCode==HttpStatusCode.Unauthorized?"LOCAL_PROVIDER_AUTH_FAILED":"LOCAL_PROVIDER_HTTP_FAILED","本机向量调用未成功返回，未更新索引。");
        await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);using var buffer=new MemoryStream();var bytes=new byte[8192];int count;
        while((count=await stream.ReadAsync(bytes,deadline.Token))>0){if(buffer.Length+count>ModelEmbeddings.MaxOutputBytes)throw new ApiError(422,"EMBEDDING_OUTPUT_LIMIT","本机向量响应过大。");buffer.Write(bytes,0,count);}
        var output=new UTF8Encoding(false,true).GetString(buffer.ToArray());long? tokens=null;
        // Even rejected vectors/model/structure keep raw real usage. Missing or malformed usage stays unknown.
        try{using var doc=JsonDocument.Parse(output);var root=doc.RootElement;if(root.TryGetProperty("usage",out var usage) && usage.ValueKind==JsonValueKind.Object && usage.TryGetProperty("prompt_tokens",out var value) && value.TryGetInt64(out var number) && number>=0 && usage.EnumerateObject().Count(p=>p.Name=="prompt_tokens")==1)tokens=number;}catch(Exception ex)when(ex is JsonException or InvalidOperationException){}
        return new(output,new(tokens,null,0,null,"LocalMeasured"));
    }
}
