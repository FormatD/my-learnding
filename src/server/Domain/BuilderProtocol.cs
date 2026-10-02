using System.Text.Json;

namespace Learning;

public record BuilderFragment(Guid Id,string Text);
public record BuilderCandidateOutput(string Name,string Subject,string KcType,int GradeMin,int GradeMax,string MeasurableBehavior,string Boundary,Guid[] SourceChunkIds,string[] SupportingQuotes,decimal ModelScore);
public record BuilderEnvelope(string SchemaVersion,BuilderCandidateOutput[] Candidates);
public record BuilderProviderRequest(BuilderFragment[] Fragments,string Schema,string? InvalidOutput=null,string? ValidationCode=null);
public interface IBuilderCandidateProvider
{
    Task<string> Generate(BuilderProviderRequest request,CancellationToken ct);
}
public record BuilderProtocolResult(BuilderEnvelope Output,int Calls,bool Repaired);

public static class BuilderProtocol
{
    public const int MaxFragments=100,MaxInputCharacters=100_000,MaxOutputCharacters=1_000_000;
    public const string Schema="""
    {"type":"object","additionalProperties":false,"required":["schemaVersion","candidates"],"properties":{"schemaVersion":{"const":"kc-candidate/1"},"candidates":{"type":"array","maxItems":100,"items":{"type":"object","additionalProperties":false,"required":["name","subject","kcType","gradeMin","gradeMax","measurableBehavior","boundary","sourceChunkIds","supportingQuotes","modelScore"],"properties":{"name":{"type":"string","minLength":1,"maxLength":100},"subject":{"const":"MATH"},"kcType":{"enum":["Procedure","Concept","Application","Representation","Misconception"]},"gradeMin":{"type":"integer","minimum":1,"maximum":12},"gradeMax":{"type":"integer","minimum":1,"maximum":12},"measurableBehavior":{"type":"string","minLength":1,"maxLength":4000},"boundary":{"type":"string","minLength":1,"maxLength":4000},"sourceChunkIds":{"type":"array","minItems":1,"maxItems":10,"uniqueItems":true,"items":{"type":"string","format":"uuid"}},"supportingQuotes":{"type":"array","minItems":1,"maxItems":10,"items":{"type":"string","minLength":1,"maxLength":1000}},"modelScore":{"type":"number","minimum":0,"maximum":1}}}}}}
    """;
    static ApiError Invalid(string code)=>new(422,code,"建库输出未通过结构或来源核对，请人工检查；没有生成正式内容。");
    public static void ValidateInput(BuilderFragment[] fragments,BuilderLimits? limits=null)
    {
        limits??=new();limits.Validate();
        if(fragments.Length==0 || fragments.Length>limits.MaxFragments || fragments.Any(f=>f.Id==Guid.Empty || string.IsNullOrWhiteSpace(f.Text)) || fragments.Select(f=>f.Id).Distinct().Count()!=fragments.Length || fragments.Sum(f=>(long)f.Text.Length)>limits.MaxInputCharacters)
            throw Invalid("BUILDER_INPUT_LIMIT");
    }
    static void Shape(JsonElement value,params string[] required)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Invalid("BUILDER_SCHEMA_INVALID");
        var fields=value.EnumerateObject().Select(p=>p.Name).ToArray();
        if(fields.Length!=required.Length || fields.Distinct().Count()!=fields.Length || required.Except(fields).Any())throw Invalid("BUILDER_SCHEMA_INVALID");
    }
    static string Text(JsonElement value,string name,int max)
    {
        var field=value.GetProperty(name);if(field.ValueKind!=JsonValueKind.String)throw Invalid("BUILDER_SCHEMA_INVALID");
        var text=field.GetString()!;if(string.IsNullOrWhiteSpace(text) || text.Length>max)throw Invalid("BUILDER_SCHEMA_INVALID");return text;
    }
    static JsonElement[] Array(JsonElement value,string name,int min,int max)
    {
        var field=value.GetProperty(name);if(field.ValueKind!=JsonValueKind.Array || field.GetArrayLength()<min || field.GetArrayLength()>max)throw Invalid("BUILDER_SCHEMA_INVALID");return field.EnumerateArray().ToArray();
    }
    public static BuilderEnvelope Validate(string output,BuilderFragment[] fragments,BuilderLimits? limits=null)
    {
        limits??=new();ValidateInput(fragments,limits);
        if(output.Length>limits.MaxOutputCharacters)throw Invalid("BUILDER_OUTPUT_LIMIT");
        try
        {
            using var document=JsonDocument.Parse(output,new JsonDocumentOptions{MaxDepth=8});var root=document.RootElement;
            Shape(root,"schemaVersion","candidates");if(Text(root,"schemaVersion",40)!="kc-candidate/1")throw Invalid("BUILDER_SCHEMA_INVALID");
            var chunks=fragments.ToDictionary(f=>f.Id);var result=new List<BuilderCandidateOutput>();
            foreach(var item in Array(root,"candidates",0,100))
            {
                Shape(item,"name","subject","kcType","gradeMin","gradeMax","measurableBehavior","boundary","sourceChunkIds","supportingQuotes","modelScore");
                var name=Text(item,"name",100);var subject=Text(item,"subject",20);var type=Text(item,"kcType",20);var behavior=Text(item,"measurableBehavior",4000);var boundary=Text(item,"boundary",4000);
                if(subject!="MATH" || !new[]{"Procedure","Concept","Application","Representation","Misconception"}.Contains(type) || !item.GetProperty("gradeMin").TryGetInt32(out var min) || !item.GetProperty("gradeMax").TryGetInt32(out var max) || min<1 || max>12 || min>max || !item.GetProperty("modelScore").TryGetDecimal(out var score) || score<0 || score>1)throw Invalid("BUILDER_SCHEMA_INVALID");
                var refs=Array(item,"sourceChunkIds",1,10);var quotes=Array(item,"supportingQuotes",1,10);
                if(refs.Length!=quotes.Length)throw Invalid("BUILDER_SOURCE_INVALID");
                var ids=new List<Guid>();var texts=new List<string>();
                for(var i=0;i<refs.Length;i++)
                {
                    if(refs[i].ValueKind!=JsonValueKind.String || !Guid.TryParseExact(refs[i].GetString(),"D",out var id) || !chunks.TryGetValue(id,out var chunk) || quotes[i].ValueKind!=JsonValueKind.String)throw Invalid("BUILDER_SOURCE_INVALID");
                    var quote=quotes[i].GetString()!;if(string.IsNullOrWhiteSpace(quote) || quote.Length>1000 || !chunk.Text.Contains(quote,StringComparison.Ordinal))throw Invalid("BUILDER_SOURCE_INVALID");
                    ids.Add(id);texts.Add(quote);
                }
                if(ids.Distinct().Count()!=ids.Count)throw Invalid("BUILDER_SOURCE_INVALID");
                result.Add(new(name,subject,type,min,max,behavior,boundary,ids.ToArray(),texts.ToArray(),score));
            }
            return new("kc-candidate/1",result.ToArray());
        }
        catch(Exception ex)when(ex is JsonException or InvalidOperationException or FormatException or OverflowException){throw Invalid("BUILDER_SCHEMA_INVALID");}
    }
    public static async Task<BuilderProtocolResult> Run(IBuilderCandidateProvider provider,BuilderFragment[] fragments,TimeSpan timeout,CancellationToken ct)
    {
        if(timeout<=TimeSpan.Zero || timeout>TimeSpan.FromMinutes(2))throw new ArgumentOutOfRangeException(nameof(timeout));
        return await Run(provider,fragments,new BuilderLimits(TimeoutMilliseconds:(int)timeout.TotalMilliseconds),ct);
    }
    public static async Task<BuilderProtocolResult> Run(IBuilderCandidateProvider provider,BuilderFragment[] fragments,BuilderLimits limits,CancellationToken ct)
    {
        fragments=fragments.ToArray();ValidateInput(fragments,limits);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(limits.TimeoutMilliseconds);
        try
        {
            string? invalid=null;string? code=null;
            for(var call=1;call<=1+limits.RepairAttempts;call++)
            {
                var raw=await provider.Generate(new(fragments.ToArray(),Schema,invalid,code),deadline.Token).WaitAsync(deadline.Token);
                try{return new(Validate(raw,fragments,limits),call,call==2);}
                catch(ApiError ex)when(ex.Code is "BUILDER_SCHEMA_INVALID" or "BUILDER_SOURCE_INVALID")
                {if(call==1+limits.RepairAttempts)throw Invalid("BUILDER_NEEDS_REPAIR");invalid=raw;code=ex.Code;}
            }
            throw Invalid("BUILDER_NEEDS_REPAIR");
        }
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new ApiError(422,"BUILDER_TIMEOUT","建库处理超时，请人工检查或稍后重新准备任务。");}
    }
}

// Local workflow fixture only. No external model or invented extraction quality.
public sealed class MockBuilderCandidateProvider:IBuilderCandidateProvider
{
    static string Prefix(string value,int max){var length=Math.Min(max,value.Length);if(length<value.Length && char.IsHighSurrogate(value[length-1]))length--;return value[..length];}
    public Task<string> Generate(BuilderProviderRequest request,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var candidates=request.Fragments.Select(f=>{
            var length=Math.Min(80,f.Text.Length);if(length<f.Text.Length && char.IsHighSurrogate(f.Text[length-1]))length--;var quote=f.Text[..length];
            return new BuilderCandidateOutput(Prefix(quote,24),"MATH","Procedure",1,12,"请审核者补充独立可测行为","请审核者补充排除范围",[f.Id],[quote],0);
        }).ToArray();
        return Task.FromResult(Json.Write(new BuilderEnvelope("kc-candidate/1",candidates)));
    }
}
