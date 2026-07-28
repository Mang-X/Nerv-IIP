using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleOperationReceipt(
    [property: JsonRequired, Required] string OperationType,
    [property: JsonRequired, Required] string Authority,
    [property: JsonRequired, Required] string ResourceType,
    [property: JsonRequired, Required] string ResourceId,
    [property: JsonRequired, Required] string Outcome,
    [property: JsonRequired, Required] bool StateConfirmed,
    [property: JsonRequired, Required] bool ReadbackRequired,
    [property: JsonRequired, Required] string IdempotencyKey,
    DateTimeOffset? ChangedAtUtc,
    string? ResourceStatus = null,
    string? ReadbackMethod = null,
    string? ReadbackPath = null);

internal static class BusinessConsoleOperationReceipts
{
    public static BusinessConsoleOperationReceipt Confirmed(
        string operationType,
        string authority,
        string resourceType,
        string resourceId,
        DateTimeOffset changedAtUtc,
        string resourceStatus,
        string idempotencyKey)
    {
        if (changedAtUtc == default)
        {
            throw InvalidReceipt();
        }

        return new(
            Required(operationType),
            Required(authority),
            Required(resourceType),
            Required(resourceId),
            "confirmed",
            StateConfirmed: true,
            ReadbackRequired: false,
            IdempotencyKey: Required(idempotencyKey),
            ChangedAtUtc: changedAtUtc,
            ResourceStatus: Required(resourceStatus));
    }

    public static BusinessConsoleOperationReceipt Accepted(
        string operationType,
        string authority,
        string resourceType,
        string resourceId,
        string readbackPath,
        string idempotencyKey,
        DateTimeOffset? changedAtUtc = null)
    {
        var normalizedReadbackPath = Required(readbackPath);
        if (!normalizedReadbackPath.StartsWith("/api/business-console/v1/", StringComparison.Ordinal)
            || Uri.TryCreate(normalizedReadbackPath, UriKind.Absolute, out _))
        {
            throw InvalidReceipt();
        }

        return new(
            Required(operationType),
            Required(authority),
            Required(resourceType),
            Required(resourceId),
            "accepted",
            StateConfirmed: false,
            ReadbackRequired: true,
            IdempotencyKey: Required(idempotencyKey),
            ChangedAtUtc: changedAtUtc,
            ReadbackMethod: HttpMethod.Get.Method,
            ReadbackPath: normalizedReadbackPath);
    }

    private static string Required(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw InvalidReceipt()
            : value.Trim();

    private static BusinessServiceProxyException InvalidReceipt() =>
        BusinessServiceProxyException.FromSafeDownstreamMessage(
            System.Net.HttpStatusCode.BadGateway,
            "downstream-invalid-response");
}
