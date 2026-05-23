using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;

public sealed record AcceptPlanningSuggestionCommand(
    PlanningSuggestionId SuggestionId,
    string DownstreamService,
    string DownstreamDocumentType,
    string DownstreamDocumentId) : ICommand;

public sealed class AcceptPlanningSuggestionCommandValidator : AbstractValidator<AcceptPlanningSuggestionCommand>
{
    public AcceptPlanningSuggestionCommandValidator()
    {
        RuleFor(x => x.SuggestionId).NotEmpty();
        RuleFor(x => x.DownstreamService).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DownstreamDocumentType).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DownstreamDocumentId).NotEmpty().MaximumLength(128);
    }
}

public sealed class AcceptPlanningSuggestionCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<AcceptPlanningSuggestionCommand>
{
    public async Task Handle(AcceptPlanningSuggestionCommand request, CancellationToken cancellationToken)
    {
        var suggestion = await dbContext.PlanningSuggestions.SingleOrDefaultAsync(x => x.Id == request.SuggestionId, cancellationToken)
            ?? throw new KnownException($"Planning suggestion was not found: {request.SuggestionId}");
        try
        {
            suggestion.Accept(request.DownstreamService, request.DownstreamDocumentType, request.DownstreamDocumentId);
        }
        catch (InvalidOperationException ex)
        {
            throw new KnownException(ex.Message);
        }
    }
}
