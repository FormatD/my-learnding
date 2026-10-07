using System.Text;
using System.Text.Json;
namespace Learning;

// This space belongs to a real embedding provider. Existing mock space/recipes stay unchanged.
public record ModelEmbeddingSpace(string Provider,string Model,string ModelVersion,int Dimensions,string PreprocessingVersion)
{
    public const string Preprocessing="nfkc-trim-lower-invariant/1";
    public string Id=>"embedding/1/"+Content.Hash(Json.Write(new{Provider,Model,ModelVersion,Dimensions,PreprocessingVersion}));
    public void Validate()
    {
        if(Provider!="LocalOmlx" || string.IsNullOrWhiteSpace(Model) || Model.Length>200 || string.IsNullOrWhiteSpace(ModelVersion) || ModelVersion.Length>200 || Dimensions is <1 or >8192 || PreprocessingVersion!=Preprocessing || Model.Any(char.IsControl) || ModelVersion.Any(char.IsControl))throw new ApiError(422,"EMBEDDING_CONFIGURATION_INVALID","本机向量模型、固定版本、维度或文本处理方式无效。");
    }
    public string Prepare(string text){Validate();if(text==null)throw new ApiError(422,"EMBEDDING_INPUT_INVALID","向量输入不能为空。");return text.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();}
}
public record ModelEmbeddingInput(Guid EntityRevisionId,string EntityType,string Text);
public record ModelEmbeddingVector(Guid EntityRevisionId,string EntityType,string TextHash,string Space,int Dimensions,double[] Vector);
public record ModelEmbeddingResponse(string Output,BuilderUsage Usage);
public static class ModelEmbeddings
{
    public const int MaxInputs=64,MaxInputCharacters=24_000,MaxOutputBytes=16_000_000;
    static ApiError Invalid(string code)=>new(422,code,"模型向量未通过固定空间、对象、维度或数值核对，未更新索引。");
    public static string[] Inputs(ModelEmbeddingSpace space,ModelEmbeddingInput[] inputs)
    {
        space.Validate();
        if(inputs==null || inputs.Length is <1 or >MaxInputs || inputs.Any(i=>i==null || i.EntityRevisionId==Guid.Empty || i.EntityType is not ("KC" or "Candidate") || string.IsNullOrWhiteSpace(i.Text)) || inputs.Select(i=>(i.EntityType,i.EntityRevisionId)).Distinct().Count()!=inputs.Length)throw Invalid("EMBEDDING_INPUT_INVALID");
        var texts=inputs.Select(i=>space.Prepare(i.Text)).ToArray();if(texts.Any(string.IsNullOrWhiteSpace) || texts.Sum(t=>(long)t.Length)>MaxInputCharacters)throw Invalid("EMBEDDING_INPUT_LIMIT");return texts;
    }
    public static double[] Unit(double[] vector,int dimensions)
    {
        if(dimensions is <1 or >8192 || vector==null || vector.Length!=dimensions || vector.Any(v=>!double.IsFinite(v)))throw Invalid("EMBEDDING_VECTOR_INVALID");
        // Scale before squaring so large finite coordinates cannot overflow the norm.
        var scale=vector.Max(v=>Math.Abs(v));if(scale==0)throw Invalid("EMBEDDING_VECTOR_INVALID");
        var scaled=vector.Select(v=>v/scale).ToArray();var norm=Math.Sqrt(scaled.Sum(v=>v*v));return scaled.Select(v=>v/norm).ToArray();
    }
    public static ModelEmbeddingVector[] Validate(ModelEmbeddingResponse response,ModelEmbeddingSpace space,ModelEmbeddingInput[] inputs)
    {
        var texts=Inputs(space,inputs);
        if(response?.Output==null || Encoding.UTF8.GetByteCount(response.Output)>MaxOutputBytes)throw Invalid("EMBEDDING_OUTPUT_LIMIT");
        try
        {
            using var doc=JsonDocument.Parse(response.Output,new JsonDocumentOptions{MaxDepth=8});var root=doc.RootElement;
            Unique(root);if(root.GetProperty("model").GetString()!=space.Model)throw Invalid("EMBEDDING_MODEL_MISMATCH");
            if(root.GetProperty("object").GetString()!="list" || root.GetProperty("data").ValueKind!=JsonValueKind.Array || root.GetProperty("data").GetArrayLength()!=inputs.Length)throw Invalid("EMBEDDING_RESPONSE_INVALID");
            var vectors=new ModelEmbeddingVector[inputs.Length];var seen=new HashSet<int>();
            foreach(var item in root.GetProperty("data").EnumerateArray())
            {
                Unique(item);if(item.GetProperty("object").GetString()!="embedding" || !item.GetProperty("index").TryGetInt32(out var index) || index<0 || index>=inputs.Length || !seen.Add(index) || item.GetProperty("embedding").ValueKind!=JsonValueKind.Array || item.GetProperty("embedding").GetArrayLength()!=space.Dimensions)throw Invalid("EMBEDDING_RESPONSE_INVALID");
                var raw=item.GetProperty("embedding").EnumerateArray().Select(n=>n.GetDouble()).ToArray();var vector=Unit(raw,space.Dimensions);var input=inputs[index];vectors[index]=new(input.EntityRevisionId,input.EntityType,Content.Hash(texts[index]),space.Id,space.Dimensions,vector);
            }
            return vectors;
        }
        catch(Exception ex)when(ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException){throw Invalid("EMBEDDING_RESPONSE_INVALID");}
    }
    static void Unique(JsonElement item){if(item.ValueKind!=JsonValueKind.Object || item.EnumerateObject().GroupBy(p=>p.Name,StringComparer.Ordinal).Any(g=>g.Count()>1))throw Invalid("EMBEDDING_RESPONSE_INVALID");}
    public static decimal Similarity(ModelEmbeddingVector a,ModelEmbeddingVector b,ModelEmbeddingSpace space)
    {
        space.Validate();
        if(a==null || b==null || a.Space!=space.Id || a.Space!=b.Space || a.Dimensions!=b.Dimensions || a.Dimensions!=space.Dimensions || a.Dimensions is <1 or >8192)throw Invalid("EMBEDDING_SPACE_CONFLICT");
        var left=Unit(a.Vector,a.Dimensions);var right=Unit(b.Vector,b.Dimensions);return Math.Clamp((decimal)left.Zip(right,(x,y)=>x*y).Sum(),-1,1);
    }
    // Small in-memory exact recall. Caller supplies a frozen library and complete validated vectors.
    public static Match[] Candidates(Guid candidateRevisionId,BuilderCandidateOutput candidate,KC[] library,ModelEmbeddingSpace space,ModelEmbeddingVector query,ModelEmbeddingVector[] vectors,int topK,BuilderAliasSnapshot[] aliases)
    {
        space.Validate();if(topK is <1 or >50 || candidate==null || candidate.Subject!="MATH" || !KCTypes.ValidDefinition(candidate.KcType) || library==null || vectors==null || aliases==null || library.Any(k=>k==null || k.Id==Guid.Empty || k.RevisionId==Guid.Empty) || library.Select(k=>k.Id).Distinct().Count()!=library.Length || library.Select(k=>k.RevisionId).Distinct().Count()!=library.Length)throw Invalid("EMBEDDING_INPUT_INVALID");
        if(string.IsNullOrWhiteSpace(candidate.Name) || candidate.Name.Length>100 || string.IsNullOrWhiteSpace(candidate.MeasurableBehavior) || candidate.MeasurableBehavior.Length>4000 || string.IsNullOrWhiteSpace(candidate.Boundary) || candidate.Boundary.Length>4000 || candidate.GradeMin<1 || candidate.GradeMax>12 || candidate.GradeMin>candidate.GradeMax || aliases.Length>1000 || aliases.Any(a=>a==null || a.Id==Guid.Empty || a.KCId==Guid.Empty || a.CandidateId==Guid.Empty || a.ReviewedBy==Guid.Empty || string.IsNullOrWhiteSpace(a.Text) || a.Text.Length>100 || a.Normalized!=Retrieval.Normalize(a.Text)) || aliases.Select(a=>a.Id).Distinct().Count()!=aliases.Length || aliases.Select(a=>(a.KCId,a.Normalized)).Distinct().Count()!=aliases.Length)throw Invalid("EMBEDDING_INPUT_INVALID");
        var queryText=space.Prepare(candidate.Name+" "+candidate.MeasurableBehavior+" "+candidate.Boundary);
        Check(query,"Candidate",candidateRevisionId,queryText,space);
        var index=LibraryVectors(library,space,vectors);
        var name=Retrieval.Normalize(candidate.Name);
        var matches=library.Where(k=>k.Subject==candidate.Subject && k.Type==candidate.KcType).Select(k=>new Match(k.Id,k.Name,k.Behavior,k.Boundary,Similarity(query,index[k.RevisionId],space),space.Id,aliases.Where(a=>a.KCId==k.Id && a.Normalized==name).ToArray())).ToArray();
        return BuilderLexicalRetrieval.Fuse(candidate,matches,topK);
    }
    public static Dictionary<Guid,ModelEmbeddingVector> LibraryVectors(KC[] library,ModelEmbeddingSpace space,ModelEmbeddingVector[] vectors)
    {
        space.Validate();
        if(library==null || vectors==null || library.Any(k=>k==null || k.Id==Guid.Empty || k.RevisionId==Guid.Empty || string.IsNullOrWhiteSpace(k.Name) || string.IsNullOrWhiteSpace(k.Behavior) || string.IsNullOrWhiteSpace(k.Boundary)) || library.Select(k=>k.Id).Distinct().Count()!=library.Length || library.Select(k=>k.RevisionId).Distinct().Count()!=library.Length)throw Invalid("EMBEDDING_INPUT_INVALID");
        if(vectors.Length!=library.Length || vectors.Any(v=>v==null || v.EntityType!="KC") || vectors.Select(v=>v.EntityRevisionId).Distinct().Count()!=vectors.Length)throw Invalid("EMBEDDING_INDEX_INCOMPLETE");
        var index=vectors.ToDictionary(v=>v.EntityRevisionId);
        foreach(var kc in library){if(!index.TryGetValue(kc.RevisionId,out var vector))throw Invalid("EMBEDDING_INDEX_INCOMPLETE");Check(vector,"KC",kc.RevisionId,space.Prepare(kc.Name+" "+kc.Behavior+" "+kc.Boundary),space);}
        return index;
    }
    static void Check(ModelEmbeddingVector vector,string type,Guid revision,string text,ModelEmbeddingSpace space)
    {
        if(vector==null || revision==Guid.Empty || vector.EntityType!=type || vector.EntityRevisionId!=revision || vector.Space!=space.Id || vector.Dimensions!=space.Dimensions || vector.TextHash!=Content.Hash(text))throw Invalid("EMBEDDING_SPACE_CONFLICT");Unit(vector.Vector,space.Dimensions);
    }
}
