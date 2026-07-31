using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;

namespace Nerv.IIP.Business.Quality.Domain.Tests;

public sealed class InspectionTaskAssignmentReceiptTests
{
    [Fact]
    public void Create_ShouldCaptureTrustedAssignmentAuditAndReplayFingerprint()
    {
        var taskId = new InspectionTaskId(
            Guid.Parse("018f7b14-9fb0-7d9b-a7fb-78bd14f9b301"));

        var receipt = InspectionTaskAssignmentReceipt.Create(
            "org-001",
            "env-dev",
            taskId,
            "claim",
            "claim-task-001",
            "fingerprint-001",
            "qa-user-001",
            null,
            "TEAM-QA-01",
            "qa-user-001",
            "TEAM-QA-01",
            null,
            3,
            DateTimeOffset.Parse("2026-07-05T08:30:00Z"));

        Assert.Equal(taskId, receipt.InspectionTaskId);
        Assert.Equal("claim", receipt.Action);
        Assert.Equal("qa-user-001", receipt.ActorPrincipalId);
        Assert.Equal(3, receipt.ResultVersion);
        Assert.True(receipt.MatchesPayload("fingerprint-001"));
        Assert.False(receipt.MatchesPayload("fingerprint-002"));
    }
}
