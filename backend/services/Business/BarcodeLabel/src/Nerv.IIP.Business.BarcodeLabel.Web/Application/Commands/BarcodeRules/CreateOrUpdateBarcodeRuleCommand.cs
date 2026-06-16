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
    string Status,
    int? Gs1CompanyPrefixLength = null) : ICommand<BarcodeRuleId>;

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
        When(x => !string.IsNullOrWhiteSpace(x.BarcodeType)
            && x.BarcodeType.StartsWith("gs1-", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.Prefix).Matches("^[0-9]{13}$").WithMessage("GS1 barcode rules require a 13-digit GTIN root prefix.");
            RuleFor(x => x.ChecksumRule).Equal("gs1-mod10", StringComparer.OrdinalIgnoreCase);
            RuleFor(x => x.Gs1CompanyPrefixLength).NotNull().InclusiveBetween(6, 12);
        });
        When(x => !string.IsNullOrWhiteSpace(x.BarcodeType)
            && !x.BarcodeType.StartsWith("gs1-", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.Gs1CompanyPrefixLength).Null();
        });
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
            try
            {
                existing.Update(request.BarcodeType, request.Prefix, request.Length, request.ChecksumRule, request.AllowedSourceDocumentTypes, request.Status, request.Gs1CompanyPrefixLength);
            }
            catch (ArgumentException ex)
            {
                throw new KnownException(ex.Message, ex);
            }
            catch (InvalidOperationException ex)
            {
                throw new KnownException(ex.Message, ex);
            }

            return existing.Id;
        }

        BarcodeRule rule;
        try
        {
            rule = BarcodeRule.Create(
                request.OrganizationId,
                request.EnvironmentId,
                request.RuleCode,
                request.BarcodeType,
                request.Prefix,
                request.Length,
                request.ChecksumRule,
                request.AllowedSourceDocumentTypes,
                request.Status,
                request.Gs1CompanyPrefixLength);
        }
        catch (ArgumentException ex)
        {
            throw new KnownException(ex.Message, ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new KnownException(ex.Message, ex);
        }

        dbContext.BarcodeRules.Add(rule);
        return rule.Id;
    }
}
