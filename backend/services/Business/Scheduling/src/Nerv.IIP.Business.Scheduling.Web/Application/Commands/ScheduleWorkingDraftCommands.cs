using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleWorkingDraftAggregate;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record SaveScheduleWorkingDraftCommand(string OrganizationId, string EnvironmentId, string PlanId,
    string UserId, SchedulingWorkingDraftStateContract State) : ICommand<SchedulingWorkingDraftContract>;

public sealed class SaveScheduleWorkingDraftCommandValidator : AbstractValidator<SaveScheduleWorkingDraftCommand>
{
    public SaveScheduleWorkingDraftCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(96);
        RuleFor(x => x.State).NotNull();
        When(x => x.State is not null, () =>
        {
            RuleFor(x => x.State.ContractVersion).Equal(1);
            RuleFor(x => x.State.Orders).NotNull();
            RuleFor(x => x.State.Tasks).NotNull();
            RuleFor(x => x.State.PendingOperations).NotNull();
        });
    }
}

public sealed class SaveScheduleWorkingDraftCommandHandler(ApplicationDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<SaveScheduleWorkingDraftCommand, SchedulingWorkingDraftContract>
{
    public async Task<SchedulingWorkingDraftContract> Handle(SaveScheduleWorkingDraftCommand request, CancellationToken cancellationToken)
    {
        if (!await dbContext.SchedulePlans.AnyAsync(x => x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId && x.PlanId == request.PlanId, cancellationToken))
            throw new KnownException($"未找到排程方案，请刷新后重试，方案 ID = {request.PlanId}");
        var draft = await dbContext.ScheduleWorkingDrafts.SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId && x.PlanId == request.PlanId && x.UserId == request.UserId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var json = JsonSerializer.Serialize(request.State, SchedulingJson.Options);
        if (draft is null)
            dbContext.ScheduleWorkingDrafts.Add(new ScheduleWorkingDraft(request.OrganizationId, request.EnvironmentId,
                request.PlanId, request.UserId, json, now));
        else
            draft.Replace(json, now);
        return new SchedulingWorkingDraftContract(request.PlanId, now, request.State);
    }
}

public sealed record ClearScheduleWorkingDraftCommand(string OrganizationId, string EnvironmentId, string PlanId, string UserId)
    : ICommand;

public sealed class ClearScheduleWorkingDraftCommandValidator : AbstractValidator<ClearScheduleWorkingDraftCommand>
{
    public ClearScheduleWorkingDraftCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(96);
    }
}

public sealed class ClearScheduleWorkingDraftCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ClearScheduleWorkingDraftCommand>
{
    public async Task Handle(ClearScheduleWorkingDraftCommand request, CancellationToken cancellationToken)
    {
        var draft = await dbContext.ScheduleWorkingDrafts.SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId && x.PlanId == request.PlanId && x.UserId == request.UserId, cancellationToken);
        if (draft is not null) dbContext.ScheduleWorkingDrafts.Remove(draft);
    }
}
