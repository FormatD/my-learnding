using System.Security.Cryptography;
using System.Text.Json;
namespace Learning;
public record AssessmentPrefixHashPair(string Full,string? Prefix);
public static class AssessmentPrefixHashes
{
    // Keep assessment-prefix/3's exact JSON byte contract. Hash the stream instead of
    // allocating the complete JSON string, and feed the prior-prefix hash in the same pass.
    public static AssessmentPrefixHashPair Compute(string zone,IEnumerable<AssessmentInput> inputs,TeachingAnchor[] teaching,long? prefixCount=null,CancellationToken ct=default)
    {
        if(prefixCount<0)throw new ArgumentOutOfRangeException(nameof(prefixCount));
        using var full=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);using var prefix=prefixCount==null?null:IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var sink=new JsonHashStream(full,prefix);using var writer=new Utf8JsonWriter(sink,WriterOptions());
        writer.WriteStartObject();writer.WriteString("prefixVersion","assessment-prefix/3");writer.WriteString("version",IncrementalAssessment.Version);writer.WriteString("inputVersion",Assessment.InputHashVersion);writer.WriteString("timeZone",zone);writer.WriteString("evidence",Assessment.EvidenceRuleVersion);writer.WriteString("mastery",Assessment.MasteryModelVersion);writer.WriteString("review",Assessment.ReviewRuleVersion);writer.WritePropertyName("inputs");writer.WriteStartArray();writer.Flush();
        long count=0;var captured=false;
        void Capture()
        {
            if(prefix==null || count!=prefixCount)return;
            sink.CaptureSecond=false;prefix.AppendData("],\"teaching\":"u8);
            using var tail=new JsonHashStream(prefix);using var tailWriter=new Utf8JsonWriter(tail,WriterOptions());WriteTeaching(tailWriter,teaching,ct);tailWriter.Flush();prefix.AppendData("}"u8);captured=true;
        }
        Capture();
        foreach(var x in inputs)
        {
            ct.ThrowIfCancellationRequested();
            JsonSerializer.Serialize(writer,new{x.Attempt,x.Question,x.Grade,x.Kcs,x.MappingReleaseId,x.CorrectionBatchId,x.MappingSetRevisionId,x.GradingCorrectionBatchId,x.MappingCorrectionBatchId,x.ReviewConfirmation,session=new{x.Session.Id,x.Session.FamilyId,x.Session.StudentId,x.Session.TaskId,x.Session.ReleaseId,x.Session.QuestionId},task=new{x.Task.Id,x.Task.FamilyId,x.Task.StudentId,x.Task.Type,x.Task.ReviewTargetId}},Json.Options);
            writer.Flush();count++;Capture();
        }
        writer.WriteEndArray();writer.WritePropertyName("teaching");WriteTeaching(writer,teaching,ct);writer.WriteEndObject();writer.Flush();
        ct.ThrowIfCancellationRequested();return new(Hex(full.GetHashAndReset()),captured?Hex(prefix!.GetHashAndReset()):null);
    }
    static JsonWriterOptions WriterOptions()=>new(){Encoder=Json.Options.Encoder,Indented=Json.Options.WriteIndented};
    static void WriteTeaching(Utf8JsonWriter writer,TeachingAnchor[] teaching,CancellationToken ct)
    {
        writer.WriteStartArray();foreach(var anchor in teaching){ct.ThrowIfCancellationRequested();JsonSerializer.Serialize(writer,anchor,Json.Options);writer.Flush();}writer.WriteEndArray();writer.Flush();
    }
    static string Hex(byte[] hash)=>Convert.ToHexString(hash).ToLowerInvariant();
}
