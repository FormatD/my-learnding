using System.Text;
using Microsoft.EntityFrameworkCore;
namespace Learning;

// Storage foundation only. No endpoint or worker accepts untracked provider results.
// The artifact digest is a frozen declaration; actual artifact verification and call ledger
// must be connected before production recall may select these snapshots.
public class ModelEmbeddingIndex:Row
{
    public Guid LibraryReleaseId {get;set;}
    public string LibraryHash {get;set;}="";
    public string SpaceId {get;set;}="";
    public string ConfigurationPayload {get;set;}="";
    public string LibraryPayload {get;set;}="";
    public string VectorsPayload {get;set;}="";
    public string SnapshotHash {get;set;}="";
    public int Count {get;set;}
}
public record ModelEmbeddingIndexSnapshot(LocalEmbeddingConfiguration Configuration,KC[] Library,ModelEmbeddingVector[] Vectors);
public static class ModelEmbeddingIndexes
{
    public const int MaxKcs=512,MaxBytes=32_000_000;
    static ApiError Conflict()=>new(422,"EMBEDDING_INDEX_CONFLICT","向量索引与原发布、固定模型空间或完整能力修订不一致。");
    static string Hash(ModelEmbeddingIndex row)=>Content.Hash(Json.Write(new{Version="embedding-index/1",row.FamilyId,row.LibraryReleaseId,row.LibraryHash,row.SpaceId,row.ConfigurationPayload,row.LibraryPayload,row.VectorsPayload,row.Count}));
    static void Configuration(LocalEmbeddingConfiguration configuration)
    {
        configuration.Validate();var version=configuration.Space.ModelVersion;
        if(version.Length!=71 || !version.StartsWith("sha256:",StringComparison.Ordinal) || version[7..].Any(c=>!(c is >= '0' and <= '9' or >= 'a' and <= 'f')))throw new ApiError(422,"EMBEDDING_ARTIFACT_DIGEST_REQUIRED","持久索引须固定模型文件清单的 SHA-256 摘要，不能仅用显示名称作为版本。");
    }
    static KC[] Library(Release release)
    {
        if(release.Withdrawn || Content.Hash(release.Payload)!=release.Hash)throw Conflict();
        var library=Json.Read<Catalog>(release.Payload).Kcs;
        if(library==null || library.Length is <1 or >MaxKcs)throw new ApiError(422,"EMBEDDING_INDEX_LIMIT","持久向量索引须包含 1 至 512 个能力。");
        return library.OrderBy(k=>k.RevisionId).ToArray();
    }
    // Caller owns the family transaction/lock; inserts a whole immutable snapshot atomically.
    // No incomplete building row can be mistaken for a usable index.
    public static async Task<ModelEmbeddingIndex> Save(Database db,Guid familyId,Guid releaseId,LocalEmbeddingConfiguration configuration,ModelEmbeddingVector[] vectors,CancellationToken ct=default)
    {
        if(db.Database.CurrentTransaction==null)throw new InvalidOperationException("Index save requires caller family transaction and lock");
        Configuration(configuration);
        var release=await db.Releases.AsNoTracking().SingleOrDefaultAsync(r=>r.Id==releaseId && r.FamilyId==familyId,ct)??throw new ApiError(404,"NOT_FOUND","原能力库发布不存在。");
        var library=Library(release);ModelEmbeddings.LibraryVectors(library,configuration.Space,vectors);
        var row=new ModelEmbeddingIndex{FamilyId=familyId,LibraryReleaseId=releaseId,LibraryHash=release.Hash,SpaceId=configuration.Space.Id,ConfigurationPayload=Json.Write(configuration),LibraryPayload=Json.Write(library),VectorsPayload=Json.Write(vectors.OrderBy(v=>v.EntityRevisionId).Select(v=>v with{Vector=ModelEmbeddings.Unit(v.Vector,v.Dimensions)}).ToArray()),Count=library.Length};
        if(Encoding.UTF8.GetByteCount(row.ConfigurationPayload)+Encoding.UTF8.GetByteCount(row.LibraryPayload)+Encoding.UTF8.GetByteCount(row.VectorsPayload)>MaxBytes)throw new ApiError(422,"EMBEDDING_INDEX_LIMIT","完整向量索引超出保存上限。");
        row.SnapshotHash=Hash(row);
        var existing=await db.Set<ModelEmbeddingIndex>().AsNoTracking().SingleOrDefaultAsync(i=>i.FamilyId==familyId && i.LibraryReleaseId==releaseId && i.SpaceId==row.SpaceId,ct);
        if(existing!=null){if(existing.SnapshotHash!=row.SnapshotHash || Hash(existing)!=existing.SnapshotHash)throw Conflict();return existing;}
        db.Add(row);return row;
    }
    public static async Task<ModelEmbeddingIndexSnapshot> Read(Database db,Guid familyId,Guid indexId,Guid expectedReleaseId,ModelEmbeddingSpace expectedSpace,CancellationToken ct=default)
    {
        expectedSpace.Validate();
        // One statement reads the index and current release withdrawal/hash state together.
        var pair=await (from i in db.Set<ModelEmbeddingIndex>().AsNoTracking() join r in db.Releases.AsNoTracking() on new{Id=i.LibraryReleaseId,i.FamilyId} equals new{r.Id,r.FamilyId} where i.Id==indexId && i.FamilyId==familyId select new{Index=i,Release=r}).SingleOrDefaultAsync(ct)??throw new ApiError(404,"NOT_FOUND","向量索引不存在。");
        var row=pair.Index;
        if(row.LibraryReleaseId!=expectedReleaseId || row.SpaceId!=expectedSpace.Id || row.LibraryHash!=pair.Release.Hash || Hash(row)!=row.SnapshotHash)throw Conflict();
        var configuration=Json.Read<LocalEmbeddingConfiguration>(row.ConfigurationPayload);Configuration(configuration);
        if(configuration.Space!=expectedSpace)throw Conflict();
        var library=Library(pair.Release);if(Json.Write(library)!=row.LibraryPayload || row.Count!=library.Length)throw Conflict();
        var vectors=Json.Read<ModelEmbeddingVector[]>(row.VectorsPayload);ModelEmbeddings.LibraryVectors(library,expectedSpace,vectors);
        return new(configuration,library,vectors);
    }
}
