using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Web.Application.Seed;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 班次 / 班组两个维度的落库语义。
///
/// 回归背景：L1 背景历史引擎曾把**班组编码**（<c>TEAM-WB-MC-A</c>）写进工序任务的 <c>shift_id</c>，
/// 而 <c>OperationTaskEntityTypeConfiguration</c> 的列注释本就声明该列是「MasterData shift public id」。
/// 派工看板与工序任务页的「班次」列于是直接把班组编码吐到界面上。班次交接单则更进一步——
/// <c>ShiftId</c> 里放班组编码、<c>TeamId</c> 里放班组**名称**，两个字段都装错了东西。
///
/// 这里按五个 asOfDate（含 2026-07-27 这类边界日）断言两个维度各自落在各自的取值域里。
/// </summary>
public sealed class WorldHistoryShiftTeamSemanticsTests
{
    private static readonly string[] ExpectedShiftCodes =
        [WorldHistoryCalendar.EarlyShiftCode, WorldHistoryCalendar.MiddleShiftCode];

    [Theory]
    [InlineData("2026-01-15")]
    [InlineData("2026-03-31")]
    [InlineData("2026-06-30")]
    [InlineData("2026-07-27")]
    [InlineData("2026-12-31")]
    public async Task Dispatched_operation_tasks_carry_a_shift_code_and_a_separate_team(string asOfDate)
    {
        await using var dbContext = WorldHistorySeedTestContext.Create();
        await WorldHistorySeedTestContext.SeedWorkOrderChainAsync(dbContext, DateOnly.Parse(asOfDate), scale: 0.05d);

        var dispatched = await dbContext.OperationTasks
            .AsNoTracking()
            .Where(x => x.AssignedUserId != null)
            .Select(x => new { x.ShiftId, x.TeamId, x.TeamName })
            .ToArrayAsync(CancellationToken.None);

        Assert.NotEmpty(dispatched);

        var teamCodes = WorldHistoryMesSpec.Operators.Select(x => x.TeamCode).ToHashSet(StringComparer.Ordinal);
        var teamNames = WorldHistoryMesSpec.Operators.Select(x => x.TeamName).ToHashSet(StringComparer.Ordinal);

        foreach (var row in dispatched)
        {
            // 班次维度：只能是 L0 的班次编码，绝不能是班组编码。
            Assert.Contains(row.ShiftId, ExpectedShiftCodes);
            Assert.DoesNotContain("TEAM-", row.ShiftId ?? string.Empty, StringComparison.Ordinal);

            // 班组维度：编码归编码、名称归名称，不得互串。
            Assert.NotNull(row.TeamId);
            Assert.NotNull(row.TeamName);
            Assert.Contains(row.TeamId!, teamCodes);
            Assert.Contains(row.TeamName!, teamNames);
            Assert.StartsWith("TEAM-", row.TeamId!, StringComparison.Ordinal);
            Assert.DoesNotContain("TEAM-", row.TeamName!, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("2026-01-15")]
    [InlineData("2026-03-31")]
    [InlineData("2026-06-30")]
    [InlineData("2026-07-27")]
    [InlineData("2026-12-31")]
    public async Task Shift_handovers_separate_the_shift_code_from_the_team_code_and_name(string asOfDate)
    {
        var handovers = WorldHistoryFloorEventsSpec.BuildShiftHandovers(DateOnly.Parse(asOfDate), scale: 0.05d);

        Assert.NotEmpty(handovers);

        var teamCodes = WorldHistoryFloorEventsSpec.Teams.Select(x => x.TeamCode).ToHashSet(StringComparer.Ordinal);
        var teamNames = WorldHistoryFloorEventsSpec.Teams.Select(x => x.TeamName).ToHashSet(StringComparer.Ordinal);

        foreach (var handover in handovers)
        {
            Assert.Contains(handover.ShiftId, ExpectedShiftCodes);
            Assert.Contains(handover.TeamId, teamCodes);
            Assert.NotNull(handover.TeamName);
            Assert.Contains(handover.TeamName!, teamNames);

            // 这正是历史 bug 的形状：班次域里装班组编码、班组域里装班组名称。
            Assert.DoesNotContain("TEAM-", handover.ShiftId, StringComparison.Ordinal);
            Assert.DoesNotContain("车间", handover.TeamId, StringComparison.Ordinal);
        }
    }

    /// <summary>每个班组归属的班次要与 L0 的班组表一致（早班组落 EARLY、中班组落 MIDDLE）。</summary>
    [Fact]
    public void Team_shift_mapping_matches_the_world_bible()
    {
        foreach (var team in WorldHistoryFloorEventsSpec.Teams)
        {
            var expected = team.TeamName.Contains("早班", StringComparison.Ordinal)
                ? WorldHistoryCalendar.EarlyShiftCode
                : WorldHistoryCalendar.MiddleShiftCode;

            Assert.Equal(expected, WorldHistoryCalendar.ShiftCode(team.ShiftIndex));
        }
    }
}
