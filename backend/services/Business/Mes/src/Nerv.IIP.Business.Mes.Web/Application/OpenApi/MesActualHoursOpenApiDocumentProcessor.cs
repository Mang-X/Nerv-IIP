using NJsonSchema;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Nerv.IIP.Business.Mes.Web.Application.OpenApi;

public sealed class MesActualHoursOpenApiDocumentProcessor : IDocumentProcessor
{
    private static readonly (string PropertyName, string PairedPropertyName)[] RequiredNullableProperties =
    [
        ("actualLaborHours", "actualMachineHours"),
        ("operationActualLaborHours", "operationActualMachineHours"),
    ];

    public void Process(DocumentProcessorContext context)
    {
        foreach (var (propertyName, pairedPropertyName) in RequiredNullableProperties)
        {
            var schema = FindSchemaWithProperty(context, propertyName);
            MarkRequiredNullable(schema, propertyName);
            MarkRequiredNullable(schema, pairedPropertyName);
        }
    }

    private static JsonSchema FindSchemaWithProperty(DocumentProcessorContext context, string propertyName)
    {
        var matches = context.Document.Components.Schemas.Values
            .Where(schema => schema.Properties.ContainsKey(propertyName))
            .ToArray();

        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected exactly one MES OpenAPI schema containing {propertyName}, found {matches.Length}.");
    }

    private static void MarkRequiredNullable(JsonSchema schema, string propertyName)
    {
        if (!schema.Properties.TryGetValue(propertyName, out var property))
        {
            throw new InvalidOperationException($"Missing MES actual-hours OpenAPI property: {propertyName}");
        }

        schema.RequiredProperties.Add(propertyName);
        property.IsNullableRaw = true;
    }
}
