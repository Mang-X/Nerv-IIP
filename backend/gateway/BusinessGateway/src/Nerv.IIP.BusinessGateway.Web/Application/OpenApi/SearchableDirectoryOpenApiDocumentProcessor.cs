using NJsonSchema;
using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

public sealed class SearchableDirectoryOpenApiDocumentProcessor : IDocumentProcessor
{
    private const string Path = "/api/business-console/v1/directories/{directoryType}";

    private static readonly IReadOnlyDictionary<string, string[]> ParameterValues =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["directoryType"] =
            [
                "personnel", "team", "equipment", "work-center", "station", "workshop", "material", "priority",
                "location", "batch", "serial", "defect-code", "scrap-reason", "downtime-reason", "maintenance-reason",
            ],
            ["scopeKind"] = ["team", "workshop", "work-center", "site"],
            ["rankingMode"] = ["default", "recent", "suggested"],
        };

    public void Process(DocumentProcessorContext context)
    {
        if (!context.Document.Paths.TryGetValue(Path, out var pathItem)
            || !pathItem.TryGetValue(OpenApiOperationMethod.Get, out var operation))
        {
            throw new InvalidOperationException($"Missing searchable directory OpenAPI operation: GET {Path}");
        }

        foreach (var (name, values) in ParameterValues)
        {
            var parameter = operation.Parameters.SingleOrDefault(item =>
                string.Equals(item.Name, name, StringComparison.Ordinal)) ?? throw new InvalidOperationException($"Missing searchable directory OpenAPI parameter: {name}");
            parameter.Schema.Type = JsonObjectType.String;
            parameter.Schema.Format = null;
            parameter.Schema.Enumeration.Clear();
            parameter.Schema.EnumerationNames.Clear();
            foreach (var value in values)
            {
                parameter.Schema.Enumeration.Add(value);
            }
        }
    }
}
