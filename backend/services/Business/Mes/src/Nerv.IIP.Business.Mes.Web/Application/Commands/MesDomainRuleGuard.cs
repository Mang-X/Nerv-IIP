using System;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands;

/// <summary>
/// MES domain aggregates express business-rule violations as <see cref="InvalidOperationException"/> or
/// <see cref="ArgumentOutOfRangeException"/> (e.g. "Only queued operation task can be started",
/// "Received quantity cannot exceed requested quantity"). Those are not <see cref="KnownException"/>, so
/// without wrapping they escape as an unhandled HTTP 500 — the gateway then reports a generic
/// "downstream-request-failed" and the real reason is lost. Wrap the mutating domain call in
/// <see cref="Enforce(Action)"/> so a rule violation surfaces as a clean success=false business error.
/// </summary>
internal static class MesDomainRuleGuard
{
    public static void Enforce(Action mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        try
        {
            mutation();
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new KnownException(exception.Message, exception);
        }
    }
}
