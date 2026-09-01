using MediatR;
using Microsoft.Data.Sqlite;
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
    private const string VariableSchemaJson =
        """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80}]}""";
    private const string LabelValuesJson = """{"skuCode":"SKU-FG-1000"}""";
    private const string TemplateJson =
        """{"format":"nerv-iip.label-template","version":1,"media":{"dpi":203,"widthDots":812,"heightDots":406},"fields":[{"kind":"text","x":40,"y":30,"fontHeight":30,"fontWidth":30,"variable":"skuCode"},{"kind":"barcode","x":40,"y":90,"moduleWidth":2,"height":100,"variable":"label.value"}]}""";
    private static readonly string AssetSha256 = $"sha256:{new string('a', 64)}";

    [Fact]
    public async Task Relational_database_rejects_a_partially_missing_replay_snapshot()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new ApplicationDbContext(options, new NoopMediator());
        await dbContext.Database.EnsureCreatedAsync();
        var (batch, template) = CreateReplayableBatch(1);
        dbContext.AddRange(template, batch);
        await dbContext.SaveChangesAsync();

        dbContext.Entry(batch).Property(x => x.TemplateFileIdSnapshot).CurrentValue = null;

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Dispatch_and_reprint_compile_the_same_frozen_item_to_identical_bytes()
    {
        await using var dbContext = CreateDbContext();
        var (batch, template) = CreateReplayableBatch(2);
        dbContext.AddRange(template, batch);
        await dbContext.SaveChangesAsync();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));
        var assetPort = ValidAssetPort();
        var recorder = new RecordingAttemptRecorder();

        await new ScopedDispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer, recorder)
            .Handle(
                new ScopedDispatchLabelPrintBatchCommand(
                    batch.Id,
                    "org-001",
                    "env-dev",
                    "printer-01"),
                CancellationToken.None);
        await new ScopedReprintLabelCommandHandler(dbContext, assetPort, printer, recorder)
            .Handle(
                new ScopedReprintLabelCommand(
                    batch.Id,
                    2,
                    "org-001",
                    "env-dev",
                    "printer-01"),
                CancellationToken.None);

        Assert.Equal(2, printer.Calls.Count);
        Assert.Equal(printer.Calls[0][1], Assert.Single(printer.Calls[1]));
        Assert.Equal("sent-to-printer", batch.Status);
        Assert.All(batch.Items, item => Assert.Equal("created", item.Status));
    }

    [Fact]
    public async Task Dispatch_prevalidates_the_complete_batch_before_transport()
    {
        await using var dbContext = CreateDbContext();
        var (batch, template) = CreateReplayableBatch(2);
        dbContext.AddRange(template, batch);
        await dbContext.SaveChangesAsync();
        var assetPort = new RecordingAssetPort(reference =>
            new VerifiedLabelTemplateAsset(reference.FileId, AssetSha256, "{}"));
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));

        await Assert.ThrowsAsync<KnownException>(() =>
            new ScopedDispatchLabelPrintBatchCommandHandler(
                dbContext,
                assetPort,
                printer,
                new RecordingAttemptRecorder())
            .Handle(
                new ScopedDispatchLabelPrintBatchCommand(
                    batch.Id,
                    "org-001",
                    "env-dev",
                    "printer-01"),
                CancellationToken.None));

        Assert.Single(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    [Theory]
    [InlineData("org-other", "env-dev")]
    [InlineData("org-001", "env-other")]
    public async Task Scoped_dispatch_rejects_cross_scope_batch_before_asset_or_transport(
        string organizationId,
        string environmentId)
    {
        await using var dbContext = CreateDbContext();
        var (batch, template) = CreateReplayableBatch(1);
        dbContext.AddRange(template, batch);
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));

        await Assert.ThrowsAsync<KnownException>(() =>
            new ScopedDispatchLabelPrintBatchCommandHandler(
                dbContext,
                assetPort,
                printer,
                new RecordingAttemptRecorder())
            .Handle(
                new ScopedDispatchLabelPrintBatchCommand(
                    batch.Id,
                    organizationId,
                    environmentId,
                    "printer-01"),
                CancellationToken.None));

        Assert.Empty(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    [Fact]
    public async Task Dispatch_cancellation_records_the_closed_attempt_and_rethrows_the_original_cancellation()
    {
        await using var dbContext = CreateDbContext();
        var (batch, template) = CreateReplayableBatch(1);
        dbContext.AddRange(template, batch);
        await dbContext.SaveChangesAsync();
        using var cancellation = new CancellationTokenSource();
        var printer = new CancelingPrinter(cancellation);
        var recorder = new RecordingAttemptRecorder();

        var exception = await Assert.ThrowsAsync<LabelPrinterDispatchCanceledException>(() =>
            new ScopedDispatchLabelPrintBatchCommandHandler(
                dbContext,
                ValidAssetPort(),
                printer,
                recorder)
            .Handle(
                new ScopedDispatchLabelPrintBatchCommand(
                    batch.Id,
                    "org-001",
                    "env-dev",
                    "printer-01"),
                cancellation.Token));

        Assert.Same(printer.OriginalCancellation, exception.InnerException);
        var recorded = Assert.Single(recorder.DispatchAttempts);
        Assert.Equal(batch.Id, recorded.BatchId);
        Assert.Equal("failed", recorded.Result.Status);
    }

    [Fact]
    public async Task Reprint_delivery_unknown_updates_attempt_facts_without_marking_the_item_reprinted()
    {
        await using var dbContext = CreateDbContext();
        var (batch, template) = CreateReplayableBatch(1);
        batch.RecordSentToPrinter("printer-01", "dispatch-job");
        batch.RecordPrinted();
        var completedAtUtc = batch.CompletedAtUtc;
        dbContext.AddRange(template, batch);
        await dbContext.SaveChangesAsync();
        var printer = new RecordingPrinter(
            LabelPrinterDispatchResult.DeliveryUnknown("reprint-job", "partial write"));

        var result = await new ScopedReprintLabelCommandHandler(
            dbContext,
            ValidAssetPort(),
            printer,
            new RecordingAttemptRecorder())
            .Handle(
                new ScopedReprintLabelCommand(
                    batch.Id,
                    1,
                    "org-001",
                    "env-dev",
                    "printer-02"),
                CancellationToken.None);

        Assert.Equal("delivery-unknown", result.Status);
        Assert.Contains("现场确认", result.FailureReason, StringComparison.Ordinal);
        Assert.Equal("printed", batch.Status);
        Assert.Equal("printed", batch.Items.Single().Status);
        Assert.Equal(completedAtUtc, batch.CompletedAtUtc);
        Assert.Equal("reprint-job", batch.PrintJobId);
    }

    [Fact]
    public async Task Legacy_batch_fails_closed_before_asset_or_transport()
    {
        await using var dbContext = CreateDbContext();
        var rule = ActiveRule();
        var template = ActiveTemplate();
        var batch = LabelPrintBatch.CreateLegacyWithoutReplaySnapshot(
            "org-001",
            "env-dev",
            rule,
            template.Id,
            "wms.inbound",
            "ASN-001",
            "idem-legacy",
            LabelValuesJson,
            1);
        dbContext.AddRange(template, batch);
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));

        await Assert.ThrowsAsync<KnownException>(() =>
            new DispatchLabelPrintBatchCommandHandler(
                dbContext,
                assetPort,
                printer,
                new RecordingAttemptRecorder())
            .Handle(
                new DispatchLabelPrintBatchCommand(batch.Id, "printer-01"),
                CancellationToken.None));

        Assert.Empty(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    private static (LabelPrintBatch Batch, LabelTemplate Template) CreateReplayableBatch(int quantity)
    {
        var rule = ActiveRule();
        var template = ActiveTemplate();
        var batch = LabelPrintBatch.Create(
            "org-001",
            "env-dev",
            rule,
            template.Id,
            new LabelPrintBatchSnapshot(
                template.TemplateFileId,
                AssetSha256,
                VariableSchemaJson,
                rule.BarcodeType,
                ZplV1LabelCompiler.ContractVersion),
            "wms.inbound",
            "ASN-001",
            $"idem-print-{Guid.NewGuid():N}",
            LabelValuesJson,
            quantity);
        return (batch, template);
    }

    private static BarcodeRule ActiveRule() =>
        BarcodeRule.Create(
            "org-001",
            "env-dev",
            "FG",
            "code128",
            "FG",
            40,
            "none",
            ["wms.inbound"],
            "active");

    private static LabelTemplate ActiveTemplate() =>
        LabelTemplate.Create(
            "org-001",
            "env-dev",
            "FG_BOX",
            "Finished goods box",
            "file-template-001",
            VariableSchemaJson,
            "active");

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static RecordingAssetPort ValidAssetPort() =>
        new(reference => new VerifiedLabelTemplateAsset(reference.FileId, AssetSha256, TemplateJson));

    private sealed class RecordingAssetPort(
        Func<LabelTemplateAssetReference, VerifiedLabelTemplateAsset> responseFactory)
        : ILabelTemplateAssetPort
    {
        public List<LabelTemplateAssetReference> Requests { get; } = [];

        public Task<VerifiedLabelTemplateAsset> GetVerifiedAsync(
            LabelTemplateAssetReference reference,
            CancellationToken cancellationToken)
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

    private sealed class CancelingPrinter(CancellationTokenSource cancellation) : ILabelPrinter
    {
        public OperationCanceledException? OriginalCancellation { get; private set; }

        public Task<LabelPrinterDispatchResult> PrintAsync(
            string printerId,
            IReadOnlyCollection<CompiledLabelDocument> documents,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            OriginalCancellation = new OperationCanceledException("request canceled", cancellation.Token);
            return Task.FromException<LabelPrinterDispatchResult>(
                new LabelPrinterDispatchCanceledException(
                    LabelPrinterDispatchResult.Failed("pre-write cancellation"),
                    OriginalCancellation,
                    cancellation.Token));
        }
    }

    private sealed class RecordingAttemptRecorder : ILabelPrintAttemptRecorder
    {
        public List<(LabelPrintBatchId BatchId, LabelPrinterDispatchResult Result)> DispatchAttempts { get; } = [];

        public Task<bool> TryRecordDispatchCanceledAsync(
            string organizationId,
            string environmentId,
            LabelPrintBatchId printBatchId,
            string printerId,
            LabelPrinterDispatchResult result)
        {
            DispatchAttempts.Add((printBatchId, result));
            return Task.FromResult(true);
        }

        public Task<bool> TryRecordReprintCanceledAsync(
            string organizationId,
            string environmentId,
            LabelPrintBatchId printBatchId,
            int sequenceNo,
            string printerId,
            LabelPrinterDispatchResult result) =>
            Task.FromResult(true);
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
