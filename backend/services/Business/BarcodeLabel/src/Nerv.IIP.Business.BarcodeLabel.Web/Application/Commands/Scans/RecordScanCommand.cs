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
    string? RejectionReason) : ICommand<ScanRecordId>;

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
            request.RejectionReason);

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
