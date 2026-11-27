using NJsonSchema;
using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

// EndpointWithoutRequest streams HttpRequest.Body. NSwag does not infer its binary body
// from Accepts<Stream> metadata or infer the scope read by the shared download handler.
public sealed class QualityEvidenceTusOpenApiDocumentProcessor : IDocumentProcessor
{
    public void Process(DocumentProcessorContext context)
    {
        var patch = context.Document.Paths["/api/business-console/v1/files/quality-evidence/tus/{uploadSessionId}"]
            [OpenApiOperationMethod.Patch];
        patch.RequestBody = new OpenApiRequestBody
        {
            IsRequired = true,
            Content =
            {
                ["application/offset+octet-stream"] = new OpenApiMediaType
                {
                    Schema = new JsonSchema { Type = JsonObjectType.String, Format = "binary" }
                }
            }
        };

        var download = context.Document.Paths["/api/business-console/v1/files/quality-evidence/{fileId}/content"]
            [OpenApiOperationMethod.Get];
        var content = download.Responses["200"].Content;
        var binary = content["application/json"];
        content.Clear();
        content.Add("application/octet-stream", binary);
        foreach (var name in new[] { "organizationId", "environmentId" })
        {
            download.Parameters.Add(new OpenApiParameter
            {
                Name = name,
                Kind = OpenApiParameterKind.Query,
                IsRequired = true,
                Schema = new JsonSchema { Type = JsonObjectType.String }
            });
        }
    }
}
