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
    string? IdempotencyKey = null) : ICommand<AcceptPlanningSuggestionResult>;

public sealed record AcceptPlanningSuggestionResult(
    string DownstreamService,
    string DownstreamDocumentType,
    string? DownstreamDocumentId);

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
    : ICommandHandler<AcceptPlanningSuggestionCommand, AcceptPlanningSuggestionResult>
{
    public async Task<AcceptPlanningSuggestionResult> Handle(AcceptPlanningSuggestionCommand request, CancellationToken cancellationToken)
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
            return new AcceptPlanningSuggestionResult(
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
        if (suggestion.Status == PlanningSuggestionStatus.Accepted &&
            IsSameDownstreamTarget(suggestion, request))
        {
            return new PlanningSuggestionDownstreamReference(
                suggestion.AcceptedDownstreamService ?? request.DownstreamService,
                suggestion.AcceptedDownstreamDocumentType ?? request.DownstreamDocumentType,
                suggestion.AcceptedDownstreamDocumentId);
        }

        if (suggestion.Status == PlanningSuggestionStatus.Accepted)
        {
            throw new KnownException("Planning suggestion has already been accepted with a different downstream reference.");
        }

        EnsureCanCreateDownstreamReference(suggestion);
        if (IsBridgeManagedDownstreamTarget(suggestion, request))
        {
            return await CreateDownstreamReferenceAsync(suggestion, request, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(request.DownstreamDocumentId))
        {
            return new PlanningSuggestionDownstreamReference(
                request.DownstreamService,
                request.DownstreamDocumentType,
                request.DownstreamDocumentId.Trim());
        }

        return await CreateDownstreamReferenceAsync(suggestion, request, cancellationToken);
    }

    private Task<PlanningSuggestionDownstreamReference> CreateDownstreamReferenceAsync(
        PlanningSuggestion suggestion,
        AcceptPlanningSuggestionCommand request,
        CancellationToken cancellationToken)
    {
        var bridge = downstreamBridge ?? new UnsupportedPlanningSuggestionDownstreamBridge();
        return bridge.CreateDownstreamAsync(
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
            && (IsErpPurchaseRequisitionTarget(request)
                || string.IsNullOrWhiteSpace(request.DownstreamDocumentId)
                || string.Equals(suggestion.AcceptedDownstreamDocumentId, NormalizeOptional(request.DownstreamDocumentId), StringComparison.Ordinal));
    }

    private static bool IsErpPurchaseRequisitionTarget(AcceptPlanningSuggestionCommand request)
    {
        return string.Equals(request.DownstreamService, DemandPlanningDownstreamReferences.BusinessErp, StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.DownstreamDocumentType, DemandPlanningDownstreamReferences.PurchaseRequisition, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBridgeManagedDownstreamTarget(PlanningSuggestion suggestion, AcceptPlanningSuggestionCommand request)
    {
        return (string.Equals(suggestion.SuggestionType, DemandPlanningSuggestionTypes.PlannedPurchase, StringComparison.OrdinalIgnoreCase)
                && IsErpPurchaseRequisitionTarget(request))
            || (string.Equals(suggestion.SuggestionType, DemandPlanningSuggestionTypes.PlannedWorkOrder, StringComparison.OrdinalIgnoreCase)
                && string.Equals(request.DownstreamService, DemandPlanningDownstreamReferences.BusinessMes, StringComparison.OrdinalIgnoreCase)
                && string.Equals(request.DownstreamDocumentType, DemandPlanningDownstreamReferences.WorkOrder, StringComparison.OrdinalIgnoreCase));
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
