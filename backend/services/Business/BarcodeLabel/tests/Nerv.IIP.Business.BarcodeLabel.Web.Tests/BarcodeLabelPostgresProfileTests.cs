using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.BarcodeLabel.Domain;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TraceabilityAggregate;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.PrintBatches;
using Npgsql;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class BarcodeLabelPostgresProfileTests
{
    private const string PostgresConnectionStringEnvironmentVariable = "NERV_IIP_TEST_POSTGRES";

    [RealPostgresFact]
    public async Task Canceled_attempt_facts_commit_outside_the_rolling_back_command_transaction()
    {
        await ResetBarcodeLabelSchemaAsync();
        await using (var migrationDb = CreatePostgresDbContext(LaneConnectionString))
        {
            AssertUsesGovernedDatabase(migrationDb);
            await migrationDb.Database.MigrateAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssembly(typeof(DispatchLabelPrintBatchCommand).Assembly)
            .AddUnitOfWorkBehaviors());
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
            LaneConnectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", BarcodeLabelFacts.Schema)));
        services.AddUnitOfWork<ApplicationDbContext>();
        services.AddScoped<ILabelPrintAttemptRecorder, IndependentLabelPrintAttemptRecorder>();
        services.AddSingleton<ILabelTemplateAssetPort>(new FixedTemplateAssetPort());
        using var cancellation = new CancellationTokenSource();
        services.AddSingleton<ILabelPrinter>(new CancelingLabelPrinter(cancellation));
        await using var provider = services.BuildServiceProvider();

        LabelPrintBatchId batchId;
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rule = BarcodeRule.Create(
                "org-001", "env-dev", "FG", "code128", "FG", 40, "none", ["wms.inbound"], "active");
            var template = LabelTemplate.Create(
                "org-001", "env-dev", "FG_BOX", "Finished goods box", "file-template-001",
                """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80}]}""", "active");
            var batch = LabelPrintBatch.Create(
                "org-001",
                "env-dev",
                rule,
                template.Id,
                new LabelPrintBatchSnapshot(
                    "file-template-001",
                    $"sha256:{new string('a', 64)}",
                    """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80}]}""",
                    "code128",
                    "zpl-v1"),
                "wms.inbound",
                "ASN-INDEPENDENT-ATTEMPT",
                "idem-independent-attempt",
                """{"skuCode":"SKU-FG-1000"}""",
                1);
            batchId = batch.Id;
            setupDb.AddRange(template, batch);
            await setupDb.SaveChangesAsync();
        }

        await using (var commandScope = provider.CreateAsyncScope())
        {
            var sender = commandScope.ServiceProvider.GetRequiredService<ISender>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.Send(
                new DispatchLabelPrintBatchCommand(
                    "org-001",
                    "env-dev",
                    batchId,
                    "printer-independent"),
                cancellation.Token));
        }

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await verificationDb.LabelPrintBatches.SingleAsync(batch => batch.Id == batchId);
        Assert.Equal("failed", persisted.Status);
        Assert.Equal("printer-independent", persisted.PrinterId);
        Assert.Null(persisted.PrintJobId);
        Assert.Equal("调用方取消前未写入首字节。", persisted.FailureReason);
    }

    [RealPostgresFact]
    public async Task Canceled_reprint_attempt_facts_commit_outside_the_rolling_back_command_transaction()
    {
        await ResetAndMigrateSchemaAsync();
        using var cancellation = new CancellationTokenSource();
        await using var provider = CreateCommandProvider(new CancelingLabelPrinter(cancellation));
        var batchId = await AddReplayableBatchAsync(provider, "idem-independent-reprint", markSent: true);

        await using (var commandScope = provider.CreateAsyncScope())
        {
            var sender = commandScope.ServiceProvider.GetRequiredService<ISender>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.Send(
                new ReprintLabelCommand(
                    "org-001",
                    "env-dev",
                    batchId,
                    1,
                    "printer-reprint-independent"),
                cancellation.Token));
        }

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await verificationDb.LabelPrintBatches.SingleAsync(batch => batch.Id == batchId);
        Assert.Equal("sent-to-printer", persisted.Status);
        Assert.Equal("printer-reprint-independent", persisted.PrinterId);
        Assert.Null(persisted.PrintJobId);
        Assert.Equal("调用方取消前未写入首字节。", persisted.FailureReason);
    }

    [RealPostgresFact]
    public async Task Canceled_reprint_attempt_does_not_overwrite_facts_when_the_item_was_concurrently_voided()
    {
        await ResetAndMigrateSchemaAsync();
        using var cancellation = new CancellationTokenSource();
        await using var provider = CreateCommandProvider(new MutatingCancelingLabelPrinter(
            cancellation,
            async () =>
            {
                await using var concurrentDb = CreatePostgresDbContext(LaneConnectionString);
                var concurrentBatch = await concurrentDb.LabelPrintBatches
                    .Include(batch => batch.Items)
                    .SingleAsync(batch => batch.IdempotencyKey == "idem-concurrent-void-reprint");
                concurrentBatch.VoidItem(1, "打印期间并发作废。");
                await concurrentDb.SaveChangesAsync();
            }));
        var batchId = await AddReplayableBatchAsync(provider, "idem-concurrent-void-reprint", markSent: true);

        await using (var commandScope = provider.CreateAsyncScope())
        {
            var sender = commandScope.ServiceProvider.GetRequiredService<ISender>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.Send(
                new ReprintLabelCommand(
                    "org-001",
                    "env-dev",
                    batchId,
                    1,
                    "printer-must-not-overwrite"),
                cancellation.Token));
        }

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await verificationDb.LabelPrintBatches
            .Include(batch => batch.Items)
            .SingleAsync(batch => batch.Id == batchId);
        Assert.Equal("sent-to-printer", persisted.Status);
        Assert.Equal("printer-original", persisted.PrinterId);
        Assert.Equal("initial-job", persisted.PrintJobId);
        Assert.Null(persisted.FailureReason);
        Assert.Equal("voided", persisted.Items.Single().Status);
    }

    [RealPostgresFact]
    public async Task Postgres_unique_conflicts_are_mapped_for_scan_natural_key_and_epcis_event()
    {
        await ResetBarcodeLabelSchemaAsync();

        await using (var dbContext = CreatePostgresDbContext(LaneConnectionString))
        {
            AssertUsesGovernedDatabase(dbContext);
            await dbContext.Database.MigrateAsync();
        }

        await using (var dbContext = CreatePostgresDbContext(LaneConnectionString))
        {
            dbContext.ScanRecords.Add(NewPlainInventoryScan("idem-postgres-natural-001"));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreatePostgresDbContext(LaneConnectionString))
        {
            dbContext.ScanRecords.Add(NewPlainInventoryScan("idem-postgres-natural-002"));

            var exception = await Assert.ThrowsAsync<KnownException>(() => dbContext.SaveChangesAsync());

            Assert.Equal("条码扫描记录已存在，请检查幂等键、条码或来源单据。", exception.Message);
        }

        await using (var dbContext = CreatePostgresDbContext(LaneConnectionString))
        {
            var epcisEvent = NewEpcisObjectEvent("idem-postgres-epcis-001");
            dbContext.EpcisEvents.Add(epcisEvent);
            dbContext.Entry(epcisEvent).Property(nameof(EpcisEvent.ScanRecordId)).CurrentValue = null;
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreatePostgresDbContext(LaneConnectionString))
        {
            var epcisEvent = NewEpcisObjectEvent("idem-postgres-epcis-002");
            dbContext.EpcisEvents.Add(epcisEvent);
            dbContext.Entry(epcisEvent).Property(nameof(EpcisEvent.ScanRecordId)).CurrentValue = null;

            var exception = await Assert.ThrowsAsync<KnownException>(() => dbContext.SaveChangesAsync());

            Assert.Equal("条码追溯事件已存在，请检查事件类型和唯一标识。", exception.Message);
        }
    }

    private static ApplicationDbContext CreatePostgresDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", BarcodeLabelFacts.Schema))
            .Options;

        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static async Task ResetAndMigrateSchemaAsync()
    {
        await ResetBarcodeLabelSchemaAsync();
        await using var migrationDb = CreatePostgresDbContext(LaneConnectionString);
        AssertUsesGovernedDatabase(migrationDb);
        await migrationDb.Database.MigrateAsync();
    }

    private static ServiceProvider CreateCommandProvider(ILabelPrinter printer)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssembly(typeof(DispatchLabelPrintBatchCommand).Assembly)
            .AddUnitOfWorkBehaviors());
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
            LaneConnectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", BarcodeLabelFacts.Schema)));
        services.AddUnitOfWork<ApplicationDbContext>();
        services.AddScoped<ILabelPrintAttemptRecorder, IndependentLabelPrintAttemptRecorder>();
        services.AddSingleton<ILabelTemplateAssetPort>(new FixedTemplateAssetPort());
        services.AddSingleton(printer);
        return services.BuildServiceProvider();
    }

    private static async Task<LabelPrintBatchId> AddReplayableBatchAsync(
        ServiceProvider provider,
        string idempotencyKey,
        bool markSent)
    {
        await using var setupScope = provider.CreateAsyncScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rule = BarcodeRule.Create(
            "org-001", "env-dev", "FG", "code128", "FG", 40, "none", ["wms.inbound"], "active");
        var template = LabelTemplate.Create(
            "org-001", "env-dev", "FG_BOX", "Finished goods box", "file-template-001",
            """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80}]}""", "active");
        var batch = LabelPrintBatch.Create(
            "org-001",
            "env-dev",
            rule,
            template.Id,
            new LabelPrintBatchSnapshot(
                "file-template-001",
                $"sha256:{new string('a', 64)}",
                """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80}]}""",
                "code128",
                "zpl-v1"),
            "wms.inbound",
            "ASN-INDEPENDENT-ATTEMPT",
            idempotencyKey,
            """{"skuCode":"SKU-FG-1000"}""",
            1);
        if (markSent)
        {
            batch.RecordSentToPrinter("printer-original", "initial-job");
        }

        setupDb.AddRange(template, batch);
        await setupDb.SaveChangesAsync();
        return batch.Id;
    }

    private sealed class FixedTemplateAssetPort : ILabelTemplateAssetPort
    {
        public Task<VerifiedLabelTemplateAsset> GetVerifiedAsync(
            LabelTemplateAssetReference reference,
            CancellationToken cancellationToken) =>
            Task.FromResult(new VerifiedLabelTemplateAsset(
                reference.FileId,
                $"sha256:{new string('a', 64)}",
                """{"format":"nerv-iip.label-template","version":1,"media":{"dpi":203,"widthDots":812,"heightDots":406},"fields":[{"kind":"text","x":40,"y":30,"fontHeight":30,"fontWidth":30,"variable":"skuCode"},{"kind":"barcode","x":40,"y":90,"moduleWidth":2,"height":100,"variable":"label.value"}]}"""));
    }

    private sealed class CancelingLabelPrinter(CancellationTokenSource cancellation) : ILabelPrinter
    {
        public Task<LabelPrinterDispatchResult> PrintAsync(
            string printerId,
            IReadOnlyCollection<CompiledLabelDocument> documents,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            var inner = new OperationCanceledException(cancellation.Token);
            throw new LabelPrinterDispatchCanceledException(
                LabelPrinterDispatchResult.Failed("调用方取消前未写入首字节。"),
                inner,
                cancellation.Token);
        }
    }

    private sealed class MutatingCancelingLabelPrinter(
        CancellationTokenSource cancellation,
        Func<Task> mutateAsync) : ILabelPrinter
    {
        public async Task<LabelPrinterDispatchResult> PrintAsync(
            string printerId,
            IReadOnlyCollection<CompiledLabelDocument> documents,
            CancellationToken cancellationToken)
        {
            await mutateAsync();
            cancellation.Cancel();
            var inner = new OperationCanceledException(cancellation.Token);
            throw new LabelPrinterDispatchCanceledException(
                LabelPrinterDispatchResult.Failed("调用方取消前未写入首字节。"),
                inner,
                cancellation.Token);
        }
    }

    private static ScanRecord NewPlainInventoryScan(string idempotencyKey)
    {
        return ScanRecord.Record(
            "org-001",
            "env-dev",
            "PDA-01",
            "PLAIN-POSTGRES-NATURAL-001",
            "inventory.receipt",
            "ASN-POSTGRES-NATURAL",
            idempotencyKey,
            "accepted",
            null,
            "SKU-FG-1000",
            "EA",
            "SITE-01",
            "STAGE-01",
            "qualified",
            "owned",
            null,
            2);
    }

    private static EpcisEvent NewEpcisObjectEvent(string idempotencyKey)
    {
        return EpcisEvent.ObjectEvent(
            "org-001",
            "env-dev",
            ScanRecord.Record(
                "org-001",
                "env-dev",
                "PDA-01",
                "(01)09506000134352(10)LOT-PG\u001D(21)SN-PG-0001",
                "inventory.receipt",
                "ASN-POSTGRES-EPCIS",
                idempotencyKey,
                "accepted",
                null,
                "SKU-FG-1000",
                "EA",
                "SITE-01",
                "STAGE-01",
                "qualified",
                "owned",
                null,
                2));
    }

    // NERV-688 拆解③：BarcodeLabel 的 PostgreSQL 用例使用 lane runner 注入的成员数据库
    // （NERV_IIP_TEST_POSTGRES），不再自建内层数据库——内层数据库外层既读不到失败诊断，也证明不了清理。
    private static string LaneConnectionString =>
        Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)
        ?? throw new InvalidOperationException(
            $"{PostgresConnectionStringEnvironmentVariable} must be set for BarcodeLabel PostgreSQL profile tests.");

    private static async Task ResetBarcodeLabelSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(LaneConnectionString);
        await connection.OpenAsync();
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(BarcodeLabelFacts.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
        await command.ExecuteNonQueryAsync();
    }

    private static void AssertUsesGovernedDatabase(ApplicationDbContext dbContext)
    {
        var governed = new NpgsqlConnectionStringBuilder(LaneConnectionString);
        var observed = new NpgsqlConnectionStringBuilder(dbContext.Database.GetDbConnection().ConnectionString);
        // 只比库名不足以证明"跑在受治理的成员库上"：同名库可能在另一台主机或另一个端口。
        Assert.Equal(
            (governed.Host, governed.Port, governed.Database),
            (observed.Host, observed.Port, observed.Database));
    }

    private sealed class RealPostgresFactAttribute : FactAttribute
    {
        public RealPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)))
            {
                Skip = $"Set {PostgresConnectionStringEnvironmentVariable} to run this real PostgreSQL BarcodeLabel profile test.";
            }
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            _ = notification;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            _ = notification;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("PostgreSQL profile mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("PostgreSQL profile mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("PostgreSQL profile mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("PostgreSQL profile mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("PostgreSQL profile mediator cannot stream requests.");
        }
    }
}
