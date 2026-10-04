using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text.RegularExpressions;
namespace Learning;

// Independent commits preserve call facts if candidate work or its transaction rolls back.
public sealed class BuilderCallTracking(Database owner,BuilderRun run,int attemptNumber,IBuilderCandidateProvider inner,JobLease? lease=null):IBuilderCandidateProvider
{
    readonly Guid executionId=Guid.NewGuid();readonly string connection=owner.Database.GetConnectionString()??throw new InvalidOperationException("Builder ledger connection unavailable");int number;
    Database Open()=>new(new DbContextOptionsBuilder<Database>().UseNpgsql(connection).Options);
    public static void ValidateUsage(BuilderUsage? usage)
    {
        if(usage==null)return;
        if(usage.InputTokens<0 || usage.OutputTokens<0 || usage.ChargedCost<0 || usage.ChargedCost>9_999_999_999.999999m || usage.ChargedCost!=null && decimal.Round(usage.ChargedCost.Value,6)!=usage.ChargedCost.Value || usage.BillingStatus is not ("Unknown" or "Confirmed" or "LocalNoCharge") || usage.Currency!=null && !Regex.IsMatch(usage.Currency,"^[A-Z]{3}$") || usage.ChargedCost!=null && usage.BillingStatus=="Unknown" || usage.BillingStatus=="Confirmed" && (usage.ChargedCost==null || usage.Currency==null) || usage.BillingStatus=="LocalNoCharge" && (usage.ChargedCost!=0 || usage.Currency!=null || usage.InputTokens!=null || usage.OutputTokens!=null))
            throw new ApiError(422,"BUILDER_USAGE_INVALID","调用用量或费用记录无效，已保留调用事实并停止处理。");
    }
    public BuilderQuote Quote(BuilderProviderRequest request)=>inner.Quote(request);
    public async Task<BuilderProviderResponse> Generate(BuilderProviderRequest request,CancellationToken ct)
    {
        var call=new BuilderCall{FamilyId=run.FamilyId,RunId=run.Id,ExecutionId=executionId,RetryRound=run.RetryRound,AttemptNumber=attemptNumber,CallNumber=++number,Repair=request.InvalidOutput!=null,Provider=run.Provider,Model=run.Model,InputHash=run.InputHash,ModelConfigHash=run.ModelConfigHash};
        await using(var started=Open()){var denied=await BuilderBudget.Reserve(started,call,inner.Quote(request),ct,lease);if(denied!=null)throw new ApiError(422,denied,"调用预算或并发名额不足，请查看调用账本并核对未确认调用。");}
        var watch=Stopwatch.StartNew();
        try
        {
            var response=await inner.Generate(request,ct).WaitAsync(ct);ValidateUsage(response.Usage);
            await Finish(call.Id,"Returned",response,null,watch.ElapsedMilliseconds);return response;
        }
        catch(Exception ex)
        {
            await Finish(call.Id,ex is OperationCanceledException?"Cancelled":"Failed",null,ex is ApiError api?api.Code:ex is OperationCanceledException?"CALL_CANCELLED":"PROVIDER_CALL_FAILED",watch.ElapsedMilliseconds);throw;
        }
    }
    async Task Finish(Guid id,string status,BuilderProviderResponse? response,string? code,long elapsed)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));await using var db=Open();await using var tx=await db.Database.BeginTransactionAsync(deadline.Token);await BuilderBudget.Lock(db,run.FamilyId,deadline.Token);var call=await db.Set<BuilderCall>().SingleOrDefaultAsync(c=>c.Id==id && c.FamilyId==run.FamilyId,deadline.Token);if(call==null || call.Status!="Started")return;
        call.Status=status;call.FinishedAt=DateTimeOffset.UtcNow;call.ElapsedMilliseconds=elapsed;call.ErrorCode=code;
        if(response!=null){call.OutputHash=response.Output==null?null:Content.Hash(response.Output);call.InputTokens=response.Usage?.InputTokens;call.OutputTokens=response.Usage?.OutputTokens;call.ChargedCost=response.Usage?.ChargedCost;call.Currency=response.Usage?.Currency;call.BillingStatus=response.Usage?.BillingStatus??"Unknown";}
        call.BudgetState="Unresolved";
        if(response?.Usage is {} usage)
        {
            var quote=Json.Read<BuilderQuote>(call.QuotePayload!);var used=usage.InputTokens==null || usage.OutputTokens==null?(decimal?)null:(decimal)usage.InputTokens+usage.OutputTokens;
            if(usage.ChargedCost>quote.MaxCost || used>quote.MaxTokens || usage.BillingStatus=="Confirmed" && usage.Currency!=quote.Currency)call.BudgetState="Overrun";
            else if(usage.BillingStatus=="LocalNoCharge" || usage.BillingStatus=="Confirmed" && used!=null)call.BudgetState="Settled";
        }
        await db.SaveChangesAsync(deadline.Token);await tx.CommitAsync(deadline.Token);
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/builder/calls/window",async(int? pageSize,Guid? runId,string? cursor,Database db,HttpContext ctx)=>
        {
            var actor=ctx.Actor();actor.Require("ContentEditor");var size=pageSize??20;var ct=ctx.RequestAborted;
            if(size<1 || size>50)throw new ApiError(422,"INVALID_PAGE","每页请使用1～50条。");
            CallCursor? position=null;
            if(cursor!=null)
            {
                try
                {
                    if(cursor.Length>1024)throw new FormatException();
                    position=Json.Read<CallCursor>(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor)));
                    if(position==null || position.Version!=1 || position.FamilyId!=actor.FamilyId || position.RunId!=runId || position.PageSize!=size || position.Id==Guid.Empty || position.CreatedAt.Offset!=TimeSpan.Zero || position.CreatedAt==default)throw new FormatException();
                }
                catch(Exception ex) when(ex is FormatException or System.Text.Json.JsonException or ArgumentException)
                {throw new ApiError(422,"INVALID_CURSOR","翻页位置无效，请刷新账本后重试。");}
            }
            await using var snapshot=await ReadSnapshot.Begin(db,ctx);
            if(runId!=null && !await db.BuilderRuns.AnyAsync(r=>r.Id==runId && r.FamilyId==actor.FamilyId,ct))throw new ApiError(404,"NOT_FOUND","找不到本家庭任务。");
            var query=db.Set<BuilderCall>().AsNoTracking().Where(c=>c.FamilyId==actor.FamilyId && (runId==null || c.RunId==runId));var total=await query.CountAsync(ct);
            if(position!=null){var time=position.CreatedAt;var id=position.Id;query=query.Where(c=>c.CreatedAt<time || c.CreatedAt==time && c.Id.CompareTo(id)>0);}
            var window=await query.OrderByDescending(c=>c.CreatedAt).ThenBy(c=>c.Id).Take(size+1).ToArrayAsync(ct);
            var calls=window.Take(size).ToArray();string? nextCursor=null;
            if(window.Length>size){var last=calls[^1];nextCursor=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Json.Write(new CallCursor(1,actor.FamilyId,runId,size,last.CreatedAt,last.Id))));}
            var ids=calls.Select(c=>c.Id).ToArray();var reconciliations=await db.Set<BuilderBudgetReconciliation>().AsNoTracking().Where(r=>r.FamilyId==actor.FamilyId && ids.Contains(r.CallId)).ToArrayAsync(ct);
            await snapshot.CommitAsync(ct);return new{pageSize=size,total,calls,reconciliations,nextCursor};
        }).WithMetadata(new OptionalResponseFieldsMetadata("reconciliations","nextCursor"));
        api.MapGet("/builder/calls",async(int? page,int? pageSize,Guid? runId,Database db,HttpContext ctx)=>
        {
            var actor=ctx.Actor();actor.Require("ContentEditor");var p=page??1;var size=pageSize??20;if(p<1 || size<1 || size>50 || p>100_000)throw new ApiError(422,"INVALID_PAGE","请使用有效页码及每页1～50条。");
            if(runId!=null && !await db.BuilderRuns.AnyAsync(r=>r.Id==runId && r.FamilyId==actor.FamilyId))throw new ApiError(404,"NOT_FOUND","找不到本家庭任务。");
            var query=db.Set<BuilderCall>().Where(c=>c.FamilyId==actor.FamilyId && (runId==null || c.RunId==runId));var total=await query.CountAsync();var calls=await query.OrderByDescending(c=>c.StartedAt).ThenBy(c=>c.Id).Skip((p-1)*size).Take(size).ToArrayAsync();var ids=calls.Select(c=>c.Id).ToArray();var reconciliations=await db.Set<BuilderBudgetReconciliation>().Where(r=>r.FamilyId==actor.FamilyId && ids.Contains(r.CallId)).ToArrayAsync();return new{page=p,pageSize=size,total,calls,reconciliations};
        }).WithMetadata(new OptionalResponseFieldsMetadata("reconciliations"));
    }
}

public record CallCursor(int Version,Guid FamilyId,Guid? RunId,int PageSize,DateTimeOffset CreatedAt,Guid Id);
