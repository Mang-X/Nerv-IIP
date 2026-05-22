using System.Reflection;
using FastEndpoints;

namespace Nerv.IIP.PlatformGateway.Web.Application.OpenApi;

public static class GatewayOperationIdConvention
{
    private const string EndpointSuffix = "Endpoint";

    public static string Generate(EndpointNameGenerationContext context)
    {
        var operationId = context.EndpointType
            .GetCustomAttribute<GatewayOperationIdAttribute>(inherit: false)
            ?.OperationId;

        // Explicit attributes are stable contract names; only unannotated endpoints use the convention fallback.
        return operationId ?? ToLowerCamelCase(RemoveEndpointSuffix(context.EndpointType.Name));
    }

    private static string RemoveEndpointSuffix(string endpointTypeName)
    {
        return endpointTypeName.EndsWith(EndpointSuffix, StringComparison.Ordinal)
            ? endpointTypeName[..^EndpointSuffix.Length]
            : endpointTypeName;
    }

    private static string ToLowerCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value) || char.IsLower(value[0]))
        {
            return value;
        }

        return char.ToLowerInvariant(value[0]) + value[1..];
    }
}
