using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
namespace Learning;

// Storage changes never change the engine's complete, canonical snapshot contract.
public static class AssessmentStateDeltas
{
    public const string Version="assessment-state-delta/1";
    public record Change(string[] Path,string Value,int? Retain=null);
    public record Delta(string Version,Change[] Changes);
    public static string Create(string before,string after)
    {
        using var a=JsonDocument.Parse(before);using var b=JsonDocument.Parse(after);
        var changes=new List<Change>();Diff(a.RootElement,b.RootElement,[],changes);
        return Json.Write(new Delta(Version,changes.ToArray()));
    }
    static void Diff(JsonElement a,JsonElement b,string[] path,List<Change> changes)
    {
        if(a.GetRawText()==b.GetRawText())return;
        if(a.ValueKind==JsonValueKind.Object && b.ValueKind==JsonValueKind.Object && a.EnumerateObject().Select(x=>x.Name).SequenceEqual(b.EnumerateObject().Select(x=>x.Name)))
        {foreach(var p in b.EnumerateObject())Diff(a.GetProperty(p.Name),p.Value,[..path,p.Name],changes);return;}
        if(a.ValueKind==JsonValueKind.Array && b.ValueKind==JsonValueKind.Array)
        {
            var common=Math.Min(a.GetArrayLength(),b.GetArrayLength());
            for(var i=0;i<common;i++)Diff(a[i],b[i],[..path,i.ToString(System.Globalization.CultureInfo.InvariantCulture)],changes);
            if(a.GetArrayLength()!=b.GetArrayLength())changes.Add(new(path,"["+string.Join(",",b.EnumerateArray().Skip(common).Select(x=>x.GetRawText()))+"]",common));
            return;
        }
        changes.Add(new(path,b.GetRawText()));
    }
    public static string Apply(string before,string delta)
    {
        try
        {
            var d=Json.Read<Delta>(delta);if(d==null || d.Version!=Version || d.Changes==null)throw new InvalidOperationException();
            JsonNode? root=JsonNode.Parse(before);
            foreach(var change in d.Changes)
            {
                if(change==null || change.Path==null)throw new InvalidOperationException();
                using var valueDoc=JsonDocument.Parse(change.Value);var rawValue=valueDoc.RootElement;
                JsonNode? target=root;
                for(var i=0;i<change.Path.Length-(change.Retain==null?1:0);i++)target=Child(target,change.Path[i]);
                if(change.Retain is int retain)
                {
                    if(target is not JsonArray array || retain<0 || retain>array.Count || rawValue.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException();
                    while(array.Count>retain)array.RemoveAt(array.Count-1);
                    foreach(var item in rawValue.EnumerateArray())array.Add(JsonNode.Parse(item.GetRawText()));
                }
                else
                {
                    var value=JsonNode.Parse(change.Value);
                    if(change.Path.Length==0)root=value;
                    else if(target is JsonObject obj && obj.ContainsKey(change.Path[^1]))obj[change.Path[^1]]=value;
                    else if(target is JsonArray array && Index(change.Path[^1],array.Count) is int index)array[index]=value;
                    else throw new InvalidOperationException();
                }
            }
            if(root is not JsonObject)throw new InvalidOperationException();
            using var stream=new MemoryStream();using(var writer=new Utf8JsonWriter(stream)){WriteExact(writer,root);}
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch(Exception ex)when(ex is JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or NullReferenceException)
        {throw new ApiError(422,"INCREMENTAL_STATE_INVALID","增量状态差量无效，不能静默重建或覆盖。");}
    }
    // Parsed scalar tokens preserve date offsets, decimal scale and original escaping.
    static void WriteExact(Utf8JsonWriter writer,JsonNode? node)
    {
        if(node is JsonObject obj){writer.WriteStartObject();foreach(var field in obj){writer.WritePropertyName(field.Key);WriteExact(writer,field.Value);}writer.WriteEndObject();}
        else if(node is JsonArray array){writer.WriteStartArray();foreach(var item in array)WriteExact(writer,item);writer.WriteEndArray();}
        else if(node is JsonValue value && value.TryGetValue<JsonElement>(out var token))writer.WriteRawValue(token.GetRawText());
        else if(node==null)writer.WriteNullValue();
        else throw new InvalidOperationException();
    }
    static int Index(string key,int count)
    {if(!int.TryParse(key,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var i) || i<0 || i>=count)throw new InvalidOperationException();return i;}
    static JsonNode? Child(JsonNode? node,string key)=>node switch{JsonObject obj when obj.ContainsKey(key)=>obj[key],JsonArray arr=>arr[Index(key,arr.Count)],_=>throw new InvalidOperationException()};
}

public static class AssessmentCheckpointPayloads
{
    public const string Version="checkpoint-storage/1";
    public const int MaximumDepth=31;
    static ApiError Invalid()=>new(422,"INCREMENTAL_STATE_INVALID","增量状态存储链或摘要无效，不能静默重建或覆盖。");
    public static async Task<string> Read(Database db,Student student,AssessmentCheckpoint checkpoint,CancellationToken ct)
    {
        var parents=new Dictionary<Guid,AssessmentCheckpoint>();
        if(checkpoint.BaseCheckpointId!=null)
        {
            // One bounded statement captures the owned immutable ancestry; cycles are rejected below.
            var rows=await db.Set<AssessmentCheckpoint>().FromSqlInterpolated($"""
                WITH RECURSIVE chain AS (
                    SELECT c.*,0 AS storage_hops FROM "AssessmentCheckpoint" c
                    WHERE c."Id"={checkpoint.BaseCheckpointId} AND c."FamilyId"={student.FamilyId} AND c."StudentId"={student.Id} AND c."GenerationId"={checkpoint.GenerationId}
                    UNION ALL
                    SELECT c.*,chain.storage_hops+1 FROM "AssessmentCheckpoint" c JOIN chain ON c."Id"=chain."BaseCheckpointId"
                    WHERE c."FamilyId"={student.FamilyId} AND c."StudentId"={student.Id} AND c."GenerationId"={checkpoint.GenerationId} AND chain.storage_hops<{MaximumDepth-1}
                ) SELECT * FROM chain
                """).AsNoTracking().ToArrayAsync(ct);
            foreach(var row in rows)parents.TryAdd(row.Id,row);
        }
        var chain=new List<AssessmentCheckpoint>();var seen=new HashSet<Guid>();var current=checkpoint;
        while(true)
        {
            ct.ThrowIfCancellationRequested();
            if(!seen.Add(current.Id) || current.FamilyId!=student.FamilyId || current.StudentId!=student.Id || current.GenerationId!=checkpoint.GenerationId || current.EngineVersion!=checkpoint.EngineVersion || Content.Hash(current.Payload)!=current.PayloadHash)throw Invalid();
            chain.Add(current);
            if(current.StorageVersion==null)
            {if(current.BaseCheckpointId!=null || current.DeltaDepth!=null || current.StatePayloadHash!=null)throw Invalid();break;}
            if(current.StorageVersion!=Version || current.StatePayloadHash?.Length!=64 || current.DeltaDepth is not int depth || depth<0 || depth>MaximumDepth)throw Invalid();
            if(depth==0){if(current.BaseCheckpointId!=null)throw Invalid();break;}
            if(current.BaseCheckpointId==null || chain.Count>MaximumDepth)throw Invalid();
            parents.TryGetValue(current.BaseCheckpointId.Value,out var parent);
            if(parent==null || parent.InputCount>=current.InputCount || (parent.DeltaDepth??0)!=depth-1)throw Invalid();
            current=parent;
        }
        var payload=chain[^1].Payload;
        for(var i=chain.Count-1;i>=0;i--)
        {
            ct.ThrowIfCancellationRequested();
            var row=chain[i];if(i<chain.Count-1)payload=AssessmentStateDeltas.Apply(payload,row.Payload);
            if(row.StorageVersion!=null && Content.Hash(payload)!=row.StatePayloadHash)throw Invalid();
            try
            {
                using var doc=JsonDocument.Parse(payload);var root=doc.RootElement;
                if(root.GetProperty("familyId").GetGuid()!=student.FamilyId || root.GetProperty("studentId").GetGuid()!=student.Id || root.GetProperty("generationId").GetGuid()!=row.GenerationId || root.GetProperty("inputCount").GetInt64()!=row.InputCount || root.GetProperty("version").GetString()!=row.EngineVersion)throw Invalid();
            }
            catch(Exception ex)when(ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException){throw Invalid();}
        }
        return payload;
    }
}
