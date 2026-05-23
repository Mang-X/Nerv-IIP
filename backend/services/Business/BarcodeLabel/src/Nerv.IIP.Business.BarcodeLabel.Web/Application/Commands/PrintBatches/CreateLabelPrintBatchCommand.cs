using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.PrintBatches;

public sealed record CreateLabelPrintBatchCommand(
    string OrganizationId,
    string EnvironmentId,
    BarcodeRuleId BarcodeRuleId,
    LabelTemplateId LabelTemplateId,
    string SourceDocumentType,
    string SourceDocumentId,
    string IdempotencyKey,
    string LabelValuesJson,
    int RequestedQuantity) : ICommand<LabelPrintBatchId>;

public sealed class CreateLabelPrintBatchCommandValidator : AbstractValidator<CreateLabelPrintBatchCommand>
{
    public CreateLabelPrintBatchCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BarcodeRuleId).NotEmpty();
        RuleFor(x => x.LabelTemplateId).NotEmpty();
        RuleFor(x => x.SourceDocumentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(x => x.LabelValuesJson).NotEmpty();
        RuleFor(x => x.RequestedQuantity).GreaterThan(0);
    }
}

public sealed class CreateLabelPrintBatchCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateLabelPrintBatchCommand, LabelPrintBatchId>
{
    public async Task<LabelPrintBatchId> Handle(CreateLabelPrintBatchCommand request, CancellationToken cancellationToken)
    {
        var rule = await dbContext.BarcodeRules.SingleOrDefaultAsync(x => x.Id == request.BarcodeRuleId, cancellationToken)
            ?? throw new KnownException($"Barcode rule not found, BarcodeRuleId = {request.BarcodeRuleId}");
        var templateExists = await dbContext.LabelTemplates.AnyAsync(x => x.Id == request.LabelTemplateId, cancellationToken);
        if (!templateExists)
        {
            throw new KnownException($"Label template not found, LabelTemplateId = {request.LabelTemplateId}");
        }

        var candidate = LabelPrintBatch.Create(
            request.OrganizationId,
            request.EnvironmentId,
            rule,
            request.LabelTemplateId,
            request.SourceDocumentType,
            request.SourceDocumentId,
            request.IdempotencyKey,
            request.LabelValuesJson,
            request.RequestedQuantity);

        var existing = await dbContext.LabelPrintBatches
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.IdempotencyKey == request.IdempotencyKey,
                cancellationToken);
        if (existing is not null)
        {
            try
            {
                existing.EnsureSameIdempotencyPayload(candidate);
            }
            catch (InvalidOperationException ex)
            {
                throw new KnownException(ex.Message, ex);
            }

            return existing.Id;
        }

        dbContext.LabelPrintBatches.Add(candidate);
        return candidate.Id;
    }
}
