using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalTemplateAggregate;
using Nerv.IIP.Business.Approval.Web.Application.Validation;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Approval.Web.Application.Commands.Chains;

public sealed record StartApprovalChainCommand(
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode,
    string SourceService,
    string DocumentType,
    string DocumentId,
    string? DocumentLineId,
    string StartedBy,
    decimal? Amount = null,
    string? RoutingOrganizationId = null,
    string? DepartmentId = null) : ICommand<ApprovalChainId>;

public sealed class StartApprovalChainCommandValidator : AbstractValidator<StartApprovalChainCommand>
{
    public StartApprovalChainCommandValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredApprovalCode(100);
        RuleFor(x => x.EnvironmentId).RequiredApprovalCode(100);
        RuleFor(x => x.TemplateCode).RequiredApprovalCode(100);
        RuleFor(x => x.SourceService).RequiredApprovalCode(100);
        RuleFor(x => x.DocumentType).RequiredApprovalCode(100);
        RuleFor(x => x.DocumentId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DocumentLineId).MaximumLength(150);
        RuleFor(x => x.StartedBy).RequiredApprovalCode(150);
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).When(x => x.Amount.HasValue);
        RuleFor(x => x.RoutingOrganizationId).OptionalApprovalCode(100);
        RuleFor(x => x.DepartmentId).OptionalApprovalCode(100);
    }
}

public sealed class StartApprovalChainCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<StartApprovalChainCommand, ApprovalChainId>
{
    private const string PendingIdentityUniqueIndex = "IX_approval_chains_pending_identity_key";
    private const string DuplicateRecoverySavepoint = "approval_chain_start_duplicate";

    public async Task<ApprovalChainId> Handle(StartApprovalChainCommand request, CancellationToken cancellationToken)
    {
        var documentReference = new ApprovalDocumentReference(
            request.SourceService,
            request.DocumentType,
            request.DocumentId,
            request.DocumentLineId,
            request.Amount,
            request.RoutingOrganizationId ?? request.OrganizationId,
            request.DepartmentId);
        var pendingIdentityKey = ApprovalChain.BuildPendingIdentityKey(request.OrganizationId, request.EnvironmentId, request.TemplateCode, documentReference);
        var existingChainId = await dbContext.ApprovalChains
            .Where(x => x.PendingIdentityKey == pendingIdentityKey
                || (x.PendingIdentityKey == null
                    && x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.TemplateCode == request.TemplateCode
                    && x.Status == ApprovalChainStatuses.Pending
                    && x.DocumentReference.SourceService == documentReference.SourceService
                    && x.DocumentReference.DocumentType == documentReference.DocumentType
                    && x.DocumentReference.DocumentId == documentReference.DocumentId
                    && x.DocumentReference.DocumentLineId == documentReference.DocumentLineId))
            .Select(x => x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingChainId is not null)
        {
            return existingChainId;
        }

        var template = await dbContext.ApprovalTemplates
            .Include(x => x.Steps)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.TemplateCode == request.TemplateCode
                && x.DocumentType == request.DocumentType,
                cancellationToken)
            ?? throw new KnownException("Approval template was not found.");
        ApprovalChain chain;
        try
        {
            chain = ApprovalChain.Start(
                template,
                documentReference,
                request.StartedBy);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw new KnownException(exception.Message, exception);
        }

        dbContext.ApprovalChains.Add(chain);
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction is not null)
        {
            await transaction.CreateSavepointAsync(DuplicateRecoverySavepoint, cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.ReleaseSavepointAsync(DuplicateRecoverySavepoint, cancellationToken);
            }

            return chain.Id;
        }
        catch (DbUpdateException exception) when (IsPendingIdentityConflict(exception))
        {
            if (transaction is not null)
            {
                await transaction.RollbackToSavepointAsync(DuplicateRecoverySavepoint, cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
            return await dbContext.ApprovalChains
                .AsNoTracking()
                .Where(x => x.PendingIdentityKey == pendingIdentityKey)
                .Select(x => x.Id)
                .SingleOrDefaultAsync(cancellationToken)
                ?? RethrowDuplicateConflict(exception);
        }
    }

    private bool IsPendingIdentityConflict(DbUpdateException exception)
    {
        if (!dbContext.ChangeTracker.Entries<ApprovalChain>().Any(x =>
                x.State == EntityState.Added && x.Entity.PendingIdentityKey is not null))
        {
            return false;
        }

        var indexName = dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true
            ? null
            : PendingIdentityUniqueIndex;
        return ProcessedIntegrationEventInbox.IsUniqueConflict(exception, dbContext, indexName);
    }

    private static ApprovalChainId RethrowDuplicateConflict(DbUpdateException exception)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        throw new InvalidOperationException("Unreachable duplicate conflict rethrow path.");
    }
}
