using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.DomainEvents;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.PrintBatches;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class PrintLabelLifecycleCommandTests
{
    [Fact]
    public async Task Reprint_only_changes_the_requested_item_and_does_not_repeat_batch_completion()
    {
        await using var dbContext = CreateDbContext();
        var batch = CreateBatch(2);
        batch.RecordSentToPrinter("printer-01", "initial-job");
        batch.RecordPrinted();
        batch.VoidItem(1, "damaged");
        dbContext.LabelPrintBatches.Add(batch);
        await dbContext.SaveChangesAsync();
        var completedEventsBefore = batch.GetDomainEvents().Count(x => x is LabelPrintBatchCompletedDomainEvent);

        var handler = new ReprintLabelCommandHandler(dbContext, new PrintedPrinter());
        await handler.Handle(new ReprintLabelCommand(batch.Id, 2, "printer-01"), CancellationToken.None);

        Assert.Equal("printed", batch.Status);
        Assert.Equal("voided", batch.Items.Single(x => x.SequenceNo == 1).Status);
        Assert.Equal("reprinted", batch.Items.Single(x => x.SequenceNo == 2).Status);
        Assert.Equal(completedEventsBefore, batch.GetDomainEvents().Count(x => x is LabelPrintBatchCompletedDomainEvent));
    }

    private static LabelPrintBatch CreateBatch(int quantity)
    {
        var rule = BarcodeRule.Create("org-001", "env-dev", "FG", "code128", "FG", 13, "none", ["wms.inbound"], "active");
        return LabelPrintBatch.Create("org-001", "env-dev", rule, new LabelTemplateId(Guid.CreateVersion7()), "wms.inbound", "ASN-001", "idem-print", "{}", quantity);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class PrintedPrinter : ILabelPrinter
    {
        public Task<LabelPrinterDispatchResult> PrintAsync(string printerId, IReadOnlyCollection<string> labelValues, CancellationToken cancellationToken) =>
            Task.FromResult(LabelPrinterDispatchResult.Printed("reprint-job"));
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
