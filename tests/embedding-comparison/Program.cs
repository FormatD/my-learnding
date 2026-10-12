using Learning;
using System.Diagnostics;
using System.Text.Json;
if(args.Length!=5)throw new ArgumentException("fixture model keyFile outputDir artifactDigest");
var options=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
var fixture=JsonSerializer.Deserialize<Fixture>(File.ReadAllText(args[0]),options)!;
var output=Path.GetFullPath(args[3]);Directory.CreateDirectory(output);
var library=fixture.Library.Select((k,i)=>new KC(Id(i),Id(100+i),k.Id,k.Name,k.Behavior,k.Boundary,Subject:"MATH",GradeMin:2,GradeMax:3)).ToArray();
var candidates=fixture.Queries.Select(q=>new BuilderCandidateOutput(q.Name,"MATH","Procedure",2,3,q.Behavior,q.Boundary,[Id(999)],["原创检索测试样本"],0)).ToArray();
var config=new BuilderRetrievalConfiguration("builder-retrieval/4",10,Retrieval.Space,"CandidateDefinition","ExactKCType",[],BuilderLexicalRetrieval.KeywordPolicy,BuilderLexicalRetrieval.FusionPolicy,"ExactRecordedSubject","NoGradeExclusion");
var baseline=candidates.Select(c=>Retrieval.Candidates(c,library,config)).ToArray();
var baselineRows=candidates.Select((c,i)=>new{query=fixture.Queries[i].Id,expected=fixture.Queries[i].Expected,baseline=baseline[i].Select(m=>library.Single(k=>k.Id==m.KCId).Code).ToArray()}).ToArray();
File.WriteAllText(Path.Combine(output,"baseline-results.json"),JsonSerializer.Serialize(baselineRows));
if(args[1]=="baseline")return;
var dimensions=768;
var baseSpace=new ModelEmbeddingSpace("LocalOmlx",args[1],args[4],dimensions,ModelEmbeddingSpace.Preprocessing);

var results=new List<object>();
foreach(var recipe in new[]{"raw","sts"}){
 var space=baseSpace with{InputRecipeVersion=recipe=="sts"?"embeddinggemma2-sts/1":null};
 var provider=new LocalEmbeddingProvider(new("embedding-local/1","http://127.0.0.1:8000/v1",space,180000),args[2]);
 string Text(string n,string b,string x)=>n+" "+b+" "+x;
 var inputs=library.Select(k=>new ModelEmbeddingInput(k.RevisionId,"KC",Text(k.Name,k.Behavior,k.Boundary))).Concat(candidates.Select((c,i)=>new ModelEmbeddingInput(Id(200+i),"Candidate",Text(c.Name,c.MeasurableBehavior,c.Boundary)))).ToArray();
 var watch=Stopwatch.StartNew();var response=await provider.Generate(inputs,CancellationToken.None);watch.Stop();File.WriteAllText(Path.Combine(output,recipe+"-response.json"),response.Output);
 var vectors=ModelEmbeddings.Validate(response,space,inputs);
 var rows=candidates.Select((c,i)=>{
  var matches=library.Select((k,j)=>new Match(k.Id,k.Name,k.Behavior,k.Boundary,ModelEmbeddings.Similarity(vectors[library.Length+i],vectors[j],space),space.Id)).ToArray();
  var semantic=matches.OrderByDescending(m=>m.Score).ThenBy(m=>m.KCId).Take(10).ToArray();var hybrid=ModelEmbeddings.Candidates(Id(200+i),c,library,space,vectors[library.Length+i],vectors.Take(library.Length).ToArray(),10,[]);
  string[] Names(Match[] ms)=>ms.Select(m=>library.Single(k=>k.Id==m.KCId).Code).ToArray();
  return new{query=fixture.Queries[i].Id,expected=fixture.Queries[i].Expected,baseline=Names(baseline[i]),semantic=Names(semantic),hybrid=Names(hybrid)};
 }).ToArray();
 results.Add(new{recipe,dimensions,space=space.Id,elapsedMs=watch.Elapsed.TotalMilliseconds,response.Usage,rows});
 Console.WriteLine($"{recipe}: validated {vectors.Length} vectors, {watch.Elapsed.TotalMilliseconds:F0}ms");
}
File.WriteAllText(Path.Combine(output,"retrieval-results.json"),JsonSerializer.Serialize(new{fixtureSha256=Content.Hash(File.ReadAllText(args[0])),model=args[1],artifactDigest=args[4],note="Experimental raw/STS input recipes; no production index or family writes. Same current BM25/RRF and subject/type policy, no aliases.",results},new JsonSerializerOptions{WriteIndented=true}));
static Guid Id(int number)=>Guid.Parse($"00000000-0000-0000-0000-{(number+1).ToString("x12")}");
record Definition(string Id,string Name,string Behavior,string Boundary);
record Query(string Id,string Name,string Behavior,string Boundary,string Expected);
record Fixture(Definition[] Library,Query[] Queries);
