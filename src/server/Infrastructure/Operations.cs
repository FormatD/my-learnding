using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace Learning;
public record OperationSignal(string Code,string Severity,string Title,int Count,string Guidance);
public sealed class RequestMetrics
{
    sealed class Minute { public long Total,Errors,Rejected; }
    readonly ConcurrentDictionary<(Guid Family,long Minute),Minute> buckets=new();
    public DateTimeOffset StartedAt { get; }=DateTimeOffset.UtcNow;
    public void Record(Guid family,int status)
    {
        var minute=DateTimeOffset.UtcNow.ToUnixTimeSeconds()/60;var bucket=buckets.GetOrAdd((family,minute),_=>new());Interlocked.Increment(ref bucket.Total);
        if(status>=500)Interlocked.Increment(ref bucket.Errors);else if(status>=400)Interlocked.Increment(ref bucket.Rejected);
        foreach(var key in buckets.Keys.Where(k=>k.Minute<minute-14))buckets.TryRemove(key,out _);
    }
    public object Read(Guid family)
    {
        var minute=DateTimeOffset.UtcNow.ToUnixTimeSeconds()/60;var current=buckets.Where(k=>k.Key.Family==family && k.Key.Minute>=minute-14).Select(k=>k.Value).ToArray();
        var total=current.Sum(m=>Interlocked.Read(ref m.Total));var errors=current.Sum(m=>Interlocked.Read(ref m.Errors));
        return new{startedAt=StartedAt,windowMinutes=15,requests=total,serverErrors=errors,rejected=current.Sum(m=>Interlocked.Read(ref m.Rejected)),errorRate=total==0?(double?)null:(double)errors/total,notice="仅统计本次服务启动后的家庭认证接口请求；重启后重新计数，空窗口不推定正常。"};
    }
}
public record StorageStatus(bool Available,long? TotalBytes,long? FreeBytes,double? FreeFraction,bool Low,string Notice);
public sealed class StorageProbe(string dataRoot)
{
    public StorageStatus Read()
    {
        try{var drive=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dataRoot))!);var total=drive.TotalSize;var free=drive.AvailableFreeSpace;var fraction=total==0?0:(double)free/total;return new(true,total,free,fraction,free<1_073_741_824 || fraction<.05,"检查本地应用数据所在文件系统；低于1GB或5%可用空间时提示。未检查独立备份磁盘。");}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){return new(false,null,null,null,false,"暂时无法读取应用数据所在文件系统的容量。");}
    }
}
public static class Operations
{
    public static async Task<OperationSignal[]> Signals(Database db,Guid family,DateTimeOffset now,CancellationToken ct=default)
    {
        var late=await db.Outbox.CountAsync(j=>j.FamilyId==family && j.ProcessedAt==null && j.CreatedAt<now.AddSeconds(-30),ct);
        var failed=await db.Outbox.CountAsync(j=>j.FamilyId==family && j.ProcessedAt==null && j.Retries>=3,ct);
        var builderFailed=await db.BuilderRuns.CountAsync(r=>r.FamilyId==family && r.Status=="Failed",ct);
        var builderLong=await db.BuilderRuns.CountAsync(r=>r.FamilyId==family && r.Status=="Queued" && r.CreatedAt<now.AddMinutes(-2),ct);
        var signals=new List<OperationSignal>();
        if(late>0)signals.Add(new("ProjectionDelayed","Warning","学习结果更新超过30秒",late,"原始作答已经保存。查看待处理结果；请勿用新的作答标识重复提交。"));
        if(failed>0)signals.Add(new("ProjectionFailed","Error","学习结果更新需要人工处理",failed,"自动重试已停止。确认本地服务和数据库正常后重新处理，或在证据页重建结果。"));
        if(builderFailed>0)signals.Add(new("BuilderFailed","Warning","辅助建库有失败记录",builderFailed,"在辅助建库查看来源和失败原因；历史失败记录保留，不代表已发布学习内容不可用。"));
        if(builderLong>0)signals.Add(new("BuilderDelayed","Warning","辅助建库等待超过两分钟",builderLong,"检查后台与来源解析状态，已发布学习内容仍可使用。"));
        return signals.ToArray();
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/operations",async(Database db,HttpContext ctx,RequestMetrics metrics,StorageProbe storage)=>
        {
            var a=ctx.Actor();a.Require("Parent");var now=DateTimeOffset.UtcNow;var editor=a.Can("ContentEditor");
            var pending=await db.Outbox.Where(j=>j.FamilyId==a.FamilyId && j.ProcessedAt==null).OrderBy(j=>j.CreatedAt).ToArrayAsync();
            var names=await db.Students.Where(s=>s.FamilyId==a.FamilyId).ToDictionaryAsync(s=>s.Id,s=>s.Name);
            var signals=(await Signals(db,a.FamilyId,now)).Where(s=>editor || !s.Code.StartsWith("Builder")).ToList();
            var owner=(await db.Families.SingleAsync(f=>f.Id==a.FamilyId)).OwnerAccountId==a.AccountId;var disk=owner?storage.Read():null;if(disk?.Low==true)signals.Add(new("StorageLow","Warning","本地存储空间不足",1,"请检查本地存储容量与备份位置；不要直接删除数据库或私有附件。"));
            return new{observedAt=now,signals,storage=disk,requests=metrics.Read(a.FamilyId),projection=new{pending=pending.Length,failed=pending.Count(j=>j.Retries>=3),oldestSeconds=pending.Length==0?(double?)null:Math.Max(0,(now-pending[0].CreatedAt).TotalSeconds),jobs=pending.Take(100).Select(j=>new{j.Id,j.StudentId,student=names.GetValueOrDefault(j.StudentId,"学生"),j.CreatedAt,j.Retries,j.NextAttemptAt,errorCode=j.Error,canRetry=j.Retries>=3})},builder=editor?new{queued=await db.BuilderRuns.CountAsync(r=>r.FamilyId==a.FamilyId&&r.Status=="Queued"),failed=await db.BuilderRuns.CountAsync(r=>r.FamilyId==a.FamilyId&&r.Status=="Failed"),needsOCR=await db.BuilderRuns.CountAsync(r=>r.FamilyId==a.FamilyId&&r.Status=="NeedsOCR"),unreviewed=await db.Candidates.CountAsync(c=>c.FamilyId==a.FamilyId&&c.Status=="Pending"),drafts=await db.Drafts.CountAsync(d=>d.FamilyId==a.FamilyId&&d.Status!="Published")} : null,backup=new{verified=false,notice="尚未配置并验收每日加密备份、独立磁盘与30天保留；手工导出不能替代完整备份。"},model=new{connected=false,notice="外部模型按当前安排尚未接入；已发布内容可以继续学习。"}};
        });
    }
}
// Independent of the projection worker, so stalled processing can still be reported.
public sealed class OperationsMonitor(IServiceScopeFactory scopes,ILogger<OperationsMonitor> logger,StorageProbe storage):BackgroundService
{
    readonly Dictionary<(Guid Family,string Code),int> active=new();
    bool? storageLow;
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<Database>();var now=DateTimeOffset.UtcNow;var observed=new Dictionary<(Guid Family,string Code),int>();
                var disk=storage.Read();if(disk.Available && storageLow!=disk.Low){if(disk.Low)logger.LogWarning("Operational alert StorageLow FreeBytes {FreeBytes}",disk.FreeBytes);else if(storageLow==true)logger.LogInformation("Operational alert resolved StorageLow");storageLow=disk.Low;}
                var families=await db.Outbox.Where(j=>j.ProcessedAt==null).Select(j=>j.FamilyId).Union(db.BuilderRuns.Where(r=>r.Status=="Failed"||r.Status=="Queued").Select(r=>r.FamilyId)).Distinct().ToArrayAsync(ct);
                foreach(var family in families)foreach(var signal in await Operations.Signals(db,family,now,ct))
                {
                    var key=(family,signal.Code);observed[key]=signal.Count;
                    if(!active.TryGetValue(key,out var count)||count!=signal.Count)logger.LogWarning("Operational alert {Code} Family {FamilyId} Count {Count}",signal.Code,family,signal.Count);
                }
                foreach(var key in active.Keys.Except(observed.Keys))logger.LogInformation("Operational alert resolved {Code} Family {FamilyId}",key.Code,key.Family);
                active.Clear();foreach(var row in observed)active.Add(row.Key,row.Value);
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Operational monitoring unavailable; inspect database connectivity");}
            try{await Task.Delay(5000,ct);}catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
        }
    }
}
