using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record PreviewSchedulingCandidatesCommand(SchedulingCandidatePreviewRequestContract Input) : ICommand<SchedulingCandidateSetContract>;
public sealed class PreviewSchedulingCandidatesCommandValidator : AbstractValidator<PreviewSchedulingCandidatesCommand>
{
    public PreviewSchedulingCandidatesCommandValidator()
    {
        RuleFor(x => x.Input.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Input.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Input.BaselinePlanId).NotEmpty().MaximumLength(96);
    }
}
public sealed class PreviewSchedulingCandidatesCommandHandler(SchedulingCandidateService candidates)
    : ICommandHandler<PreviewSchedulingCandidatesCommand, SchedulingCandidateSetContract>
{
    public Task<SchedulingCandidateSetContract> Handle(PreviewSchedulingCandidatesCommand request, CancellationToken ct) =>
        candidates.PreviewAsync(request.Input, ct);
}
public sealed record SelectSchedulingCandidateCommand(SchedulingCandidateSelectRequestContract Input, string UserId)
    : ICommand<SchedulingCandidateSelectionContract>;
public sealed class SelectSchedulingCandidateCommandValidator : AbstractValidator<SelectSchedulingCandidateCommand>
{
    public SelectSchedulingCandidateCommandValidator()
    {
        RuleFor(x => x.Input.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Input.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Input.BaselinePlanId).NotEmpty().MaximumLength(96);
        RuleFor(x => x.Input.InputFingerprint).NotEmpty().Length(64);
        RuleFor(x => x.Input.Strategy).IsInEnum();
        RuleFor(x => x.Input.AsOfUtc).NotEqual(default(DateTimeOffset));
    }
}
public sealed class SelectSchedulingCandidateCommandHandler(SchedulingCandidateService candidates)
    : ICommandHandler<SelectSchedulingCandidateCommand, SchedulingCandidateSelectionContract>
{
    public Task<SchedulingCandidateSelectionContract> Handle(SelectSchedulingCandidateCommand request, CancellationToken ct) =>
        candidates.SelectAsync(request.Input, request.UserId, ct);
}
