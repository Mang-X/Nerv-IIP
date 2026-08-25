using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;
using System.Text.Json;

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
    public void Sent_and_delivery_unknown_require_a_job_id_before_transport_result_is_returned()
    {
        Assert.Throws<ArgumentException>(() => LabelPrinterDispatchResult.Sent(" "));
        Assert.Throws<ArgumentException>(() => LabelPrinterDispatchResult.DeliveryUnknown(" ", "partial write"));
    }

    [Fact]
    public void Failed_and_delivery_unknown_require_a_failure_reason()
    {
        Assert.Throws<ArgumentException>(() => LabelPrinterDispatchResult.Failed(" "));
        Assert.Throws<ArgumentException>(() => LabelPrinterDispatchResult.DeliveryUnknown("job-001", " "));
    }
}
