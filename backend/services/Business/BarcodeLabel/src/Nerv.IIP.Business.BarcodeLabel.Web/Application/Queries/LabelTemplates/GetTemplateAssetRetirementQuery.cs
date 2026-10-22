using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Printing;
using Nerv.IIP.Contracts.BarcodeLabel;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.LabelTemplates;

public sealed record GetTemplateAssetRetirementQuery(
    string OrganizationId, string EnvironmentId, LabelTemplateId TemplateId, string FileId)
    : IQuery<TemplateAssetRetirementResponse>;

public sealed class GetTemplateAssetRetirementQueryHandler(
    ApplicationDbContext dbContext, HttpFileStorageLabelTemplateAssetAdapter assets, TimeProvider clock)
    : IQueryHandler<GetTemplateAssetRetirementQuery, TemplateAssetRetirementResponse>
{
    public async Task<TemplateAssetRetirementResponse> Handle(
        GetTemplateAssetRetirementQuery request, CancellationToken cancellationToken)
    {
        var template = await dbContext.LabelTemplates.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId
            && x.Id == request.TemplateId, cancellationToken)
            ?? throw new KnownException("标签模板不存在。");
        var decision = await dbContext.TemplateAssetRetirementDecisions.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId
            && x.LabelTemplateId == request.TemplateId && x.TemplateFileId == request.FileId, cancellationToken);

        // A retained decision owns historical assets; otherwise only the template's current asset is addressable.
        if (decision is null && template.TemplateFileId != request.FileId)
            throw new KnownException("模板资产不属于当前标签模板。");

        var fence = await dbContext.TemplateAssetRetirementReplayFences.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId
            && x.TemplateFileId == request.FileId, cancellationToken);
        if (fence is not null && clock.GetUtcNow() >= fence.ReplayUntilUtc)
            return new(request.FileId, null, fence.Id.Id, TemplateAssetRetirementStatus.ReplayWindowExpired);

        if (decision is not null)
        {
            var status = decision.Status switch
            {
                TemplateAssetRetirementDecision.PendingStatus => TemplateAssetRetirementStatus.Pending,
                TemplateAssetRetirementDecision.QuotaReleasedStatus => TemplateAssetRetirementStatus.QuotaReleased,
                TemplateAssetRetirementDecision.ExecutionOutcomeUnknownStatus => TemplateAssetRetirementStatus.ExecutionOutcomeUnknown,
                _ => throw new InvalidOperationException("Unsupported template asset retirement status."),
            };
            return new(request.FileId, decision.TemplateAssetSha256, decision.Id.Id, status);
        }

        var checksum = await assets.GetChecksumAsync(new LabelTemplateAssetReference(
            request.FileId, request.OrganizationId, request.EnvironmentId, template.TemplateCode), cancellationToken);
        return new(request.FileId, checksum, null, null);
    }
}
