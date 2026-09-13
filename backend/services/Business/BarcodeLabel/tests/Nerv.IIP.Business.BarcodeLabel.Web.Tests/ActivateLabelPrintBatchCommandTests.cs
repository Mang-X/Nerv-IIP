using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Concurrency;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.PrintBatches;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class ActivateLabelPrintBatchCommandTests
{
    [Fact]
    public async Task Same_mes_association_replays_but_a_different_association_is_rejected_without_overwrite()
    {
        await using var dbContext = CreateDbContext();
        var batch = ReservedBatch();
        dbContext.LabelPrintBatches.Add(batch);
        await dbContext.SaveChangesAsync();
        var handler = new ActivateLabelPrintBatchCommandHandler(dbContext, new NoopActivationFence());
        var command = new ActivateLabelPrintBatchCommand(
            batch.Id,
            "org-001",
            "env-dev",
            "report-id-001",
            "PR-001");

        Assert.Equal(batch.Id, await handler.Handle(command, CancellationToken.None));
        Assert.Equal(batch.Id, await handler.Handle(command, CancellationToken.None));
        var conflict = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            command with { ProductionReportId = "report-id-002", ProductionReportNo = "PR-002" },
            CancellationToken.None));

        Assert.Equal("打印批次已关联其他 MES 生产上报，不能覆盖。", conflict.Message);
        Assert.Equal("report-id-001", batch.ProductionReportId);
        Assert.Equal("PR-001", batch.ProductionReportNo);
    }

    private static LabelPrintBatch ReservedBatch()
    {
        var rule = BarcodeRule.Create(
            "org-001",
            "env-dev",
            "FG",
            "code128",
            "FG",
            40,
            "none",
            ["wms.inbound"],
            "active");
        return LabelPrintBatch.CreateWithAllocatedSerialNumbers(
            "org-001",
            "env-dev",
            rule,
            new LabelTemplateId(Guid.CreateVersion7()),
            new LabelPrintBatchSnapshot(
                "file-template-001",
                $"sha256:{new string('a', 64)}",
                """{"version":1,"variables":[]}""",
                rule.BarcodeType,
                ZplV1LabelCompiler.ContractVersion),
            "wms.inbound",
            "ASN-001",
            "report-intent-001",
            "{}",
            1,
            ["00000000001"]);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopActivationFence : ILabelPrintBatchActivationFence
    {
        public Task AcquireAsync(
            string organizationId,
            string environmentId,
            LabelPrintBatchId printBatchId,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
