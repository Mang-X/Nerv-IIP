using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;

namespace Nerv.IIP.Business.Quality.Domain.Tests;

public sealed class InspectionTaskTests
{
    /// <summary>
    /// 改前这里断言「触发幂等键长得像周期检、却没给来源行」会被构造守卫拒掉。#3319 把
    /// 「按触发幂等键的文本形状判来源形状」整段退休——幂等键不再兼职来源形状判别器，
    /// <c>InspectionRecordSourceDocumentId()</c> 与那道构造守卫一起没了，原命题因此没有主语。
    ///
    /// <para>它当年真正要保的不变量是「周期检的每个窗口各成独立检验记录」。改后这条不变量的承载点
    /// 换了：记录的链身份含来源行（<c>ux_inspection_records_source_attempt</c>），而周期检的来源行
    /// 由 <see cref="PeriodicInspectionSourceLine.LineId"/> 在**两个**生产触发点铸造，两处铸出的取值
    /// 都在 <c>PeriodicInspectionIntegrationEventTests</c> 与 <c>PeriodicInspectionPostgresConcurrencyTests</c>
    /// 里被逐字断言。</para>
    ///
    /// <para><b>本用例只覆盖「铸造出来的来源行原样落到任务上」这一段</b>：<c>CreatePending</c> 会把它交给
    /// <c>Optional()</c>，任何归一、截断或宽度守卫的改动都会在这里报红。**它不覆盖**「周期检必须有来源行」
    /// ——那条判据随二分退休已经没有主语了，改由上面两处生产路径的逐字断言看守。
    /// 也别把它读成「铸造函数不会产出空来源行」：<c>LineId</c> 是纯插值，kind 与 Guid 段恒非空，
    /// 那句话在构造上不可证伪，因此本用例不写它。</para>
    /// </summary>
    [Fact]
    public void A_minted_periodic_source_line_reaches_the_task_verbatim()
    {
        var lineId = PeriodicInspectionSourceLine.LineId(
            "OP-10",
            PeriodicInspectionSourceLine.TimeKind,
            Guid.Parse("0f9c1a2b-3d4e-4f50-8617-2a3b4c5d6e7f"),
            1);

        var task = InspectionTask.CreatePending(
            "org-001", "env-dev",
            new InspectionPlanId(Guid.Parse("018f7b14-9fb0-7d9b-a7fb-78bd14f9b101")),
            "operation", "mes", "WO-001", lineId, "SKU-FG-1000", 5m, "pcs", null, null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"), DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            PeriodicInspectionSourceLine.TriggerIdempotencyKey(
                PeriodicInspectionSourceLine.TimeKind,
                Guid.Parse("0f9c1a2b-3d4e-4f50-8617-2a3b4c5d6e7f"),
                1));

