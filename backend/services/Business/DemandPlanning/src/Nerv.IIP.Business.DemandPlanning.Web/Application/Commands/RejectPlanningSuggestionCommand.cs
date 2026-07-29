using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;

public sealed record RejectPlanningSuggestionCommand(
    PlanningSuggestionId SuggestionId,
    string RejectedBy,
    string Reason) : ICommand;

public sealed class RejectPlanningSuggestionCommandValidator : AbstractValidator<RejectPlanningSuggestionCommand>
{
    public RejectPlanningSuggestionCommandValidator()
    {
        RuleFor(x => x.SuggestionId).NotEmpty();
        RuleFor(x => x.RejectedBy).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(128);
    }
}

public sealed class RejectPlanningSuggestionCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RejectPlanningSuggestionCommand>
{
    public async Task Handle(RejectPlanningSuggestionCommand request, CancellationToken cancellationToken)
    {
        var suggestion = await dbContext.PlanningSuggestions
            .SingleOrDefaultAsync(x => x.Id == request.SuggestionId, cancellationToken)
            ?? throw new KnownException($"Planning suggestion was not found: {request.SuggestionId}");
        if (suggestion.Status == PlanningSuggestionStatus.Rejected)
        {
            // Replayed rejections are tolerated; the original rejection reason is preserved.
            return;
        }

        try
        {
            suggestion.Reject(request.RejectedBy, request.Reason);
        }
        catch (InvalidOperationException ex)
        {
            throw new KnownException(ex.Message);
        }
    }
}
