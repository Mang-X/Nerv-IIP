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

    // DomainInvariant / #4259：独立确认不产生报工，且不放宽发送状态机。
    [Fact]
    public async Task Confirm_reserved_batch_readback_is_ready_without_mes_identity()
    {
        await using var dbContext = CreateDbContext();
        var batch = ReservedBatch();
        Assert.Throws<LabelPrintLifecycleRejectedException>(() => batch.EnsureCanBeDispatched());
        dbContext.LabelPrintBatches.Add(batch);
        await dbContext.SaveChangesAsync();
        var handler = new ConfirmLabelPrintBatchCommandHandler(dbContext, new NoopActivationFence());

        Assert.Equal(batch.Id, await handler.Handle(
            new ConfirmLabelPrintBatchCommand(batch.Id, "org-001", "env-dev"), CancellationToken.None));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var readback = await dbContext.LabelPrintBatches.SingleAsync(x => x.Id == batch.Id);
        Assert.Equal("ready-to-print", readback.Status);
        Assert.Null(readback.ProductionReportId);
        Assert.Null(readback.ProductionReportNo);
        readback.EnsureCanBeDispatched();
        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ConfirmLabelPrintBatchCommand(batch.Id, "org-001", "env-dev"), CancellationToken.None));
    }

    [Theory]
    [InlineData("org-other", "env-dev")]
    [InlineData("org-001", "env-other")]
    public async Task Confirm_cannot_change_a_batch_outside_the_requested_scope(string organizationId, string environmentId)
    {
        await using var dbContext = CreateDbContext();
        var batch = ReservedBatch();
        dbContext.LabelPrintBatches.Add(batch);
        await dbContext.SaveChangesAsync();
        var handler = new ConfirmLabelPrintBatchCommandHandler(dbContext, new NoopActivationFence());
        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ConfirmLabelPrintBatchCommand(batch.Id, organizationId, environmentId), CancellationToken.None));
        Assert.Equal("reserved", batch.Status);
    }

    [Fact]
    public async Task Confirm_rejects_mes_activated_batch_without_changing_the_report_identity()
    {
        await using var dbContext = CreateDbContext();
        var batch = ReservedBatch();
        batch.Activate("report-id-001", "PR-001");
        dbContext.LabelPrintBatches.Add(batch);
        await dbContext.SaveChangesAsync();
        var handler = new ConfirmLabelPrintBatchCommandHandler(dbContext, new NoopActivationFence());
        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ConfirmLabelPrintBatchCommand(batch.Id, "org-001", "env-dev"), CancellationToken.None));
        Assert.Equal("ready-to-print", batch.Status);
        Assert.Equal("report-id-001", batch.ProductionReportId);
        Assert.Equal("PR-001", batch.ProductionReportNo);
    }

    [Theory]
    [InlineData("", "PR-001")]
    [InlineData("report-id-001", "")]
    public void Mes_activation_still_requires_both_real_report_identifiers(string reportId, string reportNo)
    {
        var batch = ReservedBatch();
        Assert.Throws<ArgumentException>(() => batch.Activate(reportId, reportNo));
        Assert.Equal("reserved", batch.Status);
        Assert.Null(batch.ProductionReportId);
        Assert.Null(batch.ProductionReportNo);
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
            "opaque:report-intent-a",
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