        Assert.Equal(lineId, task.SourceDocumentLineId);
    }

    [Fact]
    public void CreatePending_ShouldCaptureSourcePlanAndPendingState()
    {
        var task = InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            new InspectionPlanId(Guid.Parse("018f7b14-9fb0-7d9b-a7fb-78bd14f9b101")),
            "receiving",
            "wms",
            "IN-001",
            "LINE-001",
            "SKU-RM-1000",
            10m,
            "kg",
            "LOT-001",
            null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            "wms:inbound-completed:org-001:env-dev:IN-001:LINE-001");

        Assert.Equal(InspectionTaskStatuses.Pending, task.Status);
        Assert.Equal("receiving", task.SourceType);
        Assert.Equal("wms", task.SourceService);
        Assert.Equal("IN-001", task.SourceDocumentId);
        Assert.Equal("LINE-001", task.SourceDocumentLineId);
        Assert.Equal("SKU-RM-1000", task.SkuCode);
        Assert.Equal("kg", task.UomCode);
        Assert.Equal("LOT-001", task.BatchNo);
        Assert.Equal(DateTimeOffset.Parse("2026-07-06T08:00:00Z"), task.DueAtUtc);
    }

    [Fact]
    public void StartAndComplete_ShouldMovePendingToInProgressThenCompleted()
    {
        var task = NewTask();
        var inspectionRecordId = new InspectionRecordId(Guid.Parse("018f7b14-9fb0-7d9b-a7fb-78bd14f9b201"));

        task.Start("qa-user-001", DateTimeOffset.Parse("2026-07-05T09:00:00Z"));
        task.Complete(inspectionRecordId, DateTimeOffset.Parse("2026-07-05T10:00:00Z"));

        Assert.Equal(InspectionTaskStatuses.Completed, task.Status);
        Assert.Equal("qa-user-001", task.AssignedUserId);
        Assert.Equal(inspectionRecordId, task.InspectionRecordId);
        Assert.Equal(DateTimeOffset.Parse("2026-07-05T10:00:00Z"), task.CompletedAtUtc);
    }

    [Fact]
    public void Complete_ShouldRejectPendingTaskWithoutStart()
    {
        var task = NewTask();

        Assert.Throws<InvalidOperationException>(() =>
            task.Complete(new InspectionRecordId(Guid.Parse("018f7b14-9fb0-7d9b-a7fb-78bd14f9b202")), DateTimeOffset.Parse("2026-07-05T10:00:00Z")));
    }

    [Fact]
    public void AssignToInspector_ShouldPersistAssignmentAndAdvanceVersion()
    {
        var task = NewTask();

        task.Assign(
            "qa-user-001",
            null,
            expectedVersion: 1,
            DateTimeOffset.Parse("2026-07-05T08:30:00Z"));

        Assert.Equal("qa-user-001", task.AssignedUserId);
        Assert.Null(task.AssignedTeamId);
        Assert.Equal(2, task.Version);
        Assert.Equal(InspectionTaskStatuses.Pending, task.Status);
    }

    [Fact]
    public void ClaimTeamTask_ShouldRequireAuthorizedTeamAndAssignActor()
    {
        var task = NewTask();
        task.Assign(
            null,
            "TEAM-QA-01",
            expectedVersion: 1,
            DateTimeOffset.Parse("2026-07-05T08:20:00Z"));

        Assert.Throws<UnauthorizedAccessException>(() =>
            task.Claim(
                "qa-user-001",
                ["TEAM-QA-02"],
                expectedVersion: 2,
                DateTimeOffset.Parse("2026-07-05T08:30:00Z")));

        task.Claim(
            "qa-user-001",
            ["TEAM-QA-01"],
            expectedVersion: 2,
            DateTimeOffset.Parse("2026-07-05T08:30:00Z"));

        Assert.Equal("qa-user-001", task.AssignedUserId);
        Assert.Equal("TEAM-QA-01", task.AssignedTeamId);
        Assert.Equal(InspectionTaskStatuses.InProgress, task.Status);
        Assert.Equal(3, task.Version);
    }

    [Fact]
    public void Claim_ShouldAllowAnUnassignedPendingTask()
    {
        var task = NewTask();

        task.Claim(
            "qa-user-001",
            [],
            expectedVersion: 1,
            DateTimeOffset.Parse("2026-07-05T08:30:00Z"));

        Assert.Equal(InspectionTaskStatuses.InProgress, task.Status);
        Assert.Equal("qa-user-001", task.AssignedUserId);
        Assert.Equal(2, task.Version);
    }

    [Fact]
    public void Claim_ShouldTreatExplicitUserMatchAsSufficientWhenTeamAlsoDiffers()
    {
        var task = NewTask();
        task.Assign(
            "qa-user-001",
            "TEAM-QA-02",
            expectedVersion: 1,
            DateTimeOffset.Parse("2026-07-05T08:20:00Z"));

        task.Claim(
            "qa-user-001",
            ["TEAM-QA-01"],
            expectedVersion: 2,
            DateTimeOffset.Parse("2026-07-05T08:30:00Z"));

        Assert.Equal("qa-user-001", task.AssignedUserId);
        Assert.Equal("TEAM-QA-02", task.AssignedTeamId);
        Assert.Equal(InspectionTaskStatuses.InProgress, task.Status);
        Assert.Equal(3, task.Version);
    }

    [Fact]
    public void Claim_ShouldRejectPendingAssignmentOutsideActorAndPreserveVersionAndLifecycleConflicts()
    {
        var task = NewTask();
        task.Assign(
            "qa-user-002",
            null,
            expectedVersion: 1,
            DateTimeOffset.Parse("2026-07-05T08:20:00Z"));

        Assert.Throws<UnauthorizedAccessException>(() =>
            task.Claim(
                "qa-user-001",
                [],
                expectedVersion: 2,
                DateTimeOffset.Parse("2026-07-05T08:30:00Z")));
        Assert.Throws<InvalidOperationException>(() =>
            task.Claim(
                "qa-user-002",
                [],
                expectedVersion: 1,
                DateTimeOffset.Parse("2026-07-05T08:30:00Z")));

        task.Claim(
            "qa-user-002",
            [],
            expectedVersion: 2,
            DateTimeOffset.Parse("2026-07-05T08:30:00Z"));
        task.Complete(
            new InspectionRecordId(Guid.Parse("018f7b14-9fb0-7d9b-a7fb-78bd14f9b203")),
            DateTimeOffset.Parse("2026-07-05T09:00:00Z"));

        Assert.Throws<InvalidOperationException>(() =>
            task.Claim(
                "qa-user-002",
                [],
                expectedVersion: 4,
                DateTimeOffset.Parse("2026-07-05T09:30:00Z")));
    }

    [Fact]
    public void Claim_ShouldReportAlreadyClaimedWhenAnotherInspectorRetriesAfterClaim()
    {
        var task = NewTask();
        task.Assign(
            null,
            "TEAM-QA-01",
            expectedVersion: 1,
            DateTimeOffset.Parse("2026-07-05T08:20:00Z"));
        task.Claim(
            "qa-user-001",
            ["TEAM-QA-01"],
            expectedVersion: 2,
            DateTimeOffset.Parse("2026-07-05T08:30:00Z"));

        Assert.Throws<InspectionTaskAlreadyClaimedException>(() =>
            task.Claim(
                "qa-user-002",
                ["TEAM-QA-01"],
                expectedVersion: 3,
                DateTimeOffset.Parse("2026-07-05T08:31:00Z")));
    }

    [Fact]
    public void Claim_ShouldPreserveCompletedLifecycleConflictWhenAssignedToAnotherInspector()
    {
        var task = NewTask();
        task.Assign(
            "qa-user-002",
            null,
            expectedVersion: 1,
            DateTimeOffset.Parse("2026-07-05T08:20:00Z"));
        task.Claim(
            "qa-user-002",
            [],
            expectedVersion: 2,
            DateTimeOffset.Parse("2026-07-05T08:30:00Z"));
        task.Complete(
            new InspectionRecordId(Guid.Parse("018f7b14-9fb0-7d9b-a7fb-78bd14f9b204")),
            DateTimeOffset.Parse("2026-07-05T09:00:00Z"));

        Assert.Throws<InvalidOperationException>(() =>
            task.Claim(
                "qa-user-001",
                [],
                expectedVersion: 4,
                DateTimeOffset.Parse("2026-07-05T09:30:00Z")));
    }

    [Fact]
    public void EnsureAssignedInspector_ShouldRejectCrossInspectorCompletion()
    {
        var task = NewTask();
        task.Assign(
            "qa-user-002",
            null,
            expectedVersion: 1,
            DateTimeOffset.Parse("2026-07-05T08:20:00Z"));
        task.Claim(
            "qa-user-002",
            [],
            expectedVersion: 2,
            DateTimeOffset.Parse("2026-07-05T08:30:00Z"));

        Assert.Throws<UnauthorizedAccessException>(() =>
            task.EnsureAssignedInspector("qa-user-001"));
        task.EnsureAssignedInspector("qa-user-002");
    }

    private static InspectionTask NewTask()
    {
        return InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            new InspectionPlanId(Guid.Parse("018f7b14-9fb0-7d9b-a7fb-78bd14f9b101")),
            "operation",
            "mes",
            "WO-001",
            "OP-10",
            "SKU-FG-1000",
            5m,
            "pcs",
            null,
            null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            "mes:operation-completed:org-001:env-dev:WO-001:OP-10");
    }
}
