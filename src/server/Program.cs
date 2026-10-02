using Learning;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;

var builder=WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options=>options.Limits.MaxRequestBodySize=ApiPolicy.MaxRequestBytes);
builder.Services.Configure<RouteHandlerOptions>(options=>options.ThrowOnBadRequest=true);
builder.Services.AddDbContext<Database>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Learning") ?? $"Host=127.0.0.1;Port=55432;Database=learning;Username={Environment.UserName}"));
builder.Services.AddOpenApi(OpenApiResponses.Configure);
var privateRoot=Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath,"../../.local"));
var keyRoot=Path.Combine(privateRoot,"keys");Directory.CreateDirectory(keyRoot);
if(!OperatingSystem.IsWindows())
{
    var privateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute;
    File.SetUnixFileMode(privateRoot,privateMode);File.SetUnixFileMode(keyRoot,privateMode);
}
builder.Configuration["DeletionLedger"]??=Path.Combine(privateRoot,"deleted-students.txt");
builder.Configuration["FamilyDeletionLedger"]??=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(builder.Configuration["DeletionLedger"]!))!,"deleted-families.txt");
builder.Configuration["ExportDirectory"]??=Path.Combine(privateRoot,"exports");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(builder.Configuration["FamilyDeletionLedger"]!))!);
using(var ledger=new FileStream(builder.Configuration["FamilyDeletionLedger"]!,FileMode.OpenOrCreate,FileAccess.Write,FileShare.Read)){}
builder.Services.AddHostedService<ExportCleanup>();
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyRoot)).SetApplicationName("FamilyLearning");
builder.Services.AddHostedService<ProjectionWorker>();
builder.Services.AddSingleton<RequestMetrics>();
builder.Services.AddSingleton(new StorageProbe(privateRoot));
builder.Services.AddSingleton(new BackupProbe(builder.Configuration["BackupConfigFile"],Path.Combine(privateRoot,"../scripts/daily_backup.py"),builder.Configuration.GetConnectionString("Learning")??$"Host=127.0.0.1;Port=55432;Database=learning;Username={Environment.UserName}"));
builder.Services.AddHostedService<OperationsMonitor>();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode=StatusCodes.Status429TooManyRequests;
    o.OnRejected=async(context,ct)=>await ApiProblems.Write(context.HttpContext,429,"登录尝试过于频繁，请稍后再试。","RATE_LIMITED");
    o.AddPolicy(ApiPolicy.AuthRatePolicy,ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString()??"local",_ => new() { PermitLimit=10,Window=TimeSpan.FromMinutes(1),QueueLimit=0 }));
});
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy=System.Text.Json.JsonNamingPolicy.CamelCase);
var app=builder.Build();
await using (var scope=app.Services.CreateAsyncScope())
{
    var db=scope.ServiceProvider.GetRequiredService<Database>();
    await db.Database.MigrateAsync();
    // Register snapshots created before the normalized revision registry migration.
    foreach (var release in await db.Releases.Where(r=>!db.Set<ReleaseItem>().Any(i=>i.ReleaseId==r.Id)).ToListAsync())
    {
        await using var transaction=await db.Database.BeginTransactionAsync();await db.Lock(release.FamilyId);
        if(!await db.Set<ReleaseItem>().AnyAsync(i=>i.ReleaseId==release.Id)){await Publishing.Register(db,release);await db.SaveChangesAsync();}
        await transaction.CommitAsync();
    }
}
app.Use(async(ctx,next)=>
{
    try{await next();}finally{if(ctx.Items["actor"] is Actor actor)ctx.RequestServices.GetRequiredService<RequestMetrics>().Record(actor.FamilyId,ctx.Response.StatusCode);}
});
app.Use(async (ctx,next) =>
{
    try { await next(); }
    catch (ApiError ex)
    { await ApiProblems.Write(ctx,ex.Status,ex.Message,ex.Code); }
    catch(BadHttpRequestException ex)
    {await ApiProblems.Write(ctx,ex.StatusCode,"请求格式或大小无效。","INVALID_REQUEST");}
    catch (Exception ex)
    {
        app.Logger.LogError(ex,"Request failed {TraceId}",ctx.TraceIdentifier);
        await ApiProblems.Write(ctx,500,"暂时无法完成，请稍后重试。","SERVER_ERROR");
    }
});
app.UseStatusCodePages(async context=>
{
    var ctx=context.HttpContext;
    if(!ctx.Request.Path.StartsWithSegments("/api"))return;
    var status=ctx.Response.StatusCode;
    var code=status switch{404=>"NOT_FOUND",405=>"METHOD_NOT_ALLOWED",413=>"TOO_LARGE",_=>"INVALID_REQUEST"};
    await ApiProblems.Write(ctx,status,"接口路径、方法或请求格式无效。",code);
});
app.UseRateLimiter();
app.Use(async (ctx,next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"]="nosniff";
    ctx.Response.Headers["Referrer-Policy"]="same-origin";
    ctx.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'";
    if (!ctx.Request.Path.StartsWithSegments("/api")) { await next(); return; }
    ctx.Response.Headers.CacheControl="no-store";
    if (ctx.Request.ContentLength>ApiPolicy.MaxRequestBytes) throw new ApiError(413,"TOO_LARGE","请求内容过大。");
    if (!ApiPolicy.IsRead(ctx.Request.Method))
    {
        var origin=ctx.Request.Headers[ApiPolicy.OriginHeader].ToString();
        if (origin!="" && (!Uri.TryCreate(origin,UriKind.Absolute,out var uri) || uri.Authority!=ctx.Request.Host.Value || uri.Scheme!=ctx.Request.Scheme)) throw new ApiError(403,"ORIGIN_DENIED","请从本应用提交。");
        if (ctx.Request.Headers[ApiPolicy.CsrfHeader]!="1") throw new ApiError(403,"CSRF_DENIED","请求缺少校验标记。");
    }
    // Do not serve the SPA shell for an API route which failed to match.
    // Keep routing's media/method rejection endpoints intact (415/405).
    if(ctx.GetEndpoint() is Microsoft.AspNetCore.Routing.RouteEndpoint {Order:int.MaxValue})throw new ApiError(404,"NOT_FOUND","接口不存在。");
    if (ApiPolicy.AllowsAnonymous(ctx.Request.Path)) { await next(); return; }
    var db=ctx.RequestServices.GetRequiredService<Database>();
    var hash=Content.Hash(ctx.Request.Cookies[Security.Cookie]??"");
    var session=await db.AuthSessions.SingleOrDefaultAsync(s => s.TokenHash==hash && !s.Revoked && s.ExpiresAt>DateTimeOffset.UtcNow);
    if (session==null) throw new ApiError(401,"LOGIN_REQUIRED","请先登录。");
    var roles=session.AccountId==null ? "Child" : (await db.Set<FamilyMembership>().SingleOrDefaultAsync(m=>m.AccountId==session.AccountId && m.FamilyId==session.FamilyId))?.Roles??"";
    var actor=new Actor(session.Id,session.FamilyId,session.AccountId,session.StudentId,session.Role,roles); ctx.Items["actor"]=actor;
    if (ApiPolicy.IsRead(ctx.Request.Method))
    { var readFamily=await db.Families.SingleOrDefaultAsync(f=>f.Id==actor.FamilyId)??throw new ApiError(401,"LOGIN_REQUIRED","请重新登录。");ctx.Response.Headers.ETag=$"\"{readFamily.Version}\""; await next(); return; }
    var key=ctx.Request.Headers[ApiPolicy.IdempotencyHeader].ToString();
    if (key.Length<ApiPolicy.MinIdempotencyLength || key.Length>ApiPolicy.MaxIdempotencyLength) throw new ApiError(422,"IDEMPOTENCY_REQUIRED","请提供有效的请求标识。");
    ctx.Request.EnableBuffering(); using var reader=new StreamReader(ctx.Request.Body,leaveOpen:true); var body=await reader.ReadToEndAsync(); ctx.Request.Body.Position=0;
    var requestHash=Content.Hash(body); var scopeKey=ctx.Request.Method+ctx.Request.Path;
    await using var tx=await db.Database.BeginTransactionAsync(); await db.Lock(actor.FamilyId);
    var currentSession=await db.AuthSessions.AsNoTracking().SingleOrDefaultAsync(s=>s.Id==actor.SessionId && !s.Revoked && s.ExpiresAt>DateTimeOffset.UtcNow);
    if(currentSession==null)throw new ApiError(401,"LOGIN_REQUIRED","权限或登录已变化，请重新登录。");
    var currentRoles=currentSession.AccountId==null?"Child":(await db.Set<FamilyMembership>().AsNoTracking().SingleOrDefaultAsync(m=>m.AccountId==currentSession.AccountId && m.FamilyId==currentSession.FamilyId))?.Roles??"";
    actor=actor with {Roles=currentRoles};ctx.Items["actor"]=actor;

    var cached=await db.Commands.SingleOrDefaultAsync(c => c.FamilyId==actor.FamilyId && c.ActorId==actor.Id && c.Scope==scopeKey && c.Key==key);
    if (cached!=null)
    {
        if (cached.Hash!=requestHash) throw new ApiError(409,"IDEMPOTENCY_CONFLICT","同一请求标识不能提交不同内容。");
        if(cached.CookieCipher!=null){var protector=ctx.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector("CommandCookies/1");ctx.Response.Headers.SetCookie=protector.Unprotect(cached.CookieCipher);}
        ctx.Response.StatusCode=cached.StatusCode; ctx.Response.ContentType="application/json"; await ctx.Response.WriteAsync(cached.Response); return;
    }
    var family=await db.Families.SingleAsync(f => f.Id==actor.FamilyId);
    if (ApiPolicy.RequiresVersion(ctx.Request.Method,ctx.Request.Path) && ctx.Request.Headers.IfMatch!=$"\"{family.Version}\"") throw new ApiError(412,"VERSION_CONFLICT","数据已更新，请刷新后重新确认。");
    var response=ctx.Response.Body; await using var buffer=new MemoryStream(); ctx.Response.Body=buffer;
    try
    {
        await next();
        if (ctx.Response.StatusCode<400)
        {
            if(ctx.Items.ContainsKey("familyDeleted"))
            {
                await db.SaveChangesAsync();await tx.CommitAsync();
            }
            else
            {
            family.Version++; await db.SaveChangesAsync();
            buffer.Position=0; var result=await new StreamReader(buffer,leaveOpen:true).ReadToEndAsync();
            var cookie=ctx.Response.Headers.SetCookie.ToString();var cookieCipher=cookie.Length==0 ? null : ctx.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector("CommandCookies/1").Protect(cookie);
            db.Commands.Add(new() { FamilyId=actor.FamilyId,ActorId=actor.Id,Scope=scopeKey,Key=key,Hash=requestHash,Response=result,StatusCode=ctx.Response.StatusCode,CookieCipher=cookieCipher });
            db.Audits.Add(new() { FamilyId=actor.FamilyId,ActorId=actor.Id,Action=scopeKey,Details=Content.Hash(body) });
            await db.SaveChangesAsync(); await tx.CommitAsync(); ctx.Response.Headers.ETag=$"\"{family.Version}\"";
            }

        }
        buffer.Position=0; await buffer.CopyToAsync(response);
    }
    finally { ctx.Response.Body=response; }
});
app.MapGet("/api/health",() => new { status="ok",version="0.1.0",rules=new[] { Assessment.EvidenceRuleVersion,Assessment.MasteryModelVersion,Planning.RuleVersion,Assessment.ReviewRuleVersion } });
app.MapLearningEndpoints();
app.MapOpenApi("/api/v1/openapi.json");
app.UseDefaultFiles(); app.UseStaticFiles(); app.MapFallbackToFile("index.html");
app.Run();
public partial class Program { }
