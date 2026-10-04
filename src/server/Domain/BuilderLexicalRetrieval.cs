using System.Text;
namespace Learning;
public record BuilderRetrievalEvidence(string KeywordPolicy,string FusionPolicy,string[] MatchedTerms,decimal KeywordScore,int? KeywordRank,int? VectorRank,decimal FusionScore);
public static class BuilderLexicalRetrieval
{
    public const string KeywordPolicy="unicode-bigram-bm25/1/k1-1.2/b-0.75";
    public const string FusionPolicy="rrf/1/k-60/source-top-k";
    // NFKC first. CJK runs use adjacent Unicode scalar pairs; Latin/numeric runs use whole words.
    // Punctuation separates runs; no pairs cross a field, punctuation, or writing-system boundary.
    public static string[] Terms(string text)
    {
        var terms=new List<string>();var run=new List<Rune>();bool? chinese=null;
        void Flush(){if(chinese==true){for(var i=1;i<run.Count;i++)terms.Add(run[i-1].ToString()+run[i]);}else if(run.Count>0)terms.Add(string.Concat(run.Select(r=>r.ToString())));run.Clear();chinese=null;}
        foreach(var rune in Retrieval.Normalize(text).EnumerateRunes())
        {
            if(!Rune.IsLetterOrDigit(rune)){Flush();continue;}
            var cjk=rune.Value is >=0x3400 and <=0x9fff or >=0xf900 and <=0xfaff or >=0x20000 and <=0x323af;
            if(chinese!=null && chinese!=cjk)Flush();chinese=cjk;run.Add(rune);
        }
        Flush();return terms.ToArray();
    }
    static string[] DefinitionTerms(string name,string behavior,string boundary)=>Terms(name).Concat(Terms(behavior)).Concat(Terms(boundary)).ToArray();
    public static Match[] Fuse(BuilderCandidateOutput candidate,Match[] matches,int topK)
    {
        if(matches.Length==0)return [];
        var query=DefinitionTerms(candidate.Name,candidate.MeasurableBehavior,candidate.Boundary).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var documents=matches.ToDictionary(m=>m.KCId,m=>DefinitionTerms(m.Name,m.Behavior,m.Boundary));var average=documents.Values.Average(t=>t.Length);
        var termCounts=documents.ToDictionary(d=>d.Key,d=>d.Value.GroupBy(t=>t,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal));
        var frequency=query.ToDictionary(t=>t,t=>termCounts.Values.Count(d=>d.ContainsKey(t)),StringComparer.Ordinal);
        var lexical=matches.Select(m=>
        {
            var terms=documents[m.KCId];var counts=termCounts[m.KCId];var hits=query.Where(counts.ContainsKey).ToArray();double score=0;
            foreach(var term in hits){var tf=counts[term];var idf=Math.Log(1+(matches.Length-frequency[term]+.5)/(frequency[term]+.5));score+=idf*tf*2.2/(tf+1.2*(.25+.75*terms.Length/average));}
            return new{m.KCId,Hits=hits,Score=(decimal)score};
        }).ToArray();
        var keywordRanks=lexical.Where(x=>x.Score>0).OrderByDescending(x=>x.Score).ThenBy(x=>x.KCId).Take(topK).Select((x,i)=>new{x.KCId,Rank=i+1}).ToDictionary(x=>x.KCId,x=>x.Rank);
        var vectorRanks=matches.OrderByDescending(m=>m.Score).ThenBy(m=>m.KCId).Take(topK).Select((m,i)=>new{m.KCId,Rank=i+1}).ToDictionary(x=>x.KCId,x=>x.Rank);
        var scores=lexical.ToDictionary(x=>x.KCId);
        return matches.Where(m=>keywordRanks.ContainsKey(m.KCId) || vectorRanks.ContainsKey(m.KCId) || m.MatchedAliases is {Length:>0}).Select(m=>
        {
            var words=scores[m.KCId];int? rank=keywordRanks.TryGetValue(m.KCId,out var r)?r:null;int? vector=vectorRanks.TryGetValue(m.KCId,out var v)?v:null;var fused=(vector==null?0:1m/(60+vector.Value))+(rank==null?0:1m/(60+rank.Value));
            return m with{RetrievalEvidence=new(KeywordPolicy,FusionPolicy,words.Hits,words.Score,rank,vector,fused)};
        }).OrderByDescending(m=>m.MatchedAliases is {Length:>0}).ThenByDescending(m=>m.RetrievalEvidence!.FusionScore).ThenBy(m=>m.KCId).Take(topK).ToArray();
    }
}
