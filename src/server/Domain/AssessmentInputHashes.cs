using System.Security.Cryptography;
using System.Text.Json;
namespace Learning;
public static class AssessmentInputHashes
{
    // Preserve the original assessment-input/2 JSON, including full Session and Task
    // records. Its contract differs from the mathematical prefix projection.
    public static string Compute(string zone,IEnumerable<AssessmentInput> inputs,TeachingAnchor[] teaching,CancellationToken ct=default)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);using var sink=new JsonHashStream(hash);using var writer=new Utf8JsonWriter(sink,new(){Encoder=Json.Options.Encoder,Indented=Json.Options.WriteIndented});
        writer.WriteStartObject();writer.WritePropertyName("inputs");writer.WriteStartArray();
        foreach(var input in inputs){ct.ThrowIfCancellationRequested();JsonSerializer.Serialize(writer,input,Json.Options);writer.Flush();}
        writer.WriteEndArray();writer.WritePropertyName("teaching");writer.WriteStartArray();
        foreach(var anchor in teaching){ct.ThrowIfCancellationRequested();JsonSerializer.Serialize(writer,anchor,Json.Options);writer.Flush();}
        writer.WriteEndArray();writer.WriteString("mappingContext","assessment-context/1");writer.WriteString("inputVersion",Assessment.InputHashVersion);writer.WriteString("timeZone",zone);writer.WriteString("rule",Assessment.EvidenceRuleVersion);writer.WriteString("model",Assessment.MasteryModelVersion);writer.WriteString("review",Assessment.ReviewRuleVersion);writer.WriteEndObject();writer.Flush();
        ct.ThrowIfCancellationRequested();return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
