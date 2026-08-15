using NJsonSchema;
using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

public sealed class OperationReceiptOpenApiDocumentProcessor : IDocumentProcessor
{
    private const string ReceiptSchemaName =
        "NervIIPBusinessGatewayWebApplicationBusinessServicesBusinessConsoleOperationReceipt";
    private const string ConfirmedSchemaName = "BusinessConsoleConfirmedOperationReceipt";
    private const string AcceptedSchemaName = "BusinessConsoleAcceptedOperationReceipt";

    private static readonly HashSet<string> GovernedWriteOperationIds =
    [
        "acknowledgeBusinessConsoleEquipmentAlarm",
        "shelveBusinessConsoleEquipmentAlarm",
        "unshelveBusinessConsoleEquipmentAlarm",
        "createBusinessConsoleMaintenanceWorkOrder",
        "completeBusinessConsoleMaintenanceWorkOrder",
        "createBusinessConsoleQualityInspectionRecordFromTask",
        "startBusinessConsoleMesOperationTask",
        "pauseBusinessConsoleMesOperationTask",
        "resumeBusinessConsoleMesOperationTask",
        "completeBusinessConsoleMesOperationTask",
        "recordBusinessConsoleMesProductionReport",
        "completeBusinessConsoleWmsInboundOrder",
        "completeBusinessConsoleWmsOutboundOrder",
        "completeBusinessConsoleWmsCountExecution",
    ];

    public void Process(DocumentProcessorContext context)
    {
        RewriteReceiptSchema(context.Document);
        AddStandardIdempotencyHeader(context.Document);
    }

    private static void RewriteReceiptSchema(OpenApiDocument document)
    {
        if (!document.Components.Schemas.TryGetValue(ReceiptSchemaName, out var receipt))
        {
            throw new InvalidOperationException($"Missing operation receipt schema: {ReceiptSchemaName}");
        }

        var confirmed = CreateVariant(
            outcome: "confirmed",
            stateConfirmed: true,
            readbackRequired: false,
            includeConfirmedFields: true);
        var accepted = CreateVariant(
            outcome: "accepted",
            stateConfirmed: false,
            readbackRequired: true,
            includeConfirmedFields: false);

        document.Components.Schemas[ConfirmedSchemaName] = confirmed;
        document.Components.Schemas[AcceptedSchemaName] = accepted;

        receipt.Properties.Clear();
        receipt.RequiredProperties.Clear();
        receipt.OneOf.Clear();
        receipt.OneOf.Add(new JsonSchema { Reference = confirmed });
        receipt.OneOf.Add(new JsonSchema { Reference = accepted });
        receipt.DiscriminatorObject = new OpenApiDiscriminator { PropertyName = "outcome" };
        receipt.DiscriminatorObject.Mapping["confirmed"] = new JsonSchema { Reference = confirmed };
        receipt.DiscriminatorObject.Mapping["accepted"] = new JsonSchema { Reference = accepted };
    }

    private static JsonSchema CreateVariant(
        string outcome,
        bool stateConfirmed,
        bool readbackRequired,
        bool includeConfirmedFields)
    {
        var schema = new JsonSchema { Type = JsonObjectType.Object, AllowAdditionalProperties = false };
        AddRequiredString(schema, "operationType");
        AddRequiredString(schema, "authority");
        AddRequiredString(schema, "resourceType");
        AddRequiredString(schema, "resourceId");
        AddLiteral(schema, "outcome", outcome);
        AddLiteral(schema, "stateConfirmed", stateConfirmed);
        AddLiteral(schema, "readbackRequired", readbackRequired);
        AddRequiredString(schema, "idempotencyKey");

        if (includeConfirmedFields)
        {
            AddRequiredString(schema, "changedAtUtc", "date-time");
            AddRequiredString(schema, "resourceStatus");
        }
        else
        {
            AddLiteral(schema, "readbackMethod", "GET");
            AddRequiredString(schema, "readbackPath");
            schema.Properties["changedAtUtc"] = new JsonSchemaProperty
            {
                Type = JsonObjectType.String | JsonObjectType.Null,
                Format = "date-time",
            };
        }

        return schema;
    }

    private static void AddRequiredString(JsonSchema schema, string name, string? format = null)
    {
        schema.Properties[name] = new JsonSchemaProperty
        {
            Type = JsonObjectType.String,
            Format = format,
            IsRequired = true,
        };
    }

    private static void AddLiteral(JsonSchema schema, string name, object value)
    {
        var property = new JsonSchemaProperty
        {
            Type = value is bool ? JsonObjectType.Boolean : JsonObjectType.String,
            IsRequired = true,
        };
        property.Enumeration.Add(value);
        schema.Properties[name] = property;
    }

    private static void AddStandardIdempotencyHeader(OpenApiDocument document)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in document.Paths.Values)
        {
            foreach (var operation in path.Values)
            {
                if (operation.OperationId is null || !GovernedWriteOperationIds.Contains(operation.OperationId))
                {
                    continue;
                }

                found.Add(operation.OperationId);
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "Idempotency-Key",
                    Kind = OpenApiParameterKind.Header,
                    IsRequired = false,
                    Description =
                        "Standard idempotency key for this governed write. The legacy JSON idempotencyKey field remains accepted for v1 compatibility; when both are supplied they must match.",
                    Schema = new JsonSchema
                    {
                        Type = JsonObjectType.String,
                        MaxLength = 150,
                    },
                });
            }
        }

        var missing = GovernedWriteOperationIds.Except(found, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Missing governed write OpenAPI operations: {string.Join(", ", missing)}");
        }
    }
}
