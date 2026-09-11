using System;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands;

/// <summary>
/// MES domain aggregates express business-rule violations as <see cref="InvalidOperationException"/> or
/// <see cref="ArgumentOutOfRangeException"/> (e.g. "Only queued operation task can be started",
/// "Received quantity cannot exceed requested quantity"). Those are not <see cref="KnownException"/>, so
/// without wrapping they escape as an unhandled HTTP 500 — the gateway then reports a generic
/// "downstream-request-failed" and the real reason is lost. Wrap the mutating domain call in
/// <see cref="Enforce(Action)"/> (or <see cref="Enforce{T}(Func{T})"/> when the call produces a value) so a
/// rule violation surfaces as a clean success=false business error.
/// </summary>
internal static class MesDomainRuleGuard
{
    public static void Enforce(Action mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        Enforce<bool>(() =>
        {
            mutation();
            return true;
        });
    }

    /// <summary>
    /// Same wrapping for a domain call that produces a value (e.g. an aggregate factory), so the caller keeps
    /// <c>var</c> instead of pre-declaring a <c>null!</c> local and assigning it inside the closure.
    /// </summary>
    public static T Enforce<T>(Func<T> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        try
        {
            return mutation();
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
