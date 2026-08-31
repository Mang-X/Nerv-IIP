using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

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
    ILabelTemplateAssetPort templateAssetPort)
    : ICommandHandler<CreateLabelPrintBatchCommand, LabelPrintBatchId>
{
    public async Task<LabelPrintBatchId> Handle(CreateLabelPrintBatchCommand request, CancellationToken cancellationToken)
    {
        var rule = await dbContext.BarcodeRules.SingleOrDefaultAsync(
                x => x.Id == request.BarcodeRuleId
                    && x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.Status == BarcodeRule.ActiveStatus,
                cancellationToken)
            ?? throw new KnownException($"未找到当前组织和环境内可用的条码规则，规则 ID = {request.BarcodeRuleId}。");
        var template = await dbContext.LabelTemplates.SingleOrDefaultAsync(
                x => x.Id == request.LabelTemplateId
                    && x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.Status == "active",
                cancellationToken)
            ?? throw new KnownException($"未找到当前组织和环境内可用的标签模板，模板 ID = {request.LabelTemplateId}。");

        LabelPrintBatch candidate;
        try
        {
            var asset = await templateAssetPort.GetVerifiedAsync(
                new LabelTemplateAssetReference(
                    template.TemplateFileId,
                    request.OrganizationId,
                    request.EnvironmentId,
                    template.TemplateCode),
                cancellationToken);
            candidate = LabelPrintBatch.Create(
                request.OrganizationId,
                request.EnvironmentId,
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
                request.IdempotencyKey,
                request.LabelValuesJson,
                request.RequestedQuantity);

            _ = ZplV1LabelCompiler.CompileBatch(
                LabelTemplateDocument.Parse(asset.Json),
                LabelVariableSchema.Parse(template.VariableSchemaJson),
                candidate.Items.Select(item => new LabelCompilationItem(
                    request.LabelValuesJson,
                    CreateBarcodePayload(rule.BarcodeType, item),
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
                throw new KnownException("打印批次幂等键与已有记录不一致，请检查提交内容。", ex);
            }

            return existing.Id;
        }

        dbContext.LabelPrintBatches.Add(candidate);
        return candidate.Id;
    }

    private static LabelBarcodePayload CreateBarcodePayload(string barcodeType, LabelPrintItem item) =>
        barcodeType switch
        {
            "code128" => new PlainLabelBarcodePayload(PlainLabelBarcodeType.Code128, item.LabelValue),
            "qr" => new PlainLabelBarcodePayload(PlainLabelBarcodeType.Qr, item.LabelValue),
            "datamatrix" => new PlainLabelBarcodePayload(PlainLabelBarcodeType.DataMatrix, item.LabelValue),
            "gs1-128" => new Gs1LabelBarcodePayload(
                Gs1LabelBarcodeType.Gs1128,
                Gs1ApplicationIdentifierParser.Parse(item.LabelValue)),
            "gs1-datamatrix" => new Gs1LabelBarcodePayload(
                Gs1LabelBarcodeType.DataMatrix,
                Gs1ApplicationIdentifierParser.Parse(item.LabelValue)),
            _ => throw new InvalidOperationException($"Unsupported barcode type '{barcodeType}'."),
        };
}
