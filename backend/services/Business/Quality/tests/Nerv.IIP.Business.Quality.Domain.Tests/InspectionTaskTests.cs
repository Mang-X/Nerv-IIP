using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;

namespace Nerv.IIP.Business.Quality.Domain.Tests;

public sealed class InspectionTaskTests
{
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
    public void Claim_ShouldRejectAnotherInspectorStaleVersionAndCompletedTask()
    {
        var task = NewTask();
        task.Assign(
            "qa-user-002",
            null,
            expectedVersion: 1,
            DateTimeOffset.Parse("2026-07-05T08:20:00Z"));

        Assert.Throws<InspectionTaskAlreadyClaimedException>(() =>
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
