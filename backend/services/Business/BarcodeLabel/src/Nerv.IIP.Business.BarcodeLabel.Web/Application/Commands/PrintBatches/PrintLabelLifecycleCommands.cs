using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.PrintBatches;

public sealed record DispatchLabelPrintBatchCommand(
    LabelPrintBatchId PrintBatchId,
    string PrinterId) : ICommand<LabelPrintBatchId>;

public sealed record ScopedDispatchLabelPrintBatchCommand(
    LabelPrintBatchId PrintBatchId,
    string OrganizationId,
    string EnvironmentId,
    string PrinterId) : ICommand<LabelPrintBatchId>;

public sealed record ReprintLabelCommand(
    LabelPrintBatchId PrintBatchId,
    int SequenceNo,
    string PrinterId) : ICommand<LabelPrinterDispatchResult>;

public sealed record ScopedReprintLabelCommand(
    LabelPrintBatchId PrintBatchId,
    int SequenceNo,
    string OrganizationId,
    string EnvironmentId,
    string PrinterId) : ICommand<LabelPrinterDispatchResult>;

public sealed record VoidLabelCommand(
    LabelPrintBatchId PrintBatchId,
    int SequenceNo,
    string Reason) : ICommand<LabelPrintBatchId>;

public sealed record ScopedVoidLabelCommand(
    LabelPrintBatchId PrintBatchId,
    int SequenceNo,
    string OrganizationId,
    string EnvironmentId,
    string Reason) : ICommand<LabelPrintBatchId>;

public sealed class DispatchLabelPrintBatchCommandValidator : AbstractValidator<DispatchLabelPrintBatchCommand>
{
    public DispatchLabelPrintBatchCommandValidator()
    {
        RuleFor(x => x.PrintBatchId).NotEmpty();
        RuleFor(x => x.PrinterId).NotEmpty().MaximumLength(100);
    }
}

public sealed class ScopedDispatchLabelPrintBatchCommandValidator
    : AbstractValidator<ScopedDispatchLabelPrintBatchCommand>
{
    public ScopedDispatchLabelPrintBatchCommandValidator()
    {
        RuleFor(x => x.PrintBatchId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.EnvironmentId).NotEmpty();
        RuleFor(x => x.PrinterId).NotEmpty().MaximumLength(100);
    }
}

public sealed class ReprintLabelCommandValidator : AbstractValidator<ReprintLabelCommand>
{
    public ReprintLabelCommandValidator()
    {
        RuleFor(x => x.PrintBatchId).NotEmpty();
        RuleFor(x => x.SequenceNo).GreaterThan(0);
        RuleFor(x => x.PrinterId).NotEmpty().MaximumLength(100);
    }
}

public sealed class ScopedReprintLabelCommandValidator : AbstractValidator<ScopedReprintLabelCommand>
{
    public ScopedReprintLabelCommandValidator()
    {
        RuleFor(x => x.PrintBatchId).NotEmpty();
        RuleFor(x => x.SequenceNo).GreaterThan(0);
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.EnvironmentId).NotEmpty();
        RuleFor(x => x.PrinterId).NotEmpty().MaximumLength(100);
    }
}

