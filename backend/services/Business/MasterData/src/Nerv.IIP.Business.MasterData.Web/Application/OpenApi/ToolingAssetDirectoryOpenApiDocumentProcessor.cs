using System.Text.Json;
using NJsonSchema;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;
using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Nerv.IIP.Business.MasterData.Web.Application.OpenApi;

public sealed class ToolingAssetDirectoryOpenApiDocumentProcessor : IDocumentProcessor
{
    private const string Path = "/api/business/v1/master-data/tooling-assets";

    public void Process(DocumentProcessorContext context)
    {
        if (!context.Document.Paths.TryGetValue(Path, out var pathItem)
            || !pathItem.TryGetValue(OpenApiOperationMethod.Get, out var operation))
        {
            throw new InvalidOperationException($"Missing tooling asset directory OpenAPI operation: GET {Path}");
        }

        var parameter = operation.Parameters.SingleOrDefault(item =>
            string.Equals(item.Name, "status", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Missing tooling asset directory OpenAPI parameter: status");

        var schema = new JsonSchema
        {
            Type = JsonObjectType.String,
        };
        foreach (var value in Enum.GetNames<ToolingAssetStatus>().Select(JsonNamingPolicy.CamelCase.ConvertName))
        {
            schema.Enumeration.Add(value);
        }

        parameter.Schema = schema;
    }
}
