using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.Errors;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>#3855：停机原因目录的写入口——编码重复明确拒绝，分类与损失类别只收 Maintenance 词表里的受控码。</summary>
public sealed class MaintenanceDowntimeReasonCatalogCommandTests
{
    [Fact]
    public async Task Creating_an_existing_code_is_rejected_and_leaves_the_original_reason_unchanged()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        db.DowntimeReasons.Add(DowntimeReason.Create("org-001", "env-dev", "DT-MECH", "机械故障", "breakdown", "availability"));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<MaintenanceDowntimeReasonCodeConflictException>(() =>
            new CreateDowntimeReasonCommandHandler(db).Handle(
                new CreateDowntimeReasonCommand("org-001", "env-dev", "DT-MECH", "液压系统故障", "breakdown", "availability"),
                CancellationToken.None));

        var stored = await db.DowntimeReasons.AsNoTracking().SingleAsync();
        Assert.Equal("机械故障", stored.Description);
    }

    [Fact]
    public async Task The_same_code_in_another_environment_is_a_different_reason()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        db.DowntimeReasons.Add(DowntimeReason.Create("org-001", "env-dev", "DT-MECH", "机械故障", "breakdown", "availability"));
        await db.SaveChangesAsync();

        await new CreateDowntimeReasonCommandHandler(db).Handle(
            new CreateDowntimeReasonCommand("org-001", "env-test", "DT-MECH", "机械故障", "breakdown", "availability"),
            CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.DowntimeReasons.CountAsync());
    }

    [Theory]
    [InlineData("mystery", "availability", nameof(CreateDowntimeReasonCommand.ReasonCategory))]
    [InlineData("breakdown", "equipment-failure", nameof(CreateDowntimeReasonCommand.LossCategory))]
    [InlineData("Breakdown", "availability", nameof(CreateDowntimeReasonCommand.ReasonCategory))]
    public void Create_and_update_reject_codes_outside_the_maintenance_vocabulary(string category, string loss, string property)
    {
        var create = new CreateDowntimeReasonCommandValidator().Validate(
            new CreateDowntimeReasonCommand("org-001", "env-dev", "DT-HYD", "液压系统故障", category, loss));
        var update = new UpdateDowntimeReasonCommandValidator().Validate(
            new UpdateDowntimeReasonCommand("org-001", "env-dev", "DT-HYD", "液压系统故障", category, loss));

        Assert.Contains(create.Errors, x => string.Equals(x.PropertyName, property, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(update.Errors, x => string.Equals(x.PropertyName, property, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Every_vocabulary_pair_is_accepted()
    {
        foreach (var category in DowntimeReasonVocabulary.ReasonCategories.All)
        {
            foreach (var loss in DowntimeReasonVocabulary.LossCategories.All)
            {
                Assert.True(new CreateDowntimeReasonCommandValidator().Validate(
                    new CreateDowntimeReasonCommand("org-001", "env-dev", "DT-HYD", "液压系统故障", category, loss)).IsValid);
            }
        }
    }
}