public sealed class VoidLabelCommandValidator : AbstractValidator<VoidLabelCommand>
{
    public VoidLabelCommandValidator()
    {
        RuleFor(x => x.PrintBatchId).NotEmpty();
        RuleFor(x => x.SequenceNo).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class ScopedVoidLabelCommandValidator : AbstractValidator<ScopedVoidLabelCommand>
{
    public ScopedVoidLabelCommandValidator()
    {
        RuleFor(x => x.PrintBatchId).NotEmpty();
        RuleFor(x => x.SequenceNo).GreaterThan(0);
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.EnvironmentId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class DispatchLabelPrintBatchCommandHandler(
    ApplicationDbContext dbContext,
    ILabelTemplateAssetPort templateAssetPort,
    ILabelPrinter printer,
    ILabelPrintAttemptRecorder attemptRecorder)
    : ICommandHandler<DispatchLabelPrintBatchCommand, LabelPrintBatchId>
{
    public async Task<LabelPrintBatchId> Handle(
        DispatchLabelPrintBatchCommand request,
        CancellationToken cancellationToken)
    {
        var batch = await LabelPrintLifecycle.LoadBatchAsync(
            dbContext,
            request.PrintBatchId,
            cancellationToken);
        return await LabelPrintLifecycle.DispatchBatchAsync(
            dbContext,
            templateAssetPort,
            printer,
            attemptRecorder,
            batch,
            request.PrinterId,
            cancellationToken);
    }
}

public sealed class ScopedDispatchLabelPrintBatchCommandHandler(
    ApplicationDbContext dbContext,
    ILabelTemplateAssetPort templateAssetPort,
    ILabelPrinter printer,
    ILabelPrintAttemptRecorder attemptRecorder)
    : ICommandHandler<ScopedDispatchLabelPrintBatchCommand, LabelPrintBatchId>
{
    public async Task<LabelPrintBatchId> Handle(
        ScopedDispatchLabelPrintBatchCommand request,
        CancellationToken cancellationToken)
    {
        var batch = await LabelPrintLifecycle.LoadScopedBatchAsync(
            dbContext,
            request.PrintBatchId,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
        return await LabelPrintLifecycle.DispatchBatchAsync(
            dbContext,
            templateAssetPort,
            printer,
            attemptRecorder,
            batch,
            request.PrinterId,
            cancellationToken);
    }
}

public sealed class ReprintLabelCommandHandler(
    ApplicationDbContext dbContext,
    ILabelTemplateAssetPort templateAssetPort,
    ILabelPrinter printer,
    ILabelPrintAttemptRecorder attemptRecorder)
    : ICommandHandler<ReprintLabelCommand, LabelPrinterDispatchResult>
{
    public async Task<LabelPrinterDispatchResult> Handle(
        ReprintLabelCommand request,
        CancellationToken cancellationToken)
    {
        var batch = await LabelPrintLifecycle.LoadBatchAsync(
            dbContext,
            request.PrintBatchId,
            cancellationToken);
        return await LabelPrintLifecycle.ReprintAsync(
            dbContext,
            templateAssetPort,
            printer,
            attemptRecorder,
            batch,
            request.SequenceNo,
            request.PrinterId,
            cancellationToken);
    }
}

public sealed class ScopedReprintLabelCommandHandler(
    ApplicationDbContext dbContext,
    ILabelTemplateAssetPort templateAssetPort,
    ILabelPrinter printer,
    ILabelPrintAttemptRecorder attemptRecorder)
    : ICommandHandler<ScopedReprintLabelCommand, LabelPrinterDispatchResult>
{
    public async Task<LabelPrinterDispatchResult> Handle(
        ScopedReprintLabelCommand request,
        CancellationToken cancellationToken)
    {
        var batch = await LabelPrintLifecycle.LoadScopedBatchAsync(
            dbContext,
            request.PrintBatchId,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
        return await LabelPrintLifecycle.ReprintAsync(
            dbContext,
            templateAssetPort,
            printer,
            attemptRecorder,
            batch,
            request.SequenceNo,
            request.PrinterId,
            cancellationToken);
    }
}

public interface ILabelPrintAttemptRecorder
{
    Task<bool> TryRecordDispatchCanceledAsync(
        string organizationId,
        string environmentId,
        LabelPrintBatchId printBatchId,
        string printerId,
        LabelPrinterDispatchResult result);

    Task<bool> TryRecordReprintCanceledAsync(
        string organizationId,
        string environmentId,
        LabelPrintBatchId printBatchId,
        int sequenceNo,
        string printerId,
        LabelPrinterDispatchResult result);
}

public sealed class IndependentLabelPrintAttemptRecorder(IServiceScopeFactory scopeFactory)
    : ILabelPrintAttemptRecorder
{
    public async Task<bool> TryRecordDispatchCanceledAsync(
        string organizationId,
        string environmentId,
        LabelPrintBatchId printBatchId,
        string printerId,
        LabelPrinterDispatchResult result)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var independentDbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var batch = await LabelPrintLifecycle.LoadScopedBatchAsync(
            independentDbContext,
            printBatchId,
            organizationId,
            environmentId,
            CancellationToken.None);
        try
        {
            batch.EnsureCanBeDispatched();
        }
        catch (LabelPrintLifecycleRejectedException)
        {
            return false;
        }

        LabelPrintLifecycle.ApplyResult(batch, printerId, result);
        await independentDbContext.SaveChangesAsync(CancellationToken.None);
        return true;
    }

    public async Task<bool> TryRecordReprintCanceledAsync(
        string organizationId,
        string environmentId,
        LabelPrintBatchId printBatchId,
        int sequenceNo,
        string printerId,
        LabelPrinterDispatchResult result)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var independentDbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var batch = await LabelPrintLifecycle.LoadScopedBatchAsync(
            independentDbContext,
            printBatchId,
            organizationId,
            environmentId,
            CancellationToken.None);
        try
        {
            batch.EnsureItemCanBeReprinted(sequenceNo);
        }
        catch (LabelPrintLifecycleRejectedException)
        {
            return false;
        }

        LabelPrintLifecycle.ApplyReprintResult(batch, printerId, result);
        await independentDbContext.SaveChangesAsync(CancellationToken.None);
        return true;
    }
}

public sealed class VoidLabelCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<VoidLabelCommand, LabelPrintBatchId>
{
    public async Task<LabelPrintBatchId> Handle(
        VoidLabelCommand request,
        CancellationToken cancellationToken)
    {
        var batch = await LabelPrintLifecycle.LoadBatchAsync(
            dbContext,
            request.PrintBatchId,
            cancellationToken);
        batch.VoidItem(request.SequenceNo, request.Reason);
        return batch.Id;
    }
}

public sealed class ScopedVoidLabelCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ScopedVoidLabelCommand, LabelPrintBatchId>
{
    public async Task<LabelPrintBatchId> Handle(
        ScopedVoidLabelCommand request,
        CancellationToken cancellationToken)
    {
        var batch = await LabelPrintLifecycle.LoadScopedBatchAsync(
            dbContext,
            request.PrintBatchId,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
        batch.VoidItem(request.SequenceNo, request.Reason);
        return batch.Id;
    }
}

public static class LabelPrintLifecycleKnownExceptionMapper
{
    public static KnownException Create(
        LabelPrintLifecycleRejectedException exception,
        int sequenceNo) =>
        exception.Reason switch
        {
            LabelPrintLifecycleRejectionReason.BatchCannotBeDispatched =>
                new KnownException("当前打印批次状态不允许再次下发。", exception),
            LabelPrintLifecycleRejectionReason.BatchDeliveryUnknownCannotBeDispatched =>
                new KnownException("交付结果未知，禁止再次下发打印批次。", exception),
            LabelPrintLifecycleRejectionReason.BatchCannotBeReprinted =>
                new KnownException("当前打印批次状态不允许单项再次传输。", exception),
            LabelPrintLifecycleRejectionReason.BatchDeliveryUnknownCannotBeReprinted =>
                new KnownException("交付结果未知，禁止再次传输标签。", exception),
            LabelPrintLifecycleRejectionReason.FailedBatchRequiresDispatch =>
                new KnownException("整批打印失败后不能单项再次传输，请改用整批下发。", exception),
            LabelPrintLifecycleRejectionReason.PrintItemNotFound =>
                new KnownException($"未找到打印项，序号 = {sequenceNo}。", exception),
            LabelPrintLifecycleRejectionReason.PrintItemVoided =>
                new KnownException("已作废标签不允许再次传输。", exception),
            LabelPrintLifecycleRejectionReason.PrintItemConsumed =>
                new KnownException("已消费标签不允许再次传输。", exception),
            LabelPrintLifecycleRejectionReason.ConsumedPrintItemCannotBeVoided =>
                new KnownException("已消费标签不允许作废。", exception),
            _ => throw new InvalidOperationException(
                $"未登记的打印生命周期拒绝原因：{exception.Reason}。",
                exception),
        };
}

internal static class LabelPrintLifecycle
{
    public static async Task<LabelPrintBatch> LoadBatchAsync(
        ApplicationDbContext dbContext,
        LabelPrintBatchId printBatchId,
        CancellationToken cancellationToken)
    {
        return await dbContext.LabelPrintBatches
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == printBatchId, cancellationToken)
            ?? throw new KnownException($"Print batch not found, PrintBatchId = {printBatchId}");
    }

    public static async Task<LabelPrintBatch> LoadScopedBatchAsync(
        ApplicationDbContext dbContext,
        LabelPrintBatchId printBatchId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        return await dbContext.LabelPrintBatches
            .Include(x => x.Items)
            .SingleOrDefaultAsync(
                x => x.Id == printBatchId
                    && x.OrganizationId == organizationId
                    && x.EnvironmentId == environmentId,
                cancellationToken)
            ?? throw new KnownException("未找到打印批次。");
    }

    public static async Task<LabelPrintBatchId> DispatchBatchAsync(
        ApplicationDbContext dbContext,
        ILabelTemplateAssetPort templateAssetPort,
        ILabelPrinter printer,
        ILabelPrintAttemptRecorder attemptRecorder,
        LabelPrintBatch batch,
        string printerId,
        CancellationToken cancellationToken)
    {
        try
        {
            batch.EnsureCanBeDispatched();
        }
        catch (LabelPrintLifecycleRejectedException exception)
        {
            throw LabelPrintLifecycleKnownExceptionMapper.Create(exception, sequenceNo: 0);
        }

        var documents = await CompileFrozenBatchAsync(
            dbContext,
            templateAssetPort,
            batch,
            cancellationToken);
        LabelPrinterDispatchResult result;
        try
        {
            result = await printer.PrintAsync(printerId, documents, cancellationToken);
        }
        catch (LabelPrinterDispatchCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            await attemptRecorder.TryRecordDispatchCanceledAsync(
                batch.OrganizationId,
                batch.EnvironmentId,
                batch.Id,
                printerId,
                exception.AttemptResult);
            throw;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            result = LabelPrinterDispatchResult.Failed("打印服务暂时不可用，请稍后重试。");
        }

        ApplyResult(batch, printerId, result);
        return batch.Id;
    }

    public static async Task<LabelPrinterDispatchResult> ReprintAsync(
        ApplicationDbContext dbContext,
        ILabelTemplateAssetPort templateAssetPort,
        ILabelPrinter printer,
        ILabelPrintAttemptRecorder attemptRecorder,
        LabelPrintBatch batch,
        int sequenceNo,
        string printerId,
        CancellationToken cancellationToken)
    {
        try
        {
            batch.EnsureItemCanBeReprinted(sequenceNo);
        }
        catch (LabelPrintLifecycleRejectedException exception)
        {
            throw LabelPrintLifecycleKnownExceptionMapper.Create(exception, sequenceNo);
        }

        var selectedIndex = batch.Items
            .OrderBy(item => item.SequenceNo)
            .Select((item, index) => new { Item = item, Index = index })
            .Single(item => item.Item.SequenceNo == sequenceNo)
            .Index;
        var documents = await CompileFrozenBatchAsync(
            dbContext,
            templateAssetPort,
            batch,
            cancellationToken);

        LabelPrinterDispatchResult result;
        try
        {
            result = await printer.PrintAsync(printerId, [documents[selectedIndex]], cancellationToken);
        }
        catch (LabelPrinterDispatchCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            var canceledResult = AddReprintOperatorGuidance(exception.AttemptResult);
            await attemptRecorder.TryRecordReprintCanceledAsync(
                batch.OrganizationId,
                batch.EnvironmentId,
                batch.Id,
                sequenceNo,
                printerId,
                canceledResult);
            throw;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            result = LabelPrinterDispatchResult.Failed("打印服务暂时不可用，请稍后重试。");
        }

        result = AddReprintOperatorGuidance(result);
        ApplyReprintResult(batch, printerId, result);
        return result;
    }

    public static async Task<ImmutableArray<CompiledLabelDocument>> CompileFrozenBatchAsync(
        ApplicationDbContext dbContext,
        ILabelTemplateAssetPort templateAssetPort,
        LabelPrintBatch batch,
        CancellationToken cancellationToken)
    {
        try
        {
            batch.EnsureCompleteReplaySnapshot();
            if (!string.Equals(
                batch.RendererContractVersion,
                ZplV1LabelCompiler.ContractVersion,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unsupported renderer contract version.");
            }

            var templateCode = await dbContext.LabelTemplates
                .Where(template =>
                    template.Id == batch.LabelTemplateId
                    && template.OrganizationId == batch.OrganizationId
                    && template.EnvironmentId == batch.EnvironmentId)
                .Select(template => template.TemplateCode)
                .SingleOrDefaultAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(templateCode))
            {
                throw new InvalidOperationException(
                    "Template owner reference was not found in the batch scope.");
            }

            var asset = await templateAssetPort.GetVerifiedAsync(
                new LabelTemplateAssetReference(
                    batch.TemplateFileIdSnapshot!,
                    batch.OrganizationId,
                    batch.EnvironmentId,
                    templateCode),
                cancellationToken);
            if (!string.Equals(asset.FileId, batch.TemplateFileIdSnapshot, StringComparison.Ordinal)
                || !string.Equals(asset.Sha256, batch.TemplateAssetSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The verified template asset does not match the frozen batch snapshot.");
            }

            var items = batch.Items
                .OrderBy(item => item.SequenceNo)
                .Select(item => new LabelCompilationItem(
                    batch.LabelValuesJson,
                    LabelBarcodePayloadFactory.Create(batch.BarcodeTypeSnapshot!, item.LabelValue),
                    item.SequenceNo,
                    batch.SourceDocumentId))
                .ToArray();
            return ZplV1LabelCompiler.CompileBatch(
                LabelTemplateDocument.Parse(asset.Json),
                LabelVariableSchema.Parse(batch.VariableSchemaJsonSnapshot!),
                items);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or ArgumentException or InvalidOperationException)
        {
            throw new KnownException("打印批次冻结快照验证或编译失败。", exception);
        }
    }

    public static void ApplyResult(
        LabelPrintBatch batch,
        string printerId,
        LabelPrinterDispatchResult result)
    {
        ApplyTransportResult(
            result,
            printJobId => batch.RecordSentToPrinter(printerId, printJobId),
            (printJobId, failureReason) =>
                batch.RecordDeliveryUnknown(printerId, printJobId, failureReason),
            failureReason => batch.RecordPrintFailed(printerId, failureReason));
    }

    public static void ApplyReprintResult(
        LabelPrintBatch batch,
        string printerId,
        LabelPrinterDispatchResult result)
    {
        ApplyTransportResult(
            result,
            printJobId => batch.RecordReprintSentToPrinter(printerId, printJobId),
            (printJobId, failureReason) =>
                batch.RecordReprintDeliveryUnknown(printerId, printJobId, failureReason),
            failureReason => batch.RecordReprintFailed(printerId, failureReason));
    }

    public static LabelPrinterDispatchResult AddReprintOperatorGuidance(
        LabelPrinterDispatchResult result)
    {
        if (result is not LabelPrinterDeliveryUnknownResult unknown)
        {
            return result;
        }

        const string guidance = "请先现场确认上一张标签是否已出纸，再决定是否重新重打。";
        var reason = unknown.FailureReason.Trim();
        return LabelPrinterDispatchResult.DeliveryUnknown(
            unknown.PrintJobId,
            reason.Contains(guidance, StringComparison.Ordinal)
                ? reason
                : $"{reason} {guidance}");
    }

    private static void ApplyTransportResult(
        LabelPrinterDispatchResult result,
        Action<string> recordSent,
        Action<string, string> recordDeliveryUnknown,
        Action<string> recordFailed)
    {
        switch (result)
        {
            case LabelPrinterSentResult sent:
                recordSent(sent.PrintJobId);
                break;
            case LabelPrinterDeliveryUnknownResult unknown:
                recordDeliveryUnknown(unknown.PrintJobId, unknown.FailureReason);
                break;
            case LabelPrinterFailedResult failed:
                recordFailed(failed.FailureReason);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported label printer result type: {result.GetType().FullName}.");
        }
    }
}
