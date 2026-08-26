using NJsonSchema;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

/// <summary>
/// Keeps the shared response envelope accurate for both successful responses and
/// downstream proxy failures. Success and existing local failures retain their
/// numeric HTTP code; governed downstream failure envelopes may use a semantic
/// string code.
/// </summary>
public sealed class BusinessGatewayErrorResponseOpenApiDocumentProcessor : IDocumentProcessor
{
    private const string ResponseDataSchemaName = "NetCorePalExtensionsDtoResponseData";

    public void Process(DocumentProcessorContext context)
    {
        if (!context.Document.Components.Schemas.TryGetValue(ResponseDataSchemaName, out var schema))
        {
            throw new InvalidOperationException($"Missing shared response envelope schema: {ResponseDataSchemaName}");
        }

        if (!schema.Properties.TryGetValue("code", out var code))
        {
            throw new InvalidOperationException($"Missing shared response envelope code property: {ResponseDataSchemaName}.code");
        }

        code.Type = JsonObjectType.None;
        code.Format = null;
        code.OneOf.Clear();
        code.OneOf.Add(new JsonSchema { Type = JsonObjectType.Integer, Format = "int32" });
        code.OneOf.Add(new JsonSchema { Type = JsonObjectType.String });
    }
}
