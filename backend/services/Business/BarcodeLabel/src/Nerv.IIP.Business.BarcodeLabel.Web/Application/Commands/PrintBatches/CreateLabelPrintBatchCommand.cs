using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelSerialCounterAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Concurrency;

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

public sealed class CreateLabelPrintBatchCommandHandler(
    ApplicationDbContext dbContext,
    ILabelTemplateAssetPort templateAssetPort,
    ITemplateAssetRetirementFence retirementFence,
    ILabelPrintBatchReservationFence reservationFence,
    ILabelSerialNumberAllocator serialNumberAllocator)
    : ICommandHandler<CreateLabelPrintBatchCommand, LabelPrintBatchId>
{
    public async Task<LabelPrintBatchId> Handle(CreateLabelPrintBatchCommand request, CancellationToken cancellationToken)
    {
        var organizationId = BarcodeLabelText.Required(request.OrganizationId, nameof(request.OrganizationId));
        var environmentId = BarcodeLabelText.Required(request.EnvironmentId, nameof(request.EnvironmentId));
        var idempotencyKey = BarcodeLabelText.Required(request.IdempotencyKey, nameof(request.IdempotencyKey));
        await reservationFence.AcquireAsync(
            organizationId,
            environmentId,
            idempotencyKey,
            cancellationToken);
        var existing = await dbContext.LabelPrintBatches
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (existing is not null)
        {
            if (!existing.HasSameReservationRequest(
                    organizationId,
                    environmentId,
                    request.BarcodeRuleId,
                    request.LabelTemplateId,
                    request.SourceDocumentType,
                    request.SourceDocumentId,
                    idempotencyKey,
                    request.LabelValuesJson,
                    request.RequestedQuantity))
            {
                throw new KnownException("打印批次幂等键与已有记录不一致，请检查提交内容。");
            }

            return existing.Id;
        }

        var rule = await dbContext.BarcodeRules.SingleOrDefaultAsync(
                x => x.Id == request.BarcodeRuleId
                    && x.OrganizationId == organizationId
                    && x.EnvironmentId == environmentId
                    && x.Status == BarcodeRule.ActiveStatus,
                cancellationToken)
            ?? throw new KnownException($"未找到当前组织和环境内可用的条码规则，规则 ID = {request.BarcodeRuleId}。");
        var template = await dbContext.LabelTemplates.SingleOrDefaultAsync(
                x => x.Id == request.LabelTemplateId
                    && x.OrganizationId == organizationId
                    && x.EnvironmentId == environmentId
                    && x.Status == LabelTemplate.ActiveStatus,
                cancellationToken)
            ?? throw new KnownException($"未找到当前组织和环境内可用的标签模板，模板 ID = {request.LabelTemplateId}。");

        await retirementFence.AcquireAsync(
            organizationId,
            environmentId,
            template.TemplateFileId,
            cancellationToken);
        if (await dbContext.TemplateAssetRetirementDecisions.AnyAsync(
                x => x.OrganizationId == organizationId
                    && x.EnvironmentId == environmentId
                    && x.TemplateFileId == template.TemplateFileId,
                cancellationToken) || await dbContext.TemplateAssetRetirementReplayFences.AnyAsync(
                x => x.OrganizationId == organizationId
                    && x.EnvironmentId == environmentId
                    && x.TemplateFileId == template.TemplateFileId,
                cancellationToken))
        {
            throw new KnownException("模板资产已经退役，不能冻结到新打印批次。");
        }

        LabelPrintBatch candidate;
        try
        {
            var asset = await templateAssetPort.GetVerifiedAsync(
                new LabelTemplateAssetReference(
                    template.TemplateFileId,
                    organizationId,
                    environmentId,
                    template.TemplateCode),
                cancellationToken);
            var serialNumbers = await serialNumberAllocator.AllocateAsync(
                organizationId,
                environmentId,
                rule.Id,
                rule.AllocatedSerialNumberLength,
                request.RequestedQuantity,
                cancellationToken);
            candidate = LabelPrintBatch.Reserve(
                organizationId,
                environmentId,
                rule,
                template.Id,
                new LabelPrintBatchSnapshot(
                    asset.FileId,
                    asset.Sha256,
                    template.VariableSchemaJson,
                    rule.BarcodeType,
                    ZplV1LabelCompiler.ContractVersion),
                request.SourceDocumentType,
                request.SourceDocumentId,
                idempotencyKey,
                request.LabelValuesJson,
                request.RequestedQuantity,
                serialNumbers);

            _ = ZplV1LabelCompiler.CompileBatch(
                LabelTemplateDocument.Parse(asset.Json),
                LabelVariableSchema.Parse(template.VariableSchemaJson),
                candidate.Items.Select(item => new LabelCompilationItem(
                    request.LabelValuesJson,
                    LabelBarcodePayloadFactory.Create(rule.BarcodeType, item.LabelValue),
                    item.SequenceNo,
                    candidate.SourceDocumentId)).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException)
        {
            throw new KnownException("标签打印批次验证失败，请检查模板资产、变量和条码规则。", exception);
        }

        dbContext.LabelPrintBatches.Add(candidate);
        return candidate.Id;
    }
}
