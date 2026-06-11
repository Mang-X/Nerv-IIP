using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

public sealed class WmsWarehouseTaskOpenApiDocumentProcessor : IDocumentProcessor
{
    public const string OperatorUserIdDescription =
        "Reserved for #374 P1 assigned-operator filtering. Current WMS warehouse tasks do not persist assigned operators; sending a non-empty value returns an empty list.";

    private static readonly string[] WarehouseTaskListPaths =
    [
        "/api/business-console/v1/wms/putaway-tasks",
        "/api/business-console/v1/wms/picking-tasks",
    ];

    public void Process(DocumentProcessorContext context)
    {
        foreach (var path in WarehouseTaskListPaths)
        {
            if (!context.Document.Paths.TryGetValue(path, out var pathItem)
                || !pathItem.TryGetValue(OpenApiOperationMethod.Get, out var operation))
            {
                throw new InvalidOperationException($"Missing WMS warehouse task list OpenAPI operation: GET {path}");
            }

            var parameter = operation.Parameters.SingleOrDefault(x =>
                x.Kind == OpenApiParameterKind.Query && string.Equals(x.Name, "operatorUserId", StringComparison.Ordinal));
            if (parameter is null)
            {
                throw new InvalidOperationException($"Missing WMS warehouse task operatorUserId query parameter: GET {path}");
            }

            parameter.Description = OperatorUserIdDescription;
        }
    }
}
