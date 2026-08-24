using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.PrintBatches;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class PrintLabelLifecycleCommandTests
{
    [Fact]
    public async Task Dispatch_sent_to_printer_records_delivery_without_claiming_items_were_printed()
    {
        await using var dbContext = CreateDbContext();
        var (batch, template) = AddReplayableBatch(dbContext, 2);
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("dispatch-job"));
        var handler = new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer);

        var result = await handler.Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None);

        Assert.Equal(batch.Id, result);
        Assert.Equal("sent-to-printer", batch.Status);
        Assert.All(batch.Items, item => Assert.Equal("created", item.Status));
        Assert.Equal(template.TemplateFileId, assetPort.Requests.Single().FileId);
        Assert.Equal(2, printer.Calls.Single().Count);
    }

    [Fact]
    public async Task Dispatch_delivery_unknown_records_uncertain_delivery_without_claiming_items_were_printed()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        await dbContext.SaveChangesAsync();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.DeliveryUnknown("dispatch-job", "连接在写入后中断。"));

        await new DispatchLabelPrintBatchCommandHandler(dbContext, ValidAssetPort(), printer).Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None);

        Assert.Equal("delivery-unknown", batch.Status);
        Assert.Equal("created", batch.Items.Single().Status);
        Assert.Equal("连接在写入后中断。", batch.FailureReason);
    }

    [Fact]
    public async Task Reprint_sent_to_printer_does_not_mark_the_item_reprinted()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 2);
        batch.RecordSentToPrinter("printer-01", "initial-job");
        batch.RecordPrinted();
        batch.VoidItem(1, "damaged");
        await dbContext.SaveChangesAsync();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("reprint-job"));
        var handler = new ReprintLabelCommandHandler(dbContext, ValidAssetPort(), printer);

        var result = await handler.Handle(
            new ReprintLabelCommand("org-001", "env-dev", batch.Id, 2, "printer-01"),
            CancellationToken.None);

        Assert.Equal("sent-to-printer", result.Status);
        Assert.Equal("printed", batch.Status);
        Assert.Equal("voided", batch.Items.Single(x => x.SequenceNo == 1).Status);
        Assert.Equal("printed", batch.Items.Single(x => x.SequenceNo == 2).Status);
        Assert.Single(printer.Calls.Single());
    }

    [Fact]
    public async Task Reprint_delivery_unknown_leaves_the_item_unchanged()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        batch.RecordSentToPrinter("printer-01", "initial-job");
        batch.RecordPrinted();
        await dbContext.SaveChangesAsync();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.DeliveryUnknown("reprint-job", "连接在写入后中断。"));

        var result = await new ReprintLabelCommandHandler(dbContext, ValidAssetPort(), printer).Handle(
            new ReprintLabelCommand("org-001", "env-dev", batch.Id, 1, "printer-01"),
            CancellationToken.None);

        Assert.Equal("delivery-unknown", result.Status);
        Assert.Equal("printed", batch.Status);
        Assert.Equal("printed", batch.Items.Single().Status);
    }

    [Fact]
    public async Task Dispatch_and_reprint_compile_the_same_frozen_item_to_identical_bytes()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 2);
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));

        await new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer).Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None);
        batch.RecordPrinted();
        await new ReprintLabelCommandHandler(dbContext, assetPort, printer).Handle(
            new ReprintLabelCommand("org-001", "env-dev", batch.Id, 2, "printer-01"),
            CancellationToken.None);

        Assert.Equal(2, printer.Calls.Count);
        Assert.Equal(printer.Calls[0][1], printer.Calls[1].Single());
    }

    [Fact]
    public async Task Dispatch_ignores_live_template_file_schema_and_status_changes_after_batch_creation()
    {
        await using var dbContext = CreateDbContext();
        var (batch, template) = AddReplayableBatch(dbContext, 1);
        template.Update("Changed", "file-live-changed", "{}", "inactive");
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));

        await new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer).Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None);

        Assert.Equal("file-template-001", assetPort.Requests.Single().FileId);
        Assert.Single(printer.Calls.Single());
    }

    [Theory]
    [InlineData("org-other", "env-dev")]
    [InlineData("org-001", "env-other")]
    public async Task Dispatch_rejects_a_batch_outside_the_requested_scope_before_asset_or_transport(
        string organizationId,
        string environmentId)
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));
        var handler = new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer);

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new DispatchLabelPrintBatchCommand(organizationId, environmentId, batch.Id, "printer-01"),
            CancellationToken.None));

        Assert.Empty(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    [Fact]
    public async Task Dispatch_rejects_a_legacy_batch_before_asset_or_transport()
    {
        await using var dbContext = CreateDbContext();
        var rule = ActiveRule();
        var template = ActiveTemplate();
        var batch = LabelPrintBatch.CreateLegacyWithoutReplaySnapshot(
            "org-001", "env-dev", rule, template.Id, "wms.inbound", "ASN-001", "idem-legacy", LabelValuesJson, 1);
        dbContext.AddRange(template, batch);
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));

        await Assert.ThrowsAsync<KnownException>(() => new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer).Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None));

        Assert.Empty(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    [Fact]
    public async Task Dispatch_rejects_an_asset_with_a_different_sha256_before_transport()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        await dbContext.SaveChangesAsync();
        var assetPort = new RecordingAssetPort(reference =>
            new VerifiedLabelTemplateAsset(reference.FileId, $"sha256:{new string('b', 64)}", TemplateJson));
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));

        await Assert.ThrowsAsync<KnownException>(() => new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer).Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None));

        Assert.Single(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    [Fact]
    public async Task Dispatch_precompiles_the_entire_batch_before_the_single_transport_call()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 2);
        await dbContext.SaveChangesAsync();
        var assetPort = new RecordingAssetPort(reference =>
            new VerifiedLabelTemplateAsset(reference.FileId, AssetSha256, "{}"));
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));

        await Assert.ThrowsAsync<KnownException>(() => new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer).Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None));

        Assert.Single(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    [Fact]
    public async Task Dispatch_rejects_an_unknown_renderer_before_asset_or_transport()
    {
        await using var dbContext = CreateDbContext();
        var rule = ActiveRule();
        var template = ActiveTemplate();
        var batch = LabelPrintBatch.Create(
            "org-001", "env-dev", rule, template.Id,
            new LabelPrintBatchSnapshot(template.TemplateFileId, AssetSha256, VariableSchemaJson, rule.BarcodeType, "zpl-v2"),
            "wms.inbound", "ASN-001", "idem-renderer", LabelValuesJson, 1);
        dbContext.AddRange(template, batch);
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));

        await Assert.ThrowsAsync<KnownException>(() => new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer).Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None));

        Assert.Empty(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    private const string VariableSchemaJson =
        """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80}]}""";
    private const string LabelValuesJson = """{"skuCode":"SKU-FG-1000"}""";
    private const string TemplateJson =
        """{"format":"nerv-iip.label-template","version":1,"media":{"dpi":203,"widthDots":812,"heightDots":406},"fields":[{"kind":"text","x":40,"y":30,"fontHeight":30,"fontWidth":30,"variable":"skuCode"},{"kind":"barcode","x":40,"y":90,"moduleWidth":2,"height":100,"variable":"label.value"}]}""";
    private static readonly string AssetSha256 = $"sha256:{new string('a', 64)}";

    private static (LabelPrintBatch Batch, LabelTemplate Template) AddReplayableBatch(ApplicationDbContext dbContext, int quantity)
    {
        var rule = ActiveRule();
        var template = ActiveTemplate();
        var batch = LabelPrintBatch.Create(
            "org-001", "env-dev", rule, template.Id,
            new LabelPrintBatchSnapshot(template.TemplateFileId, AssetSha256, VariableSchemaJson, rule.BarcodeType, ZplV1LabelCompiler.ContractVersion),
            "wms.inbound", "ASN-001", $"idem-print-{Guid.NewGuid():N}", LabelValuesJson, quantity);
        dbContext.AddRange(template, batch);
        return (batch, template);
    }

    private static BarcodeRule ActiveRule() =>
        BarcodeRule.Create("org-001", "env-dev", "FG", "code128", "FG", 40, "none", ["wms.inbound"], "active");

    private static LabelTemplate ActiveTemplate() =>
        LabelTemplate.Create("org-001", "env-dev", "FG_BOX", "Finished goods box", "file-template-001", VariableSchemaJson, "active");

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static RecordingAssetPort ValidAssetPort() =>
        new(reference => new VerifiedLabelTemplateAsset(reference.FileId, AssetSha256, TemplateJson));

    private sealed class RecordingAssetPort(Func<LabelTemplateAssetReference, VerifiedLabelTemplateAsset> responseFactory) : ILabelTemplateAssetPort
    {
        public List<LabelTemplateAssetReference> Requests { get; } = [];

        public Task<VerifiedLabelTemplateAsset> GetVerifiedAsync(LabelTemplateAssetReference reference, CancellationToken cancellationToken)
        {
            Requests.Add(reference);
            return Task.FromResult(responseFactory(reference));
        }
    }

    private sealed class RecordingPrinter(LabelPrinterDispatchResult result) : ILabelPrinter
    {
        public List<IReadOnlyList<byte[]>> Calls { get; } = [];

        public Task<LabelPrinterDispatchResult> PrintAsync(
            string printerId,
            IReadOnlyCollection<CompiledLabelDocument> documents,
            CancellationToken cancellationToken)
        {
            Calls.Add(documents.Select(document => document.Payload.ToArray()).ToArray());
            return Task.FromResult(result);
        }
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
