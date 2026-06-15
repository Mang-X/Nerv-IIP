using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalTemplateAggregate;
using Nerv.IIP.Business.Approval.Web.Application.Validation;

namespace Nerv.IIP.Business.Approval.Web.Application.Commands.Chains;

public sealed record StartApprovalChainCommand(
    string OrganizationId,
    string EnvironmentId,
    string TemplateCode,
    string SourceService,
    string DocumentType,
    string DocumentId,
    string? DocumentLineId,
    string StartedBy) : ICommand<ApprovalChainId>;

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
    }
}

public sealed class StartApprovalChainCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<StartApprovalChainCommand, ApprovalChainId>
{
    public async Task<ApprovalChainId> Handle(StartApprovalChainCommand request, CancellationToken cancellationToken)
    {
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
                new ApprovalDocumentReference(
                    request.SourceService,
                    request.DocumentType,
                    request.DocumentId,
                    request.DocumentLineId),
                request.StartedBy);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw new KnownException(exception.Message, exception);
        }

        dbContext.ApprovalChains.Add(chain);
        return chain.Id;
    }
}
