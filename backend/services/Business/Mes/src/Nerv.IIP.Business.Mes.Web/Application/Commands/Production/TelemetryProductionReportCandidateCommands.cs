using Microsoft.EntityFrameworkCore;
using MediatR;
using FluentValidation;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Production;

public sealed record PromoteTelemetryProductionReportCandidateCommand(
    string OrganizationId, string EnvironmentId, TelemetryProductionReportCandidateId CandidateId,
    string WorkOrderId, string OperationTaskId, string Actor, DateTimeOffset ConfirmedAtUtc) : ICommand<ProductionReportCommandResult>;
public sealed class PromoteTelemetryProductionReportCandidateCommandValidator : AbstractValidator<PromoteTelemetryProductionReportCandidateCommand>
{
    public PromoteTelemetryProductionReportCandidateCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100); RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100); RuleFor(x => x.OperationTaskId).NotEmpty().MaximumLength(100); RuleFor(x => x.Actor).NotEmpty().MaximumLength(200);
    }
}

public sealed class PromoteTelemetryProductionReportCandidateCommandHandler(ApplicationDbContext dbContext, ISender sender)
    : ICommandHandler<PromoteTelemetryProductionReportCandidateCommand, ProductionReportCommandResult>
{
    public async Task<ProductionReportCommandResult> Handle(PromoteTelemetryProductionReportCandidateCommand request, CancellationToken cancellationToken)
    {
        var candidate = await dbContext.TelemetryProductionReportCandidates.Include(x => x.Transitions).SingleOrDefaultAsync(
            x => x.Id == request.CandidateId && x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId,
            cancellationToken) ?? throw new KnownException("Telemetry production report candidate was not found.");
        if (candidate.Status == TelemetryProductionReportCandidate.ConfirmedStatus && candidate.ProductionReportId is not null)
        {
            var productionReportId = new ProductionReportId(Guid.Parse(candidate.ProductionReportId));
            var existing = await dbContext.ProductionReports.SingleAsync(x => x.Id == productionReportId, cancellationToken);
            return new ProductionReportCommandResult(existing.Id, existing.ReportNo);
        }

        // RecordProductionReportCommand owns its transaction. If candidate confirmation is interrupted,
        // replay returns the same report through the stable telemetry idempotency key before closing the candidate.
        var result = await sender.Send(new RecordProductionReportCommand(
            request.OrganizationId, request.EnvironmentId, request.WorkOrderId, request.OperationTaskId,
            candidate.GoodQuantity, 0m, false, candidate.BucketEndUtc,
            $"telemetry:{candidate.SourceIdempotencyKey}", Source: ProductionReport.TelemetrySource), cancellationToken);
        candidate.Confirm(request.WorkOrderId, request.OperationTaskId, request.Actor, request.ConfirmedAtUtc, result.Id.ToString());
        return result;
    }
}

public sealed record DismissTelemetryProductionReportCandidateCommand(
    string OrganizationId, string EnvironmentId, TelemetryProductionReportCandidateId CandidateId,
    string Reason, string Actor, DateTimeOffset DismissedAtUtc) : ICommand;
public sealed class DismissTelemetryProductionReportCandidateCommandValidator : AbstractValidator<DismissTelemetryProductionReportCandidateCommand>
{
    public DismissTelemetryProductionReportCandidateCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100); RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500); RuleFor(x => x.Actor).NotEmpty().MaximumLength(200);
    }
}

public sealed class DismissTelemetryProductionReportCandidateCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<DismissTelemetryProductionReportCandidateCommand>
{
    public async Task Handle(DismissTelemetryProductionReportCandidateCommand request, CancellationToken cancellationToken)
    {
        var candidate = await dbContext.TelemetryProductionReportCandidates.Include(x => x.Transitions).SingleOrDefaultAsync(
            x => x.Id == request.CandidateId && x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId,
            cancellationToken) ?? throw new KnownException("Telemetry production report candidate was not found.");
        candidate.Dismiss(request.Reason, request.Actor, request.DismissedAtUtc);
    }
}
