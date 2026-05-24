using System.Reflection;
using FastEndpoints;

namespace Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

public static class BusinessGatewayOperationIdConvention
{
    public static string Generate(EndpointNameGenerationContext context)
    {
        return context.EndpointType
            .GetCustomAttribute<BusinessGatewayOperationIdAttribute>(inherit: false)
            ?.OperationId ?? context.EndpointType.Name;
    }
}
