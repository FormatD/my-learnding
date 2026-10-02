using System.Text.Json;
namespace Learning;

public record BuilderLimits(int MaxFragments=100,int MaxInputCharacters=100_000,int MaxOutputCharacters=1_000_000,int TimeoutMilliseconds=30_000,int RepairAttempts=1)
{
    public void Validate()
    {
        if(MaxFragments<1 || MaxFragments>BuilderProtocol.MaxFragments || MaxInputCharacters<1 || MaxInputCharacters>BuilderProtocol.MaxInputCharacters || MaxOutputCharacters<1 || MaxOutputCharacters>BuilderProtocol.MaxOutputCharacters || TimeoutMilliseconds<1 || TimeoutMilliseconds>120_000 || RepairAttempts<0 || RepairAttempts>1)
            throw new ApiError(422,"BUILDER_CONFIGURATION_INVALID","建库运行上限无效，请检查本地配置。");
    }
}
public record BuilderModelConfiguration(string Version,string Provider,string Model,string PromptVersion,string SchemaHash,BuilderLimits Limits);
public static class BuilderConfiguration
{
    public static BuilderModelConfiguration Current(IConfiguration configuration)
    {
        int Setting(string key,int fallback)=>configuration.GetValue<int?>("Builder:"+key)??fallback;
        var limits=new BuilderLimits(Setting("MaxFragments",100),Setting("MaxInputCharacters",100_000),Setting("MaxOutputCharacters",1_000_000),Setting("TimeoutMilliseconds",30_000),Setting("RepairAttempts",1));limits.Validate();
        return new("builder-config/1","Mock","fixture/1","kc-candidate/1",Content.Hash(BuilderProtocol.Schema),limits);
    }
    static ApiError Unknown()=>new(422,"RUN_CONFIGURATION_UNKNOWN","原建库配置无法验证，请重新准备任务；不会替换为当前配置。");
    static void Fields(JsonElement value,params string[] names)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Unknown();var fields=value.EnumerateObject().Select(p=>p.Name).ToArray();if(fields.Length!=names.Length || fields.Distinct().Count()!=fields.Length || names.Except(fields).Any())throw Unknown();
    }
    public static BuilderLimits Resolve(BuilderRun run)
    {
        // Preserve an explicit legacy path; never backfill a configuration never saved.
        if(run.ModelConfigPayload==null && run.ModelConfigHash==null){if(run.InputVersion=="builder-input/3")throw Unknown();return new BuilderLimits();}
        if(run.ModelConfigPayload==null || run.ModelConfigHash==null || Content.Hash(run.ModelConfigPayload)!=run.ModelConfigHash)throw Unknown();
        try
        {
            using var doc=JsonDocument.Parse(run.ModelConfigPayload);Fields(doc.RootElement,"version","provider","model","promptVersion","schemaHash","limits");Fields(doc.RootElement.GetProperty("limits"),"maxFragments","maxInputCharacters","maxOutputCharacters","timeoutMilliseconds","repairAttempts");
            var c=Json.Read<BuilderModelConfiguration>(run.ModelConfigPayload);
            if(c.Version!="builder-config/1" || c.Provider!=run.Provider || c.Model!=run.Model || c.PromptVersion!=run.PromptVersion || c.SchemaHash!=Content.Hash(BuilderProtocol.Schema) || c.Limits==null)throw Unknown();c.Limits.Validate();return c.Limits;
        }
        catch(Exception ex)when(ex is JsonException or InvalidOperationException or KeyNotFoundException){throw Unknown();}
    }
}
