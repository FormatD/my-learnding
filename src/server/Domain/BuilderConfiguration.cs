using System.Text.Json;
using System.Text.Json.Serialization;
namespace Learning;

public record BuilderLimits(int MaxFragments=100,int MaxInputCharacters=100_000,int MaxOutputCharacters=1_000_000,int TimeoutMilliseconds=30_000,int RepairAttempts=1)
{
    public void Validate()
    {
        if(MaxFragments<1 || MaxFragments>BuilderProtocol.MaxFragments || MaxInputCharacters<1 || MaxInputCharacters>BuilderProtocol.MaxInputCharacters || MaxOutputCharacters<1 || MaxOutputCharacters>BuilderProtocol.MaxOutputCharacters || TimeoutMilliseconds<1 || TimeoutMilliseconds>120_000 || RepairAttempts<0 || RepairAttempts>1)
            throw new ApiError(422,"BUILDER_CONFIGURATION_INVALID","建库运行上限无效，请检查本地配置。");
    }
}
public record BuilderRetrievalConfiguration(string Version,int TopK,string Space,string QueryMode,string TypePolicy)
{
    public void Validate(){if(Version!="builder-retrieval/1" || TopK is <1 or >50 || Space!=Retrieval.Space || QueryMode!="CandidateDefinition" || TypePolicy!="ExactKCType")throw new ApiError(422,"BUILDER_CONFIGURATION_INVALID","原候选检索设置无法验证，请重新准备任务。");}
}
public record BuilderModelConfiguration(string Version,string Provider,string Model,string PromptVersion,string SchemaHash,BuilderLimits Limits,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] BuilderRetrievalConfiguration? Retrieval=null);
public static class BuilderConfiguration
{
    public static BuilderModelConfiguration Current(IConfiguration configuration)
    {
        int Setting(string key,int fallback)=>configuration.GetValue<int?>("Builder:"+key)??fallback;
        var limits=new BuilderLimits(Setting("MaxFragments",100),Setting("MaxInputCharacters",100_000),Setting("MaxOutputCharacters",1_000_000),Setting("TimeoutMilliseconds",30_000),Setting("RepairAttempts",1));limits.Validate();
        var retrieval=new BuilderRetrievalConfiguration("builder-retrieval/1",Setting("RetrievalTopK",10),Learning.Retrieval.Space,"CandidateDefinition","ExactKCType");retrieval.Validate();
        return new("builder-config/2","Mock","fixture/1","kc-candidate/1",Content.Hash(BuilderProtocol.Schema),limits,retrieval);
    }
    static ApiError Unknown()=>new(422,"RUN_CONFIGURATION_UNKNOWN","原建库配置无法验证，请重新准备任务；不会替换为当前配置。");
    static void Fields(JsonElement value,params string[] names)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Unknown();var fields=value.EnumerateObject().Select(p=>p.Name).ToArray();if(fields.Length!=names.Length || fields.Distinct().Count()!=fields.Length || names.Except(fields).Any())throw Unknown();
    }
    public static BuilderLimits Resolve(BuilderRun run)=>ResolveConfiguration(run)?.Limits??new BuilderLimits();
    public static BuilderRetrievalConfiguration? ResolveRetrieval(BuilderRun run)=>ResolveConfiguration(run)?.Retrieval;
    static BuilderModelConfiguration? ResolveConfiguration(BuilderRun run)
    {
        // Preserve an explicit legacy path; never backfill a configuration never saved.
        if(run.ModelConfigPayload==null && run.ModelConfigHash==null){if(run.InputVersion is "builder-input/3" or "builder-input/4")throw Unknown();return null;}
        if(run.ModelConfigPayload==null || run.ModelConfigHash==null || Content.Hash(run.ModelConfigPayload)!=run.ModelConfigHash)throw Unknown();
        try
        {
            using var doc=JsonDocument.Parse(run.ModelConfigPayload);
            var modern=doc.RootElement.GetProperty("version").GetString()=="builder-config/2";
            Fields(doc.RootElement,modern?["version","provider","model","promptVersion","schemaHash","limits","retrieval"]:["version","provider","model","promptVersion","schemaHash","limits"]);Fields(doc.RootElement.GetProperty("limits"),"maxFragments","maxInputCharacters","maxOutputCharacters","timeoutMilliseconds","repairAttempts");
            var c=Json.Read<BuilderModelConfiguration>(run.ModelConfigPayload);
            if(c.Version is not ("builder-config/1" or "builder-config/2") || (run.InputVersion=="builder-input/4")!=modern || c.Provider!=run.Provider || c.Model!=run.Model || c.PromptVersion!=run.PromptVersion || c.SchemaHash!=Content.Hash(BuilderProtocol.Schema) || c.Limits==null)throw Unknown();
            if(modern){Fields(doc.RootElement.GetProperty("retrieval"),"version","topK","space","queryMode","typePolicy");if(c.Retrieval==null)throw Unknown();c.Retrieval.Validate();}
            c.Limits.Validate();return c;
        }
        catch(Exception ex)when(ex is JsonException or InvalidOperationException or KeyNotFoundException){throw Unknown();}
    }
}
