using System.Diagnostics;
using Nerv.IIP.Sdk.Core;

namespace Nerv.IIP.Sdk.Observability;

public static class ObservabilityContext
{
    public static PlatformRequestContext CreateRequestContext(
        string organizationId,
        string environmentId,
        string? correlationId = null,
        string? idempotencyKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);

        return new PlatformRequestContext(
            organizationId,
            environmentId,
            string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("n") : correlationId,
            idempotencyKey,
            Activity.Current?.Id);
    }
}
