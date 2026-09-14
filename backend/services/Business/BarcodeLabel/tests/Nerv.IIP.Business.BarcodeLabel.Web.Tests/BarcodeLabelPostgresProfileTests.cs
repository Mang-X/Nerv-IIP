using MediatR;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.Contracts.BarcodeLabel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Retirement;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.BarcodeLabel.Domain;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelSerialCounterAggregate;
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
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.PrintBatches;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.Resolutions;
using Nerv.IIP.Testing;
using Npgsql;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed partial class BarcodeLabelPostgresProfileTests
{
    private const string PostgresConnectionStringEnvironmentVariable = "NERV_IIP_TEST_POSTGRES";

    [RealPostgresFact]
    public async Task Serial_allocator_reserves_non_overlapping_ordered_ranges_concurrently_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await using var firstDb = CreatePostgresDbContext(LaneConnectionString);
        await using var firstTransaction = await firstDb.Database.BeginTransactionAsync();
        var firstValues = await new PostgresLabelSerialNumberAllocator(firstDb).AllocateAsync(
            "org-serial", "env-serial", 20, 3, CancellationToken.None);
        var holderProcessId = ((NpgsqlConnection)firstDb.Database.GetDbConnection()).ProcessID;

        await using var secondDb = CreatePostgresDbContext(LaneConnectionString);
        await using var secondTransaction = await secondDb.Database.BeginTransactionAsync();
        var secondTask = new PostgresLabelSerialNumberAllocator(secondDb).AllocateAsync(
            "org-serial", "env-serial", 20, 3, CancellationToken.None);
        var waitEdge = await WaitForBlockedWaiterOrCompletionAsync(
            holderProcessId,
            secondTask,
            "serial counter row update");
        Assert.True(waitEdge.Waiters > 0);
        Assert.False(waitEdge.CompetingTaskCompleted);

        await firstTransaction.CommitAsync();
        var secondValues = await secondTask;
        await secondTransaction.CommitAsync();

        Assert.Equal(firstValues.Order(StringComparer.Ordinal), firstValues);
        Assert.Equal(secondValues.Order(StringComparer.Ordinal), secondValues);
        Assert.Empty(firstValues.Intersect(secondValues, StringComparer.Ordinal));

        string nextValue;
        await using (var nextDb = CreatePostgresDbContext(LaneConnectionString))
        await using (var nextTransaction = await nextDb.Database.BeginTransactionAsync())
        {
            nextValue = Assert.Single(await new PostgresLabelSerialNumberAllocator(nextDb).AllocateAsync(
                "org-serial", "env-serial", 20, 1, CancellationToken.None));
            await nextTransaction.CommitAsync();
        }

        Assert.NotEqual(firstValues[0], nextValue);
        Assert.NotEqual(secondValues[0], nextValue);

        string otherScopeValue;
        await using (var otherScopeDb = CreatePostgresDbContext(LaneConnectionString))
        await using (var otherScopeTransaction = await otherScopeDb.Database.BeginTransactionAsync())
        {
            otherScopeValue = Assert.Single(await new PostgresLabelSerialNumberAllocator(otherScopeDb).AllocateAsync(
                "org-serial-other", "env-serial", 20, 1, CancellationToken.None));
            await otherScopeTransaction.CommitAsync();
        }

        Assert.Equal(firstValues[0], otherScopeValue);
        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        var currentValues = await verificationDb.LabelSerialCounters
            .Select(counter => counter.CurrentValue)
            .Order()
            .ToArrayAsync();
        Assert.Equal([1L, 7L], currentValues);
    }

    [RealPostgresFact]
    public async Task Concurrent_same_intent_creates_one_batch_and_allocates_once_on_postgres()
    {
        await AssertConcurrentDifferentReportIntentFingerprintsConflictAsync();
        await ResetAndMigrateSchemaAsync();
        var assetPort = new BlockingTemplateAssetPort();
        await using var provider = CreateRetirementCommandProvider(templateAssetPort: assetPort);
        BarcodeRuleId ruleId;
        LabelTemplateId templateId;
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rule = BarcodeRule.Create(
                "org-intent", "env-intent", "INTENT", "code128", "I", 40, "none", ["work-order"], "active");
            var template = LabelTemplate.Create(
                "org-intent", "env-intent", "TPL-INTENT", "Intent template", "file-intent",
                """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80}]}""", "active");
            setupDb.AddRange(rule, template);
            await setupDb.SaveChangesAsync();
            ruleId = rule.Id;
            templateId = template.Id;
        }

        var command = new CreateLabelPrintBatchCommand(
            "org-intent", "env-intent", ruleId, templateId, "work-order", "WO-INTENT",
            "same-intent", """{"skuCode":"SKU-FG-1000"}""", 2)
        {
            ReportIntentFingerprint = "opaque:report-intent-a",
        };
        using var callerCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var firstScope = provider.CreateAsyncScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var firstTask = firstScope.ServiceProvider.GetRequiredService<ISender>()
            .Send(command, callerCancellation.Token);
        await assetPort.WaitUntilEnteredAsync(callerCancellation.Token);
        var holderProcessId = ((NpgsqlConnection)firstDb.Database.GetDbConnection()).ProcessID;

        await using var secondScope = provider.CreateAsyncScope();
        var secondTask = secondScope.ServiceProvider.GetRequiredService<ISender>()
            .Send(command, callerCancellation.Token);
        try
        {
            await WaitForAdvisoryWaitersAsync(holderProcessId, 1, "same print intent reservation fence");
            Assert.False(secondTask.IsCompleted);
        }
        finally
        {
            assetPort.Release();
        }

        var batchIds = await TestTimeout.RunAsync(
            "same print intent commands finish after the first asset read is released",
            async cancellationToken => await Task.WhenAll(firstTask, secondTask).WaitAsync(cancellationToken),
            TimeSpan.FromSeconds(15),
            callerCancellation.Token,
            sensitiveValues: [LaneConnectionString]);
        Assert.Equal(batchIds[0], batchIds[1]);
        Assert.Equal(1, assetPort.RequestCount);

        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        Assert.Equal(1, await verificationDb.LabelPrintBatches.CountAsync());
        Assert.Equal(2, await verificationDb.LabelPrintItems.CountAsync());
        Assert.Equal(2, await verificationDb.LabelSerialCounters.Select(counter => counter.CurrentValue).SingleAsync());
        Assert.Equal(
            "opaque:report-intent-a",
            await verificationDb.LabelPrintBatches.Select(batch => batch.ReportIntentFingerprint).SingleAsync());
    }

    private static async Task AssertConcurrentDifferentReportIntentFingerprintsConflictAsync()
    {
        await ResetAndMigrateSchemaAsync();
        var assetPort = new BlockingTemplateAssetPort();
        await using var provider = CreateRetirementCommandProvider(templateAssetPort: assetPort);
        BarcodeRuleId ruleId;
        LabelTemplateId templateId;
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rule = BarcodeRule.Create(
                "org-fingerprint", "env-fingerprint", "FINGERPRINT", "code128", "F", 40, "none", ["work-order"], "active");
            var template = LabelTemplate.Create(
                "org-fingerprint", "env-fingerprint", "TPL-FINGERPRINT", "Fingerprint template", "file-fingerprint",
                """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80}]}""", "active");
            setupDb.AddRange(rule, template);
            await setupDb.SaveChangesAsync();
            ruleId = rule.Id;
            templateId = template.Id;
        }

        var firstCommand = new CreateLabelPrintBatchCommand(
            "org-fingerprint", "env-fingerprint", ruleId, templateId, "work-order", "WO-FINGERPRINT",
            "same-fingerprint-key", """{"skuCode":"SKU-FG-1000"}""", 2)
        {
            ReportIntentFingerprint = "opaque:report-intent-first",
        };
        var competingCommand = firstCommand with
        {
            ReportIntentFingerprint = "opaque:report-intent-competing",
        };
        using var callerCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var firstScope = provider.CreateAsyncScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var firstTask = firstScope.ServiceProvider.GetRequiredService<ISender>()
            .Send(firstCommand, callerCancellation.Token);
        await assetPort.WaitUntilEnteredAsync(callerCancellation.Token);
        var holderProcessId = ((NpgsqlConnection)firstDb.Database.GetDbConnection()).ProcessID;

        await using var secondScope = provider.CreateAsyncScope();
        var competingTask = CaptureFailureAsync(async () =>
            _ = await secondScope.ServiceProvider.GetRequiredService<ISender>()
                .Send(competingCommand, callerCancellation.Token));
        try
        {
            await WaitForAdvisoryWaitersAsync(holderProcessId, 1, "different report fingerprint reservation fence");
            Assert.False(competingTask.IsCompleted);
        }
        finally
        {
            assetPort.Release();
        }

        var outcome = await TestTimeout.RunAsync(
            "different report fingerprint commands finish after the first reservation commits",
            async cancellationToken =>
            {
                var firstBatchId = await firstTask.WaitAsync(cancellationToken);
                var competingFailure = await competingTask.WaitAsync(cancellationToken);
                return (firstBatchId, competingFailure);
            },
            TimeSpan.FromSeconds(15),
            callerCancellation.Token,
            sensitiveValues: [LaneConnectionString]);
        var conflict = Assert.IsType<KnownException>(outcome.competingFailure);
        Assert.Equal("打印批次幂等键与已有记录不一致，请检查提交内容。", conflict.Message);
        Assert.Equal(1, assetPort.RequestCount);

        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        var persisted = await verificationDb.LabelPrintBatches.SingleAsync();
        Assert.Equal(outcome.firstBatchId, persisted.Id);
        Assert.Equal(firstCommand.ReportIntentFingerprint, persisted.ReportIntentFingerprint);
        Assert.Equal(2, await verificationDb.LabelPrintItems.CountAsync());
        Assert.Equal(2, await verificationDb.LabelSerialCounters.Select(counter => counter.CurrentValue).SingleAsync());
    }

    [RealPostgresFact]
    public async Task Create_handler_preserves_short_rule_capacity_after_wide_partition_advances_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await using (var wideDb = CreatePostgresDbContext(LaneConnectionString))
        await using (var wideTransaction = await wideDb.Database.BeginTransactionAsync())
        {
            _ = await new PostgresLabelSerialNumberAllocator(wideDb).AllocateAsync(
                "org-short-serial", "env-short-serial", 20, 3844, CancellationToken.None);
            await wideTransaction.CommitAsync();
        }

        await using var provider = CreateRetirementCommandProvider();
        BarcodeRuleId ruleId;
        LabelTemplateId templateId;
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rule = BarcodeRule.Create(
                "org-short-serial", "env-short-serial", "FG-SHORT", "code128", "FG", 4, "none", ["wms.inbound"], "active");
            var template = LabelTemplate.Create(
                "org-short-serial",
                "env-short-serial",
                "TPL-SHORT",
                "Short serial template",
                "file-template-short",
                """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80}]}""",
                "active");
            setupDb.AddRange(rule, template);
            await setupDb.SaveChangesAsync();
            ruleId = rule.Id;
            templateId = template.Id;
        }

        LabelPrintBatchId batchId;
        await using (var commandScope = provider.CreateAsyncScope())
        {
            batchId = await commandScope.ServiceProvider.GetRequiredService<ISender>().Send(
                new CreateLabelPrintBatchCommand(
                    "org-short-serial",
                    "env-short-serial",
                    ruleId,
                    templateId,
                    "wms.inbound",
                    "ASN-SHORT",
                    "short-serial-intent",
                    """{"skuCode":"SKU-FG-1000"}""",
                    1)
                {
                    ReportIntentFingerprint = "opaque:short-serial-intent",
                });
        }

        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        var batch = await verificationDb.LabelPrintBatches
            .Include(candidate => candidate.Items)
            .SingleAsync(candidate => candidate.Id == batchId);
        var item = Assert.Single(batch.Items);
        Assert.Equal("01", item.SerialNumber);
        Assert.Equal(4, item.LabelValue.Length);
        Assert.Equal($"FG{item.SerialNumber}", item.LabelValue);
        var counterValues = await verificationDb.LabelSerialCounters
            .Select(counter => counter.CurrentValue)
            .Order()
            .ToArrayAsync();
        Assert.Equal([1L, 3844L], counterValues);
    }

    [RealPostgresFact]
    public async Task Create_handlers_preserve_org_environment_scope_and_gs1_serial_authority_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await using var provider = CreateRetirementCommandProvider();
        var definitions = new[]
        {
            new { Organization = "org-a", Environment = "env-a", Code = "PLAIN-A", Type = "code128", Prefix = "A", Length = 40, Gs1PrefixLength = (int?)null },
            new { Organization = "org-b", Environment = "env-a", Code = "PLAIN-B", Type = "code128", Prefix = "B", Length = 40, Gs1PrefixLength = (int?)null },
            new { Organization = "org-a", Environment = "env-b", Code = "GS1-C", Type = "gs1-128", Prefix = "0950600013435", Length = 80, Gs1PrefixLength = (int?)7 },
        };
        var identities = new List<(string Organization, string Environment, BarcodeRuleId RuleId, LabelTemplateId TemplateId)>();
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var definition in definitions)
            {
                var rule = BarcodeRule.Create(
                    definition.Organization,
                    definition.Environment,
                    definition.Code,
                    definition.Type,
                    definition.Prefix,
                    definition.Length,
                    definition.Type.StartsWith("gs1-", StringComparison.Ordinal) ? "gs1-mod10" : "none",
                    ["work-order"],
                    "active",
                    definition.Gs1PrefixLength);
                var template = LabelTemplate.Create(
                    definition.Organization,
                    definition.Environment,
                    $"TPL-{definition.Code}",
                    $"{definition.Code} template",
                    $"file-{definition.Code.ToLowerInvariant()}",
                    definition.Type.StartsWith("gs1-", StringComparison.Ordinal)
                        ? """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80},{"name":"lotNo","type":"string","required":true,"maxLength":100},{"name":"serialPrefix","type":"string","required":false,"maxLength":100}]}"""
                        : """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80}]}""",
                    "active");
                setupDb.AddRange(rule, template);
                identities.Add((definition.Organization, definition.Environment, rule.Id, template.Id));
            }

            await setupDb.SaveChangesAsync();
        }

        var batchIds = new List<LabelPrintBatchId>();
        foreach (var identity in identities)
        {
            await using var commandScope = provider.CreateAsyncScope();
            var labelValuesJson = identity.RuleId == identities[2].RuleId
                ? """{"skuCode":"SKU-FG-1000","lotNo":"LOT-A","serialPrefix":"CALLER-CONTROLLED-"}"""
                : """{"skuCode":"SKU-FG-1000"}""";
            batchIds.Add(await commandScope.ServiceProvider.GetRequiredService<ISender>().Send(
                new CreateLabelPrintBatchCommand(
                    identity.Organization,
                    identity.Environment,
                    identity.RuleId,
                    identity.TemplateId,
                    "work-order",
                    $"WO-{identity.Organization}-{identity.Environment}",
                    $"intent-{identity.Organization}-{identity.Environment}",
                    labelValuesJson,
                    1)
                {
                    ReportIntentFingerprint = $"opaque:{identity.Organization}:{identity.Environment}",
                }));
        }

        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        var counters = await verificationDb.LabelSerialCounters
            .OrderBy(counter => counter.OrganizationId)
            .ThenBy(counter => counter.EnvironmentId)
            .Select(counter => new { counter.OrganizationId, counter.EnvironmentId, counter.CurrentValue })
            .ToArrayAsync();
        Assert.Equal(3, counters.Length);
        Assert.All(identities, identity => Assert.Contains(
            counters,
            counter => counter.OrganizationId == identity.Organization
                && counter.EnvironmentId == identity.Environment
                && counter.CurrentValue == 1));

        var gs1BatchId = batchIds[2];
        var gs1Item = await verificationDb.LabelPrintItems
            .SingleAsync(item => item.LabelPrintBatchId == gs1BatchId);
        Assert.Equal(20, gs1Item.SerialNumber!.Length);
        Assert.Equal("09506000134352", gs1Item.Gtin);
        Assert.Equal("LOT-A", gs1Item.LotNo);
        Assert.Contains($"(21){gs1Item.SerialNumber}", gs1Item.LabelValue, StringComparison.Ordinal);
        Assert.DoesNotContain("CALLER-CONTROLLED-", gs1Item.LabelValue, StringComparison.Ordinal);
    }

    [RealPostgresFact]
    public async Task Create_handlers_allocate_unique_gs1_serials_across_rules_in_same_scope_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await using var provider = CreateRetirementCommandProvider();
        BarcodeRuleId firstRuleId;
        BarcodeRuleId secondRuleId;
        LabelTemplateId firstTemplateId;
        LabelTemplateId secondTemplateId;
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var firstRule = BarcodeRule.Create(
                "org-gs1-rules", "env-gs1-rules", "GS1-A", "gs1-128", "0950600013435", 80,
                "gs1-mod10", ["work-order"], "active", 7);
            var secondRule = BarcodeRule.Create(
                "org-gs1-rules", "env-gs1-rules", "GS1-B", "gs1-128", "0950600013435", 80,
                "gs1-mod10", ["work-order"], "active", 7);
            var variableSchema =
                """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80},{"name":"lotNo","type":"string","required":true,"maxLength":100}]}""";
            var firstTemplate = LabelTemplate.Create(
                "org-gs1-rules", "env-gs1-rules", "TPL-GS1-A", "GS1 A template", "file-gs1-a",
                variableSchema, "active");
            var secondTemplate = LabelTemplate.Create(
                "org-gs1-rules", "env-gs1-rules", "TPL-GS1-B", "GS1 B template", "file-gs1-b",
                variableSchema, "active");
            setupDb.AddRange(firstRule, secondRule, firstTemplate, secondTemplate);
            await setupDb.SaveChangesAsync();
            firstRuleId = firstRule.Id;
            secondRuleId = secondRule.Id;
            firstTemplateId = firstTemplate.Id;
            secondTemplateId = secondTemplate.Id;
        }

        LabelPrintBatchId firstBatchId;
        await using (var firstScope = provider.CreateAsyncScope())
        {
            firstBatchId = await firstScope.ServiceProvider.GetRequiredService<ISender>().Send(
                new CreateLabelPrintBatchCommand(
                    "org-gs1-rules", "env-gs1-rules", firstRuleId, firstTemplateId,
                    "work-order", "WO-GS1-A", "intent-gs1-a",
                    """{"skuCode":"SKU-FG-1000","lotNo":"LOT-A"}""", 1)
                {
                    ReportIntentFingerprint = "opaque:gs1-a",
                });
        }

        LabelPrintBatchId secondBatchId;
        await using (var secondScope = provider.CreateAsyncScope())
        {
            secondBatchId = await secondScope.ServiceProvider.GetRequiredService<ISender>().Send(
                new CreateLabelPrintBatchCommand(
                    "org-gs1-rules", "env-gs1-rules", secondRuleId, secondTemplateId,
                    "work-order", "WO-GS1-B", "intent-gs1-b",
                    """{"skuCode":"SKU-FG-1000","lotNo":"LOT-B"}""", 1)
                {
                    ReportIntentFingerprint = "opaque:gs1-b",
                });
        }

        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        var items = await verificationDb.LabelPrintItems
            .Where(item => item.LabelPrintBatchId == firstBatchId || item.LabelPrintBatchId == secondBatchId)
            .OrderBy(item => item.CreatedAtUtc)
            .ToArrayAsync();
        Assert.Equal(2, items.Length);
        Assert.NotEqual(items[0].SerialNumber, items[1].SerialNumber);
        Assert.NotEqual(items[0].EpcUri, items[1].EpcUri);
        Assert.Equal(2, await verificationDb.LabelSerialCounters.Select(counter => counter.CurrentValue).SingleAsync());
    }

    [RealPostgresFact]
    public async Task Advisory_lock_domains_do_not_deadlock_crossed_reservation_and_template_keys_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await using var firstDb = CreatePostgresDbContext(LaneConnectionString);
        await using var firstTransaction = await firstDb.Database.BeginTransactionAsync();
        await using var secondDb = CreatePostgresDbContext(LaneConnectionString);
        await using var secondTransaction = await secondDb.Database.BeginTransactionAsync();

        await new PostgresLabelPrintBatchReservationFence(firstDb).AcquireAsync(
            "org-lock", "env-lock", "A", CancellationToken.None);
        await new PostgresLabelPrintBatchReservationFence(secondDb).AcquireAsync(
            "org-lock", "env-lock", "B", CancellationToken.None);

        var firstTemplateTask = new PostgresTemplateAssetRetirementFence(firstDb).AcquireAsync(
            "org-lock", "env-lock", "B", CancellationToken.None);
        var secondTemplateTask = new PostgresTemplateAssetRetirementFence(secondDb).AcquireAsync(
            "org-lock", "env-lock", "A", CancellationToken.None);

        await TestTimeout.RunAsync(
            "crossed reservation and template lock domains finish without deadlock",
            async cancellationToken => await Task.WhenAll(firstTemplateTask, secondTemplateTask).WaitAsync(cancellationToken),
            TimeSpan.FromSeconds(5),
            sensitiveValues: [LaneConnectionString]);
    }

    [RealPostgresFact]
    public async Task Concurrent_mes_activation_commits_one_association_and_rejects_overwrite_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        LabelPrintBatchId batchId;
        await using (var setupDb = CreatePostgresDbContext(LaneConnectionString))
        {
            var rule = BarcodeRule.Create(
                "org-mes-activation", "env-mes-activation", "MES-ACTIVATE", "code128", "M", 40,
                "none", ["work-order"], "active");
            var batch = AllocatedBatch(rule, "report-intent-concurrent", "WO-CONCURRENT", "MES-SERIAL-001");
            setupDb.AddRange(rule, batch);
            await setupDb.SaveChangesAsync();
            batchId = batch.Id;
        }

        await using var firstDb = CreatePostgresDbContext(LaneConnectionString);
        await using var firstTransaction = await firstDb.Database.BeginTransactionAsync();
        var firstHandler = new ActivateLabelPrintBatchCommandHandler(
            firstDb,
            new PostgresLabelPrintBatchActivationFence(firstDb));
        await firstHandler.Handle(
            new ActivateLabelPrintBatchCommand(
                batchId, "org-mes-activation", "env-mes-activation", "report-a", "RPT-A"),
            CancellationToken.None);
        await firstDb.SaveChangesAsync();
        var holderProcessId = ((NpgsqlConnection)firstDb.Database.GetDbConnection()).ProcessID;

        await using var secondDb = CreatePostgresDbContext(LaneConnectionString);
        await using var secondTransaction = await secondDb.Database.BeginTransactionAsync();
        var secondHandler = new ActivateLabelPrintBatchCommandHandler(
            secondDb,
            new PostgresLabelPrintBatchActivationFence(secondDb));
        var secondTask = Task.Run(async () =>
        {
            await secondHandler.Handle(
                new ActivateLabelPrintBatchCommand(
                    batchId, "org-mes-activation", "env-mes-activation", "report-b", "RPT-B"),
                CancellationToken.None);
            await secondDb.SaveChangesAsync();
        });

        var waitEdge = await WaitForAdvisoryWaiterOrCompletionAsync(
            holderProcessId,
            secondTask,
            "MES print-batch activation");
        Assert.True(waitEdge.Waiters > 0);
        Assert.False(waitEdge.CompetingTaskCompleted);

        await firstTransaction.CommitAsync();
        var conflict = await Assert.ThrowsAsync<KnownException>(() => secondTask);
        Assert.Equal("打印批次已关联其他 MES 生产上报，不能覆盖。", conflict.Message);
        await secondTransaction.RollbackAsync();

        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        var persisted = await verificationDb.LabelPrintBatches.SingleAsync(batch => batch.Id == batchId);
        Assert.Equal("ready-to-print", persisted.Status);
        Assert.Equal("report-a", persisted.ProductionReportId);
        Assert.Equal("RPT-A", persisted.ProductionReportNo);
    }

    [RealPostgresFact]
    public async Task Mes_activation_migration_preserves_historical_batch_items_and_makes_pending_printable_on_postgres()
    {
        await AssertReportIntentFingerprintMigrationAsync();
        await ResetBarcodeLabelSchemaAsync();
        await using (var setupDb = CreatePostgresDbContext(LaneConnectionString))
        {
            await setupDb.GetService<IMigrator>().MigrateAsync("20260913092130_AddBarcodeSerialAllocation");
            await setupDb.Database.ExecuteSqlRawAsync("""
                INSERT INTO barcode.label_print_batches (
                    id, organization_id, environment_id, barcode_rule_id, label_template_id,
                    source_document_type, source_document_id, idempotency_key, label_values_json,
                    requested_quantity, status, created_at_utc)
                VALUES
                    ('00000000-0000-0000-0000-000000000411', 'org-mes-migration', 'env-mes-migration',
                     '00000000-0000-0000-0000-000000000421', '00000000-0000-0000-0000-000000000431',
                     'mes.report', 'WO-MIGRATION', 'report-intent-migration', '{{}}', 1,
                     'pending', '2026-09-13T00:00:00Z');

                INSERT INTO barcode.label_print_items (
                    id, label_print_batch_id, organization_id, environment_id, sequence_no,
                    label_value, serial_number, status, created_at_utc)
                VALUES
                    ('00000000-0000-0000-0000-000000000441', '00000000-0000-0000-0000-000000000411',
                     'org-mes-migration', 'env-mes-migration', 1,
                     'SERIAL-MIGRATION', 'SERIAL-MIGRATION', 'created', '2026-09-13T00:00:00Z');
                """);
        }

        await using (var upgradeDb = CreatePostgresDbContext(LaneConnectionString))
        {
            await upgradeDb.Database.MigrateAsync();
        }

        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        var status = await verificationDb.Database.SqlQueryRaw<string>("""
            SELECT status AS "Value"
            FROM barcode.label_print_batches
            WHERE id = '00000000-0000-0000-0000-000000000411'
            """).SingleAsync();
        Assert.Equal("ready-to-print", status);
        var preservedItem = await verificationDb.Database.SqlQueryRaw<string>("""
            SELECT id::text || ':' || serial_number AS "Value"
            FROM barcode.label_print_items
            WHERE label_print_batch_id = '00000000-0000-0000-0000-000000000411'
            """).SingleAsync();
        Assert.Equal("00000000-0000-0000-0000-000000000441:SERIAL-MIGRATION", preservedItem);
        var mesColumns = await verificationDb.Database.SqlQueryRaw<string>("""
            SELECT production_report_id::text || ':' || production_report_no::text AS "Value"
            FROM barcode.label_print_batches
            WHERE id = '00000000-0000-0000-0000-000000000411'
            """).SingleOrDefaultAsync();
        Assert.Null(mesColumns);
    }

    private static async Task AssertReportIntentFingerprintMigrationAsync()
    {
        await ResetBarcodeLabelSchemaAsync();
        await using (var setupDb = CreatePostgresDbContext(LaneConnectionString))
        {
            await setupDb.GetService<IMigrator>().MigrateAsync("20260913144054_AddBarcodeMesActivation");
            await setupDb.Database.ExecuteSqlRawAsync("""
                INSERT INTO barcode.label_print_batches (
                    id, organization_id, environment_id, barcode_rule_id, label_template_id,
                    source_document_type, source_document_id, idempotency_key, label_values_json,
                    requested_quantity, status, created_at_utc)
                VALUES
                    ('00000000-0000-0000-0000-000000000511', 'org-fingerprint-migration', 'env-fingerprint-migration',
                     '00000000-0000-0000-0000-000000000521', '00000000-0000-0000-0000-000000000531',
                     'legacy', 'LEGACY-FINGERPRINT', 'legacy-fingerprint', '{{}}', 1,
                     'ready-to-print', '2026-09-14T00:00:00Z');
                """);
        }

        await using (var upgradeDb = CreatePostgresDbContext(LaneConnectionString))
        {
            await upgradeDb.Database.MigrateAsync();
        }

        await using (var historicalDb = CreatePostgresDbContext(LaneConnectionString))
        {
            var historicalFingerprint = await historicalDb.Database.SqlQueryRaw<string>("""
                SELECT report_intent_fingerprint AS "Value"
                FROM barcode.label_print_batches
                WHERE id = '00000000-0000-0000-0000-000000000511'
                """).SingleOrDefaultAsync();
            Assert.Null(historicalFingerprint);
            Assert.Equal(
                256,
                await historicalDb.Database.SqlQueryRaw<int>("""
                    SELECT character_maximum_length::integer AS "Value"
                    FROM information_schema.columns
                    WHERE table_schema = 'barcode'
                      AND table_name = 'label_print_batches'
                      AND column_name = 'report_intent_fingerprint'
                    """).SingleAsync());
            Assert.Equal(1, await historicalDb.LabelPrintBatches.CountAsync());
        }

        await using var provider = CreateRetirementCommandProvider();
        BarcodeRuleId ruleId;
        LabelTemplateId templateId;
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rule = BarcodeRule.Create(
                "org-fingerprint-migration", "env-fingerprint-migration", "FINGERPRINT-MIGRATION",
                "code128", "F", 40, "none", ["work-order"], "active");
            var template = LabelTemplate.Create(
                "org-fingerprint-migration", "env-fingerprint-migration", "TPL-FINGERPRINT-MIGRATION",
                "Fingerprint migration template", "file-fingerprint-migration",
                """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80}]}""", "active");
            setupDb.AddRange(rule, template);
            await setupDb.SaveChangesAsync();
            ruleId = rule.Id;
            templateId = template.Id;
        }

        var command = new CreateLabelPrintBatchCommand(
            "org-fingerprint-migration", "env-fingerprint-migration", ruleId, templateId,
            "work-order", "WO-FINGERPRINT-MIGRATION", "new-fingerprint",
            """{"skuCode":"SKU-FG-1000"}""", 1)
        {
            ReportIntentFingerprint = "  opaque:Fingerprint/Migration  ",
        };
        LabelPrintBatchId createdBatchId;
        await using (var commandScope = provider.CreateAsyncScope())
        {
            createdBatchId = await commandScope.ServiceProvider.GetRequiredService<ISender>().Send(command);
        }

        var generalCommand = command with
        {
            SourceDocumentId = "WO-GENERAL-LABEL",
            IdempotencyKey = "new-general-label-intent",
            ReportIntentFingerprint = null,
        };
        LabelPrintBatchId generalBatchId;
        await using (var commandScope = provider.CreateAsyncScope())
        {
            generalBatchId = await commandScope.ServiceProvider.GetRequiredService<ISender>().Send(generalCommand);
        }

        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        Assert.Equal(
            command.ReportIntentFingerprint,
            await verificationDb.LabelPrintBatches
                .Where(batch => batch.IdempotencyKey == "new-fingerprint")
                .Select(batch => batch.ReportIntentFingerprint)
                .SingleAsync());
        var scopedDetail = await new GetScopedLabelPrintBatchQueryHandler(verificationDb).Handle(
            new GetScopedLabelPrintBatchQuery(
                createdBatchId,
                "org-fingerprint-migration",
                "env-fingerprint-migration"),
            CancellationToken.None);
        Assert.Equal(command.ReportIntentFingerprint, scopedDetail.ReportIntentFingerprint);
        var generalDetail = await new GetScopedLabelPrintBatchQueryHandler(verificationDb).Handle(
            new GetScopedLabelPrintBatchQuery(
                generalBatchId,
                "org-fingerprint-migration",
                "env-fingerprint-migration"),
            CancellationToken.None);
        Assert.Null(generalDetail.ReportIntentFingerprint);
        Assert.Null(await verificationDb.LabelPrintBatches
            .Where(batch => batch.Id == generalBatchId)
            .Select(batch => batch.ReportIntentFingerprint)
            .SingleAsync());
        Assert.Equal(3, await verificationDb.LabelPrintBatches.CountAsync());
        Assert.Equal(2, await verificationDb.LabelPrintItems.CountAsync());
    }

    [RealPostgresFact]
    public async Task Print_item_serial_is_unique_inside_organization_and_environment_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await using var dbContext = CreatePostgresDbContext(LaneConnectionString);
        var rule = BarcodeRule.Create(
            "org-serial", "env-serial", "RULE-A", "code128", "A", 40, "none", ["work-order"], "active");
        var first = AllocatedBatch(rule, "intent-a", "WO-A", "00000000001");
        dbContext.AddRange(rule, first);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        dbContext.Add(AllocatedBatch(rule, "intent-b", "WO-B", "00000000001"));

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        var postgresFailure = Assert.IsType<PostgresException>(failure.InnerException);
        Assert.Equal("UX_label_print_items_serial_number", postgresFailure.ConstraintName);

        dbContext.ChangeTracker.Clear();
        var secondRule = BarcodeRule.Create(
            "org-serial", "env-serial", "RULE-B", "code128", "B", 40, "none", ["work-order"], "active");
        dbContext.AddRange(secondRule, AllocatedBatch(secondRule, "intent-c", "WO-C", "00000000001"));
        failure = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        postgresFailure = Assert.IsType<PostgresException>(failure.InnerException);
        Assert.Equal("UX_label_print_items_serial_number", postgresFailure.ConstraintName);
    }

    [RealPostgresFact]
    public async Task Migration_and_formatter_share_base62_case_order_for_legacy_gs1_serials_on_postgres()
    {
        await ResetBarcodeLabelSchemaAsync();
        await using (var setupDb = CreatePostgresDbContext(LaneConnectionString))
        {
            await setupDb.GetService<IMigrator>().MigrateAsync("20260912121435_AddTemplateAssetRetirementRetention");
            await setupDb.Database.ExecuteSqlRawAsync("""
                INSERT INTO barcode.label_print_batches (
                    id, organization_id, environment_id, barcode_rule_id, label_template_id,
                    source_document_type, source_document_id, idempotency_key, label_values_json,
                    requested_quantity, status, created_at_utc)
                VALUES
                    ('00000000-0000-0000-0000-000000000311', 'org-case-order', 'env-case-order',
                     '00000000-0000-0000-0000-000000000321', '00000000-0000-0000-0000-000000000331',
                     'legacy', 'LEGACY-UPPER-1', 'legacy-upper-1', '{{}}', 1, 'pending', '2026-09-01T00:00:00Z'),
                    ('00000000-0000-0000-0000-000000000312', 'org-case-order', 'env-case-order',
                     '00000000-0000-0000-0000-000000000322', '00000000-0000-0000-0000-000000000332',
                     'legacy', 'LEGACY-UPPER-2', 'legacy-upper-2', '{{}}', 1, 'pending', '2026-09-01T00:00:01Z'),
                    ('00000000-0000-0000-0000-000000000313', 'org-case-order', 'env-case-order',
                     '00000000-0000-0000-0000-000000000323', '00000000-0000-0000-0000-000000000333',
                     'legacy', 'LEGACY-LOWER-1', 'legacy-lower-1', '{{}}', 1, 'pending', '2026-09-01T00:00:02Z');

                INSERT INTO barcode.label_print_items (
                    id, label_print_batch_id, sequence_no, label_value, serial_number, status, created_at_utc)
                VALUES
                    ('00000000-0000-0000-0000-000000000341', '00000000-0000-0000-0000-000000000311',
                     1, 'LEGACY-UPPER-1', '000000000000000A0001', 'created', '2026-09-01T00:00:00Z'),
                    ('00000000-0000-0000-0000-000000000342', '00000000-0000-0000-0000-000000000312',
                     1, 'LEGACY-UPPER-2', '000000000000000A0002', 'created', '2026-09-01T00:00:01Z'),
                    ('00000000-0000-0000-0000-000000000343', '00000000-0000-0000-0000-000000000313',
                     1, 'LEGACY-LOWER-1', '000000000000000a0001', 'created', '2026-09-01T00:00:02Z');
                """);
        }

        await using (var upgradeDb = CreatePostgresDbContext(LaneConnectionString))
        {
            await upgradeDb.Database.MigrateAsync();
        }

        await using var provider = CreateRetirementCommandProvider();
        BarcodeRuleId ruleId;
        LabelTemplateId templateId;
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rule = BarcodeRule.Create(
                "org-case-order", "env-case-order", "GS1-CASE", "gs1-128", "0950600013435", 80,
                "gs1-mod10", ["legacy"], "active", 7);
            var template = LabelTemplate.Create(
                "org-case-order", "env-case-order", "TPL-GS1-CASE", "GS1 case-order template", "file-gs1-case",
                """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80},{"name":"lotNo","type":"string","required":true,"maxLength":100}]}""",
                "active");
            setupDb.AddRange(rule, template);
            await setupDb.SaveChangesAsync();
            ruleId = rule.Id;
            templateId = template.Id;
        }

        LabelPrintBatchId batchId;
        await using (var commandScope = provider.CreateAsyncScope())
        {
            batchId = await commandScope.ServiceProvider.GetRequiredService<ISender>().Send(
                new CreateLabelPrintBatchCommand(
                    "org-case-order", "env-case-order", ruleId, templateId, "legacy", "LEGACY-NEXT",
                    "legacy-next", """{"skuCode":"SKU-GS1-CASE","lotNo":"LOT-CASE"}""", 1)
                {
                    ReportIntentFingerprint = "opaque:legacy-next",
                });
        }

        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        var created = await verificationDb.LabelPrintItems
            .SingleAsync(item => item.LabelPrintBatchId == batchId);
        Assert.Equal("000000000000000a0002", created.SerialNumber);
        Assert.Contains("(21)000000000000000a0002", created.LabelValue, StringComparison.Ordinal);
    }

    [RealPostgresFact]
    public async Task Migration_excludes_legacy_gs1_serial_above_current_int64_generation_space_on_postgres()
    {
        await ResetBarcodeLabelSchemaAsync();
        await using (var setupDb = CreatePostgresDbContext(LaneConnectionString))
        {
            await setupDb.GetService<IMigrator>().MigrateAsync("20260912121435_AddTemplateAssetRetirementRetention");
            await setupDb.Database.ExecuteSqlRawAsync("""
                INSERT INTO barcode.label_print_batches (
                    id, organization_id, environment_id, barcode_rule_id, label_template_id,
                    source_document_type, source_document_id, idempotency_key, label_values_json,
                    requested_quantity, status, created_at_utc)
                VALUES
                    ('00000000-0000-0000-0000-000000000351', 'org-int64-bound', 'env-int64-bound',
                     '00000000-0000-0000-0000-000000000352', '00000000-0000-0000-0000-000000000353',
                     'legacy', 'LEGACY-INT64-BOUND', 'legacy-int64-bound', '{{}}', 1, 'pending', '2026-09-01T00:00:00Z');

                INSERT INTO barcode.label_print_items (
                    id, label_print_batch_id, sequence_no, label_value, serial_number, status, created_at_utc)
                VALUES
                    ('00000000-0000-0000-0000-000000000354', '00000000-0000-0000-0000-000000000351',
                     1, 'LEGACY-INT64-BOUND', 'zzzzzzzzzzzzzzzz0001', 'created', '2026-09-01T00:00:00Z');
                """);
        }

        await using (var upgradeDb = CreatePostgresDbContext(LaneConnectionString))
        {
            await upgradeDb.Database.MigrateAsync();
        }

        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        Assert.Equal("zzzzzzzzzzzzzzzz0001", await verificationDb.LabelPrintItems
            .Select(item => item.SerialNumber)
            .SingleAsync());
        Assert.False(await verificationDb.LabelSerialCounters.AnyAsync(
            counter => counter.OrganizationId == "org-int64-bound"
                && counter.EnvironmentId == "env-int64-bound"));
    }

    [RealPostgresFact]
    public async Task Migration_preserves_unambiguous_historical_serials_and_nulls_on_postgres()
    {
        await ResetBarcodeLabelSchemaAsync();
        await using (var setupDb = CreatePostgresDbContext(LaneConnectionString))
        {
            await setupDb.GetService<IMigrator>().MigrateAsync("20260912121435_AddTemplateAssetRetirementRetention");
            await setupDb.Database.ExecuteSqlRawAsync("""
                INSERT INTO barcode.label_print_batches (
                    id, organization_id, environment_id, barcode_rule_id, label_template_id,
                    source_document_type, source_document_id, idempotency_key, label_values_json,
                    requested_quantity, status, created_at_utc)
                VALUES
                    ('00000000-0000-0000-0000-000000000111', 'org-history', 'env-history',
                     '00000000-0000-0000-0000-000000000121', '00000000-0000-0000-0000-000000000131',
                     'legacy', 'LEGACY-A', 'legacy-a', '{{}}', 1, 'pending', '2026-09-01T00:00:00Z'),
                    ('00000000-0000-0000-0000-000000000112', 'org-history', 'env-history',
                     '00000000-0000-0000-0000-000000000122', '00000000-0000-0000-0000-000000000132',
                     'legacy', 'LEGACY-B', 'legacy-b', '{{}}', 1, 'pending', '2026-09-01T00:00:01Z'),
                    ('00000000-0000-0000-0000-000000000113', 'org-history', 'env-history',
                     '00000000-0000-0000-0000-000000000123', '00000000-0000-0000-0000-000000000133',
                     'legacy', 'LEGACY-C', 'legacy-c-non-base62', '{{}}', 1, 'pending', '2026-09-01T00:00:02Z');

                INSERT INTO barcode.label_print_items (
                    id, label_print_batch_id, sequence_no, label_value, serial_number, status, created_at_utc)
                VALUES
                    ('00000000-0000-0000-0000-000000000141', '00000000-0000-0000-0000-000000000111',
                     1, 'LEGACY-SERIAL', '0z', 'created', '2026-09-01T00:00:00Z'),
                    ('00000000-0000-0000-0000-000000000142', '00000000-0000-0000-0000-000000000112',
                     1, 'LEGACY-PLAIN', NULL, 'created', '2026-09-01T00:00:01Z'),
                    ('00000000-0000-0000-0000-000000000143', '00000000-0000-0000-0000-000000000113',
                     1, 'LEGACY-NON-BASE62', 'LEGACY-SERIAL', 'created', '2026-09-01T00:00:02Z');
                """);
        }

        await using (var upgradeDb = CreatePostgresDbContext(LaneConnectionString))
        {
            await upgradeDb.Database.MigrateAsync();
        }

        await using (var verificationDb = CreatePostgresDbContext(LaneConnectionString))
        {
            var items = await verificationDb.LabelPrintItems.OrderBy(item => item.Id).ToArrayAsync();
            Assert.Equal(3, items.Length);
            Assert.All(items, item =>
            {
                Assert.Equal("org-history", item.OrganizationId);
                Assert.Equal("env-history", item.EnvironmentId);
            });
            Assert.Contains(items, item => item.SerialNumber == "0z");
            Assert.Contains(items, item => item.SerialNumber == "LEGACY-SERIAL");
            Assert.Null(items.Single(item => item.SerialNumber is null).SerialNumber);
        }

        await using var provider = CreateRetirementCommandProvider();
        BarcodeRuleId ruleId;
        LabelTemplateId templateId;
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rule = BarcodeRule.Create(
                "org-history", "env-history", "HISTORY", "code128", "H", 3,
                "none", ["legacy"], "active");
            var template = LabelTemplate.Create(
                "org-history", "env-history", "TPL-HISTORY", "History template", "file-history",
                """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80}]}""",
                "active");
            setupDb.AddRange(rule, template);
            await setupDb.SaveChangesAsync();
            ruleId = rule.Id;
            templateId = template.Id;
        }

        LabelPrintBatchId batchId;
        await using (var commandScope = provider.CreateAsyncScope())
        {
            batchId = await commandScope.ServiceProvider.GetRequiredService<ISender>().Send(
                new CreateLabelPrintBatchCommand(
                    "org-history", "env-history", ruleId, templateId, "legacy", "LEGACY-C",
                    "legacy-c", """{"skuCode":"SKU-HISTORY"}""", 1)
                {
                    ReportIntentFingerprint = "opaque:legacy-c",
                });
        }

        await using var resultDb = CreatePostgresDbContext(LaneConnectionString);
        Assert.Equal("10", await resultDb.LabelPrintItems
            .Where(item => item.LabelPrintBatchId == batchId)
            .Select(item => item.SerialNumber)
            .SingleAsync());
        Assert.Equal(62, await resultDb.LabelSerialCounters.Select(counter => counter.CurrentValue).SingleAsync());
    }

    [RealPostgresFact]
    public async Task Migration_fails_closed_when_historical_serial_reaches_width_capacity_on_postgres()
    {
        await ResetBarcodeLabelSchemaAsync();
        await using (var setupDb = CreatePostgresDbContext(LaneConnectionString))
        {
            await setupDb.GetService<IMigrator>().MigrateAsync("20260912121435_AddTemplateAssetRetirementRetention");
            await setupDb.Database.ExecuteSqlRawAsync("""
                INSERT INTO barcode.label_print_batches (
                    id, organization_id, environment_id, barcode_rule_id, label_template_id,
                    source_document_type, source_document_id, idempotency_key, label_values_json,
                    requested_quantity, status, created_at_utc)
                VALUES
                    ('00000000-0000-0000-0000-000000000151', 'org-capacity', 'env-capacity',
                     '00000000-0000-0000-0000-000000000152', '00000000-0000-0000-0000-000000000153',
                     'legacy', 'LEGACY-CAPACITY', 'legacy-capacity', '{{}}', 1, 'pending', '2026-09-01T00:00:00Z');

                INSERT INTO barcode.label_print_items (
                    id, label_print_batch_id, sequence_no, label_value, serial_number, status, created_at_utc)
                VALUES
                    ('00000000-0000-0000-0000-000000000154', '00000000-0000-0000-0000-000000000151',
                     1, 'LEGACY-CAPACITY', 'zz', 'created', '2026-09-01T00:00:00Z');
                """);
        }

        PostgresException failure;
        await using (var upgradeDb = CreatePostgresDbContext(LaneConnectionString))
        {
            failure = await Assert.ThrowsAsync<PostgresException>(() => upgradeDb.Database.MigrateAsync());
        }

        Assert.Equal(PostgresErrorCodes.IntegrityConstraintViolation, failure.SqlState);
        Assert.Contains("AddBarcodeSerialAllocation aborted", failure.MessageText, StringComparison.Ordinal);
        Assert.Contains("org-capacity / env-capacity / width 2 / zz", failure.MessageText, StringComparison.Ordinal);

        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        Assert.Equal(1, await verificationDb.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM barcode.label_print_items WHERE serial_number = 'zz'").SingleAsync());
        Assert.DoesNotContain(
            "20260913092130_AddBarcodeSerialAllocation",
            await verificationDb.Database.GetAppliedMigrationsAsync());
    }

    [RealPostgresFact]
    public async Task Migration_fails_closed_with_conflict_facts_for_duplicate_historical_serials_on_postgres()
    {
        await ResetBarcodeLabelSchemaAsync();
        await using (var setupDb = CreatePostgresDbContext(LaneConnectionString))
        {
            await setupDb.GetService<IMigrator>().MigrateAsync("20260912121435_AddTemplateAssetRetirementRetention");
            await setupDb.Database.ExecuteSqlRawAsync("""
                INSERT INTO barcode.label_print_batches (
                    id, organization_id, environment_id, barcode_rule_id, label_template_id,
                    source_document_type, source_document_id, idempotency_key, label_values_json,
                    requested_quantity, status, created_at_utc)
                VALUES
                    ('00000000-0000-0000-0000-000000000211', 'org-conflict', 'env-conflict',
                     '00000000-0000-0000-0000-000000000221', '00000000-0000-0000-0000-000000000231',
                     'legacy', 'LEGACY-A', 'legacy-a', '{{}}', 1, 'pending', '2026-09-01T00:00:00Z'),
                    ('00000000-0000-0000-0000-000000000212', 'org-conflict', 'env-conflict',
                     '00000000-0000-0000-0000-000000000222', '00000000-0000-0000-0000-000000000232',
                     'legacy', 'LEGACY-B', 'legacy-b', '{{}}', 1, 'pending', '2026-09-01T00:00:01Z');

                INSERT INTO barcode.label_print_items (
                    id, label_print_batch_id, sequence_no, label_value, serial_number, status, created_at_utc)
                VALUES
                    ('00000000-0000-0000-0000-000000000241', '00000000-0000-0000-0000-000000000211',
                     1, 'LEGACY-DUPLICATE', 'LEGACY-DUPLICATE', 'created', '2026-09-01T00:00:00Z'),
                    ('00000000-0000-0000-0000-000000000242', '00000000-0000-0000-0000-000000000212',
                     1, 'LEGACY-DUPLICATE', 'LEGACY-DUPLICATE', 'created', '2026-09-01T00:00:01Z');
                """);
        }

        PostgresException failure;
        await using (var upgradeDb = CreatePostgresDbContext(LaneConnectionString))
        {
            failure = await Assert.ThrowsAsync<PostgresException>(() => upgradeDb.Database.MigrateAsync());
        }

        Assert.Equal(PostgresErrorCodes.IntegrityConstraintViolation, failure.SqlState);
        Assert.Contains("AddBarcodeSerialAllocation aborted", failure.MessageText, StringComparison.Ordinal);
        Assert.Contains(
            "org-conflict / env-conflict / LEGACY-DUPLICATE",
            failure.MessageText,
            StringComparison.Ordinal);
        Assert.Contains("00000000-0000-0000-0000-000000000241@00000000-0000-0000-0000-000000000211", failure.MessageText, StringComparison.Ordinal);
        Assert.Contains("00000000-0000-0000-0000-000000000242@00000000-0000-0000-0000-000000000212", failure.MessageText, StringComparison.Ordinal);

        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        Assert.Equal(2, await verificationDb.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM barcode.label_print_items WHERE serial_number = 'LEGACY-DUPLICATE'").SingleAsync());
        Assert.DoesNotContain(
            "20260913092130_AddBarcodeSerialAllocation",
            await verificationDb.Database.GetAppliedMigrationsAsync());
    }

    // Oracle: #3045 / #3028, one durable decision, frozen inputs, seven-day recovery boundary.
    [RealPostgresFact]
    public async Task Retirement_executor_recovers_lost_response_after_restart_with_frozen_inputs_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        var decision = await SeedExecutionDecisionAsync();
        var clock = new FakeTimeProvider(ExecutionEpoch);
        var remote = new RetirementTransport(decision, loseResponses: true);
        await AssertRetirementFactMetricsAsync(clock, "pending_decisions 1", "terminal_decisions{outcome=\"success\"} 0");
        await RunRetirementHostAsync(clock, remote, "pending");
        var first = await ReadExecutionDecisionAsync();
        Assert.Equal(ExecutionEpoch, first.FirstSentAtUtc);
        Assert.Equal(ExecutionEpoch.AddDays(7), first.RecoveryUntilUtc);
        Assert.Equal("pending", first.Status);
        Assert.Equal(1, remote.Acceptances);

        // Fresh context/executor after the original short proof has expired; the remote receipt is retained.
        clock.Advance(TimeSpan.FromMinutes(11));
        remote.LoseResponses = false;
        await RunRetirementHostAsync(clock, remote, "quota-released",
            ExecutionOptions with { ClientWindowSeconds = 1, LeaseSeconds = 1 });
        var completed = await ReadExecutionDecisionAsync();
        Assert.Equal("quota-released", completed.Status);
        Assert.Equal(2592000, completed.ClientWindowSeconds);
        Assert.Equal(300, completed.ExecutorLeaseSeconds);
        Assert.Equal(2592000, completed.ReplayHorizonSeconds);
        Assert.Equal(ExecutionEpoch, completed.QuotaReleasedAtUtc);
        Assert.Equal(2, remote.Requests.Count);
        // Recreate the collector/DbContext against persisted facts, as a restarted process does.
        await AssertRetirementFactMetricsAsync(clock, "pending_decisions 0", "terminal_decisions{outcome=\"success\"} 1");
        Assert.NotEqual(remote.Requests[0].Signature, remote.Requests[1].Signature);
        Assert.Equal(1, remote.Acceptances);
        Assert.False(await ExecuteRetirementAsync(clock, remote));
        Assert.Equal(2, remote.Requests.Count);
    }

    [RealPostgresFact]
    public async Task Retirement_executor_concurrent_delivery_and_abandoned_lease_recover_once_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        var decision = await SeedExecutionDecisionAsync();
        var clock = new FakeTimeProvider(ExecutionEpoch);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remote = new RetirementTransport(decision) { BeforeResponse = async ct =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
        }};
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var first = ExecuteRetirementAsync(clock, remote, ct: timeout.Token);
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            Assert.False(await ExecuteRetirementAsync(clock, remote, ct: timeout.Token));
            Assert.Single(remote.Requests);
        }
        finally { release.TrySetResult(); }
        await first;
        Assert.Equal("quota-released", (await ReadExecutionDecisionAsync()).Status);

        await ResetAndMigrateSchemaAsync();
        decision = await SeedExecutionDecisionAsync();
        await using (var abandoned = CreatePostgresDbContext(LaneConnectionString))
            Assert.NotNull(await new TemplateAssetRetirementExecutionStore(abandoned, clock).ClaimAsync(2592000, 300, 300, timeout.Token));
        var restartedRemote = new RetirementTransport(decision);
        Assert.False(await ExecuteRetirementAsync(clock, restartedRemote));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(await ExecuteRetirementAsync(clock, restartedRemote));
        Assert.Single(restartedRemote.Requests);
        Assert.Equal("quota-released", (await ReadExecutionDecisionAsync()).Status);
    }

    [RealPostgresFact]
    public async Task Retirement_executor_deadline_unknown_wins_both_inflight_orderings_on_postgres()
    {
        foreach (var afterBoundary in new[] { TimeSpan.Zero, TimeSpan.FromTicks(10) })
        foreach (var expiryFirst in new[] { false, true })
        {
            await ResetAndMigrateSchemaAsync();
            var decision = await SeedExecutionDecisionAsync();
            var clock = new FakeTimeProvider(ExecutionEpoch);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var remote = new RetirementTransport(decision) { BeforeResponse = async ct =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }};
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var inFlight = ExecuteRetirementAsync(clock, remote, ct: timeout.Token);
            try
            {
                await entered.Task.WaitAsync(timeout.Token);
                clock.Advance(TimeSpan.FromDays(7) + afterBoundary);
                if (expiryFirst) Assert.False(await ExecuteRetirementAsync(clock, remote, ct: timeout.Token));
            }
            finally { release.TrySetResult(); }
            await inFlight;
            await AssertPermanentUnknownAsync(clock, remote);
        }
    }

    [RealPostgresFact]
    public async Task Retirement_result_waiting_for_row_lock_samples_deadline_after_lock_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await SeedExecutionDecisionAsync();
        var clock = new FakeTimeProvider(ExecutionEpoch);
        await using var claimant = CreatePostgresDbContext(LaneConnectionString);
        var claim = await new TemplateAssetRetirementExecutionStore(claimant, clock).ClaimAsync(2592000, 300, 300, default);
        Assert.NotNull(claim);
        await using var holder = CreatePostgresDbContext(LaneConnectionString);
        await using var transaction = await holder.Database.BeginTransactionAsync();
        _ = await holder.TemplateAssetRetirementDecisions.FromSqlInterpolated(
            $"SELECT * FROM barcode.template_asset_retirement_decisions WHERE id = {claim.Id.Id} FOR UPDATE").SingleAsync();
        await using var completing = CreatePostgresDbContext(LaneConnectionString);
        await completing.Database.OpenConnectionAsync();
        var completingPid = ((Npgsql.NpgsqlConnection)completing.Database.GetDbConnection()).ProcessID;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var completion = new TemplateAssetRetirementExecutionStore(completing, clock)
            .CompleteAsync(claim, ExecutionEpoch, 2592000, timeout.Token);
        try
        {
            await Eventually.WaitAsync("retirement result waits for owned row", async ct =>
            {
                await using var command = holder.Database.GetDbConnection().CreateCommand();
                command.Transaction = holder.Database.CurrentTransaction!.GetDbTransaction();
                command.CommandText = $"SELECT cardinality(pg_blocking_pids({completingPid}))";
                return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
            }, count => count > 0, count => $"blockingProcesses={count}",
                new EventuallyOptions(TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(20), [LaneConnectionString]));
            clock.Advance(TimeSpan.FromDays(7));
        }
        finally { await transaction.CommitAsync(); }
        await completion;
        Assert.Equal("execution-outcome-unknown", (await ReadExecutionDecisionAsync()).Status);
    }

    [RealPostgresFact]
    public async Task Retirement_executor_result_before_deadline_commits_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        var decision = await SeedExecutionDecisionAsync();
        var clock = new FakeTimeProvider(ExecutionEpoch);
        var remote = new RetirementTransport(decision) { BeforeResponse = _ =>
        {
            clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromTicks(10));
            return Task.CompletedTask;
        }};
        await ExecuteRetirementAsync(clock, remote);
        Assert.Equal("quota-released", (await ReadExecutionDecisionAsync()).Status);
        clock.Advance(TimeSpan.FromDays(1));
        Assert.False(await ExecuteRetirementAsync(clock, remote));
        Assert.Equal("quota-released", (await ReadExecutionDecisionAsync()).Status);
    }

    [RealPostgresFact]
    public async Task Retirement_executor_unknown_preserves_both_remote_outcome_counterexamples_on_postgres()
    {
        foreach (var accepted in new[] { false, true })
        {
            await ResetAndMigrateSchemaAsync();
            var decision = await SeedExecutionDecisionAsync();
            var clock = new FakeTimeProvider(ExecutionEpoch);
            var remote = new RetirementTransport(decision, loseResponses: true) { Accept = accepted };
            await ExecuteRetirementAsync(clock, remote);
            // Model either remote physical completion or no acceptance at all; both are unobservable locally.
            remote.PhysicallyCompleted = accepted;
            clock.Advance(TimeSpan.FromDays(7));
            var registry = Prometheus.Metrics.NewCustomRegistry();
            var metrics = new TemplateAssetRetirementMetrics(registry,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<TemplateAssetRetirementMetrics>.Instance);
            for (var scan = 0; scan < 3; scan++)
                Assert.False(await ExecuteRetirementAsync(clock, remote, metrics: metrics));
            Assert.Equal(1, remote.Signatures);
            Assert.Single(remote.Requests);
            Assert.Contains("zero_reexecution_fence_hits_total{cause=\"execution-outcome-unknown\"} 3",
                await TemplateAssetRetirementMetricsTests.SamplesAsync(registry));
            await AssertRetirementFactMetricsAsync(clock, "execution_outcome_unknown_decisions 1",
                "terminal_decisions{outcome=\"success\"} 0");
            await AssertPermanentUnknownAsync(clock, remote);
            Assert.Equal(accepted ? 1 : 0, remote.Acceptances);
            Assert.Equal(accepted, remote.PhysicallyCompleted);
        }
    }

    [RealPostgresFact]
    public async Task Retirement_http_proof_rejections_leave_zero_decisions_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        var template = await SeedRetirementTemplateAsync();
        await using var factory = RetirementHttpFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "retirement-http-token");
        var request = RetirementProofCases.Request(template.Id.Id);
        foreach (var (name, invalid) in RetirementProofCases.InvalidRequests(request)
            .Append(("missing-proof", request with { Proof = "" })))
        {
            using var response = await client.PostAsJsonAsync(TemplateAssetRetirementProofV1.Route, invalid);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                $"{name}: expected 403, actual {(int)response.StatusCode}");
            var error = await response.Content.ReadFromJsonAsync<TemplateAssetRetirementProofError>();
            Assert.Equal("template-asset-retirement-proof-invalid", error!.Code);
            await using var db = CreatePostgresDbContext(LaneConnectionString);
            Assert.Equal(0, await db.TemplateAssetRetirementDecisions.CountAsync());
        }
        client.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await client.PostAsJsonAsync(TemplateAssetRetirementProofV1.Route, request);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await using var verification = CreatePostgresDbContext(LaneConnectionString);
        Assert.Equal(0, await verification.TemplateAssetRetirementDecisions.CountAsync());
    }

    [RealPostgresFact]
    public async Task Retirement_http_valid_proof_commits_authenticated_decision_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        var template = await SeedRetirementTemplateAsync();
        await using var factory = RetirementHttpFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "retirement-http-token");
        var request = RetirementProofCases.Request(template.Id.Id);
        using var response = await client.PostAsJsonAsync(TemplateAssetRetirementProofV1.Route, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var decisionId = body.RootElement.GetProperty("data").GetProperty("decisionId").GetGuid();
        await using var verification = CreatePostgresDbContext(LaneConnectionString);
        var decision = await verification.TemplateAssetRetirementDecisions.SingleAsync();
        Assert.Equal(decisionId, decision.Id.Id);
        Assert.Equal("user-3042", decision.RequesterSubject);
        Assert.Equal("business.barcodes.template-assets.retire", decision.Permission);
        Assert.Equal(request.Reason, decision.Reason);
        Assert.Equal(request.Checksum, decision.TemplateAssetSha256);
        Assert.Equal(decision.Id, (await verification.LabelTemplates.SingleAsync()).RetiredCurrentFileByDecisionId);
    }

    private static async Task<LabelTemplate> SeedRetirementTemplateAsync()
    {
        var template = LabelTemplate.Create("org-3042", "env-3042", "TPL-3042", "退役入口测试",
            "file-3042", """{"version":1,"variables":[]}""", "inactive");
        await using var setup = CreatePostgresDbContext(LaneConnectionString);
        setup.LabelTemplates.Add(template);
        await setup.SaveChangesAsync();
        return template;
    }

    private static WebApplicationFactory<Program> RetirementHttpFactory(TimeProvider? clock = null) => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:PostgreSQL", LaneConnectionString);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSQL"] = LaneConnectionString,
                    ["InternalService:BearerToken"] = "retirement-http-token",
                }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(clock ?? new RetirementProofCases.Clock());
                services.AddSingleton(Prometheus.Metrics.NewCustomRegistry());
            });
        });

    // #3047 / #3028: local terminal clock + frozen H, permanent minimal fence after detail cleanup.
    [RealPostgresFact]
    public async Task Retirement_retention_deadline_preserves_http_replay_fence_and_zero_execution_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        var template = await SeedRetirementTemplateAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(RetirementProofCases.Now));
        await using var factory = RetirementHttpFactory(clock);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "retirement-http-token");
        var request = RetirementProofCases.Request(template.Id.Id);
        using (var response = await client.PostAsJsonAsync(TemplateAssetRetirementProofV1.Route, request))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var decision = await ReadExecutionDecisionAsync();
        var remote = new RetirementTransport(decision);
        clock.Advance(TimeSpan.FromHours(2));
        var completedAt = clock.GetUtcNow();
        Assert.True(await ExecuteRetirementAsync(clock, remote));
        var completed = await ReadExecutionDecisionAsync();
        Assert.Equal(completedAt, completed.CompletedAtUtc);
        Assert.Equal(completedAt.AddDays(30), completed.ReplayUntilUtc);
        Assert.NotEqual(completed.QuotaReleasedAtUtc, completed.CompletedAtUtc);
        var deadline = completed.ReplayUntilUtc!.Value;
        var changedOptions = ExecutionOptions with { ClientWindowSeconds = 1, LeaseSeconds = 1 };

        foreach (var offset in new[] { -1L, 0L, 1L })
        {
            clock.SetUtcNow(deadline.AddTicks(offset));
            var fields = RetirementProofCases.Fields(request);
            fields[4] = clock.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            fields[5] = (clock.GetUtcNow().ToUnixTimeSeconds() + 300).ToString(System.Globalization.CultureInfo.InvariantCulture);
            request = request with { Proof = RetirementProofCases.Sign(fields) };
            using (var response = await client.PostAsJsonAsync(TemplateAssetRetirementProofV1.Route, request))
            {
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (offset < 0)
                    Assert.Equal(decision.Id.Id, body.RootElement.GetProperty("data").GetProperty("decisionId").GetGuid());
                else
                    Assert.Equal("replay-window-expired", body.RootElement.GetProperty("message").GetString());
            }
            Assert.False(await ExecuteRetirementAsync(clock, remote, changedOptions));
            await using var db = CreatePostgresDbContext(LaneConnectionString);
            Assert.Equal(offset < 0 ? 1 : 0, await db.TemplateAssetRetirementDecisions.CountAsync());
            var fence = await db.TemplateAssetRetirementReplayFences.SingleAsync();
            Assert.Equal(deadline, fence.ReplayUntilUtc);
            Assert.Equal(decision.Id, fence.Id);
            Assert.Equal(TemplateAssetRetirementReplayFence.DigestKey(request.IdempotencyKey), fence.IdempotencyKeyDigest);
            // The persisted permanent field set itself is a #3047 privacy acceptance deliverable.
            Assert.Equal(new[] { "EnvironmentId", "Id", "IdempotencyKeyDigest", "OrganizationId", "ReplayUntilUtc", "TemplateFileId" },
                db.Model.FindEntityType(typeof(TemplateAssetRetirementReplayFence))!.GetProperties().Select(x => x.Name).Order(StringComparer.Ordinal));
            Assert.Equal(1, remote.Signatures);
            Assert.Single(remote.Requests);
            Assert.Equal(1, remote.Acceptances);
            await AssertRetirementFactMetricsAsync(clock, "terminal_decisions{outcome=\"success\"} 1",
                $"replay_window_expired_decisions {(offset < 0 ? 0 : 1)}");
            var exposed = await client.GetStringAsync("/metrics");
            Assert.Contains("nerv_iip_barcode_retirement_accepted_decisions 1", exposed);
            if (offset >= 0)
                Assert.Contains($"zero_reexecution_fence_hits_total{{cause=\"replay-window-expired\"}} {offset + 1}", exposed);
        }

        await using var provider = CreateRetirementCommandProvider(clock: clock);
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var reuse = await Assert.ThrowsAsync<KnownException>(() => sender.Send(new CreateOrUpdateLabelTemplateCommand(
            request.OrganizationId, request.EnvironmentId, "NEW-TEMPLATE", "禁止复用", request.FileId,
            """{"version":1,"variables":[]}""", "active")));
        Assert.Equal("模板资产已经退役，不能重新用于标签模板。", reuse.Message);
        var command = new CreateTemplateAssetRetirementDecisionCommand(request.OrganizationId, request.EnvironmentId,
            template.Id, request.FileId, request.Checksum, request.IdempotencyKey, "user", TemplateAssetRetirementDecision.RequiredPermission,
            request.Reason, "retention-test");
        foreach (var replay in new[] { command with { IdempotencyKey = "different-key" }, command with { TemplateFileId = "different-file" } })
            Assert.Equal("replay-window-expired", (await Assert.ThrowsAsync<KnownException>(() => sender.Send(replay))).Message);
    }

    [RealPostgresFact]
    public async Task Retirement_cleanup_preserves_pending_failed_attempt_and_permanent_unknown_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        var decision = await SeedExecutionDecisionAsync();
        var clock = new FakeTimeProvider(ExecutionEpoch);
        await using (var db = CreatePostgresDbContext(LaneConnectionString))
        {
            clock.Advance(TimeSpan.FromDays(100));
            Assert.Equal(0, await new TemplateAssetRetirementExecutionStore(db, clock).CleanupAsync(CancellationToken.None));
            Assert.Equal("pending", (await ReadExecutionDecisionAsync()).Status);
        }
        var remote = new RetirementTransport(decision, loseResponses: true);
        Assert.True(await ExecuteRetirementAsync(clock, remote));
        clock.Advance(TimeSpan.FromDays(1));
        await using (var db = CreatePostgresDbContext(LaneConnectionString))
            Assert.Equal(0, await new TemplateAssetRetirementExecutionStore(db, clock).CleanupAsync(CancellationToken.None));
        Assert.Equal("pending", (await ReadExecutionDecisionAsync()).Status);
        clock.Advance(TimeSpan.FromDays(100));
        Assert.False(await ExecuteRetirementAsync(clock, remote));
        await AssertPermanentUnknownAsync(clock, remote);
        await using var verification = CreatePostgresDbContext(LaneConnectionString);
        Assert.Empty(await verification.TemplateAssetRetirementReplayFences.ToListAsync());
        Assert.Null((await ReadExecutionDecisionAsync()).ReplayUntilUtc);
    }

    [RealPostgresFact]
    public async Task Retirement_retention_migration_preserves_historical_terminal_clock_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        var decision = await SeedExecutionDecisionAsync();
        await using var db = CreatePostgresDbContext(LaneConnectionString);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260909074251_AddTemplateAssetRetirementExecution");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE barcode.template_asset_retirement_decisions SET status = 'quota-released', updated_at_utc = {ExecutionEpoch}, replay_horizon_seconds = 2592000 WHERE id = {decision.Id.Id}");
        await migrator.MigrateAsync();
        var restored = await ReadExecutionDecisionAsync();
        Assert.Equal(ExecutionEpoch, restored.CompletedAtUtc);
        Assert.Equal(ExecutionEpoch.AddDays(30), restored.ReplayUntilUtc);
        var fence = await db.TemplateAssetRetirementReplayFences.SingleAsync();
        Assert.Equal(restored.ReplayUntilUtc, fence.ReplayUntilUtc);
        Assert.Equal(TemplateAssetRetirementReplayFence.DigestKey(decision.IdempotencyKey), fence.IdempotencyKeyDigest);
        var clock = new FakeTimeProvider(ExecutionEpoch.AddDays(30));
        Assert.Equal(1, await new TemplateAssetRetirementExecutionStore(db, clock).CleanupAsync(CancellationToken.None));
        Assert.Empty(await db.TemplateAssetRetirementDecisions.ToListAsync());
        Assert.Single(await db.TemplateAssetRetirementReplayFences.ToListAsync());
    }

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
                    1)
                {
                    ReportIntentFingerprint = "opaque:retired-asset",
                }));
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
            var batch = LabelPrintBatch.ReconstituteHistorical(
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
            var batch = LabelPrintBatch.ReconstituteHistorical(
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
            var batch = LabelPrintBatch.ReconstituteHistorical(
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
        await AssertHistoricalFactOutcomeAsync("whitespace-file-snapshot", approved: false, "模板资产引用事实不完整，退役已安全拒绝。");
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
            var ownerAuditReplayId = await sender.Send(original with
            {
                LabelTemplateId = new LabelTemplateId(Guid.CreateVersion7()),
            });
            Assert.Equal(firstId, ownerAuditReplayId);
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
        var failures = await TestTimeout.RunAsync(
            "retirement and template reuse complete after the file fence is released",
            async cancellationToken => await Task.WhenAll(retirementTask, reuseTask).WaitAsync(cancellationToken),
            TimeSpan.FromSeconds(15),
            sensitiveValues: [LaneConnectionString]);
        Assert.Equal(1, failures.Count(failure => failure is null));
        Assert.Single(failures, failure => failure is KnownException);
    }

    [RealPostgresFact]
    public async Task Retirement_and_cross_file_template_rebind_preserve_a_consistent_marker_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await using var setupProvider = CreateRetirementCommandProvider();
        var templateId = await AddRetirementTemplateAsync(
            setupProvider,
            "TPL-CONCURRENT-REBIND",
            "file-concurrent-old");
        var barrier = new RetirementSaveBarrier();
        await using var retirementProvider = CreateRetirementCommandProvider(barrier);
        await using var rebindProvider = CreateRetirementCommandProvider();
        await using var retirementScope = retirementProvider.CreateAsyncScope();
        await using var rebindScope = rebindProvider.CreateAsyncScope();
        var retirementTask = CaptureFailureAsync(async () =>
            _ = await retirementScope.ServiceProvider.GetRequiredService<ISender>().Send(
                RetirementCommand(templateId, "file-concurrent-old", "concurrent-retirement-rebind")));

        await TestTimeout.RunAsync(
            "retirement command reaches the pre-save barrier",
            async cancellationToken => await barrier.WaitUntilEnteredAsync(cancellationToken),
            TimeSpan.FromSeconds(15),
            sensitiveValues: [LaneConnectionString]);
        Assert.True(barrier.HolderProcessId > 0);

        var rebindTask = CaptureFailureAsync(async () =>
            _ = await rebindScope.ServiceProvider.GetRequiredService<ISender>().Send(
                new CreateOrUpdateLabelTemplateCommand(
                    "org-retirement",
                    "env-retirement",
                    "TPL-CONCURRENT-REBIND",
                    "Concurrent rebound template",
                    "file-concurrent-new",
                    """{"version":1,"variables":[]}""",
                    LabelTemplate.InactiveStatus)));
        var observation = await WaitForAdvisoryWaiterOrCompletionAsync(
            barrier.HolderProcessId,
            rebindTask,
            "retirement A vs template rebind A-to-B");

        barrier.Release();
        var failures = await TestTimeout.RunAsync(
            "retirement and cross-file rebind complete after the barrier is released",
            async cancellationToken => await Task.WhenAll(retirementTask, rebindTask).WaitAsync(cancellationToken),
            TimeSpan.FromSeconds(15),
            sensitiveValues: [LaneConnectionString]);

        Assert.False(
            observation.CompetingTaskCompleted,
            "The A-to-B rebind completed without waiting for retirement's lock on old file A.");
        Assert.True(observation.Waiters > 0);
        Assert.All(failures, failure => Assert.Null(failure));

        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        var template = await verificationDb.LabelTemplates.AsNoTracking().SingleAsync(x => x.Id == templateId);
        var decision = await verificationDb.TemplateAssetRetirementDecisions.AsNoTracking().SingleAsync();
        Assert.Equal("file-concurrent-new", template.TemplateFileId);
        Assert.Null(template.RetiredCurrentFileByDecisionId);
        Assert.Equal("file-concurrent-old", decision.TemplateFileId);
    }

    [RealPostgresFact]
    public async Task Template_create_observation_fails_closed_when_same_code_appears_before_target_file_lock_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await using var provider = CreateRetirementCommandProvider();
        await using var gateDb = CreatePostgresDbContext(LaneConnectionString);
        await using var gateTransaction = await gateDb.Database.BeginTransactionAsync();
        await new PostgresTemplateAssetRetirementFence(gateDb).AcquireAsync(
            "org-retirement", "env-retirement", "file-appearing-new", CancellationToken.None);
        var holderProcessId = ((NpgsqlConnection)gateDb.Database.GetDbConnection()).ProcessID;

        await using var updateScope = provider.CreateAsyncScope();
        var updateTask = CaptureFailureAsync(async () =>
            _ = await updateScope.ServiceProvider.GetRequiredService<ISender>().Send(
                new CreateOrUpdateLabelTemplateCommand(
                    "org-retirement",
                    "env-retirement",
                    "TPL-APPEARING-WHILE-WAITING",
                    "Appearing template",
                    "file-appearing-new",
                    """{"version":1,"variables":[]}""",
                    LabelTemplate.InactiveStatus)));

        try
        {
            await WaitForAdvisoryWaitersAsync(holderProcessId, 1, "missing template create vs target file lock");
            Assert.False(updateTask.IsCompleted);

            var templateId = await AddRetirementTemplateAsync(
                provider,
                "TPL-APPEARING-WHILE-WAITING",
                "file-appearing-old");
            await using (var retirementScope = provider.CreateAsyncScope())
            {
                _ = await retirementScope.ServiceProvider.GetRequiredService<ISender>().Send(
                    RetirementCommand(templateId, "file-appearing-old", "retire-appearing-old"));
            }
        }
        finally
        {
            await gateTransaction.CommitAsync();
        }

        var updateFailure = await TestTimeout.RunAsync(
            "template command fails closed after a same-code template appears",
            async cancellationToken => await updateTask.WaitAsync(cancellationToken),
            TimeSpan.FromSeconds(15),
            sensitiveValues: [LaneConnectionString]);
        var knownFailure = Assert.IsType<KnownException>(updateFailure);
        Assert.Equal("标签模板当前文件已发生并发变化，请重试。", knownFailure.Message);

        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        var template = await verificationDb.LabelTemplates.AsNoTracking().SingleAsync();
        var decision = await verificationDb.TemplateAssetRetirementDecisions.AsNoTracking().SingleAsync();
        Assert.Equal("file-appearing-old", template.TemplateFileId);
        Assert.Equal(decision.Id, template.RetiredCurrentFileByDecisionId);
    }

    [RealPostgresFact]
    public async Task Retirement_rejects_when_inactive_template_rebind_to_target_file_commits_while_waiting_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        await using var setupProvider = CreateRetirementCommandProvider();
        var templateId = await AddRetirementTemplateAsync(
            setupProvider,
            "TPL-REBIND-WINS",
            "file-rebind-source");
        var barrier = new TemplateRebindSaveBarrier("file-rebind-target");
        await using var updateProvider = CreateRetirementCommandProvider(barrier);
        await using var retirementProvider = CreateRetirementCommandProvider();
        await using var updateScope = updateProvider.CreateAsyncScope();
        await using var retirementScope = retirementProvider.CreateAsyncScope();

        var updateTask = CaptureFailureAsync(async () =>
            _ = await updateScope.ServiceProvider.GetRequiredService<ISender>().Send(
                new CreateOrUpdateLabelTemplateCommand(
                    "org-retirement",
                    "env-retirement",
                    "TPL-REBIND-WINS",
                    "Rebound inactive template",
                    "file-rebind-target",
                    """{"version":1,"variables":[]}""",
                    LabelTemplate.InactiveStatus)));
        await TestTimeout.RunAsync(
            "inactive template rebind reaches the pre-save barrier",
            async cancellationToken => await barrier.WaitUntilEnteredAsync(cancellationToken),
            TimeSpan.FromSeconds(15),
            sensitiveValues: [LaneConnectionString]);
        Assert.True(barrier.HolderProcessId > 0);

        var retirementTask = CaptureFailureAsync(async () =>
            _ = await retirementScope.ServiceProvider.GetRequiredService<ISender>().Send(
                RetirementCommand(templateId, "file-rebind-target", "retire-after-rebind-wins")));
        try
        {
            await WaitForAdvisoryWaitersAsync(
                barrier.HolderProcessId,
                1,
                "inactive A-to-B rebind vs retirement B");
            Assert.False(updateTask.IsCompleted);
            Assert.False(retirementTask.IsCompleted);
        }
        finally
        {
            barrier.Release();
        }

        var failures = await TestTimeout.RunAsync(
            "inactive template rebind and retirement complete after the barrier is released",
            async cancellationToken => await Task.WhenAll(updateTask, retirementTask).WaitAsync(cancellationToken),
            TimeSpan.FromSeconds(15),
            sensitiveValues: [LaneConnectionString]);
        Assert.Null(failures[0]);
        var retirementFailure = Assert.IsType<KnownException>(failures[1]);
        Assert.Equal("模板资产引用事实不完整，退役已安全拒绝。", retirementFailure.Message);

        await using var verificationDb = CreatePostgresDbContext(LaneConnectionString);
        var template = await verificationDb.LabelTemplates.AsNoTracking().SingleAsync(x => x.Id == templateId);
        Assert.Equal("file-rebind-target", template.TemplateFileId);
        Assert.Null(template.RetiredCurrentFileByDecisionId);
        Assert.Empty(await verificationDb.TemplateAssetRetirementDecisions.AsNoTracking().ToListAsync());
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
            LabelTemplate.ActiveStatus,
            """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80}]}""");
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
                "batch-concurrent-retirement", """{"skuCode":"SKU-FG-1000"}""", 1)
            {
                ReportIntentFingerprint = "opaque:concurrent-retirement",
            }));

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
        var failures = await TestTimeout.RunAsync(
            "retirement and new batch freeze complete after the file fence is released",
            async cancellationToken => await Task.WhenAll(retirementTask, batchTask).WaitAsync(cancellationToken),
            TimeSpan.FromSeconds(15),
            sensitiveValues: [LaneConnectionString]);
        Assert.IsType<KnownException>(failures[0]);
        Assert.Null(failures[1]);

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
            var replayBatch = LabelPrintBatch.ReconstituteHistorical(
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

            var whitespaceConstraintFailure = await Assert.ThrowsAsync<PostgresException>(() =>
                dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE barcode.label_print_batches
                    SET template_file_id_snapshot = {"\t\n\u00a0"}
                    WHERE id = {replayBatch.Id.Id}
                    """));
            Assert.Equal(PostgresErrorCodes.CheckViolation, whitespaceConstraintFailure.SqlState);

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
