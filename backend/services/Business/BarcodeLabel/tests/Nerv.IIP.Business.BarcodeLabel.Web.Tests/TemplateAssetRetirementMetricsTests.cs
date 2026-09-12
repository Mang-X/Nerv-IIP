using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.TemplateAssetRetirements;
using Prometheus;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class TemplateAssetRetirementMetricsTests
{
    // #3050: the published descriptors and their finite dimensions are the deliverable.
    [Fact]
    public async Task Published_metrics_keep_closed_dimensions_and_do_not_publish_attempts()
    {
        var registry = Metrics.NewCustomRegistry();
        var metrics = new TemplateAssetRetirementMetrics(registry, NullLogger<TemplateAssetRetirementMetrics>.Instance);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var db = new ApplicationDbContext(options, new MediatR.Mediator(services));
        await metrics.RefreshAsync(db, DateTimeOffset.UnixEpoch, default);
        metrics.RecordExpiredReplay();
        metrics.RecordUnknownExclusions(2);
        metrics.RecordUnknownExclusions(0);
        using var exposition = new MemoryStream();
        await registry.CollectAndExportAsTextAsync(exposition);
        var types = Encoding.UTF8.GetString(exposition.ToArray()).Split('\n')
            .Where(line => line.StartsWith("# TYPE nerv_iip_barcode_retirement_", StringComparison.Ordinal))
            .Select(line => line.Replace("# TYPE nerv_iip_barcode_retirement_", "", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
        Assert.Equal(new[]
        {
            "accepted_decisions gauge", "execution_outcome_unknown_decisions gauge", "pending_decisions gauge",
            "replay_window_expired_decisions gauge", "terminal_decisions gauge", "zero_reexecution_fence_hits_total counter",
        }, types);
        Assert.Equal(new[]
        {
            "accepted_decisions 0",
            "execution_outcome_unknown_decisions 0",
            "pending_decisions 0",
            "replay_window_expired_decisions 0",
            "terminal_decisions{outcome=\"failure\"} 0",
            "terminal_decisions{outcome=\"success\"} 0",
            "zero_reexecution_fence_hits_total{cause=\"execution-outcome-unknown\"} 2",
            "zero_reexecution_fence_hits_total{cause=\"replay-window-expired\"} 1",
        }, await SamplesAsync(registry));
    }

    internal static async Task<string[]> SamplesAsync(CollectorRegistry registry)
    {
        using var stream = new MemoryStream();
        await registry.CollectAndExportAsTextAsync(stream);
        const string prefix = "nerv_iip_barcode_retirement_";
        return Encoding.UTF8.GetString(stream.ToArray()).Split('\n')
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(line => line[prefix.Length..]).Order(StringComparer.Ordinal).ToArray();
    }
}
