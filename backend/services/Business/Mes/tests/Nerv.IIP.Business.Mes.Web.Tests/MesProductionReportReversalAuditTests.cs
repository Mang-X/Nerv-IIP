using FluentValidation.TestHelper;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesProductionReportReversalAuditTests
{
    [Fact]
    public void Reverse_command_requires_bounded_actor_ref()
    {
        var validator = new ReverseProductionReportCommandValidator();
        var timestamp = DateTimeOffset.Parse("2026-07-12T08:00:00Z");

        validator.TestValidate(new ReverseProductionReportCommand("org", "env", "RPT-1", "reason", timestamp, "", "key"))
            .ShouldHaveValidationErrorFor(x => x.ActorRef);
        validator.TestValidate(new ReverseProductionReportCommand("org", "env", "RPT-1", "reason", timestamp, new string('a', 101), "key"))
            .ShouldHaveValidationErrorFor(x => x.ActorRef);
    }

    [Fact]
    public async Task Reverse_persists_trimmed_actor_while_historical_reports_remain_null()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(Reverse_persists_trimmed_actor_while_historical_reports_remain_null))
            .Options;
        await using var dbContext = new ApplicationDbContext(options, new NoopMediator());
        var original = ProductionReport.Record("org", "env", "RPT-1", "WO-1", "OP-1", 1m, 0m, false, DateTimeOffset.UtcNow);

        var reversal = ProductionReport.Reverse(original, "RPT-2", DateTimeOffset.UtcNow, "reason", "  user-42  ");
        dbContext.ProductionReports.AddRange(original, reversal);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        Assert.Null((await dbContext.ProductionReports.SingleAsync(x => x.ReportNo == "RPT-1")).ReversedBy);
        Assert.Equal("user-42", (await dbContext.ProductionReports.SingleAsync(x => x.ReportNo == "RPT-2")).ReversedBy);
    }
}
