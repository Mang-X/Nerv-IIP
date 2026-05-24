namespace Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class BusinessGatewayOperationIdAttribute(string operationId) : Attribute
{
    public string OperationId { get; } = string.IsNullOrWhiteSpace(operationId)
        ? throw new ArgumentException("BusinessGateway operationId must not be empty.", nameof(operationId))
        : operationId;
}
