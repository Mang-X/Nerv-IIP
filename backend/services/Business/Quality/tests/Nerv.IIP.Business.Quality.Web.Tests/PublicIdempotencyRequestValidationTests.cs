using FluentValidation;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionTasks;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class PublicIdempotencyRequestValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Public_quality_write_request_rejects_missing_or_blank_idempotency_key(string? key)
    {
        var result = new CreateInspectionRecordFromTaskRequestValidator().Validate(
            new CreateInspectionRecordFromTaskRequest(
                InspectionTaskId: new InspectionTaskId(Guid.CreateVersion7()),
                InspectorUserId: "inspector-001",
                ResultLines: [],
                DispositionReason: null,
                DispositionAttachmentFileIds: [],
                IdempotencyKey: key!,
                OrganizationId: "org-001",
                EnvironmentId: "env-dev"));

        Assert.False(result.IsValid);
    }
}
