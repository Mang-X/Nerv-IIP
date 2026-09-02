using MediatR;
using Microsoft.EntityFrameworkCore;
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
    private const string PostgresConnectionStringEnvironmentVariable = "NERV_IIP_TEST_POSTGRES";

    [RealPostgresFact]
    public async Task Retirement_reference_and_reuse_matrix_is_enforced_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await using var provider = CreateRetirementCommandProvider();
        LabelTemplateId templateId;
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var template = LabelTemplate.Create(
                "org-retirement",
                "env-retirement",
                "TPL-RETIREMENT",
                "Retirement template",
                "file-retirement-001",
                """{"version":1,"variables":[]}""",
                "inactive");
            setupDb.LabelTemplates.Add(template);
            await setupDb.SaveChangesAsync();
            templateId = template.Id;
        }

        TemplateAssetRetirementDecisionId decisionId;
        await using (var commandScope = provider.CreateAsyncScope())
        {
            var sender = commandScope.ServiceProvider.GetRequiredService<ISender>();
            decisionId = await sender.Send(new CreateTemplateAssetRetirementDecisionCommand(
                "org-retirement",
                "env-retirement",
                templateId,
                "file-retirement-001",
                $"sha256:{new string('a', 64)}",
                "retirement-key-001",
                "user-retirement-001",
                "business.barcodes.template-assets.retire",
                "不再使用旧标签模板资产。",
                "correlation-retirement-001"));
        }

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var decision = await verificationDb.TemplateAssetRetirementDecisions.SingleAsync();
        var templateAfterDecision = await verificationDb.LabelTemplates.SingleAsync(x => x.Id == templateId);
        Assert.Equal(decisionId, decision.Id);
        Assert.Equal("pending", decision.Status);
        Assert.Equal("unreferenced", decision.ReferenceResult);
        Assert.Equal(decisionId, templateAfterDecision.RetiredCurrentFileByDecisionId);

        await using (var reuseScope = provider.CreateAsyncScope())
        {
            var sender = reuseScope.ServiceProvider.GetRequiredService<ISender>();
            var exception = await Assert.ThrowsAsync<KnownException>(() => sender.Send(
                new CreateOrUpdateLabelTemplateCommand(
                    "org-retirement",
                    "env-retirement",
                    "TPL-RETIREMENT",
                    "Retirement template reused",
                    "file-retirement-001",
                    """{"version":1,"variables":[]}""",
                    "active")));
            Assert.Equal("模板资产已经退役，不能重新用于标签模板。", exception.Message);
        }

        BarcodeRuleId retiredAssetRuleId;
        await using (var bypassSetupScope = provider.CreateAsyncScope())
        {
            var bypassSetupDb = bypassSetupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rule = BarcodeRule.Create(
                "org-retirement", "env-retirement", "RETIRE-BYPASS", "code128", "RB", 40, "none", ["work-order"], "active");
            bypassSetupDb.BarcodeRules.Add(rule);
            await bypassSetupDb.SaveChangesAsync();
            await bypassSetupDb.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE barcode.label_templates
                SET status = 'active'
                WHERE id = {templateId.Id}
                """);
            retiredAssetRuleId = rule.Id;
        }

        await using (var batchReuseScope = provider.CreateAsyncScope())
        {
            var sender = batchReuseScope.ServiceProvider.GetRequiredService<ISender>();
            var exception = await Assert.ThrowsAsync<KnownException>(() => sender.Send(
                new CreateLabelPrintBatchCommand(
                    "org-retirement",
                    "env-retirement",
                    retiredAssetRuleId,
                    templateId,
                    "work-order",
                    "WO-RETIRED-ASSET",
                    "batch-retired-asset",
                    "{}",
                    1)));
            Assert.Equal("模板资产已经退役，不能冻结到新打印批次。", exception.Message);
        }

        await ResetAndMigrateSchemaAsync();
        await using var pendingProvider = CreateRetirementCommandProvider();
        LabelTemplateId pendingTemplateId;
        await using (var setupScope = pendingProvider.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rule = BarcodeRule.Create(
                "org-retirement", "env-retirement", "RETIRE", "code128", "RET", 40, "none", ["work-order"], "active");
            var template = LabelTemplate.Create(
                "org-retirement",
                "env-retirement",
                "TPL-RETIREMENT",
                "Retirement template",
                "file-retirement-001",
                """{"version":1,"variables":[]}""",
                "inactive");
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
                "batch-retirement-pending",
                "{}",
                1);
            setupDb.AddRange(rule, template, batch);
            await setupDb.SaveChangesAsync();
            pendingTemplateId = template.Id;
        }

        await using (var commandScope = pendingProvider.CreateAsyncScope())
        {
            var sender = commandScope.ServiceProvider.GetRequiredService<ISender>();
            var exception = await Assert.ThrowsAsync<KnownException>(() => sender.Send(
                new CreateTemplateAssetRetirementDecisionCommand(
                    "org-retirement",
                    "env-retirement",
                    pendingTemplateId,
                    "file-retirement-001",
                    $"sha256:{new string('a', 64)}",
                    "retirement-key-pending",
                    "user-retirement-001",
                    "business.barcodes.template-assets.retire",
                    "仍存在 pending 批次时不得退役。",
                    "correlation-retirement-pending")));
            Assert.Equal("模板资产仍可被打印批次引用，不能退役。", exception.Message);
        }

        await using var pendingVerificationScope = pendingProvider.CreateAsyncScope();
        var pendingVerificationDb = pendingVerificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await pendingVerificationDb.TemplateAssetRetirementDecisions.ToListAsync());
    }

    [RealPostgresFact]
    public async Task Failed_batch_keeps_its_template_asset_reachable_on_postgres()
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
                "batch-retirement-failed",
                "{}",
                1);
            batch.RecordPrintFailed("printer-retirement", "确定未发送。");
            setupDb.AddRange(rule, template, batch);
            await setupDb.SaveChangesAsync();
            templateId = template.Id;
        }

        await using var commandScope = provider.CreateAsyncScope();
        var sender = commandScope.ServiceProvider.GetRequiredService<ISender>();
        var exception = await Assert.ThrowsAsync<KnownException>(() => sender.Send(
            new CreateTemplateAssetRetirementDecisionCommand(
                "org-retirement",
                "env-retirement",
                templateId,
                "file-retirement-001",
                $"sha256:{new string('a', 64)}",
                "retirement-key-failed",
                "user-retirement-001",
                "business.barcodes.template-assets.retire",
                "failed 批次仍可重试。",
                "correlation-retirement-failed")));
        Assert.Equal("模板资产仍可被打印批次引用，不能退役。", exception.Message);
    }

    [RealPostgresFact]
    public async Task Sent_and_printed_batches_follow_the_item_reprint_matrix_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await AssertBatchDecisionOutcomeAsync("sent-to-printer", "created", approved: false);
        await AssertBatchDecisionOutcomeAsync("printed", "printed", approved: false);
        await AssertBatchDecisionOutcomeAsync("sent-to-printer", "voided", approved: true);
        await AssertBatchDecisionOutcomeAsync("printed", "consumed", approved: true);
        await AssertBatchDecisionOutcomeAsync("sent-to-printer", "mixed-voided-created", approved: false);
        await AssertBatchDecisionOutcomeAsync("printed", "mixed-consumed-voided", approved: true);
    }

    [RealPostgresFact]
    public async Task Delivery_unknown_batch_holds_template_asset_retirement_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await AssertBatchDecisionOutcomeAsync(
            "delivery-unknown",
            "created",
            approved: false,
            "模板资产存在未封闭的交付事实，退役已安全拒绝。");
    }

    [RealPostgresFact]
    public async Task Conflicting_snapshot_checksum_is_unknown_on_postgres()
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
                    $"sha256:{new string('b', 64)}",
                    """{"version":1,"variables":[]}""",
                    "code128",
                    "zpl-v1"),
                "work-order",
                "WO-CHECKSUM-CONFLICT",
                "batch-checksum-conflict",
                "{}",
                1);
            batch.RecordSentToPrinter("printer-retirement", "job-retirement");
            batch.VoidItem(1, "不可再打印。");
            setupDb.AddRange(rule, template, batch);
            await setupDb.SaveChangesAsync();
            templateId = template.Id;
        }

        await using var commandScope = provider.CreateAsyncScope();
        var sender = commandScope.ServiceProvider.GetRequiredService<ISender>();
        var exception = await Assert.ThrowsAsync<KnownException>(() => sender.Send(
            new CreateTemplateAssetRetirementDecisionCommand(
                "org-retirement",
                "env-retirement",
                templateId,
                "file-retirement-001",
                $"sha256:{new string('a', 64)}",
                "retirement-key-checksum-conflict",
                "user-retirement-001",
                "business.barcodes.template-assets.retire",
                "摘要冲突必须失败关闭。",
                "correlation-checksum-conflict")));
        Assert.Equal("模板资产引用事实不完整，退役已安全拒绝。", exception.Message);
    }

    [RealPostgresFact]
    public async Task Legacy_partial_owner_and_unknown_partitions_fail_closed_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await AssertHistoricalFactOutcomeAsync("active-template", approved: false, "模板资产仍可被标签模板引用，不能退役。");
        await AssertHistoricalFactOutcomeAsync("legacy-empty", approved: true);
        await AssertHistoricalFactOutcomeAsync("partial-snapshot", approved: false, "模板资产引用事实不完整，退役已安全拒绝。");
        await AssertHistoricalFactOutcomeAsync("missing-owner", approved: false, "模板资产引用事实不完整，退役已安全拒绝。");
        await AssertHistoricalFactOutcomeAsync("unknown-template", approved: false, "模板资产引用事实不完整，退役已安全拒绝。");
        await AssertHistoricalFactOutcomeAsync("untrusted-retirement-marker", approved: false, "模板资产引用事实不完整，退役已安全拒绝。");
        await AssertHistoricalFactOutcomeAsync("unknown-batch", approved: false, "模板资产引用事实不完整，退役已安全拒绝。");
        await AssertHistoricalFactOutcomeAsync("unknown-item", approved: false, "模板资产引用事实不完整，退役已安全拒绝。");
        await AssertHistoricalFactOutcomeAsync("non-target", approved: true);
        await AssertHistoricalFactOutcomeAsync("cross-scope", approved: true);
    }

    [RealPostgresFact]
    public async Task Retirement_idempotency_and_unique_conflicts_are_stable_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await using var provider = CreateRetirementCommandProvider();
        LabelTemplateId templateId;
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var template = LabelTemplate.Create(
                "org-retirement", "env-retirement", "TPL-IDEMPOTENCY", "Idempotency template",
                "file-idempotency-001", """{"version":1,"variables":[]}""", "inactive");
            setupDb.LabelTemplates.Add(template);
            await setupDb.SaveChangesAsync();
            templateId = template.Id;
        }

        var original = new CreateTemplateAssetRetirementDecisionCommand(
            "org-retirement",
            "env-retirement",
            templateId,
            "file-idempotency-001",
            $"sha256:{new string('a', 64)}",
            "retirement-key-idempotency",
            "user-retirement-001",
            "business.barcodes.template-assets.retire",
            "验证幂等重放。",
            "correlation-idempotency");
        TemplateAssetRetirementDecisionId firstId;
        await using (var firstScope = provider.CreateAsyncScope())
        {
            firstId = await firstScope.ServiceProvider.GetRequiredService<ISender>().Send(original);
        }

        await using (var replayScope = provider.CreateAsyncScope())
        {
            var replayId = await replayScope.ServiceProvider.GetRequiredService<ISender>().Send(original);
            Assert.Equal(firstId, replayId);
        }

        await using (var changedScope = provider.CreateAsyncScope())
        {
            var sender = changedScope.ServiceProvider.GetRequiredService<ISender>();
            var auditMetadataReplayId = await sender.Send(original with
            {
                RequesterSubject = "user-retirement-002",
                CorrelationId = "correlation-idempotency-replay",
            });
            Assert.Equal(firstId, auditMetadataReplayId);
            var reasonConflict = await Assert.ThrowsAsync<KnownException>(() => sender.Send(
                original with { Reason = "不同退役原因。" }));
            Assert.Equal("模板资产退役幂等键与已有记录不一致，请检查提交内容。", reasonConflict.Message);
            var crossKeyConflict = await Assert.ThrowsAsync<KnownException>(() => sender.Send(
                original with { IdempotencyKey = "retirement-key-cross-file" }));
            Assert.Equal("模板资产已存在退役裁决，不能创建第二条记录。", crossKeyConflict.Message);
        }

        await ResetAndMigrateSchemaAsync();
        var directTemplateId = new LabelTemplateId(Guid.CreateVersion7());
        var firstDirect = TemplateAssetRetirementDecision.Create(
            "org-retirement", "env-retirement", directTemplateId, "TPL-DIRECT",
            "file-direct-001", $"sha256:{new string('a', 64)}", "direct-key-001",
            "user-retirement-001", TemplateAssetRetirementDecision.RequiredPermission,
            "验证原始唯一约束。", "correlation-direct-001");
        await using (var firstDb = CreatePostgresDbContext(LaneConnectionString))
        {
            firstDb.TemplateAssetRetirementDecisions.Add(firstDirect);
            await firstDb.SaveChangesAsync();
        }

        await using (var fileConflictDb = CreatePostgresDbContext(LaneConnectionString))
        {
            fileConflictDb.TemplateAssetRetirementDecisions.Add(TemplateAssetRetirementDecision.Create(
                "org-retirement", "env-retirement", directTemplateId, "TPL-DIRECT",
                "file-direct-001", $"sha256:{new string('a', 64)}", "direct-key-002",
                "user-retirement-002", TemplateAssetRetirementDecision.RequiredPermission,
                "不同 key 同 file。", "correlation-direct-002"));
            var exception = await Assert.ThrowsAsync<KnownException>(() => fileConflictDb.SaveChangesAsync());
            Assert.Equal("模板资产已存在退役裁决，不能创建第二条记录。", exception.Message);
        }

        await using (var keyConflictDb = CreatePostgresDbContext(LaneConnectionString))
        {
            keyConflictDb.TemplateAssetRetirementDecisions.Add(TemplateAssetRetirementDecision.Create(
                "org-retirement", "env-retirement", directTemplateId, "TPL-DIRECT",
                "file-direct-002", $"sha256:{new string('b', 64)}", "direct-key-001",
                "user-retirement-002", TemplateAssetRetirementDecision.RequiredPermission,
                "同 key 不同 payload。", "correlation-direct-003"));
            var exception = await Assert.ThrowsAsync<KnownException>(() => keyConflictDb.SaveChangesAsync());
            Assert.Equal("模板资产退役幂等键与已有记录不一致，请检查提交内容。", exception.Message);
        }

        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        Assert.Single(await verificationDb.TemplateAssetRetirementDecisions.ToListAsync());
    }

    [RealPostgresFact]
    public async Task Retirement_and_template_reuse_cannot_both_commit_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await using var provider = CreateRetirementCommandProvider();
        var templateId = await AddRetirementTemplateAsync(provider, "TPL-CONCURRENT-TEMPLATE", "file-concurrent-template");
        await using var gateDb = CreatePostgresDbContext(LaneConnectionString);
        await using var gateTransaction = await gateDb.Database.BeginTransactionAsync();
        await new PostgresTemplateAssetRetirementFence(gateDb).AcquireAsync(
            "org-retirement", "env-retirement", "file-concurrent-template", CancellationToken.None);
        var holderProcessId = ((NpgsqlConnection)gateDb.Database.GetDbConnection()).ProcessID;

        await using var retirementScope = provider.CreateAsyncScope();
        await using var reuseScope = provider.CreateAsyncScope();
        var retirementTask = CaptureFailureAsync(async () =>
            _ = await retirementScope.ServiceProvider.GetRequiredService<ISender>().Send(
                RetirementCommand(templateId, "file-concurrent-template", "concurrent-retirement-template")));
        var reuseTask = CaptureFailureAsync(async () =>
            _ = await reuseScope.ServiceProvider.GetRequiredService<ISender>().Send(
                new CreateOrUpdateLabelTemplateCommand(
                    "org-retirement", "env-retirement", "TPL-CONCURRENT-TEMPLATE", "Concurrent template",
                    "file-concurrent-template", """{"version":1,"variables":[]}""", "active")));

        await WaitForAdvisoryWaitersAsync(holderProcessId, 2, "retirement vs template reuse");
        Assert.False(retirementTask.IsCompleted);
        Assert.False(reuseTask.IsCompleted);
        await gateTransaction.CommitAsync();
        var failures = await Task.WhenAll(retirementTask, reuseTask);
        Assert.Equal(1, failures.Count(failure => failure is null));
        Assert.Single(failures, failure => failure is KnownException);
    }

    [RealPostgresFact]
    public async Task Retirement_and_new_batch_freeze_cannot_both_commit_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await using var provider = CreateRetirementCommandProvider();
        var templateId = await AddRetirementTemplateAsync(
            provider,
            "TPL-CONCURRENT-BATCH",
            "file-concurrent-batch",
            LabelTemplate.ActiveStatus);
        BarcodeRuleId ruleId;
        await using (var ruleScope = provider.CreateAsyncScope())
        {
            var db = ruleScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rule = BarcodeRule.Create(
                "org-retirement", "env-retirement", "RETIRE-CONCURRENT", "code128", "RC", 40, "none", ["work-order"], "active");
            db.BarcodeRules.Add(rule);
            await db.SaveChangesAsync();
            ruleId = rule.Id;
        }

        await using var gateDb = CreatePostgresDbContext(LaneConnectionString);
        await using var gateTransaction = await gateDb.Database.BeginTransactionAsync();
        await new PostgresTemplateAssetRetirementFence(gateDb).AcquireAsync(
            "org-retirement", "env-retirement", "file-concurrent-batch", CancellationToken.None);
        var holderProcessId = ((NpgsqlConnection)gateDb.Database.GetDbConnection()).ProcessID;

        await using var retirementScope = provider.CreateAsyncScope();
        await using var batchScope = provider.CreateAsyncScope();
        var batchTask = CaptureFailureAsync(async () =>
            _ = await batchScope.ServiceProvider.GetRequiredService<ISender>().Send(new CreateLabelPrintBatchCommand(
                "org-retirement", "env-retirement", ruleId, templateId, "work-order", "WO-CONCURRENT",
                "batch-concurrent-retirement", """{"skuCode":"SKU-FG-1000"}""", 1)));

        await WaitForAdvisoryWaitersAsync(holderProcessId, 1, "new batch read-to-fence edge");
        Assert.False(batchTask.IsCompleted);
        await gateDb.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE barcode.label_templates
            SET status = {LabelTemplate.InactiveStatus}
            WHERE id = {templateId.Id}
            """);

        var retirementTask = CaptureFailureAsync(async () =>
            _ = await retirementScope.ServiceProvider.GetRequiredService<ISender>().Send(
                RetirementCommand(templateId, "file-concurrent-batch", "concurrent-retirement-batch")));

        await WaitForAdvisoryWaitersAsync(holderProcessId, 2, "retirement vs new batch freeze");
        Assert.False(retirementTask.IsCompleted);
        Assert.False(batchTask.IsCompleted);
        await gateTransaction.CommitAsync();
        var failures = await Task.WhenAll(retirementTask, batchTask);
        Assert.True(
            failures.Count(failure => failure is null) == 1,
            string.Join(Environment.NewLine, failures.Select(failure => failure?.ToString() ?? "success")));
        Assert.Single(failures, failure => failure is KnownException);

        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        Assert.False(
            await verificationDb.TemplateAssetRetirementDecisions.AnyAsync()
            && await verificationDb.LabelPrintBatches.AnyAsync(),
            "Retirement and batch snapshot must never both commit for the same scoped file.");
    }
    [RealPostgresFact]
    public async Task Canceled_attempt_facts_commit_outside_the_rolling_back_command_transaction()
    {
        await ResetAndMigrateSchemaAsync();
        using var cancellation = new CancellationTokenSource();
        var printer = new CancelingLabelPrinter(cancellation);
        await using var provider = CreateCommandProvider(printer);
        var batchId = await AddReplayableBatchAsync(provider, "idem-independent-attempt", markSent: false);

        await using (var commandScope = provider.CreateAsyncScope())
        {
            var sender = commandScope.ServiceProvider.GetRequiredService<ISender>();
            var exception = await Assert.ThrowsAsync<LabelPrinterDispatchCanceledException>(() => sender.Send(
                new ScopedDispatchLabelPrintBatchCommand(
                    batchId,
                    "org-001",
                    "env-dev",
                    "printer-independent"),
                cancellation.Token));
            Assert.Same(printer.ThrownCancellation, exception);
            Assert.Same(printer.OriginalCancellation, exception.InnerException);
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
    public async Task Canceled_dispatch_preserves_the_original_cancellation_when_another_dispatch_committed_first()
    {
        await ResetAndMigrateSchemaAsync();
        using var cancellation = new CancellationTokenSource();
        var printer = new MutatingCancelingLabelPrinter(
            cancellation,
            async () =>
            {
                await using var concurrentDb = CreatePostgresDbContext(LaneConnectionString);
                var concurrentBatch = await concurrentDb.LabelPrintBatches
                    .SingleAsync(batch => batch.IdempotencyKey == "idem-concurrent-dispatch");
                concurrentBatch.RecordSentToPrinter("printer-concurrent", "concurrent-job");
                await concurrentDb.SaveChangesAsync();
            });
        await using var provider = CreateCommandProvider(printer);
        var batchId = await AddReplayableBatchAsync(provider, "idem-concurrent-dispatch", markSent: false);

        await using (var commandScope = provider.CreateAsyncScope())
        {
            var sender = commandScope.ServiceProvider.GetRequiredService<ISender>();
            var exception = await Assert.ThrowsAsync<LabelPrinterDispatchCanceledException>(() => sender.Send(
                new ScopedDispatchLabelPrintBatchCommand(
                    batchId,
                    "org-001",
                    "env-dev",
                    "printer-canceled"),
                cancellation.Token));
            Assert.Same(printer.ThrownCancellation, exception);
            Assert.Same(printer.OriginalCancellation, exception.InnerException);
        }

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await verificationDb.LabelPrintBatches.SingleAsync(batch => batch.Id == batchId);
        Assert.Equal("sent-to-printer", persisted.Status);
        Assert.Equal("printer-concurrent", persisted.PrinterId);
        Assert.Equal("concurrent-job", persisted.PrintJobId);
        Assert.Null(persisted.FailureReason);
    }

    [RealPostgresFact]
    public async Task Canceled_reprint_attempt_facts_commit_outside_the_rolling_back_command_transaction()
    {
        await ResetAndMigrateSchemaAsync();
        using var cancellation = new CancellationTokenSource();
        var printer = new CancelingLabelPrinter(cancellation);
        await using var provider = CreateCommandProvider(printer);
        var batchId = await AddReplayableBatchAsync(provider, "idem-independent-reprint", markSent: true);

        await using (var commandScope = provider.CreateAsyncScope())
        {
            var sender = commandScope.ServiceProvider.GetRequiredService<ISender>();
            var exception = await Assert.ThrowsAsync<LabelPrinterDispatchCanceledException>(() => sender.Send(
                new ScopedReprintLabelCommand(
                    batchId,
                    1,
                    "org-001",
                    "env-dev",
                    "printer-reprint-independent"),
                cancellation.Token));
            Assert.Same(printer.ThrownCancellation, exception);
            Assert.Same(printer.OriginalCancellation, exception.InnerException);
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
        var printer = new MutatingCancelingLabelPrinter(
            cancellation,
            async () =>
            {
                await using var concurrentDb = CreatePostgresDbContext(LaneConnectionString);
                var concurrentBatch = await concurrentDb.LabelPrintBatches
                    .Include(batch => batch.Items)
                    .SingleAsync(batch => batch.IdempotencyKey == "idem-concurrent-void-reprint");
                concurrentBatch.VoidItem(1, "打印期间并发作废。");
                await concurrentDb.SaveChangesAsync();
            });
        await using var provider = CreateCommandProvider(printer);
        var batchId = await AddReplayableBatchAsync(provider, "idem-concurrent-void-reprint", markSent: true);

        await using (var commandScope = provider.CreateAsyncScope())
        {
            var sender = commandScope.ServiceProvider.GetRequiredService<ISender>();
            var exception = await Assert.ThrowsAsync<LabelPrinterDispatchCanceledException>(() => sender.Send(
                new ScopedReprintLabelCommand(
                    batchId,
                    1,
                    "org-001",
                    "env-dev",
                    "printer-must-not-overwrite"),
                cancellation.Token));
            Assert.Same(printer.ThrownCancellation, exception);
            Assert.Same(printer.OriginalCancellation, exception.InnerException);
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
            await dbContext.GetService<IMigrator>().MigrateAsync("20260710035759_AddPrintLifecycleAndPrinterTransport");
            var legacyBatchId = Guid.CreateVersion7();
            var legacyLabelValues = "{}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO barcode.label_print_batches (
                    id, organization_id, environment_id, barcode_rule_id, label_template_id,
                    source_document_type, source_document_id, idempotency_key, label_values_json,
                    requested_quantity, status, created_at_utc)
                VALUES (
                    {legacyBatchId}, 'org-legacy', 'env-legacy', {Guid.CreateVersion7()}, {Guid.CreateVersion7()},
                    'legacy', 'LEGACY-001', 'legacy-batch', {legacyLabelValues}, 1, 'pending', {DateTimeOffset.UtcNow})
                """);
            await dbContext.Database.MigrateAsync();

            var legacy = await dbContext.LabelPrintBatches
                .AsNoTracking()
                .SingleAsync(batch => batch.Id == new LabelPrintBatchId(legacyBatchId));
            Assert.Null(legacy.TemplateFileIdSnapshot);
            Assert.Null(legacy.TemplateAssetSha256);
            Assert.Null(legacy.VariableSchemaJsonSnapshot);
            Assert.Null(legacy.BarcodeTypeSnapshot);
            Assert.Null(legacy.RendererContractVersion);

            var replayRule = BarcodeRule.Create(
                "org-replay", "env-replay", "FG-REPLAY", "code128", "R", 40, "none", ["work-order"], "active");
            var replayBatch = LabelPrintBatch.Create(
                "org-replay",
                "env-replay",
                replayRule,
                new LabelTemplateId(Guid.CreateVersion7()),
                new LabelPrintBatchSnapshot(
                    "file-template-replay",
                    $"sha256:{new string('a', 64)}",
                    """{"version":1,"variables":[]}""",
                    "code128",
                    "zpl-v1"),
                "work-order",
                "WO-REPLAY",
                "replay-batch",
                "{}",
                1);
            dbContext.AddRange(replayRule, replayBatch);
            await dbContext.SaveChangesAsync();

            var constraintFailure = await Assert.ThrowsAsync<PostgresException>(() =>
                dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE barcode.label_print_batches
                    SET template_file_id_snapshot = NULL
                    WHERE id = {replayBatch.Id.Id}
                    """));
            Assert.Equal(PostgresErrorCodes.CheckViolation, constraintFailure.SqlState);

            var rule = BarcodeRule.Create("org-001", "env-dev", "FG-A", "code128", "FGA", 40, "none", ["work-order"], "active");
            var template = LabelTemplate.Create("org-001", "env-dev", "tpl-a", "Template A", "file-a", "{}", "active");
            var first = LabelPrintBatch.CreateLegacyWithoutReplaySnapshot("org-001", "env-dev", rule, template.Id, "work-order", "WO-001", "batch-a", "{}", 1);
            var second = LabelPrintBatch.CreateLegacyWithoutReplaySnapshot("org-001", "env-dev", rule, template.Id, "work-order", "WO001", "batch-b", "{}", 1);
            var unique = LabelPrintBatch.CreateLegacyWithoutReplaySnapshot("org-001", "env-dev", rule, template.Id, "work-order", "WO-UNIQUE", "batch-unique", "{}", 1);
            Assert.Equal(first.Items.Single().LabelValue, second.Items.Single().LabelValue);
            dbContext.AddRange(rule, template, first, second, unique);
            await dbContext.SaveChangesAsync();

            var result = await new ResolveBarcodeQueryHandler(dbContext).Handle(
                new ResolveBarcodeQuery("org-001", "env-dev", first.Items.Single().LabelValue, Skip: 1, Take: 1),
                CancellationToken.None);

            Assert.Equal("ambiguous", result.Status);
            Assert.Equal(2, result.Total);
            Assert.Equal("WO001", Assert.Single(result.Candidates).SourceDocumentId);

            var uniqueResult = await new ResolveBarcodeQueryHandler(dbContext).Handle(
                new ResolveBarcodeQuery("org-001", "env-dev", unique.Items.Single().LabelValue, Skip: 20, Take: 10),
                CancellationToken.None);

            Assert.Equal("resolved", uniqueResult.Status);
            Assert.Equal("WO-UNIQUE", Assert.Single(uniqueResult.Candidates).SourceDocumentId);
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
}
