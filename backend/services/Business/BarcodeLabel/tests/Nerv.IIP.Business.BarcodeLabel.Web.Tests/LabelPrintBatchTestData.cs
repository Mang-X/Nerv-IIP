using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

internal static class LabelPrintBatchTestData
{
    public static LabelPrintBatchSnapshot Snapshot(string barcodeType = "code128") =>
        new(
            "file-template-001",
            $"sha256:{new string('a', 64)}",
            """{"version":1,"variables":[]}""",
            barcodeType,
            "zpl-v1");
}
