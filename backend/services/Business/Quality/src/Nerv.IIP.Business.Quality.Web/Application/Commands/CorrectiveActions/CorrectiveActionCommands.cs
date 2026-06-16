using Nerv.IIP.Business.Quality.Domain.AggregatesModel.CorrectiveActionAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;

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

public sealed record VerifyCorrectiveActionEffectivenessCommand(
    CorrectiveActionId CorrectiveActionId,
    string VerifiedByUserId,
    string Result,
    DateTimeOffset VerifiedAtUtc) : ICommand;

public sealed class VerifyCorrectiveActionEffectivenessCommandHandler(ICorrectiveActionRepository repository)
    : ICommandHandler<VerifyCorrectiveActionEffectivenessCommand>
{
    public async Task Handle(VerifyCorrectiveActionEffectivenessCommand request, CancellationToken cancellationToken)
    {
        var capa = await repository.GetWithActionsAsync(request.CorrectiveActionId, cancellationToken)
            ?? throw new KnownException($"CAPA '{request.CorrectiveActionId}' was not found.");
        capa.VerifyEffectiveness(request.VerifiedByUserId, request.Result, request.VerifiedAtUtc);
    }
}

public sealed record CloseCorrectiveActionCommand(CorrectiveActionId CorrectiveActionId, string ClosedByUserId) : ICommand;

public sealed class CloseCorrectiveActionCommandHandler(ICorrectiveActionRepository repository)
    : ICommandHandler<CloseCorrectiveActionCommand>
{
    public async Task Handle(CloseCorrectiveActionCommand request, CancellationToken cancellationToken)
    {
        var capa = await repository.GetWithActionsAsync(request.CorrectiveActionId, cancellationToken)
            ?? throw new KnownException($"CAPA '{request.CorrectiveActionId}' was not found.");
        capa.Close(request.ClosedByUserId);
    }
}
