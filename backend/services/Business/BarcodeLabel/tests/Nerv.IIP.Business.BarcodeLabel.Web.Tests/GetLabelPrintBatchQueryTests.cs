using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.PrintBatches;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class GetLabelPrintBatchQueryTests
{
    [Fact]
    public async Task Detail_returns_ordered_serial_gs1_mes_and_transport_facts()
    {
        await using var dbContext = CreateDbContext();
        var rule = BarcodeRule.Create(
            "org-001",
            "env-dev",
            "GS1-FG",
            "gs1-128",
            "0950600013435",
            80,
            "gs1-mod10",
            ["wms.inbound"],
            "active",
            7);
        var templateId = new LabelTemplateId(Guid.CreateVersion7());
        var batch = LabelPrintBatch.Reserve(
            "org-001",
            "env-dev",
            rule,
            templateId,
            new LabelPrintBatchSnapshot(
                "file-template-001",
                $"sha256:{new string('a', 64)}",
                "{}",
                "gs1-128",
                "zpl-v1"),
            "wms.inbound",
            "ASN-001",
            "intent-001",
            """{"lotNo":"LOT-A"}""",
            2,
            ["00000000001", "00000000002"]);
        batch.Activate("report-id-001", "PR-001");
        batch.RecordSentToPrinter("printer-01", "job-001");
        dbContext.Add(batch);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var detail = await new GetLabelPrintBatchQueryHandler(dbContext)
            .Handle(new GetLabelPrintBatchQuery(batch.Id), CancellationToken.None);

        Assert.Equal("printer-01", detail.PrinterId);
        Assert.Equal("job-001", detail.PrintJobId);
        Assert.Equal("intent-001", detail.ReportIntentKey);
        Assert.Equal("report-id-001", detail.ProductionReportId);
        Assert.Equal("PR-001", detail.ProductionReportNo);
        Assert.Equal([1, 2], detail.Items.Select(item => item.SequenceNo).ToArray());
        Assert.Equal(["00000000001", "00000000002"], detail.Items.Select(item => item.SerialNumber!).ToArray());
        Assert.All(detail.Items, item =>
        {
            Assert.Equal("created", item.Status);
            Assert.Equal("LOT-A", item.LotNo);
            Assert.Equal("09506000134352", item.Gtin);
            Assert.EndsWith($".{item.SerialNumber}", item.EpcUri, StringComparison.Ordinal);
        });
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
