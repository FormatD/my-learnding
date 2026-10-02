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
        options.AddOperationTransformer((operation,context,ct)=>
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
            return Task.CompletedTask;
        });
    }
}
