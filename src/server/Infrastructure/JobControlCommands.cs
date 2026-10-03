using Microsoft.EntityFrameworkCore;
namespace Learning;
// Only explicit job-control endpoints bypass the long-running family work lock.
public sealed record JobControlCommandMetadata;
public static class JobControlCommands
{
    public static async Task Execute(HttpContext ctx,Func<Task> next,Database db,Actor actor,string key,string scope,string hash)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ctx.RequestAborted);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"job-control:"+actor.FamilyId+":"+actor.Id+":"+scope+":"+key},0))",ctx.RequestAborted);
        var sessions=await db.AuthSessions.FromSqlInterpolated($"SELECT * FROM \"AuthSessions\" WHERE \"Id\"={actor.SessionId} AND NOT \"Revoked\" AND \"ExpiresAt\">clock_timestamp() FOR SHARE").AsNoTracking().ToArrayAsync(ctx.RequestAborted);
        if(sessions.Length!=1)throw new ApiError(401,"LOGIN_REQUIRED","权限或登录已变化，请重新登录。");
        var memberships=await db.Set<FamilyMembership>().FromSqlInterpolated($"SELECT * FROM \"FamilyMembership\" WHERE \"FamilyId\"={actor.FamilyId} AND \"AccountId\"={sessions[0].AccountId??Guid.Empty} FOR SHARE").AsNoTracking().ToArrayAsync(ctx.RequestAborted);
        actor=actor with{Roles=memberships.SingleOrDefault()?.Roles??""};ctx.Items["actor"]=actor;
        // Recheck endpoint permission even for a cached response.
        if(actor.Role=="Child" || !actor.Can("Parent") && !actor.Can("ContentEditor"))throw new ApiError(403,"FORBIDDEN","需要家长或内容维护权限。");
        var jobId=Guid.Parse(ctx.Request.RouteValues["id"]!.ToString()!);var job=await db.Set<BackgroundJob>().AsNoTracking().SingleOrDefaultAsync(j=>j.Id==jobId && j.FamilyId==actor.FamilyId,ctx.RequestAborted)??throw new ApiError(404,"NOT_FOUND","找不到本家庭任务。");JobCancellation.Authorize(actor,job);
        var cached=await db.Commands.SingleOrDefaultAsync(c=>c.FamilyId==actor.FamilyId && c.ActorId==actor.Id && c.Scope==scope && c.Key==key,ctx.RequestAborted);
        if(cached!=null){if(cached.Hash!=hash)throw new ApiError(409,"IDEMPOTENCY_CONFLICT","同一请求标识不能提交不同内容。");ctx.Response.StatusCode=cached.StatusCode;ctx.Response.ContentType="application/json";await ctx.Response.WriteAsync(cached.Response);return;}
        var response=ctx.Response.Body;await using var buffer=new MemoryStream();ctx.Response.Body=buffer;
        try
        {
            await next();if(ctx.Response.StatusCode<400)
            {
                await db.SaveChangesAsync(ctx.RequestAborted);buffer.Position=0;var result=await new StreamReader(buffer,leaveOpen:true).ReadToEndAsync();
                db.Commands.Add(new(){FamilyId=actor.FamilyId,ActorId=actor.Id,Scope=scope,Key=key,Hash=hash,Response=result,StatusCode=ctx.Response.StatusCode});
                await db.SaveChangesAsync(ctx.RequestAborted);await tx.CommitAsync(ctx.RequestAborted);
            }
            buffer.Position=0;await buffer.CopyToAsync(response,ctx.RequestAborted);
        }
        finally{ctx.Response.Body=response;}
    }
}
