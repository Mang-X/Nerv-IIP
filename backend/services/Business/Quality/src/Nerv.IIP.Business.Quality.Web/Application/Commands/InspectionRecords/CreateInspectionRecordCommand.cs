using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.InspectionRecords;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;

public sealed record InspectionResultLineCommandInput(
    string CharacteristicCode,
    string ObservedValue,
    string? UnitCode,
    string Result,
    string? DefectReason,
    decimal? DefectQuantity,
    IReadOnlyCollection<string> AttachmentFileIds,
    decimal? MeasuredValue = null);

public sealed record StockReleaseDimensionCommandInput(
    string UomCode,
    string SiteCode,
    string LocationCode,
    string SourceQualityStatus,
    string OwnerType,
    string? OwnerId);

public sealed record CreateInspectionRecordCommand(
    string OrganizationId,
    string EnvironmentId,
    InspectionPlanId? InspectionPlanId,
    string SourceType,
    string SourceService,
    string SourceDocumentId,
    string SkuCode,
    decimal InspectedQuantity,
    string? BatchNo,
    string? SerialNo,
    IReadOnlyCollection<InspectionResultLineCommandInput> ResultLines,
    string? DispositionReason,
    IReadOnlyCollection<string> DispositionAttachmentFileIds,
    StockReleaseDimensionCommandInput? StockRelease = null) : ICommand<InspectionRecordId>;

public sealed class CreateInspectionRecordCommandValidator : AbstractValidator<CreateInspectionRecordCommand>
{
    public CreateInspectionRecordCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SourceService).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.InspectedQuantity).GreaterThan(0);
        RuleFor(x => x.ResultLines).NotEmpty();
        RuleFor(x => x.DispositionReason)
            .NotEmpty()
            .When(HasNonPassedResultLine)
            .WithMessage("Disposition reason is required when any inspection result line is rejected or conditionally released.");
    }

    private static bool HasNonPassedResultLine(CreateInspectionRecordCommand command)
    {
        return command.ResultLines?.Any(line =>
            !string.Equals(line.Result, InspectionLineResults.Passed, StringComparison.OrdinalIgnoreCase)) == true;
    }
}

public sealed class CreateInspectionRecordCommandHandler(
    IInspectionRecordRepository repository,
    IInspectionPlanRepository inspectionPlanRepository,
    IInspectionUomConversionClient? uomConversionClient = null,
    IInspectionSourceDocumentVerifier? sourceDocumentVerifier = null)
    : ICommandHandler<CreateInspectionRecordCommand, InspectionRecordId>
{
    private readonly IInspectionUomConversionClient uomConversionClient = uomConversionClient ?? NullInspectionUomConversionClient.Instance;
    private readonly IInspectionSourceDocumentVerifier sourceDocumentVerifier = sourceDocumentVerifier ?? NullInspectionSourceDocumentVerifier.Instance;

    public async Task<InspectionRecordId> Handle(CreateInspectionRecordCommand request, CancellationToken cancellationToken)
    {
        var existing = await repository.FindBySourceDocumentAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.SourceType.Trim().ToLowerInvariant(),
            request.SourceService.Trim().ToLowerInvariant(),
            request.SkuCode.Trim(),
            request.SourceDocumentId.Trim(),
            cancellationToken);
        if (existing is not null)
        {
            if (existing.InspectedQuantity != request.InspectedQuantity)
            {
                throw new KnownException("Inspection source document and SKU already have a record with a different inspected quantity.");
            }

            return existing.Id;
        }

        await VerifySourceDocumentAsync(request, cancellationToken);

        var lines = request.ResultLines.Select(x => new InspectionResultLineInput(
            x.CharacteristicCode,
            x.ObservedValue,
            x.UnitCode,
            x.Result,
            x.DefectReason,
            x.DefectQuantity,
            x.AttachmentFileIds,
            x.MeasuredValue)).ToArray();
        var stockRelease = request.StockRelease is null
            ? null
            : StockReleaseDimension.Create(
                request.StockRelease.UomCode,
                request.StockRelease.SiteCode,
                request.StockRelease.LocationCode,
                request.StockRelease.SourceQualityStatus,
                request.StockRelease.OwnerType,
                request.StockRelease.OwnerId);
        InspectionRecord record;
        if (request.InspectionPlanId is not null)
        {
            var plan = await inspectionPlanRepository.GetWithCharacteristicsAsync(
                    request.OrganizationId,
                    request.EnvironmentId,
                    request.InspectionPlanId,
                    cancellationToken)
                ?? throw new KnownException($"Inspection plan '{request.InspectionPlanId}' was not found.");
            var uomConversions = await uomConversionClient.GetConversionsAsync(
                request.OrganizationId,
                request.EnvironmentId,
                cancellationToken);
            record = InspectionRecord.CreateFromPlan(
                plan,
                request.SourceType,
                request.SourceService,
                request.SourceDocumentId,
                request.SkuCode,
                request.InspectedQuantity,
                request.BatchNo,
                request.SerialNo,
                stockRelease,
                lines,
                request.DispositionReason,
                request.DispositionAttachmentFileIds,
                uomConversions);
        }
        else
        {
            record = InspectionRecord.Create(
                request.OrganizationId,
                request.EnvironmentId,
                request.InspectionPlanId,
                request.SourceType,
                request.SourceService,
                request.SourceDocumentId,
                request.SkuCode,
                request.InspectedQuantity,
                request.BatchNo,
                request.SerialNo,
                lines,
                request.DispositionReason,
                request.DispositionAttachmentFileIds,
                stockRelease);
        }

        await repository.AddAsync(record, cancellationToken);
        return record.Id;
    }

    private async Task VerifySourceDocumentAsync(CreateInspectionRecordCommand request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.SourceType, "receiving", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var verification = await sourceDocumentVerifier.VerifyAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.SourceType,
            request.SourceService,
            request.SourceDocumentId,
            request.SkuCode,
            request.InspectedQuantity,
            cancellationToken);
        if (!verification.Exists)
        {
            throw new KnownException(verification.Message ?? $"Inspection source document '{request.SourceDocumentId}' was not found.");
        }

        if (!string.IsNullOrWhiteSpace(verification.SkuCode)
            && !string.Equals(verification.SkuCode, request.SkuCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new KnownException("Inspection source document SKU does not match the inspected SKU.");
        }

        if (verification.Quantity is { } sourceQuantity && request.InspectedQuantity > sourceQuantity)
        {
            throw new KnownException("Inspection quantity cannot exceed the source document quantity.");
        }
    }
}
