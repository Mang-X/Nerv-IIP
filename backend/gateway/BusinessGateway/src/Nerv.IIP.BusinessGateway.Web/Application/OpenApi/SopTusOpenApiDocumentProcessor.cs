using NJsonSchema;
using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

// EndpointWithoutRequest streams HttpRequest.Body. NSwag does not infer its binary body
// from Accepts<Stream> metadata; describe only this new public transport operation.
public sealed class SopTusOpenApiDocumentProcessor : IDocumentProcessor
{
    public void Process(DocumentProcessorContext context)
    {
        var patch = context.Document.Paths["/api/business-console/v1/files/sop-documents/tus/{uploadSessionId}"]
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
    }
}
