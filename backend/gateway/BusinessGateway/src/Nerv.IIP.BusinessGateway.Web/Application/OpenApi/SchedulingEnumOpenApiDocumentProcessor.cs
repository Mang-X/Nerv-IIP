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
        [SchemaName<SchedulePlanStatusContract>()] = EnumValues<SchedulePlanStatusContract>(),
        [SchemaName<ScheduleConflictReasonCodeContract>()] = EnumValues<ScheduleConflictReasonCodeContract>(),
        [SchemaName<ScheduleConflictSeverityContract>()] = EnumValues<ScheduleConflictSeverityContract>(),
        [SchemaName<ScheduleChangeTypeContract>()] = EnumValues<ScheduleChangeTypeContract>(),
        [SchemaName<ScheduleSplitPolicyContract>()] = EnumValues<ScheduleSplitPolicyContract>(),
    };

    public void Process(DocumentProcessorContext context)
    {
        foreach (var (schemaName, values) in SchedulingEnums)
        {
            if (!context.Document.Components.Schemas.TryGetValue(schemaName, out var schema))
            {
                continue;
            }

            schema.Type = JsonObjectType.String;
            schema.Format = null;
            schema.Enumeration.Clear();
            foreach (var value in values)
            {
                schema.Enumeration.Add(value);
            }
        }
    }

    private static string SchemaName<TEnum>() where TEnum : struct, Enum =>
        typeof(TEnum).FullName!.Replace(".", string.Empty, StringComparison.Ordinal);

    private static string[] EnumValues<TEnum>() where TEnum : struct, Enum =>
        Enum.GetNames<TEnum>()
            .Select(JsonNamingPolicy.CamelCase.ConvertName)
            .ToArray();
}
