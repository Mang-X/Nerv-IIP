using System.Text.Json;
using NJsonSchema;
using Nerv.IIP.Contracts.Scheduling;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

public sealed class SchedulingEnumOpenApiDocumentProcessor : IDocumentProcessor
{
    private static readonly IReadOnlyDictionary<string, string[]> SchedulingEnums = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["NervIIPContractsSchedulingSchedulePlanStatusContract"] = EnumValues<SchedulePlanStatusContract>(),
        ["NervIIPContractsSchedulingScheduleConflictReasonCodeContract"] = EnumValues<ScheduleConflictReasonCodeContract>(),
        ["NervIIPContractsSchedulingScheduleConflictSeverityContract"] = EnumValues<ScheduleConflictSeverityContract>(),
        ["NervIIPContractsSchedulingScheduleChangeTypeContract"] = EnumValues<ScheduleChangeTypeContract>(),
        ["NervIIPContractsSchedulingScheduleSplitPolicyContract"] = EnumValues<ScheduleSplitPolicyContract>(),
    };

    public void Process(DocumentProcessorContext context)
    {
        var missingSchemas = new List<string>();
        foreach (var (schemaName, values) in SchedulingEnums)
        {
            if (!context.Document.Components.Schemas.TryGetValue(schemaName, out var schema))
            {
                missingSchemas.Add(schemaName);
                continue;
            }

            schema.Type = JsonObjectType.String;
            schema.Format = null;
            schema.Enumeration.Clear();
            schema.EnumerationNames.Clear();
            foreach (var value in values)
            {
                schema.Enumeration.Add(value);
            }
        }

        if (missingSchemas.Count > 0)
        {
            throw new InvalidOperationException(
                "Missing Scheduling enum OpenAPI schema(s): " + string.Join(", ", missingSchemas));
        }
    }

    private static string[] EnumValues<TEnum>() where TEnum : struct, Enum =>
        Enum.GetNames<TEnum>()
            .Select(JsonNamingPolicy.CamelCase.ConvertName)
            .ToArray();
}
