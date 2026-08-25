namespace Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

public abstract record LabelPrinterDispatchResult
{
    internal LabelPrinterDispatchResult() { }

    public abstract string Status { get; }
    public abstract string? PrintJobId { get; }
    public abstract string? FailureReason { get; }

    public static LabelPrinterDispatchResult Sent(string printJobId) =>
        new LabelPrinterSentResult(Required(printJobId, nameof(printJobId)));

    public static LabelPrinterDispatchResult DeliveryUnknown(string printJobId, string failureReason) =>
        new LabelPrinterDeliveryUnknownResult(
            Required(printJobId, nameof(printJobId)),
            Required(failureReason, nameof(failureReason)));

    public static LabelPrinterDispatchResult Failed(string failureReason) =>
        new LabelPrinterFailedResult(Required(failureReason, nameof(failureReason)));

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be null, empty, or whitespace.", parameterName)
            : value;
}

public sealed record LabelPrinterSentResult : LabelPrinterDispatchResult
{
    internal LabelPrinterSentResult(string jobId) => PrintJobId = jobId;

    public override string Status => "sent-to-printer";
    public override string PrintJobId { get; }
    public override string? FailureReason => null;
}

public sealed record LabelPrinterDeliveryUnknownResult : LabelPrinterDispatchResult
{
    internal LabelPrinterDeliveryUnknownResult(string jobId, string failureReason)
    {
        PrintJobId = jobId;
        FailureReason = failureReason;
    }

    public override string Status => "delivery-unknown";
    public override string PrintJobId { get; }
    public override string FailureReason { get; }
}

public sealed record LabelPrinterFailedResult : LabelPrinterDispatchResult
{
    internal LabelPrinterFailedResult(string failureReason) => FailureReason = failureReason;

    public override string Status => "failed";
    public override string? PrintJobId => null;
    public override string FailureReason { get; }
}

public sealed class LabelPrinterDispatchCanceledException : OperationCanceledException
{
    public LabelPrinterDispatchCanceledException(
        LabelPrinterDispatchResult attemptResult,
        OperationCanceledException cancellation,
        CancellationToken cancellationToken)
        : base(cancellation.Message, cancellation, cancellationToken)
    {
        AttemptResult = attemptResult is LabelPrinterSentResult
            ? throw new ArgumentException("A canceled printer attempt cannot be classified as sent.", nameof(attemptResult))
            : attemptResult;
    }

    public LabelPrinterDispatchResult AttemptResult { get; }
}

public interface ILabelPrinter
{
    Task<LabelPrinterDispatchResult> PrintAsync(
        string printerId,
        IReadOnlyCollection<CompiledLabelDocument> documents,
        CancellationToken cancellationToken);
}
