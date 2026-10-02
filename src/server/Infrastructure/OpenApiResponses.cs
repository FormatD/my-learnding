using Microsoft.OpenApi;

namespace Learning;
public record DownloadResponseMetadata(string[] MediaTypes);
public record NullableResponseMetadata;
public static class OpenApiResponses
{
    public static void Configure(Microsoft.AspNetCore.OpenApi.OpenApiOptions options)
    {
        var defaultReferenceId=options.CreateSchemaReferenceId;
        // Equal anonymous property types do not imply equal JSON field names.
        options.CreateSchemaReferenceId=info=>info.Type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute),false) && info.Type.Name.Contains("AnonymousType",StringComparison.Ordinal) ? null : defaultReferenceId(info);
        options.AddOperationTransformer(async(operation,context,ct)=>
        {
            var metadata=context.Description.ActionDescriptor.EndpointMetadata;
            if(metadata.OfType<DownloadResponseMetadata>().FirstOrDefault() is { } download)
            {
                operation.Responses!["200"]=new OpenApiResponse
                {
                    Description="Private file download",
                    Content=download.MediaTypes.ToDictionary(t=>t,t=>new OpenApiMediaType {Schema=new OpenApiSchema{Type=JsonSchemaType.String,Format="binary"}})
                };
            }
            if(metadata.OfType<NullableResponseMetadata>().Any())
            {
                var content=operation.Responses!["200"].Content!["application/json"];
                content.Schema=new OpenApiSchema{AnyOf=[content.Schema!,new OpenApiSchema{Type=JsonSchemaType.Null}]};
            }
            var path=new PathString("/"+context.Description.RelativePath!.Split('?')[0]);
            var method=context.Description.HttpMethod!;
            var document=context.Document??throw new InvalidOperationException("OpenAPI document context is unavailable.");
            document.Components??=new();
            operation.Parameters??=[];
            void Header(string name,OpenApiSchema schema)=>operation.Parameters.Add(new OpenApiParameter{Name=name,In=ParameterLocation.Header,Required=true,Schema=schema});
            if(!ApiPolicy.IsRead(method))Header(ApiPolicy.CsrfHeader,new(){Type=JsonSchemaType.String,Enum=[System.Text.Json.Nodes.JsonValue.Create("1")!]});
            if(!ApiPolicy.IsRead(method))operation.Parameters.Add(new OpenApiParameter{Name=ApiPolicy.OriginHeader,In=ParameterLocation.Header,Required=false,Description="If present, must match this application's origin.",Schema=new OpenApiSchema{Type=JsonSchemaType.String,Format="uri"}});
            if(ApiPolicy.RequiresIdempotency(method,path))Header(ApiPolicy.IdempotencyHeader,new(){Type=JsonSchemaType.String,MinLength=ApiPolicy.MinIdempotencyLength,MaxLength=ApiPolicy.MaxIdempotencyLength});
            if(ApiPolicy.RequiresVersion(method,path))Header("If-Match",new(){Type=JsonSchemaType.String});
            if(!ApiPolicy.AllowsAnonymous(path))
            {
                document.Components.SecuritySchemes??=new Dictionary<string,IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["FamilySession"]=new OpenApiSecurityScheme{Type=SecuritySchemeType.ApiKey,In=ParameterLocation.Cookie,Name=Security.Cookie};
                operation.Security=[new OpenApiSecurityRequirement{[new OpenApiSecuritySchemeReference("FamilySession",document)]=[]}];
                foreach(var item in operation.Responses!.Where(r=>r.Key.StartsWith('2')))
                    if(item.Value is OpenApiResponse response)
                    {
                        response.Headers??=new Dictionary<string,IOpenApiHeader>();
                        response.Headers["ETag"]=new OpenApiHeader{Description="Family version; retained on reads/writes, but cached idempotency replies may omit it. Re-read the edited resource before another mutation.",Schema=new OpenApiSchema{Type=JsonSchemaType.String}};
                    }
            }
            var problem=await context.GetOrCreateSchemaAsync(typeof(ApiProblem),null,ct);
            document.Components.Schemas??=new Dictionary<string,IOpenApiSchema>();
            document.Components.Schemas[nameof(ApiProblem)]=problem;
            // Common domain failures are a shared envelope, not a per-operation code enum.
            var failures=new HashSet<int>{400,403,404,405,409,413,415,422,500};
            if(!ApiPolicy.AllowsAnonymous(path) || path=="/api/v1/auth/login")failures.Add(401);
            if(ApiPolicy.RequiresVersion(method,path) || !ApiPolicy.IsRead(method))failures.Add(412);
            if(metadata.OfType<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>().Any())failures.Add(429);
            foreach(var status in failures)
                operation.Responses![status.ToString(System.Globalization.CultureInfo.InvariantCulture)]=new OpenApiResponse{Description="Common API problem; code describes the cause",Content=new Dictionary<string,OpenApiMediaType>{{"application/problem+json",new(){Schema=new OpenApiSchemaReference(nameof(ApiProblem),document)}}}};
        });
    }
}
