using Learning;
using Microsoft.EntityFrameworkCore;
using Npgsql;
public static class ModelEmbeddingIndexCases
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static async Task Reject(Func<Task> action,string code){try{await action();throw new Exception("Accepted invalid index: "+code);}catch(ApiError e){Check(e.Code==code,"Wrong rejection: "+e.Code+" expected "+code);}}
    public static async Task Run(Database db)
    {
        await db.Database.MigrateAsync();
        var family=new Family();var other=new Family();var catalog=Content.Fixture();var payload=Json.Write(catalog);var release=new Release{FamilyId=family.Id,Payload=payload,Hash=Content.Hash(payload),Number=1};var second=new Release{FamilyId=family.Id,Payload=payload,Hash=release.Hash,Number=2};db.AddRange(family,other,release,second);await db.SaveChangesAsync();
        var space=new ModelEmbeddingSpace("LocalOmlx","controlled-index-fixture","sha256:"+new string('a',64),3,ModelEmbeddingSpace.Preprocessing);var config=new LocalEmbeddingConfiguration("embedding-local/1","http://127.0.0.1:8000/v1",space,10000);
        var vectors=catalog.Kcs.Select(k=>new ModelEmbeddingVector(k.RevisionId,"KC",Content.Hash(space.Prepare(k.Name+" "+k.Behavior+" "+k.Boundary)),space.Id,3,[3,4,0])).ToArray();
        async Task<ModelEmbeddingIndex> Save(LocalEmbeddingConfiguration configuration,ModelEmbeddingVector[] values,Guid? owner=null)
        {
            db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync();await db.Lock(owner??family.Id);var index=await ModelEmbeddingIndexes.Save(db,owner??family.Id,release.Id,configuration,values);await db.SaveChangesAsync();await tx.CommitAsync();return index;
        }
        var index=await Save(config,vectors.Reverse().ToArray());Check((await Save(config,vectors)).Id==index.Id,"Order changed dedupe identity");
        async Task<Guid> ConcurrentSave()
        {
            await using var writer=new Database(new DbContextOptionsBuilder<Database>().UseNpgsql(db.Database.GetConnectionString()).Options);await using var tx=await writer.Database.BeginTransactionAsync();await writer.Lock(family.Id);var same=await ModelEmbeddingIndexes.Save(writer,family.Id,release.Id,config,vectors);await writer.SaveChangesAsync();await tx.CommitAsync();return same.Id;
        }
        Check((await Task.WhenAll(ConcurrentSave(),ConcurrentSave())).All(id=>id==index.Id),"Concurrent frozen index saves duplicated snapshot");
        await using(var fresh=new Database(new DbContextOptionsBuilder<Database>().UseNpgsql(db.Database.GetConnectionString()).Options))
        {
            var read=await ModelEmbeddingIndexes.Read(fresh,family.Id,index.Id,release.Id,space);Check(read.Library.Length==catalog.Kcs.Length && read.Vectors.Length==catalog.Kcs.Length && read.Configuration==config && Math.Abs(read.Vectors[0].Vector[0]-.6)<1e-10,"Actual reload lost complete frozen normalized vectors");
        }
        await Reject(()=>Save(config,vectors.Skip(1).ToArray()),"EMBEDDING_INDEX_INCOMPLETE");
        await Reject(()=>Save(config,[..vectors.Take(vectors.Length-1),vectors[0]]),"EMBEDDING_INDEX_INCOMPLETE");
        await Reject(()=>Save(config,[vectors[0] with{TextHash=new string('b',64)},..vectors.Skip(1)]),"EMBEDDING_SPACE_CONFLICT");
        await Reject(()=>Save(config,[vectors[0] with{EntityRevisionId=Guid.NewGuid()},..vectors.Skip(1)]),"EMBEDDING_INDEX_INCOMPLETE");
        await Reject(()=>Save(config,[vectors[0] with{Vector=[0,0,0]},..vectors.Skip(1)]),"EMBEDDING_VECTOR_INVALID");
        await Reject(()=>Save(config,[vectors[0] with{Space=Retrieval.Space},..vectors.Skip(1)]),"EMBEDDING_SPACE_CONFLICT");
        await Reject(()=>Save(config with{Space=space with{ModelVersion="unverified-name"}},vectors),"EMBEDDING_ARTIFACT_DIGEST_REQUIRED");
        await Reject(()=>Save(config,vectors,other.Id),"NOT_FOUND");
        await Reject(()=>ModelEmbeddingIndexes.Read(db,other.Id,index.Id,release.Id,space),"NOT_FOUND");
        await Reject(()=>ModelEmbeddingIndexes.Read(db,family.Id,index.Id,second.Id,space),"EMBEDDING_INDEX_CONFLICT");
        await Reject(()=>ModelEmbeddingIndexes.Read(db,family.Id,index.Id,release.Id,space with{Dimensions=4}),"EMBEDDING_INDEX_CONFLICT");
        await Reject(()=>Save(config with{TimeoutMilliseconds=20000},vectors),"EMBEDDING_INDEX_CONFLICT");
        await Reject(()=>Save(config,vectors.Select(v=>v with{Vector=[1,0,0]}).ToArray()),"EMBEDDING_INDEX_CONFLICT");
        var upgraded=space with{ModelVersion="sha256:"+new string('b',64)};var newer=await Save(config with{Space=upgraded},vectors.Select(v=>v with{Space=upgraded.Id}).ToArray());Check(newer.Id!=index.Id,"Model upgrade overwrote old space");
        Check((await ModelEmbeddingIndexes.Read(db,family.Id,index.Id,release.Id,space)).Configuration.Space==space,"Old space unavailable after upgrade");
        await Reject(()=>ModelEmbeddingIndexes.Read(db,family.Id,index.Id,release.Id,upgraded),"EMBEDDING_INDEX_CONFLICT");
        db.ChangeTracker.Clear();await using(var tx=await db.Database.BeginTransactionAsync())
        {
            await db.Lock(family.Id);var rolled=await ModelEmbeddingIndexes.Save(db,family.Id,second.Id,config,vectors);await db.SaveChangesAsync();Check(await db.Set<ModelEmbeddingIndex>().AnyAsync(i=>i.Id==rolled.Id),"Rollback fixture not saved in transaction");await tx.RollbackAsync();
        }
        db.ChangeTracker.Clear();Check(!await db.Set<ModelEmbeddingIndex>().AnyAsync(i=>i.LibraryReleaseId==second.Id),"Rolled-back index leaked");
        try{await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ModelEmbeddingIndex\" SET \"Count\"=\"Count\" WHERE \"Id\"={index.Id}");throw new Exception("Immutable index accepted update");}catch(PostgresException e){Check(e.SqlState=="23514","Wrong immutability rejection");}
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ModelEmbeddingIndex\" (\"Id\",\"FamilyId\",\"LibraryReleaseId\",\"LibraryHash\",\"SpaceId\",\"ConfigurationPayload\",\"LibraryPayload\",\"VectorsPayload\",\"SnapshotHash\",\"Count\",\"CreatedAt\") SELECT {Guid.NewGuid()},{other.Id},\"LibraryReleaseId\",\"LibraryHash\",\"SpaceId\",\"ConfigurationPayload\",\"LibraryPayload\",\"VectorsPayload\",\"SnapshotHash\",\"Count\",\"CreatedAt\" FROM \"ModelEmbeddingIndex\" WHERE \"Id\"={index.Id}");throw new Exception("Cross-family release foreign key accepted");
        }
        catch(PostgresException e){Check(e.SqlState=="23503","Wrong family foreign key rejection");}
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Releases\" SET \"Withdrawn\"=TRUE WHERE \"Id\"={release.Id}");await Reject(()=>ModelEmbeddingIndexes.Read(db,family.Id,index.Id,release.Id,space),"EMBEDDING_INDEX_CONFLICT");
        Check(await db.Set<ModelEmbeddingIndex>().CountAsync(i=>i.FamilyId==family.Id)==2,"Invalid or partial snapshots persisted");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Families\" WHERE \"Id\"={family.Id}");Check(!await db.Set<ModelEmbeddingIndex>().AnyAsync(i=>i.FamilyId==family.Id),"Family cleanup left vectors");
        Console.WriteLine("PASS actual PostgreSQL immutable complete embedding snapshot/reload, normalized vectors, exact revision/text/library binding, family isolation, rollback, idempotency, conflicting rewrites rejected, digest-declared version upgrade isolation, withdrawal and family cleanup; controlled vectors only, no actual model/artifact/quality claim");
    }
}
