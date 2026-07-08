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
        When(x => string.Equals(x.Result, "accepted", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.SourceWorkflow)
                .Must(sourceWorkflow => !string.IsNullOrWhiteSpace(sourceWorkflow)
                    && ScanRecord.IsSupportedAcceptedWorkflow(sourceWorkflow))
                .WithMessage("Unsupported accepted scan workflow.");
        });
        When(x => string.Equals(x.Result, "accepted", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(x.SourceWorkflow)
            && ScanRecord.RequiresInventoryContext(x.SourceWorkflow), () =>
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
        ScanRecord candidate;
        try
        {
            candidate = ScanRecord.Record(
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
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new KnownException(ex.Message, ex);
        }
        catch (ArgumentException ex)
        {
            throw new KnownException(ex.Message, ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new KnownException(ex.Message, ex);
        }

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

        if (string.Equals(candidate.Result, "accepted", StringComparison.Ordinal))
        {
            var existingNaturalKeyScan = await dbContext.ScanRecords.SingleOrDefaultAsync(x =>
                x.OrganizationId == candidate.OrganizationId
                && x.EnvironmentId == candidate.EnvironmentId
                && x.ScannedValue == candidate.ScannedValue
                && x.SourceWorkflow == candidate.SourceWorkflow
                && x.SourceDocumentId == candidate.SourceDocumentId
                && x.Result == candidate.Result,
                cancellationToken);
            if (existingNaturalKeyScan is not null)
            {
                try
                {
                    existingNaturalKeyScan.EnsureSameNaturalKeyPayload(candidate);
                }
                catch (InvalidOperationException ex)
                {
                    throw new KnownException(ex.Message, ex);
                }

                return existingNaturalKeyScan.Id;
            }
        }

        if (string.Equals(candidate.Result, "accepted", StringComparison.Ordinal))
        {
            var duplicateSerializedScan = await dbContext.ScanRecords.AnyAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.IdempotencyKey != request.IdempotencyKey
                && (x.ScannedValue == candidate.ScannedValue
                    || (!string.IsNullOrWhiteSpace(candidate.EpcUri) && x.EpcUri == candidate.EpcUri)
                    || (!string.IsNullOrWhiteSpace(candidate.Gtin)
                        && !string.IsNullOrWhiteSpace(candidate.SerialNumber)
                        && x.Gtin == candidate.Gtin
                        && x.LotNo == candidate.LotNo
                        && x.SerialNumber == candidate.SerialNumber)),
                cancellationToken);
            if (duplicateSerializedScan)
            {
                throw new KnownException("Duplicate serialized barcode scan is not allowed.");
            }
        }

        dbContext.ScanRecords.Add(candidate);
        return candidate.Id;
    }
}
