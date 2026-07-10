namespace Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

public sealed record LabelPrinterDispatchResult(string Status, string? PrintJobId, string? FailureReason)
{
    public static LabelPrinterDispatchResult Sent(string printJobId) => new("sent-to-printer", printJobId, null);

    public static LabelPrinterDispatchResult Printed(string printJobId) => new("printed", printJobId, null);

    public static LabelPrinterDispatchResult Failed(string failureReason) => new("failed", null, failureReason);
}

public interface ILabelPrinter
{
    Task<LabelPrinterDispatchResult> PrintAsync(
        string printerId,
        IReadOnlyCollection<string> labelValues,
        CancellationToken cancellationToken);
}
