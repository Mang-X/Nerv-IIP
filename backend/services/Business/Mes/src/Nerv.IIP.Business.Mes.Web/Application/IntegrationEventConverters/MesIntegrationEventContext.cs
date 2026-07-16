using System.Diagnostics;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;

public sealed record MesIntegrationEventContext(
    string CorrelationId,
    string CausationId);

public interface IMesIntegrationEventContextAccessor
{
    MesIntegrationEventContext GetContext();
}

public sealed class HttpMesIntegrationEventContextAccessor(IHttpContextAccessor httpContextAccessor)
    : IMesIntegrationEventContextAccessor
{
    public MesIntegrationEventContext GetContext()
    {
        var httpContext = httpContextAccessor.HttpContext;
        return new MesIntegrationEventContext(
            FirstNonBlank(
                httpContext?.Request.Headers["X-Correlation-Id"].FirstOrDefault(),
                Activity.Current?.TraceId.ToString(),
                $"corr-{Guid.CreateVersion7():N}"),
            FirstNonBlank(
                httpContext?.Request.Headers["X-Causation-Id"].FirstOrDefault(),
                Activity.Current?.SpanId.ToString(),
                $"cause-{Guid.CreateVersion7():N}"));
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        throw new InvalidOperationException("At least one integration event context value is required.");
    }
}
