using Nerv.IIP.Business.Quality.Domain.AggregatesModel.CorrectiveActionAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Approvals;
using Nerv.IIP.Contracts.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.CorrectiveActions;

public sealed record OpenCorrectiveActionCommand(
    string OrganizationId,
    string EnvironmentId,
    string CapaCode,
    NonconformanceReportId? SourceNcrId,
    string RootCause,
    string ContainmentAction,
    string OwnerUserId,
    DateTimeOffset DueAtUtc) : ICommand<CorrectiveActionId>;

public sealed class OpenCorrectiveActionCommandHandler(
    ICorrectiveActionRepository correctiveActionRepository,
    INonconformanceReportRepository nonconformanceReportRepository)
    : ICommandHandler<OpenCorrectiveActionCommand, CorrectiveActionId>
{
    public async Task<CorrectiveActionId> Handle(OpenCorrectiveActionCommand request, CancellationToken cancellationToken)
    {
        CorrectiveAction capa;
        if (request.SourceNcrId is not null)
        {
            var ncr = await nonconformanceReportRepository.GetAsync(request.SourceNcrId, cancellationToken)
                ?? throw new KnownException($"NCR '{request.SourceNcrId}' was not found.");
            capa = CorrectiveAction.OpenFromNcr(
                request.OrganizationId,
                request.EnvironmentId,
                request.CapaCode,
                ncr,
                request.RootCause,
                request.ContainmentAction,
                request.OwnerUserId,
                request.DueAtUtc);
        }
        else
        {
            capa = CorrectiveAction.OpenStandalone(
                request.OrganizationId,
                request.EnvironmentId,
                request.CapaCode,
                request.RootCause,
                request.ContainmentAction,
                request.OwnerUserId,
                request.DueAtUtc);
        }

        await correctiveActionRepository.AddAsync(capa, cancellationToken);
        return capa.Id;
    }
}

public sealed record AddCorrectiveActionItemCommand(
    CorrectiveActionId CorrectiveActionId,
    string ActionType,
    string Description,
    string OwnerUserId,
    DateTimeOffset DueAtUtc) : ICommand;

public sealed class AddCorrectiveActionItemCommandHandler(ICorrectiveActionRepository repository)
    : ICommandHandler<AddCorrectiveActionItemCommand>
{
    public async Task Handle(AddCorrectiveActionItemCommand request, CancellationToken cancellationToken)
    {
        var capa = await repository.GetWithActionsAsync(request.CorrectiveActionId, cancellationToken)
            ?? throw new KnownException($"CAPA '{request.CorrectiveActionId}' was not found.");
        capa.AddAction(request.ActionType, request.Description, request.OwnerUserId, request.DueAtUtc);
    }
}

public sealed record CompleteCorrectiveActionItemCommand(
    CorrectiveActionId CorrectiveActionId,
    CorrectiveActionItemId CorrectiveActionItemId,
    string CompletedByUserId,
    DateTimeOffset CompletedAtUtc) : ICommand;

public sealed class CompleteCorrectiveActionItemCommandHandler(ICorrectiveActionRepository repository)
    : ICommandHandler<CompleteCorrectiveActionItemCommand>
{
    public async Task Handle(CompleteCorrectiveActionItemCommand request, CancellationToken cancellationToken)
    {
        var capa = await repository.GetWithActionsAsync(request.CorrectiveActionId, cancellationToken)
            ?? throw new KnownException($"CAPA '{request.CorrectiveActionId}' was not found.");
        capa.CompleteAction(request.CorrectiveActionItemId, request.CompletedByUserId, request.CompletedAtUtc);
    }
}

public sealed record VerifyCorrectiveActionEffectivenessCommand(
    CorrectiveActionId CorrectiveActionId,
    string VerifiedByUserId,
    string Result,
    DateTimeOffset VerifiedAtUtc,
    InspectionRecordId? EffectivenessInspectionRecordId = null) : ICommand;

