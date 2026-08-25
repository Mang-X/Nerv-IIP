namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessServiceAuditContext(
    string Actor,
    string CorrelationId,
    string CausationId,
    string? IdempotencyKey);
