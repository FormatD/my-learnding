namespace Learning;
public static class ApiPolicy
{
    public const string CsrfHeader="X-Learning-Request";
    public const string IdempotencyHeader="Idempotency-Key";
    public const string OriginHeader="Origin";
    public const int MinIdempotencyLength=8;
    public const int MaxIdempotencyLength=100;
    public const long MaxRequestBytes=15_000_000;
    public const string AuthRatePolicy="auth";
    public static bool IsRead(string method)=>method is "GET" or "HEAD";
    public static bool AllowsAnonymous(PathString path)=>path.StartsWithSegments("/api/v1/auth") || path=="/api/health";
    public static bool RequiresIdempotency(string method,PathString path)=>!IsRead(method) && !AllowsAnonymous(path);
    public static bool RequiresVersion(string method,PathString path)=>RequiresIdempotency(method,path) &&
        (method is "PUT" or "PATCH" || path=="/api/v1/content/resource-files" || new[]{":publish",":decide",":correct",":adjust"}.Any(s=>path.Value!.EndsWith(s,StringComparison.Ordinal)));
}

public record ApiProblem(string Type,string Title,int Status,string Code,string TraceId,IReadOnlyDictionary<string,string[]> Errors);
public static class ApiProblems
{
    public static async Task Write(HttpContext ctx,int status,string title,string code)
    {
        ctx.Response.Clear();ctx.Response.StatusCode=status;
        ctx.Response.Headers.CacheControl="no-store";
        ctx.Response.Headers["X-Content-Type-Options"]="nosniff";
        await ctx.Response.WriteAsJsonAsync(new ApiProblem("about:blank",title,status,code,ctx.TraceIdentifier,new Dictionary<string,string[]>()),options:(System.Text.Json.JsonSerializerOptions?)null,contentType:"application/problem+json",cancellationToken:ctx.RequestAborted);
    }
}
