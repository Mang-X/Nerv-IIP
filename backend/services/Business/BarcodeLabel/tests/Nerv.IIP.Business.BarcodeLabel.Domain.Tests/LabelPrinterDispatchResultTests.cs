using System.Text.Json;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.Tests;

public sealed class LabelPrinterDispatchResultTests
{
    [Fact]
    public void Factories_return_closed_transport_result_variants()
    {
        Assert.IsType<LabelPrinterSentResult>(LabelPrinterDispatchResult.Sent("job-001"));
        Assert.IsType<LabelPrinterDeliveryUnknownResult>(
            LabelPrinterDispatchResult.DeliveryUnknown("job-002", "partial write"));
        Assert.IsType<LabelPrinterFailedResult>(LabelPrinterDispatchResult.Failed("pre-write failure"));
    }

    [Theory]
    [InlineData("sent", "{\"Status\":\"sent-to-printer\",\"PrintJobId\":\"job-001\",\"FailureReason\":null}")]
    [InlineData("unknown", "{\"Status\":\"delivery-unknown\",\"PrintJobId\":\"job-001\",\"FailureReason\":\"partial write\"}")]
    [InlineData("failed", "{\"Status\":\"failed\",\"PrintJobId\":null,\"FailureReason\":\"pre-write failure\"}")]
    public void Closed_variants_preserve_the_existing_wire_shape(string kind, string expectedJson)
    {
        LabelPrinterDispatchResult result = kind switch
        {
            "sent" => LabelPrinterDispatchResult.Sent("job-001"),
            "unknown" => LabelPrinterDispatchResult.DeliveryUnknown("job-001", "partial write"),
            "failed" => LabelPrinterDispatchResult.Failed("pre-write failure"),
            _ => throw new InvalidOperationException(),
        };

        Assert.Equal(expectedJson, JsonSerializer.Serialize(result));
    }

    [Fact]
    public void Dispatch_cancellation_preserves_the_non_sent_attempt_and_original_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var attempt = LabelPrinterDispatchResult.Failed("pre-write cancellation");
        var original = new OperationCanceledException("request canceled", cancellation.Token);

        var exception = new LabelPrinterDispatchCanceledException(attempt, original, cancellation.Token);

        Assert.Same(attempt, exception.AttemptResult);
        Assert.Same(original, exception.InnerException);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }
}
