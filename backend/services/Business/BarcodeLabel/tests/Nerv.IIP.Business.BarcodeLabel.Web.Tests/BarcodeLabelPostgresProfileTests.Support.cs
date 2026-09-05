using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.BarcodeLabel.Domain;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TraceabilityAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Concurrency;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.TemplateAssetRetirements;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.LabelTemplates;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.PrintBatches;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.Resolutions;
using Nerv.IIP.Testing;
using Npgsql;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed partial class BarcodeLabelPostgresProfileTests
{
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

    private static ServiceProvider CreateRetirementCommandProvider(SaveChangesInterceptor? interceptor = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssembly(typeof(CreateTemplateAssetRetirementDecisionCommand).Assembly)
            .AddUnitOfWorkBehaviors());
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(
                LaneConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", BarcodeLabelFacts.Schema));
            if (interceptor is not null)
            {
                options.AddInterceptors(interceptor);
            }
        });
        services.AddUnitOfWork<ApplicationDbContext>();
        services.AddScoped<ITemplateAssetRetirementFence, PostgresTemplateAssetRetirementFence>();
        services.AddSingleton<ILabelTemplateAssetPort>(new FixedTemplateAssetPort());
        services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
        services.AddSingleton<LabelPrintBatchCreatedIntegrationEventConverter>();
        return services.BuildServiceProvider();
    }

    private static async Task AssertBatchDecisionOutcomeAsync(
        string batchStatus,
        string itemStatus,
        bool approved,
        string rejectionMessage = "模板资产仍可被打印批次引用，不能退役。")
    {
        await ResetAndMigrateSchemaAsync();
        await using var provider = CreateRetirementCommandProvider();
        LabelTemplateId templateId;
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rule = BarcodeRule.Create(
                "org-retirement", "env-retirement", "RETIRE", "code128", "RET", 40, "none", ["work-order"], "active");
            var template = LabelTemplate.Create(
                "org-retirement", "env-retirement", "TPL-RETIREMENT", "Retirement template",
                "file-retirement-001", """{"version":1,"variables":[]}""", "inactive");
            var batch = LabelPrintBatch.Create(
                "org-retirement",
                "env-retirement",
                rule,
                template.Id,
                new LabelPrintBatchSnapshot(
                    "file-retirement-001",
                    $"sha256:{new string('a', 64)}",
                    """{"version":1,"variables":[]}""",
                    "code128",
                    "zpl-v1"),
                "work-order",
                "WO-RETIREMENT",
                $"batch-{batchStatus}-{itemStatus}",
                "{}",
                itemStatus.StartsWith("mixed-", StringComparison.Ordinal) ? 2 : 1);
            if (batchStatus == "delivery-unknown")
            {
                batch.RecordDeliveryUnknown("printer-retirement", "job-retirement", "交付结果未知。");
            }
            else
            {
                batch.RecordSentToPrinter("printer-retirement", "job-retirement");
                if (batchStatus == "printed")
                {
                    batch.RecordPrinted();
                }
            }

            if (itemStatus == "voided")
            {
                batch.VoidItem(1, "不再使用。");
            }
            else if (itemStatus == "consumed")
            {
                batch.ConsumeItem(batch.Items.Single().Id);
            }
            else if (itemStatus == "mixed-voided-created")
            {
                batch.VoidItem(1, "不再使用。");
            }
            else if (itemStatus == "mixed-consumed-voided")
            {
                batch.ConsumeItem(batch.Items.Single(x => x.SequenceNo == 1).Id);
                batch.VoidItem(2, "不再使用。");
            }

            setupDb.AddRange(rule, template, batch);
            await setupDb.SaveChangesAsync();
            templateId = template.Id;
        }

        await using var commandScope = provider.CreateAsyncScope();
        var sender = commandScope.ServiceProvider.GetRequiredService<ISender>();
        var command = new CreateTemplateAssetRetirementDecisionCommand(
            "org-retirement",
            "env-retirement",
            templateId,
            "file-retirement-001",
            $"sha256:{new string('a', 64)}",
            $"retirement-key-{batchStatus}-{itemStatus}",
            "user-retirement-001",
            "business.barcodes.template-assets.retire",
            "验证批次与 item 状态矩阵。",
            $"correlation-{batchStatus}-{itemStatus}");
        if (approved)
        {
            _ = await sender.Send(command);
            return;
        }

        var exception = await Assert.ThrowsAsync<KnownException>(() => sender.Send(command));
        Assert.Equal(rejectionMessage, exception.Message);
    }

    private static async Task AssertHistoricalFactOutcomeAsync(
        string scenario,
        bool approved,
        string rejectionMessage = "")
    {
        await ResetAndMigrateSchemaAsync();
        await using var provider = CreateRetirementCommandProvider();
        LabelTemplateId templateId;
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ruleScope = scenario == "cross-scope" ? "org-other" : "org-retirement";
            var rule = BarcodeRule.Create(
                ruleScope, "env-retirement", $"RETIRE-{scenario}", "code128", "RET", 40, "none", ["work-order"], "active");
            var template = LabelTemplate.Create(
                "org-retirement", "env-retirement", "TPL-RETIREMENT", "Retirement template",
                "file-retirement-001", """{"version":1,"variables":[]}""",
                scenario == "active-template" ? "active" : "inactive");
            setupDb.AddRange(rule, template);
            templateId = template.Id;

            if (scenario is not ("active-template" or "untrusted-retirement-marker"))
            {
                var batch = scenario == "legacy-empty"
                    ? LabelPrintBatch.CreateLegacyWithoutReplaySnapshot(
                        "org-retirement", "env-retirement", rule, template.Id,
                        "work-order", "WO-LEGACY", "batch-legacy-empty", "{}", 1)
                    : LabelPrintBatch.Create(
                        ruleScope,
                        "env-retirement",
                        rule,
                        template.Id,
                        new LabelPrintBatchSnapshot(
                            scenario == "non-target" ? "file-other-001" : "file-retirement-001",
                            $"sha256:{new string('a', 64)}",
                            """{"version":1,"variables":[]}""",
                            "code128",
                            "zpl-v1"),
                        "work-order",
                        $"WO-{scenario}",
                        $"batch-{scenario}",
                        "{}",
                        1);
                if (scenario is "partial-snapshot" or "missing-owner" or "unknown-batch" or "unknown-item")
                {
                    batch.RecordSentToPrinter("printer-retirement", "job-retirement");
                    batch.VoidItem(1, "不可再打印。");
                }

                setupDb.LabelPrintBatches.Add(batch);
            }

            await setupDb.SaveChangesAsync();
            if (scenario == "partial-snapshot")
            {
                await setupDb.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE barcode.label_print_batches DROP CONSTRAINT ck_label_print_batches_replay_snapshot_complete");
                await setupDb.Database.ExecuteSqlRawAsync(
                    "UPDATE barcode.label_print_batches SET variable_schema_json_snapshot = NULL");
            }
            else if (scenario == "whitespace-file-snapshot")
            {
                await setupDb.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE barcode.label_print_batches DROP CONSTRAINT ck_label_print_batches_replay_snapshot_complete");
                await setupDb.Database.ExecuteSqlRawAsync(
                    "UPDATE barcode.label_print_batches SET template_file_id_snapshot = '   '");
            }
            else if (scenario == "missing-owner")
            {
                var missingOwnerId = Guid.CreateVersion7();
                await setupDb.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE barcode.label_print_batches SET label_template_id = {missingOwnerId}
                    """);
            }
            else if (scenario == "unknown-template")
            {
                await setupDb.Database.ExecuteSqlRawAsync(
                    "UPDATE barcode.label_templates SET status = 'future-state'");
            }
            else if (scenario == "untrusted-retirement-marker")
            {
                var unrelatedDecisionId = Guid.CreateVersion7();
                await setupDb.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE barcode.label_templates
                    SET retired_current_file_by_decision_id = {unrelatedDecisionId}
                    """);
            }
            else if (scenario == "unknown-batch")
            {
                await setupDb.Database.ExecuteSqlRawAsync(
                    "UPDATE barcode.label_print_batches SET status = 'future-state'");
            }
            else if (scenario == "unknown-item")
            {
                await setupDb.Database.ExecuteSqlRawAsync(
                    "UPDATE barcode.label_print_items SET status = 'future-state'");
            }
        }

        await using var commandScope = provider.CreateAsyncScope();
        var sender = commandScope.ServiceProvider.GetRequiredService<ISender>();
        var command = new CreateTemplateAssetRetirementDecisionCommand(
            "org-retirement",
            "env-retirement",
            templateId,
            "file-retirement-001",
            $"sha256:{new string('a', 64)}",
            $"retirement-key-{scenario}",
            "user-retirement-001",
            "business.barcodes.template-assets.retire",
            $"验证 {scenario} 分区。",
            $"correlation-{scenario}");
        if (approved)
        {
            _ = await sender.Send(command);
            return;
        }

        var exception = await Assert.ThrowsAsync<KnownException>(() => sender.Send(command));
        Assert.Equal(rejectionMessage, exception.Message);
    }

    private static async Task<LabelTemplateId> AddRetirementTemplateAsync(
        ServiceProvider provider,
        string templateCode,
        string fileId,
        string status = LabelTemplate.InactiveStatus,
        string variableSchemaJson = """{"version":1,"variables":[]}""")
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var template = LabelTemplate.Create(
            "org-retirement", "env-retirement", templateCode, "Concurrent retirement template",
            fileId, variableSchemaJson, status);
        db.LabelTemplates.Add(template);
        await db.SaveChangesAsync();
        return template.Id;
    }

    private static CreateTemplateAssetRetirementDecisionCommand RetirementCommand(
        LabelTemplateId templateId,
        string fileId,
        string key) =>
        new(
            "org-retirement", "env-retirement", templateId, fileId,
            $"sha256:{new string('a', 64)}", key, "user-retirement-001",
            TemplateAssetRetirementDecision.RequiredPermission, "受控并发退役。", $"correlation-{key}");

    private static async Task<Exception?> CaptureFailureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task WaitForAdvisoryWaitersAsync(int holderProcessId, int expected, string description)
    {
        await Eventually.WaitAsync(
            condition: $"{expected} PostgreSQL advisory-lock waiters for {description}",
            observe: async cancellationToken =>
            {
                await using var connection = new NpgsqlConnection(LaneConnectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    WITH holder_lock AS (
                        SELECT locktype, database, classid, objid, objsubid
                        FROM pg_locks
                        WHERE pid = @holder_pid
                          AND locktype = 'advisory'
                          AND granted
                    )
                    SELECT count(*)
                    FROM pg_locks AS waiter
                    INNER JOIN holder_lock AS holder
                        ON waiter.locktype = holder.locktype
                       AND waiter.database IS NOT DISTINCT FROM holder.database
                       AND waiter.classid IS NOT DISTINCT FROM holder.classid
                       AND waiter.objid IS NOT DISTINCT FROM holder.objid
                       AND waiter.objsubid IS NOT DISTINCT FROM holder.objsubid
                    WHERE waiter.pid <> @holder_pid
                      AND NOT waiter.granted
                    """;
                command.Parameters.AddWithValue("holder_pid", holderProcessId);
                return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            },
            isSatisfied: count => count >= expected,
            describe: count => $"advisoryLockWaiters={count}; expected>={expected}",
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromSeconds(15),
                PollInterval: TimeSpan.FromMilliseconds(50),
                SensitiveValues: [LaneConnectionString]));
    }

    private static async Task<(int Waiters, bool CompetingTaskCompleted)> WaitForAdvisoryWaiterOrCompletionAsync(
        int holderProcessId,
        Task competingTask,
        string description)
    {
        return await Eventually.WaitAsync(
            condition: $"PostgreSQL advisory-lock waiter or early completion for {description}",
            observe: async cancellationToken =>
            {
                await using var connection = new NpgsqlConnection(LaneConnectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    WITH holder_lock AS (
                        SELECT locktype, database, classid, objid, objsubid
                        FROM pg_locks
                        WHERE pid = @holder_pid
                          AND locktype = 'advisory'
                          AND granted
                    )
                    SELECT count(*)
                    FROM pg_locks AS waiter
                    INNER JOIN holder_lock AS holder
                        ON waiter.locktype = holder.locktype
                       AND waiter.database IS NOT DISTINCT FROM holder.database
                       AND waiter.classid IS NOT DISTINCT FROM holder.classid
                       AND waiter.objid IS NOT DISTINCT FROM holder.objid
                       AND waiter.objsubid IS NOT DISTINCT FROM holder.objsubid
                    WHERE waiter.pid <> @holder_pid
                      AND NOT waiter.granted
                    """;
                command.Parameters.AddWithValue("holder_pid", holderProcessId);
                var waiters = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
                return (Waiters: waiters, CompetingTaskCompleted: competingTask.IsCompleted);
            },
            isSatisfied: observation => observation.Waiters > 0 || observation.CompetingTaskCompleted,
            describe: observation =>
                $"advisoryLockWaiters={observation.Waiters}; competingTaskCompleted={observation.CompetingTaskCompleted}",
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromSeconds(15),
                PollInterval: TimeSpan.FromMilliseconds(50),
                SensitiveValues: [LaneConnectionString]));
    }

    private sealed class RetirementSaveBarrier : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int HolderProcessId { get; private set; }

        public async Task WaitUntilEnteredAsync(CancellationToken cancellationToken)
        {
            await entered.Task.WaitAsync(cancellationToken);
        }

        public void Release() => released.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ApplicationDbContext context
                && context.ChangeTracker.Entries<TemplateAssetRetirementDecision>()
                    .Any(entry => entry.State == EntityState.Added))
            {
                HolderProcessId = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
                entered.TrySetResult();
                await released.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class TemplateRebindSaveBarrier(string targetFileId) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int HolderProcessId { get; private set; }

        public async Task WaitUntilEnteredAsync(CancellationToken cancellationToken)
        {
            await entered.Task.WaitAsync(cancellationToken);
        }

        public void Release() => released.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ApplicationDbContext context
                && context.ChangeTracker.Entries<LabelTemplate>()
                    .Any(entry => entry.State == EntityState.Modified
                        && string.Equals(entry.Entity.TemplateFileId, targetFileId, StringComparison.Ordinal)))
            {
                HolderProcessId = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
                entered.TrySetResult();
                await released.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
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

    private sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CancelingLabelPrinter(CancellationTokenSource cancellation) : ILabelPrinter
    {
        public OperationCanceledException? OriginalCancellation { get; private set; }
        public LabelPrinterDispatchCanceledException? ThrownCancellation { get; private set; }

        public Task<LabelPrinterDispatchResult> PrintAsync(
            string printerId,
            IReadOnlyCollection<CompiledLabelDocument> documents,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            OriginalCancellation = new OperationCanceledException(cancellation.Token);
            ThrownCancellation = new LabelPrinterDispatchCanceledException(
                LabelPrinterDispatchResult.Failed("调用方取消前未写入首字节。"),
                OriginalCancellation,
                cancellation.Token);
            throw ThrownCancellation;
        }
    }

    private sealed class MutatingCancelingLabelPrinter(
        CancellationTokenSource cancellation,
        Func<Task> mutateAsync) : ILabelPrinter
    {
        public OperationCanceledException? OriginalCancellation { get; private set; }
        public LabelPrinterDispatchCanceledException? ThrownCancellation { get; private set; }

        public async Task<LabelPrinterDispatchResult> PrintAsync(
            string printerId,
            IReadOnlyCollection<CompiledLabelDocument> documents,
            CancellationToken cancellationToken)
        {
            await mutateAsync();
            cancellation.Cancel();
            OriginalCancellation = new OperationCanceledException(cancellation.Token);
            ThrownCancellation = new LabelPrinterDispatchCanceledException(
                LabelPrinterDispatchResult.Failed("调用方取消前未写入首字节。"),
                OriginalCancellation,
                cancellation.Token);
            throw ThrownCancellation;
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

    private static void AssertUsesGovernedDatabase(ApplicationDbContext dbContext)
    {
        var governed = new NpgsqlConnectionStringBuilder(LaneConnectionString);
        var observed = new NpgsqlConnectionStringBuilder(dbContext.Database.GetDbConnection().ConnectionString);
        // 只比库名不足以证明"跑在受治理的成员库上"：同名库可能在另一台主机或另一个端口。
        Assert.Equal(
            (governed.Host, governed.Port, governed.Database),
            (observed.Host, observed.Port, observed.Database));
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
