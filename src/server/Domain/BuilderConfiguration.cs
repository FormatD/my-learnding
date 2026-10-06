using System.Text.Json;
using System.Text.Json.Serialization;
namespace Learning;

public record BuilderLimits(int MaxFragments=100,int MaxInputCharacters=100_000,int MaxOutputCharacters=1_000_000,int TimeoutMilliseconds=30_000,int RepairAttempts=1)
{
    public const int MaxTimeoutMilliseconds=600_000;
    public void Validate()
    {
        if(MaxFragments<1 || MaxFragments>BuilderProtocol.MaxFragments || MaxInputCharacters<1 || MaxInputCharacters>BuilderProtocol.MaxInputCharacters || MaxOutputCharacters<1 || MaxOutputCharacters>BuilderProtocol.MaxOutputCharacters || TimeoutMilliseconds<1 || TimeoutMilliseconds>MaxTimeoutMilliseconds || RepairAttempts<0 || RepairAttempts>1)
            throw new ApiError(422,"BUILDER_CONFIGURATION_INVALID","建库运行上限无效，请检查本地配置。");
    }
}
public record BuilderAliasSnapshot(Guid Id,Guid KCId,string Text,string Normalized,Guid CandidateId,Guid ReviewedBy);
public record BuilderRetrievalConfiguration(string Version,int TopK,string Space,string QueryMode,string TypePolicy,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] BuilderAliasSnapshot[]? Aliases=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? KeywordPolicy=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? FusionPolicy=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? SubjectPolicy=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? GradePolicy=null)
{
    public void Validate()
    {
        if(Version is not ("builder-retrieval/1" or "builder-retrieval/2" or "builder-retrieval/3" or "builder-retrieval/4") || TopK is <1 or >50 || Space!=Retrieval.Space || QueryMode!="CandidateDefinition" || TypePolicy!="ExactKCType" || Version=="builder-retrieval/1" && Aliases!=null || Version!="builder-retrieval/1" && Aliases==null || Version is "builder-retrieval/3" or "builder-retrieval/4" && (KeywordPolicy!=BuilderLexicalRetrieval.KeywordPolicy || FusionPolicy!=BuilderLexicalRetrieval.FusionPolicy) || Version is not ("builder-retrieval/3" or "builder-retrieval/4") && (KeywordPolicy!=null || FusionPolicy!=null))throw new ApiError(422,"BUILDER_CONFIGURATION_INVALID","原候选检索设置无法验证，请重新准备任务。");
        if(Version=="builder-retrieval/4" ? SubjectPolicy!="ExactRecordedSubject" || GradePolicy!="NoGradeExclusion" : SubjectPolicy!=null || GradePolicy!=null)throw new ApiError(422,"BUILDER_CONFIGURATION_INVALID","原学科和年级检索策略无法验证，请重新准备任务。");
        if(Aliases!=null && (Aliases.Length>1000 || Aliases.Any(a=>a==null || a.Id==Guid.Empty || a.KCId==Guid.Empty || a.CandidateId==Guid.Empty || a.ReviewedBy==Guid.Empty || string.IsNullOrWhiteSpace(a.Text) || a.Text.Length>100 || a.Normalized!=Retrieval.Normalize(a.Text)) || Aliases.Select(a=>a.Id).Distinct().Count()!=Aliases.Length || Aliases.Select(a=>(a.KCId,a.Normalized)).Distinct().Count()!=Aliases.Length))throw new ApiError(422,"BUILDER_ALIAS_SNAPSHOT_INVALID","原审核别名无法核对，请检查别名记录；不会替换为当前别名。");
    }
}
public record BuilderModelConfiguration(string Version,string Provider,string Model,string PromptVersion,string SchemaHash,BuilderLimits Limits,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] BuilderRetrievalConfiguration? Retrieval=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] BuilderLocalTransport? LocalTransport=null);
public static class BuilderConfiguration
{
    public static BuilderModelConfiguration Current(IConfiguration configuration,string provider="Mock")
    {
        int Setting(string key,int fallback)=>configuration.GetValue<int?>("Builder:"+key)??fallback;
        var limits=new BuilderLimits(Setting("MaxFragments",100),Setting("MaxInputCharacters",100_000),Setting("MaxOutputCharacters",1_000_000),Setting("TimeoutMilliseconds",30_000),Setting("RepairAttempts",1));limits.Validate();
        var retrieval=new BuilderRetrievalConfiguration("builder-retrieval/4",Setting("RetrievalTopK",10),Learning.Retrieval.Space,"CandidateDefinition","ExactKCType",[],BuilderLexicalRetrieval.KeywordPolicy,BuilderLexicalRetrieval.FusionPolicy,"ExactRecordedSubject","NoGradeExclusion");retrieval.Validate();
        if(provider=="LocalOmlx"){
            var local=new BuilderLocalTransport(configuration["Omlx:Endpoint"]??"http://127.0.0.1:8000/v1",configuration.GetValue<int?>("Omlx:MaxOutputTokens")??4096,LocalOmlxProvider.PromptHash);local.Validate();
            var model=configuration["Omlx:Model"];if(string.IsNullOrWhiteSpace(model)||model.Length>200)throw new ApiError(422,"PROVIDER_UNCONFIGURED","请配置本地oMLX模型。");
            limits=limits with{TimeoutMilliseconds=configuration.GetValue<int?>("Omlx:TimeoutMilliseconds")??600_000,MaxInputCharacters=12_000};limits.Validate();
            return new("builder-config/4",provider,model,"kc-candidate/2",Content.Hash(BuilderProtocol.SchemaV2),limits,retrieval,local);
        }
        return new("builder-config/3","Mock","fixture/1","kc-candidate/2",Content.Hash(BuilderProtocol.SchemaV2),limits,retrieval);
    }
    static ApiError Unknown()=>new(422,"RUN_CONFIGURATION_UNKNOWN","原建库配置无法验证，请重新准备任务；不会替换为当前配置。");
    static void Fields(JsonElement value,params string[] names)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Unknown();var fields=value.EnumerateObject().Select(p=>p.Name).ToArray();if(fields.Length!=names.Length || fields.Distinct().Count()!=fields.Length || names.Except(fields).Any())throw Unknown();
    }
    public static BuilderLocalTransport? ResolveLocal(BuilderRun run)=>ResolveConfiguration(run)?.LocalTransport;
    public static BuilderLimits Resolve(BuilderRun run)=>ResolveConfiguration(run)?.Limits??new BuilderLimits();
    public static BuilderRetrievalConfiguration? ResolveRetrieval(BuilderRun run)=>ResolveConfiguration(run)?.Retrieval;
    static BuilderModelConfiguration? ResolveConfiguration(BuilderRun run)
    {
        // Preserve an explicit legacy path; never backfill a configuration never saved.
        if(run.ModelConfigPayload==null && run.ModelConfigHash==null){if(run.InputVersion is "builder-input/3" or "builder-input/4" || run.PromptVersion!="kc-candidate/1")throw Unknown();return null;}
        if(run.ModelConfigPayload==null || run.ModelConfigHash==null || Content.Hash(run.ModelConfigPayload)!=run.ModelConfigHash)throw Unknown();
        try
        {
            using var doc=JsonDocument.Parse(run.ModelConfigPayload);
            var modern=doc.RootElement.GetProperty("version").GetString() is "builder-config/2" or "builder-config/3" or "builder-config/4";
            Fields(doc.RootElement,doc.RootElement.GetProperty("version").GetString()=="builder-config/4"?["version","provider","model","promptVersion","schemaHash","limits","retrieval","localTransport"]:modern?["version","provider","model","promptVersion","schemaHash","limits","retrieval"]:["version","provider","model","promptVersion","schemaHash","limits"]);Fields(doc.RootElement.GetProperty("limits"),"maxFragments","maxInputCharacters","maxOutputCharacters","timeoutMilliseconds","repairAttempts");
            var c=Json.Read<BuilderModelConfiguration>(run.ModelConfigPayload);
            if(c.Version is not ("builder-config/1" or "builder-config/2" or "builder-config/3" or "builder-config/4") || (run.InputVersion=="builder-input/4")!=modern || c.Provider!=run.Provider || c.Model!=run.Model || c.PromptVersion!=run.PromptVersion || (c.Version is "builder-config/3" or "builder-config/4"?c.PromptVersion!="kc-candidate/2":c.PromptVersion!="kc-candidate/1") || c.SchemaHash!=Content.Hash(BuilderProtocol.SchemaFor(c.PromptVersion)) || c.Limits==null)throw Unknown();
            if(modern)
            {
                if(c.Retrieval==null)throw Unknown();var element=doc.RootElement.GetProperty("retrieval");
                Fields(element,c.Retrieval.Version=="builder-retrieval/4"?["version","topK","space","queryMode","typePolicy","aliases","keywordPolicy","fusionPolicy","subjectPolicy","gradePolicy"]:c.Retrieval.Version=="builder-retrieval/3"?["version","topK","space","queryMode","typePolicy","aliases","keywordPolicy","fusionPolicy"]:c.Retrieval.Version=="builder-retrieval/2"?["version","topK","space","queryMode","typePolicy","aliases"]:["version","topK","space","queryMode","typePolicy"]);c.Retrieval.Validate();
                if(c.Retrieval.Aliases!=null)foreach(var alias in element.GetProperty("aliases").EnumerateArray())Fields(alias,"id","kcId","text","normalized","candidateId","reviewedBy");
            }
            if(c.Version=="builder-config/4"){if(c.Provider!="LocalOmlx"||c.LocalTransport==null)throw Unknown();Fields(doc.RootElement.GetProperty("localTransport"),"endpoint","maxOutputTokens","promptHash");c.LocalTransport.Validate();}else if(c.LocalTransport!=null||c.Provider!="Mock")throw Unknown();
            c.Limits.Validate();return c;
        }
        catch(Exception ex)when(ex is JsonException or InvalidOperationException or KeyNotFoundException){throw Unknown();}
    }
}
