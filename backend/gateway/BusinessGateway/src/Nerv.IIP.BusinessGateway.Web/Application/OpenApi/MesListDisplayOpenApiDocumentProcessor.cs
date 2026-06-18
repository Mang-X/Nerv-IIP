using NJsonSchema;
using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

public sealed class MesListDisplayOpenApiDocumentProcessor : IDocumentProcessor
{
    private static readonly string[] MesListStatusValues =
    [
        "accepted",
        "active",
        "blocked",
        "cancelled",
        "closed",
        "completed",
        "created",
        "dispositionAccepted",
        "hold",
        "inProgress",
        "open",
        "partiallyReceived",
        "paused",
        "posted",
        "queued",
        "ready",
        "received",
        "recovered",
        "released",
        "returnAccepted",
        "reworkPending",
        "scrapAccepted",
        "scrapped",
        "requested",
        "started",
        "warning",
    ];

    private static readonly (string SchemaSuffix, string PropertyName)[] StatusProperties =
    [
        ("BusinessConsoleMesWorkOrderItem", "status"),
        ("BusinessConsoleMesOperationTaskItem", "status"),
        ("BusinessConsoleMesMaterialIssueRequestRow", "status"),
        ("BusinessConsoleMesDispatchTaskRow", "status"),
        ("BusinessConsoleMesOperationTaskRow", "status"),
        ("BusinessConsoleMesWipSummaryRow", "status"),
        ("BusinessConsoleMesRelatedQualityItemRow", "status"),
        ("BusinessConsoleMesReceiptRequestRow", "receiptStatus"),
        ("BusinessConsoleMesDowntimeEventRow", "status"),
        ("BusinessConsoleMesCapacityImpactRow", "status"),
    ];

    private static readonly string[] MesListPaths =
    [
        "/api/business-console/v1/mes/work-orders",
        "/api/business-console/v1/mes/production-plans",
        "/api/business-console/v1/mes/material-issue-requests",
        "/api/business-console/v1/mes/dispatch-tasks",
        "/api/business-console/v1/mes/operation-tasks",
        "/api/business-console/v1/mes/wip",
        "/api/business-console/v1/mes/related-quality-items",
        "/api/business-console/v1/mes/finished-goods-receipt-requests",
        "/api/business-console/v1/mes/downtime-events",
        "/api/business-console/v1/mes/shift-handovers",
        "/api/business-console/v1/mes/capacity-impacts",
    ];

    public void Process(DocumentProcessorContext context)
    {
        foreach (var (schemaSuffix, propertyName) in StatusProperties)
        {
            var schema = FindSchemaBySuffix(context, schemaSuffix);
            if (!schema.Properties.TryGetValue(propertyName, out var property))
            {
                throw new InvalidOperationException(
                    $"Missing MES list status property OpenAPI schema: {schemaSuffix}.{propertyName}");
            }

            ApplyStatusEnum(property);
        }

        foreach (var path in MesListPaths)
        {
            if (!context.Document.Paths.TryGetValue(path, out var pathItem)
                || !pathItem.TryGetValue(OpenApiOperationMethod.Get, out var operation))
            {
                throw new InvalidOperationException($"Missing MES list OpenAPI operation: GET {path}");
            }

            var statusParameter = operation.Parameters.SingleOrDefault(x =>
                x.Kind == OpenApiParameterKind.Query && string.Equals(x.Name, "status", StringComparison.Ordinal));
            if (statusParameter is null)
            {
                throw new InvalidOperationException($"Missing MES list status query parameter: GET {path}");
            }

            ApplyStatusEnum(statusParameter.Schema);
        }
    }

    private static JsonSchema FindSchemaBySuffix(DocumentProcessorContext context, string suffix)
    {
        var matches = context.Document.Components.Schemas
            .Where(x => x.Key.EndsWith(suffix, StringComparison.Ordinal))
            .Select(x => x.Value)
            .ToArray();

        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected exactly one MES OpenAPI schema ending with {suffix}, found {matches.Length}.");
    }

    private static void ApplyStatusEnum(JsonSchema schema)
    {
        schema.Type = JsonObjectType.String;
        schema.Format = null;
        schema.Enumeration.Clear();
        schema.EnumerationNames.Clear();
        foreach (var value in MesListStatusValues)
        {
            schema.Enumeration.Add(value);
        }
    }
}
