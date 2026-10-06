using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
namespace Learning;

public record MappingLocalConfiguration(string Version,string Endpoint,string Model,int MaxOutputTokens,int TimeoutMilliseconds,string PromptHash)
{
    public static MappingLocalConfiguration Current(IConfiguration configuration)=>new("mapping-local/1",configuration["Omlx:Endpoint"]??"http://127.0.0.1:8000/v1",configuration["Omlx:Model"]??"",configuration.GetValue<int?>("Omlx:MaxOutputTokens")??4096,configuration.GetValue<int?>("Omlx:TimeoutMilliseconds")??600_000,MappingModelProtocol.PromptHash);
    public void Validate()
    {
        if(Version!="mapping-local/1" || !MappingModelProtocol.KnownPrompt(PromptHash) || string.IsNullOrWhiteSpace(Model) || Model.Length>200 || !Uri.TryCreate(Endpoint,UriKind.Absolute,out var uri) || uri.Scheme!="http" || uri.Host!="127.0.0.1" || uri.AbsolutePath.TrimEnd('/')!="/v1" || uri.Query.Length>0 || uri.Fragment.Length>0 || uri.UserInfo.Length>0 || MaxOutputTokens is <128 or >8192 || TimeoutMilliseconds is <1 or >600_000)throw new ApiError(422,"MAPPING_LOCAL_CONFIGURATION_INVALID","原本机映射配置无法核对，请重新准备任务。");
    }
}
public interface IMappingModelProvider
{
    Task<BuilderProviderResponse> Generate(MappingModelInput input,CancellationToken ct);
}
// Transport returns real completion/usage facts before protocol validation; persistence is the caller's responsibility.
public sealed class LocalMappingModelProvider(MappingLocalConfiguration frozen,string keyFile):IMappingModelProvider
{
    public async Task<BuilderProviderResponse> Generate(MappingModelInput input,CancellationToken ct)
    {
        frozen.Validate();var user=MappingModelProtocol.UserFor(input);
        if(string.IsNullOrWhiteSpace(keyFile) || !File.Exists(keyFile))throw new ApiError(422,"LOCAL_PROVIDER_KEY_REQUIRED","请在本机配置oMLX认证文件。");
        if(!OperatingSystem.IsWindows() && (File.GetUnixFileMode(keyFile)&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.OtherRead|UnixFileMode.OtherWrite))!=0)throw new ApiError(422,"LOCAL_PROVIDER_KEY_NOT_PRIVATE","模型认证文件须仅本人可读写。");
        var key=(await File.ReadAllTextAsync(keyFile,ct)).Trim();if(key.Length is <1 or >4096 || key.Any(char.IsControl))throw new ApiError(422,"LOCAL_PROVIDER_KEY_REQUIRED","本机认证文件无效。");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(frozen.TimeoutMilliseconds);
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false}){Timeout=Timeout.InfiniteTimeSpan};
        using var message=new HttpRequestMessage(HttpMethod.Post,frozen.Endpoint.TrimEnd('/')+"/chat/completions");message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        message.Content=JsonContent.Create(new{model=frozen.Model,messages=new[]{new{role="system",content=MappingModelProtocol.PromptFor(frozen.PromptHash)+MappingModelProtocol.Schema},new{role="user",content=user}},temperature=0,max_tokens=frozen.MaxOutputTokens,stream=false,response_format=MappingModelProtocol.ResponseFormat(input,frozen.PromptHash),chat_template_kwargs=new{enable_thinking=false}});
        using var response=await client.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        if(!response.IsSuccessStatusCode)throw new ApiError(422,response.StatusCode==HttpStatusCode.Unauthorized?"LOCAL_PROVIDER_AUTH_FAILED":"LOCAL_PROVIDER_HTTP_FAILED","本机映射调用失败，未保存建议。");
        await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);using var buffer=new MemoryStream();var bytes=new byte[8192];int count;
        while((count=await stream.ReadAsync(bytes,deadline.Token))>0){if(buffer.Length+count>2_000_000)throw new ApiError(422,"MAPPING_MODEL_OUTPUT_LIMIT","本机映射响应过大。");buffer.Write(bytes,0,count);}
        try
        {
            using var document=JsonDocument.Parse(buffer.ToArray());var root=document.RootElement;
            if(root.GetProperty("model").GetString()!=frozen.Model)throw new ApiError(422,"LOCAL_PROVIDER_MODEL_MISMATCH","本机服务返回了不同模型，不能替换固定模型。");
            var choice=root.GetProperty("choices")[0];var output=choice.GetProperty("message").GetProperty("content").GetString()??"";
            long? Tokens(string field)=>root.TryGetProperty("usage",out var usage) && usage.TryGetProperty(field,out var token) && token.TryGetInt64(out var value) && value>=0?value:null;
            return new(output,new(Tokens("prompt_tokens"),Tokens("completion_tokens"),0,null,"LocalMeasured"),choice.GetProperty("finish_reason").GetString()=="stop");
        }
        catch(Exception ex)when(ex is JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException){throw new ApiError(422,"LOCAL_PROVIDER_RESPONSE_INVALID","本机映射响应格式无效。");}
    }
}
