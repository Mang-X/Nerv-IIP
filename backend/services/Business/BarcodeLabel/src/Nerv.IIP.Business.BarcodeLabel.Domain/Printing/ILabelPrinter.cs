namespace Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

public sealed record LabelPrinterDispatchResult
{
    private LabelPrinterDispatchResult(string status, string? printJobId, string? failureReason)
    {
        Status = status;
        PrintJobId = printJobId;
        FailureReason = failureReason;
    }

    public string Status { get; }
    public string? PrintJobId { get; }
    public string? FailureReason { get; }

    public static LabelPrinterDispatchResult Sent(string printJobId) =>
        new("sent-to-printer", Required(printJobId, nameof(printJobId)), null);

    public static LabelPrinterDispatchResult DeliveryUnknown(string printJobId, string failureReason) =>
        new("delivery-unknown", Required(printJobId, nameof(printJobId)), Required(failureReason, nameof(failureReason)));

    public static LabelPrinterDispatchResult Failed(string failureReason) =>
        new("failed", null, Required(failureReason, nameof(failureReason)));

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be null, empty, or whitespace.", parameterName)
            : value;
}

public interface ILabelPrinter
{
    Task<LabelPrinterDispatchResult> PrintAsync(
        string printerId,
        IReadOnlyCollection<CompiledLabelDocument> documents,
        CancellationToken cancellationToken);
}
