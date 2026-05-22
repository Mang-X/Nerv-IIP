namespace Nerv.IIP.PlatformGateway.Web.Application.OpenApi;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GatewayOperationIdAttribute(string operationId) : Attribute
{
    public string OperationId { get; } = string.IsNullOrWhiteSpace(operationId)
        ? throw new ArgumentException("Gateway operationId must not be empty.", nameof(operationId))
        : operationId;
}
