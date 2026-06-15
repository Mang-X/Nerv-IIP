using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.Scans;

public sealed record RecordScanCommand(
    string OrganizationId,
    string EnvironmentId,
    string DeviceCode,
    string ScannedValue,
    string SourceWorkflow,
    string SourceDocumentId,
    string IdempotencyKey,
    string Result,
    string? RejectionReason,
    string? SkuCode,
    string? UomCode,
    string? SiteCode,
    string? LocationCode,
    string? QualityStatus,
    string? OwnerType,
    string? OwnerId,
    decimal? Quantity) : ICommand<ScanRecordId>;

public sealed class RecordScanCommandValidator : AbstractValidator<RecordScanCommand>
{
    public RecordScanCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ScannedValue).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SourceWorkflow).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Result).NotEmpty().MaximumLength(30);
        RuleFor(x => x.RejectionReason).MaximumLength(500);
        RuleFor(x => x.SkuCode).MaximumLength(100);
        RuleFor(x => x.UomCode).MaximumLength(50);
        RuleFor(x => x.SiteCode).MaximumLength(100);
        RuleFor(x => x.LocationCode).MaximumLength(100);
        RuleFor(x => x.QualityStatus).MaximumLength(100);
        RuleFor(x => x.OwnerType).MaximumLength(100);
        RuleFor(x => x.OwnerId).MaximumLength(150);
        When(x => string.Equals(x.Result, "accepted", StringComparison.OrdinalIgnoreCase)
            && x.SourceWorkflow.StartsWith("inventory.", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
            RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
            RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
            RuleFor(x => x.LocationCode).NotEmpty().MaximumLength(100);
            RuleFor(x => x.QualityStatus).NotEmpty().MaximumLength(100);
            RuleFor(x => x.OwnerType).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Quantity).NotNull().GreaterThan(0);
        });
    }
}

public sealed class RecordScanCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RecordScanCommand, ScanRecordId>
{
    public async Task<ScanRecordId> Handle(RecordScanCommand request, CancellationToken cancellationToken)
    {
        var candidate = ScanRecord.Record(
            request.OrganizationId,
            request.EnvironmentId,
            request.DeviceCode,
            request.ScannedValue,
            request.SourceWorkflow,
            request.SourceDocumentId,
            request.IdempotencyKey,
            request.Result,
            request.RejectionReason,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.LocationCode,
            request.QualityStatus,
            request.OwnerType,
            request.OwnerId,
            request.Quantity);

        var existing = await dbContext.ScanRecords.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.IdempotencyKey == request.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            try
            {
                existing.EnsureSameIdempotencyPayload(candidate);
            }
            catch (InvalidOperationException ex)
            {
                throw new KnownException(ex.Message, ex);
            }

            return existing.Id;
        }

        dbContext.ScanRecords.Add(candidate);
        return candidate.Id;
    }
}
