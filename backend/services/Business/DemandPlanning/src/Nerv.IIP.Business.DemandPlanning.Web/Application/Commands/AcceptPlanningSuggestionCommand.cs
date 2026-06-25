using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Contracts.DemandPlanning;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;

public sealed record AcceptPlanningSuggestionCommand(
    PlanningSuggestionId SuggestionId,
    string DownstreamService,
    string DownstreamDocumentType,
    string? DownstreamDocumentId,
    string? IdempotencyKey = null) : ICommand;

public sealed class AcceptPlanningSuggestionCommandValidator : AbstractValidator<AcceptPlanningSuggestionCommand>
{
    public AcceptPlanningSuggestionCommandValidator()
    {
        RuleFor(x => x.SuggestionId).NotEmpty();
        RuleFor(x => x.DownstreamService).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DownstreamDocumentType).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DownstreamDocumentId).MaximumLength(128);
        RuleFor(x => x.IdempotencyKey).MaximumLength(150);
    }
}

public sealed record PlanningSuggestionDownstreamRequest(
    string DownstreamService,
    string DownstreamDocumentType,
    string? DownstreamDocumentId,
    string IdempotencyKey);

public sealed record PlanningSuggestionDownstreamReference(
    string DownstreamService,
    string DownstreamDocumentType,
    string? DownstreamDocumentId);

public interface IPlanningSuggestionDownstreamBridge
{
    Task<PlanningSuggestionDownstreamReference> CreateDownstreamAsync(
        PlanningSuggestion suggestion,
        PlanningSuggestionDownstreamRequest request,
        CancellationToken cancellationToken);
}

public sealed class UnsupportedPlanningSuggestionDownstreamBridge : IPlanningSuggestionDownstreamBridge
{
    public Task<PlanningSuggestionDownstreamReference> CreateDownstreamAsync(
        PlanningSuggestion suggestion,
        PlanningSuggestionDownstreamRequest request,
        CancellationToken cancellationToken)
    {
        throw new KnownException($"Planning suggestion downstream bridge is not configured for {request.DownstreamService}/{request.DownstreamDocumentType}.");
    }
}

public sealed class AcceptPlanningSuggestionCommandHandler(
    ApplicationDbContext dbContext,
    IPlanningSuggestionDownstreamBridge? downstreamBridge = null)
    : ICommandHandler<AcceptPlanningSuggestionCommand>
{
    public async Task Handle(AcceptPlanningSuggestionCommand request, CancellationToken cancellationToken)
    {
        var suggestion = await dbContext.PlanningSuggestions
            .Include(x => x.PeggingLinks)
            .SingleOrDefaultAsync(x => x.Id == request.SuggestionId, cancellationToken)
            ?? throw new KnownException($"Planning suggestion was not found: {request.SuggestionId}");
        var downstreamReference = await ResolveDownstreamReferenceAsync(suggestion, request, cancellationToken);
        try
        {
            suggestion.Accept(
                downstreamReference.DownstreamService,
                downstreamReference.DownstreamDocumentType,
                downstreamReference.DownstreamDocumentId);
        }
        catch (InvalidOperationException ex)
        {
            throw new KnownException(ex.Message);
        }
    }

    private async Task<PlanningSuggestionDownstreamReference> ResolveDownstreamReferenceAsync(
        PlanningSuggestion suggestion,
        AcceptPlanningSuggestionCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.DownstreamDocumentId))
        {
            return new PlanningSuggestionDownstreamReference(
                request.DownstreamService,
                request.DownstreamDocumentType,
                request.DownstreamDocumentId.Trim());
        }

        if (suggestion.Status == PlanningSuggestionStatus.Accepted &&
            IsSameDownstreamTarget(suggestion, request))
        {
            return new PlanningSuggestionDownstreamReference(
                suggestion.AcceptedDownstreamService ?? request.DownstreamService,
                suggestion.AcceptedDownstreamDocumentType ?? request.DownstreamDocumentType,
                suggestion.AcceptedDownstreamDocumentId);
        }

        EnsureCanCreateDownstreamReference(suggestion);
        if (IsErpPurchaseRequisitionAcceptance(suggestion, request))
        {
            return new PlanningSuggestionDownstreamReference(
                DemandPlanningDownstreamReferences.BusinessErp,
                DemandPlanningDownstreamReferences.PurchaseRequisition,
                null);
        }

        var bridge = downstreamBridge ?? new UnsupportedPlanningSuggestionDownstreamBridge();
        return await bridge.CreateDownstreamAsync(
            suggestion,
            new PlanningSuggestionDownstreamRequest(
                request.DownstreamService,
                request.DownstreamDocumentType,
                request.DownstreamDocumentId,
                string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    ? $"demand-planning:accept:{suggestion.OrganizationId}:{suggestion.EnvironmentId}:{suggestion.Id}"
                    : request.IdempotencyKey.Trim()),
            cancellationToken);
    }

    private static bool IsSameDownstreamTarget(PlanningSuggestion suggestion, AcceptPlanningSuggestionCommand request)
    {
        return string.Equals(suggestion.AcceptedDownstreamService, request.DownstreamService, StringComparison.OrdinalIgnoreCase)
            && string.Equals(suggestion.AcceptedDownstreamDocumentType, request.DownstreamDocumentType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(suggestion.AcceptedDownstreamDocumentId, NormalizeOptional(request.DownstreamDocumentId), StringComparison.Ordinal);
    }

    private static bool IsErpPurchaseRequisitionAcceptance(
        PlanningSuggestion suggestion,
        AcceptPlanningSuggestionCommand request)
    {
        return string.Equals(suggestion.SuggestionType, DemandPlanningSuggestionTypes.PlannedPurchase, StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.DownstreamService, DemandPlanningDownstreamReferences.BusinessErp, StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.DownstreamDocumentType, DemandPlanningDownstreamReferences.PurchaseRequisition, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(request.DownstreamDocumentId);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void EnsureCanCreateDownstreamReference(PlanningSuggestion suggestion)
    {
        if (suggestion.Status != PlanningSuggestionStatus.Open)
        {
            throw new KnownException("Only open planning suggestions can be accepted.");
        }
    }
}
