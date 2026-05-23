using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.BarcodeRules;

public sealed record CreateOrUpdateBarcodeRuleCommand(
    string OrganizationId,
    string EnvironmentId,
    string RuleCode,
    string BarcodeType,
    string Prefix,
    int Length,
    string ChecksumRule,
    IReadOnlyCollection<string> AllowedSourceDocumentTypes,
    string Status) : ICommand<BarcodeRuleId>;

public sealed class CreateOrUpdateBarcodeRuleCommandValidator : AbstractValidator<CreateOrUpdateBarcodeRuleCommand>
{
    public CreateOrUpdateBarcodeRuleCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RuleCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BarcodeType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Prefix).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Length).GreaterThan(0);
        RuleFor(x => x.ChecksumRule).NotEmpty().MaximumLength(50);
        RuleFor(x => x.AllowedSourceDocumentTypes).NotEmpty();
        RuleFor(x => x.Status).NotEmpty().MaximumLength(30);
    }
}

public sealed class CreateOrUpdateBarcodeRuleCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateOrUpdateBarcodeRuleCommand, BarcodeRuleId>
{
    public async Task<BarcodeRuleId> Handle(CreateOrUpdateBarcodeRuleCommand request, CancellationToken cancellationToken)
    {
        var existing = await dbContext.BarcodeRules.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.RuleCode == request.RuleCode,
            cancellationToken);
        if (existing is not null)
        {
            existing.Update(request.BarcodeType, request.Prefix, request.Length, request.ChecksumRule, request.AllowedSourceDocumentTypes, request.Status);
            return existing.Id;
        }

        var rule = BarcodeRule.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.RuleCode,
            request.BarcodeType,
            request.Prefix,
            request.Length,
            request.ChecksumRule,
            request.AllowedSourceDocumentTypes,
            request.Status);
        dbContext.BarcodeRules.Add(rule);
        return rule.Id;
    }
}
