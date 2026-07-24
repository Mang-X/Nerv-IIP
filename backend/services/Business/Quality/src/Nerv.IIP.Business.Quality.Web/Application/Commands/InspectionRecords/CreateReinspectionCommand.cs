using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.MeasuringDeviceAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.InspectionRecords;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;

public sealed record CreateReinspectionCommand(
    InspectionRecordId ReinspectionOfInspectionRecordId,
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<InspectionResultLineCommandInput> ResultLines,
    string? DispositionReason,
    IReadOnlyCollection<string> DispositionAttachmentFileIds,
    MeasuringDeviceId? MeasuringDeviceId = null) : ICommand<CreateReinspectionResult>;

public sealed record CreateReinspectionResult(
    InspectionRecordId InspectionRecordId,
    int AttemptNumber);

public sealed class CreateReinspectionCommandValidator : AbstractValidator<CreateReinspectionCommand>
{
    public CreateReinspectionCommandValidator()
    {
        RuleFor(x => x.ReinspectionOfInspectionRecordId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ResultLines).NotEmpty();
        RuleFor(x => x.DispositionReason)
            .NotEmpty()
            .When(command => command.ResultLines.Any(line =>
                !string.Equals(line.Result, InspectionLineResults.Passed, StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Disposition reason is required when any reinspection result line is rejected or conditionally released.");
    }
}

public sealed class CreateReinspectionCommandHandler(
    IInspectionRecordRepository inspectionRecordRepository,
    IInspectionPlanRepository inspectionPlanRepository,
    IInspectionUomConversionClient? uomConversionClient = null,
    ApplicationDbContext? dbContext = null,
    IConfiguration? configuration = null,
    TimeProvider? timeProvider = null)
    : ICommandHandler<CreateReinspectionCommand, CreateReinspectionResult>
{
    private readonly IInspectionUomConversionClient uomConversionClient =
        uomConversionClient ?? NullInspectionUomConversionClient.Instance;
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<CreateReinspectionResult> Handle(
        CreateReinspectionCommand request,
        CancellationToken cancellationToken)
    {
        var previous = await inspectionRecordRepository.GetScopedAsync(
                request.ReinspectionOfInspectionRecordId,
                request.OrganizationId,
                request.EnvironmentId,
                cancellationToken)
            ?? throw new KnownException(
                $"Inspection record '{request.ReinspectionOfInspectionRecordId}' was not found.");
        // Translate the aggregate's terminal-pass invariant into the service's stable known-error contract.
        // InspectionRecord.Reinspect repeats the guard for non-HTTP callers that bypass this handler.
        if (string.Equals(previous.Result, InspectionRecordResults.Passed, StringComparison.Ordinal))
        {
            throw new KnownException("Passed inspection records are terminal and cannot be reinspected.");
        }

        var existing = await inspectionRecordRepository.FindByReinspectionOfAsync(
            previous.Id,
            cancellationToken);
        if (existing is not null)
        {
            return new CreateReinspectionResult(existing.Id, existing.AttemptNumber);
        }

        var plan = previous.InspectionPlanId is null
            ? null
            : await inspectionPlanRepository.GetWithCharacteristicsAsync(
                    previous.OrganizationId,
                    previous.EnvironmentId,
                    previous.InspectionPlanId,
                    cancellationToken)
                ?? throw new KnownException(
                    $"Inspection plan '{previous.InspectionPlanId}' was not found.");
        var conversions = plan is null
            ? []
            : await uomConversionClient.GetConversionsAsync(
                previous.OrganizationId,
                previous.EnvironmentId,
                cancellationToken);
        var measuringDeviceUsage = await ResolveMeasuringDeviceUsageAsync(request, cancellationToken);
        var resultLines = request.ResultLines.Select(line => new InspectionResultLineInput(
            line.CharacteristicCode,
            line.ObservedValue,
            line.UnitCode,
            line.Result,
            line.DefectReason,
            line.DefectQuantity,
            line.AttachmentFileIds,
            line.MeasuredValue)).ToArray();
        var reinspection = InspectionRecord.Reinspect(
            previous,
            plan,
            resultLines,
            request.DispositionReason,
            request.DispositionAttachmentFileIds,
            conversions,
            measuringDeviceUsage);

        await inspectionRecordRepository.AddAsync(reinspection, cancellationToken);
        return new CreateReinspectionResult(reinspection.Id, reinspection.AttemptNumber);
    }

    private async Task<InspectionMeasuringDeviceUsage?> ResolveMeasuringDeviceUsageAsync(
        CreateReinspectionCommand request,
        CancellationToken cancellationToken)
    {
        if (request.MeasuringDeviceId is null)
        {
            return null;
        }

        if (dbContext is null)
        {
            throw new KnownException("Measuring-device traceability is unavailable.");
        }

        var device = await dbContext.MeasuringDevices.SingleOrDefaultAsync(
                x => x.Id == request.MeasuringDeviceId,
                cancellationToken)
            ?? throw new KnownException("Measuring device was not found.");
        if (device.OrganizationId != request.OrganizationId
            || device.EnvironmentId != request.EnvironmentId)
        {
            throw new KnownException("Measuring device does not belong to the inspection scope.");
        }

        var usage = InspectionMeasuringDeviceUsage.Create(device, timeProvider.GetUtcNow());
        var policy = configuration?["Quality:MeasuringDevice:ExpiredInspectionPolicy"] ?? "warn";
        if (MeasuringDeviceInspectionPolicy.Blocks(policy, usage.CalibrationState))
        {
            throw new KnownException(
                "The selected measuring device has expired calibration and inspection entry is blocked.");
        }

        return usage;
    }
}

public sealed class CreateReinspectionUniqueConflictBehavior(
    ApplicationDbContext dbContext,
    IQualityPersistenceConflictClassifier conflictClassifier)
    : IPipelineBehavior<CreateReinspectionCommand, CreateReinspectionResult>
{
    public async Task<CreateReinspectionResult> Handle(
        CreateReinspectionCommand request,
        RequestHandlerDelegate<CreateReinspectionResult> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken);
        }
        catch (DbUpdateException exception) when (conflictClassifier.IsReinspectionConflict(exception))
        {
            dbContext.ChangeTracker.Clear();
            return await next(cancellationToken);
        }
    }
}
