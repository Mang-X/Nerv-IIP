using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.PrintBatches;

public sealed record DispatchLabelPrintBatchCommand(LabelPrintBatchId PrintBatchId, string PrinterId) : ICommand<LabelPrintBatchId>;

public sealed record ScopedDispatchLabelPrintBatchCommand(
    LabelPrintBatchId PrintBatchId,
    string OrganizationId,
    string EnvironmentId,
    string PrinterId) : ICommand<LabelPrintBatchId>;

public sealed record ReprintLabelCommand(LabelPrintBatchId PrintBatchId, int SequenceNo, string PrinterId) : ICommand<LabelPrinterDispatchResult>;

public sealed record ScopedReprintLabelCommand(
    LabelPrintBatchId PrintBatchId,
    int SequenceNo,
    string OrganizationId,
    string EnvironmentId,
    string PrinterId) : ICommand<LabelPrinterDispatchResult>;

public sealed record VoidLabelCommand(LabelPrintBatchId PrintBatchId, int SequenceNo, string Reason) : ICommand<LabelPrintBatchId>;

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

public sealed class ScopedDispatchLabelPrintBatchCommandValidator : AbstractValidator<ScopedDispatchLabelPrintBatchCommand>
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

public sealed class DispatchLabelPrintBatchCommandHandler(ApplicationDbContext dbContext, ILabelPrinter printer)
    : ICommandHandler<DispatchLabelPrintBatchCommand, LabelPrintBatchId>
{
    public async Task<LabelPrintBatchId> Handle(DispatchLabelPrintBatchCommand request, CancellationToken cancellationToken)
    {
        var batch = await LabelPrintLifecycle.LoadBatchAsync(dbContext, request.PrintBatchId, cancellationToken);
        var result = await LabelPrintLifecycle.DispatchAsync(printer, request.PrinterId, batch.Items.Select(x => x.LabelValue).ToArray(), cancellationToken);
        LabelPrintLifecycle.ApplyResult(batch, request.PrinterId, result);
        return batch.Id;
    }
}

public sealed class ScopedDispatchLabelPrintBatchCommandHandler(ApplicationDbContext dbContext, ILabelPrinter printer)
    : ICommandHandler<ScopedDispatchLabelPrintBatchCommand, LabelPrintBatchId>
{
    public async Task<LabelPrintBatchId> Handle(ScopedDispatchLabelPrintBatchCommand request, CancellationToken cancellationToken)
    {
        var batch = await LabelPrintLifecycle.LoadScopedBatchAsync(
            dbContext,
            request.PrintBatchId,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
        var result = await LabelPrintLifecycle.DispatchScopedAsync(
            printer,
            request.PrinterId,
            batch.Items.Select(x => x.LabelValue).ToArray(),
            cancellationToken);
        LabelPrintLifecycle.ApplyResult(batch, request.PrinterId, result);
        return batch.Id;
    }
}

public sealed class ReprintLabelCommandHandler(ApplicationDbContext dbContext, ILabelPrinter printer)
    : ICommandHandler<ReprintLabelCommand, LabelPrinterDispatchResult>
{
    public async Task<LabelPrinterDispatchResult> Handle(ReprintLabelCommand request, CancellationToken cancellationToken)
    {
        var batch = await LabelPrintLifecycle.LoadBatchAsync(dbContext, request.PrintBatchId, cancellationToken);
        var item = batch.Items.SingleOrDefault(x => x.SequenceNo == request.SequenceNo)
            ?? throw new KnownException($"Print item not found, SequenceNo = {request.SequenceNo}");
        var result = await LabelPrintLifecycle.DispatchAsync(printer, request.PrinterId, [item.LabelValue], cancellationToken);
        if (result.Status == "printed")
        {
            batch.ReprintItem(request.SequenceNo);
        }

        return result;
    }
}

public sealed class ScopedReprintLabelCommandHandler(ApplicationDbContext dbContext, ILabelPrinter printer)
    : ICommandHandler<ScopedReprintLabelCommand, LabelPrinterDispatchResult>
{
    public async Task<LabelPrinterDispatchResult> Handle(ScopedReprintLabelCommand request, CancellationToken cancellationToken)
    {
        var batch = await LabelPrintLifecycle.LoadScopedBatchAsync(
            dbContext,
            request.PrintBatchId,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
        var item = LabelPrintLifecycle.FindScopedItem(batch, request.SequenceNo);
        var result = await LabelPrintLifecycle.DispatchScopedAsync(
            printer,
            request.PrinterId,
            [item.LabelValue],
            cancellationToken);
        if (result.Status == "printed")
        {
            batch.ReprintItem(request.SequenceNo);
        }

        return result;
    }
}

public sealed class VoidLabelCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<VoidLabelCommand, LabelPrintBatchId>
{
    public async Task<LabelPrintBatchId> Handle(VoidLabelCommand request, CancellationToken cancellationToken)
    {
        var batch = await LabelPrintLifecycle.LoadBatchAsync(dbContext, request.PrintBatchId, cancellationToken);
        batch.VoidItem(request.SequenceNo, request.Reason);
        return batch.Id;
    }
}

public sealed class ScopedVoidLabelCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ScopedVoidLabelCommand, LabelPrintBatchId>
{
    public async Task<LabelPrintBatchId> Handle(ScopedVoidLabelCommand request, CancellationToken cancellationToken)
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

    public static LabelPrintItem FindScopedItem(LabelPrintBatch batch, int sequenceNo)
    {
        return batch.Items.SingleOrDefault(x => x.SequenceNo == sequenceNo)
            ?? throw new KnownException("未找到打印项。");
    }

    public static async Task<LabelPrinterDispatchResult> DispatchAsync(
        ILabelPrinter printer,
        string printerId,
        IReadOnlyCollection<string> labels,
        CancellationToken cancellationToken)
    {
        try
        {
            return await printer.PrintAsync(printerId, labels, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return LabelPrinterDispatchResult.Failed($"Printer adapter failed: {exception.Message}");
        }
    }

    public static async Task<LabelPrinterDispatchResult> DispatchScopedAsync(
        ILabelPrinter printer,
        string printerId,
        IReadOnlyCollection<string> labels,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await printer.PrintAsync(printerId, labels, cancellationToken);
            return result.Status == "failed"
                ? LabelPrinterDispatchResult.Failed("打印服务暂时不可用，请稍后重试。")
                : result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return LabelPrinterDispatchResult.Failed("打印服务暂时不可用，请稍后重试。");
        }
    }

    public static void ApplyResult(LabelPrintBatch batch, string printerId, LabelPrinterDispatchResult result)
    {
        switch (result.Status)
        {
            case "sent-to-printer":
                batch.RecordSentToPrinter(printerId, RequiredJobId(result));
                break;
            case "printed":
                batch.RecordSentToPrinter(printerId, RequiredJobId(result));
                batch.RecordPrinted();
                break;
            case "failed":
                batch.RecordPrintFailed(result.FailureReason ?? "Printer adapter reported an unspecified failure.");
                break;
            default:
                batch.RecordPrintFailed($"Unsupported printer adapter status '{result.Status}'.");
                break;
        }
    }

    private static string RequiredJobId(LabelPrinterDispatchResult result)
    {
        return string.IsNullOrWhiteSpace(result.PrintJobId)
            ? throw new KnownException("Printer adapter returned no print job id.")
            : result.PrintJobId;
    }
}
