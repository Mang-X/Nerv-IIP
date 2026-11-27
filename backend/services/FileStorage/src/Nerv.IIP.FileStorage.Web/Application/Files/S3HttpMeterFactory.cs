using System.Diagnostics.Metrics;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

internal sealed class S3HttpMeterFactory : IMeterFactory
{
    // 独立 meter 避免全局 HttpClient 指标采集带有 S3 主机名的属性。
    private readonly Meter meter = new("Nerv.IIP.FileStorage.PrivateS3Http");

    public Meter Create(MeterOptions options) => meter;

    public void Dispose() => meter.Dispose();
}