public sealed class VerifyCorrectiveActionEffectivenessCommandHandler(
    ICorrectiveActionRepository repository,
    INonconformanceReportRepository nonconformanceReportRepository,
    ApplicationDbContext dbContext)
    : ICommandHandler<VerifyCorrectiveActionEffectivenessCommand>
{
    public async Task Handle(VerifyCorrectiveActionEffectivenessCommand request, CancellationToken cancellationToken)
    {
        var capa = await repository.GetWithActionsAsync(request.CorrectiveActionId, cancellationToken)
            ?? throw new KnownException($"CAPA '{request.CorrectiveActionId}' was not found.");
        var (inspectionRecordId, inspectionResult) = await ResolveEffectivenessInspectionAsync(capa, request, cancellationToken);
        capa.VerifyEffectiveness(
            request.VerifiedByUserId,
            request.Result,
            request.VerifiedAtUtc,
            inspectionRecordId,
            inspectionResult);
        await CorrectiveActionNcrRedrive.CloseRecordedScrapNcrAsync(capa, nonconformanceReportRepository, cancellationToken);
    }

    private async Task<(InspectionRecordId InspectionRecordId, string Result)> ResolveEffectivenessInspectionAsync(
        CorrectiveAction capa,
        VerifyCorrectiveActionEffectivenessCommand request,
        CancellationToken cancellationToken)
    {
        var inspectionRecordId = request.EffectivenessInspectionRecordId;
        if (inspectionRecordId is null)
        {
            throw new KnownException("CAPA effectiveness verification requires a passed verification inspection.");
        }

        var inspection = await dbContext.InspectionRecords
            .SingleOrDefaultAsync(x => x.Id == inspectionRecordId, cancellationToken)
            ?? throw new KnownException($"CAPA effectiveness verification inspection '{inspectionRecordId}' was not found.");
        if (!string.Equals(inspection.OrganizationId, capa.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(inspection.EnvironmentId, capa.EnvironmentId, StringComparison.Ordinal))
        {
            throw new KnownException("CAPA effectiveness verification inspection scope does not match the CAPA.");
        }

        if (!string.Equals(inspection.Result, "passed", StringComparison.OrdinalIgnoreCase))
        {
            throw new KnownException("CAPA effectiveness verification inspection must be passed.");
        }

        return (inspectionRecordId, inspection.Result);
    }
}

public sealed class CapaCloseApprovalOptions
{
    public bool Required { get; set; }
}

public sealed record CloseCorrectiveActionCommand(
    CorrectiveActionId CorrectiveActionId,
    string ClosedByUserId,
    string? CloseApprovalChainId = null) : ICommand;

public sealed class CloseCorrectiveActionCommandHandler(
    ICorrectiveActionRepository repository,
    INonconformanceReportRepository nonconformanceReportRepository,
    IApprovalChainStatusClient approvalChainStatusClient,
    IOptions<CapaCloseApprovalOptions> closeApprovalOptions)
    : ICommandHandler<CloseCorrectiveActionCommand>
{
    public async Task Handle(CloseCorrectiveActionCommand request, CancellationToken cancellationToken)
    {
        var capa = await repository.GetWithActionsAsync(request.CorrectiveActionId, cancellationToken)
            ?? throw new KnownException($"CAPA '{request.CorrectiveActionId}' was not found.");
        if (closeApprovalOptions.Value.Required)
        {
            if (string.IsNullOrWhiteSpace(request.CloseApprovalChainId))
            {
                throw new KnownException("CAPA close requires an approved closure approval chain.");
            }

            if (!await approvalChainStatusClient.IsApprovedForCapaClosureAsync(
                    request.CloseApprovalChainId,
                    capa.OrganizationId,
                    capa.EnvironmentId,
                    capa.CapaCode,
                    cancellationToken))
            {
                throw new KnownException("CAPA closure approval chain is not approved.");
            }
        }

        capa.Close(request.ClosedByUserId, request.CloseApprovalChainId);
        await CorrectiveActionNcrRedrive.CloseRecordedScrapNcrAsync(capa, nonconformanceReportRepository, cancellationToken);
    }
}

internal static class CorrectiveActionNcrRedrive
{
    public static async Task CloseRecordedScrapNcrAsync(
        CorrectiveAction correctiveAction,
        INonconformanceReportRepository nonconformanceReportRepository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(correctiveAction.SourceNcrId)
            || !Guid.TryParse(correctiveAction.SourceNcrId, out var sourceNcrGuid))
        {
            return;
        }

        var ncr = await nonconformanceReportRepository.GetAsync(
            new NonconformanceReportId(sourceNcrGuid),
            cancellationToken);
        if (ncr is null
            || string.Equals(ncr.Status, "closed", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(ncr.DispositionType, QualityNcrDispositionTypes.Scrap, StringComparison.OrdinalIgnoreCase)
            || !NonconformanceReport.RequiresEffectiveCapa(ncr.SourceType, ncr.DispositionType)
            || string.IsNullOrWhiteSpace(ncr.ScrapMovementId))
        {
            return;
        }

        ncr.Close(null, null, null, "CAPA effectiveness verified");
    }
}
