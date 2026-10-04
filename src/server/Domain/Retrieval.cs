using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Learning;
public class Embedding : Row
{
    public Guid EntityRevisionId { get; set; }
    public string EntityType { get; set; } = "KC";
    public string Space { get; set; } = Retrieval.Space;
    public int Dimensions { get; set; } = Retrieval.Dimensions;
    public string TextHash { get; set; } = "";
    public string Vector { get; set; } = "[]";
}
public class Alias : Row
{
    public Guid KCId { get; set; }
    public Guid CandidateId { get; set; }
    public string Text { get; set; } = "";
    public string Normalized { get; set; } = "";
    public Guid ReviewedBy { get; set; }
}
public record Match(Guid KCId,string Name,string Behavior,string Boundary,decimal Score,string EmbeddingSpace,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] BuilderAliasSnapshot[]? MatchedAliases=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] BuilderRetrievalEvidence? RetrievalEvidence=null);
public static class Retrieval
{
    // Deterministic mock feature vectors exercise space isolation. They are not model embeddings.
    public const int Dimensions=128;
    public const string Space="mock/local-char-bigram/1/128/normalization-1";
    public static string Normalize(string s)=>s.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
    public static double[] Vector(string text)
    {
        var normalized=Normalize(text);var result=new double[Dimensions];
        for (var i=0;i<normalized.Length-1;i++)
        {
            var hash=SHA256.HashData(Encoding.UTF8.GetBytes(normalized.Substring(i,2)));
            result[hash[0]%Dimensions]+=hash[1]%2==0?1:-1;
        }
        var norm=Math.Sqrt(result.Sum(v=>v*v));return norm==0 ? result : result.Select(v=>v/norm).ToArray();
    }
    public static decimal Similarity(double[] a,string aSpace,double[] b,string bSpace)
    {
        if (aSpace!=bSpace || a.Length!=b.Length || a.Length!=Dimensions) throw new ApiError(422,"EMBEDDING_SPACE_CONFLICT","不同模型空间或维度不能直接比较。");
        return (decimal)a.Zip(b,(x,y)=>x*y).Sum();
    }
    public static Match[] TopK(string text,IEnumerable<KC> kcs) => kcs.Select(k=>new Match(k.Id,k.Name,k.Behavior,k.Boundary,Similarity(Vector(text),Space,Vector(k.Name+" "+k.Behavior+" "+k.Boundary),Space),Space)).OrderByDescending(m=>m.Score).ThenBy(m=>m.KCId).Take(10).ToArray();
    public static Match[] Candidates(BuilderCandidateOutput candidate,IEnumerable<KC> kcs,BuilderRetrievalConfiguration configuration)
    {
        configuration.Validate();
        if(candidate.Subject!="MATH" || !KCTypes.ValidDefinition(candidate.KcType))throw new ApiError(422,"BUILDER_SCHEMA_INVALID","候选学科或能力类型无效。");
        var query=Vector(candidate.Name+" "+candidate.MeasurableBehavior+" "+candidate.Boundary);
        var name=Normalize(candidate.Name);
        var matches=kcs.Where(k=>k.Type==candidate.KcType && (configuration.Version!="builder-retrieval/4" || k.Subject==candidate.Subject)).Select(k=>new Match(k.Id,k.Name,k.Behavior,k.Boundary,Similarity(query,configuration.Space,Vector(k.Name+" "+k.Behavior+" "+k.Boundary),configuration.Space),configuration.Space,configuration.Aliases?.Where(a=>a.KCId==k.Id && a.Normalized==name).ToArray())).ToArray();
        if(configuration.Version is "builder-retrieval/3" or "builder-retrieval/4")return BuilderLexicalRetrieval.Fuse(candidate,matches,configuration.TopK);
        return matches.OrderByDescending(m=>m.MatchedAliases is {Length:>0}).ThenByDescending(m=>m.Score).ThenBy(m=>m.KCId).Take(configuration.TopK).ToArray();
    }
}
