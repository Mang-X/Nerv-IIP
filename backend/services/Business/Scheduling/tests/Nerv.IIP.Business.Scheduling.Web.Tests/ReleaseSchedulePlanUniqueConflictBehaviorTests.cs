using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class ReleaseSchedulePlanUniqueConflictBehaviorTests
{
    [Fact]
    public async Task Active_release_unique_violation_becomes_stable_known_concurrency_error()
    {
        var postgresException = new PostgresException(
            "duplicate key violates unique constraint \"ux_schedule_plans_scope_active_release\"",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation);
        var dbUpdateException = new DbUpdateException("release conflict", postgresException);
        var behavior = new ReleaseSchedulePlanUniqueConflictBehavior<ReleaseSchedulePlanCommand, ReleaseSchedulePlanResponse>();

        var exception = await Assert.ThrowsAsync<KnownException>(() => behavior.Handle(
            new ReleaseSchedulePlanCommand("plan-2", "org-001", "env-dev"),
            _ => Task.FromException<ReleaseSchedulePlanResponse>(dbUpdateException),
            CancellationToken.None));

        Assert.Equal("Schedule release conflicted with another release in the same scope; refresh and retry.", exception.Message);
    }
}
