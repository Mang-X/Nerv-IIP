using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.Tests;

public sealed class LabelPrinterDispatchResultTests
{
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
