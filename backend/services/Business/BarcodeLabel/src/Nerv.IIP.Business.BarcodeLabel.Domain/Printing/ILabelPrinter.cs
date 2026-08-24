namespace Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

public sealed record LabelPrinterDispatchResult(string Status, string? PrintJobId, string? FailureReason)
{
    public static LabelPrinterDispatchResult Sent(string printJobId) => new("sent-to-printer", printJobId, null);

    public static LabelPrinterDispatchResult DeliveryUnknown(string printJobId, string failureReason) =>
        new("delivery-unknown", printJobId, failureReason);

    public static LabelPrinterDispatchResult Failed(string failureReason) => new("failed", null, failureReason);
}

public interface ILabelPrinter
{
    Task<LabelPrinterDispatchResult> PrintAsync(
        string printerId,
        IReadOnlyCollection<CompiledLabelDocument> documents,
        CancellationToken cancellationToken);
}
