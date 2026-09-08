using NJsonSchema;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

public sealed class MachineOverheadOpenApiDocumentProcessor : IDocumentProcessor
{
    private static readonly IReadOnlyDictionary<string, string[]> RequiredNullableProperties =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["BusinessConsoleErpWorkOrderCostVarianceResponse"] =
            [
                "actualMachineHours",
                "machineCostUnavailableReason",
                "machineCurrencyCode",
                "appliedFixedMachineOverhead",
                "appliedVariableMachineOverhead",
                "appliedMachineOverheadTotal",
            ],
            ["BusinessConsoleErpOperationMachineOverheadItem"] =
            [
                "unavailableReason",
                "actualMachineHours",
                "appliedFixedMachineOverhead",
                "appliedVariableMachineOverhead",
                "appliedMachineOverheadTotal",
            ],
            ["BusinessConsoleErpMachineOverheadReconciliationListResponse"] =
            [
                "accountingPeriodStatus",
                "reconciliationUnavailableReason",
            ],
            ["BusinessConsoleErpMachineOverheadReconciliationItem"] =
            [
                "unavailableReason",
            ],
        };

    public void Process(DocumentProcessorContext context)
    {
        SetEnum(context, "BusinessConsoleErpMachineOverheadReconciliationListResponse",
            "accountingPeriodStatus", "open", "closed");
        FindSchemaBySuffix(context, "BusinessConsoleErpMachineOverheadReconciliationListResponse")
            .Properties["accountingPeriodStatus"].Enumeration.Add(null!);
        SetEnum(context, "BusinessConsoleErpMachineOverheadReconciliationItem",
            "abnormalDowntimeDisposition", "None", "Pending", "PeriodExpense");

        foreach (var (schemaSuffix, propertyNames) in RequiredNullableProperties)
        {
            var schema = FindSchemaBySuffix(context, schemaSuffix);
            foreach (var propertyName in propertyNames)
            {
                if (!schema.Properties.TryGetValue(propertyName, out var property))
                {
                    throw new InvalidOperationException(
                        $"Missing machine-overhead OpenAPI property: {schemaSuffix}.{propertyName}");
                }

                property.IsRequired = true;
                property.IsNullableRaw = true;
            }
        }
    }

    private static void SetEnum(DocumentProcessorContext context, string schemaSuffix, string propertyName,
        params string[] values)
    {
        var property = FindSchemaBySuffix(context, schemaSuffix).Properties[propertyName];
        property.Enumeration.Clear();
        foreach (var value in values)
        {
            property.Enumeration.Add(value);
        }
    }

    private static JsonSchema FindSchemaBySuffix(DocumentProcessorContext context, string suffix)
    {
        var matches = context.Document.Components.Schemas
            .Where(schema =>
                schema.Key.EndsWith(suffix, StringComparison.Ordinal) &&
                !schema.Key.StartsWith("NetCorePalExtensionsDtoResponseDataOf", StringComparison.Ordinal))
            .Select(schema => schema.Value)
            .ToArray();

        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected exactly one machine-overhead OpenAPI schema ending with {suffix}, found {matches.Length}.");
    }
}
