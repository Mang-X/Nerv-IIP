using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Seed;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

/// <summary>
/// L1 背景历史（条码标签域侧）的常规门禁测试：形状、确定性、幂等、隔离、
/// 扫码 ↔ 源单据对账、时间戳边界与 fail-closed。
/// 全量规模下的真实数据库耗时实测在 <see cref="WorldHistoryLabelSeedPostgresTests"/>（env-gated）。
/// </summary>
public sealed class WorldHistoryLabelSeedServiceTests(ITestOutputHelper output)
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);

    /// <summary>库写入类用例的规模：足够跑出四族打印批次与五族扫码，又不让 InMemory provider 变慢。</summary>
    private const double SmallScale = 0.05d;

    [Fact]
    public void Full_scale_print_batches_match_the_world_bible_target()
    {
        var batches = WorldHistoryLabelSpec.BuildPrintBatchFacts(AsOfDate, 1.0d);

        foreach (var group in batches.GroupBy(x => x.Family).OrderBy(x => x.Key))
        {
            output.WriteLine(FormattableString.Invariant(
                $"label-world-history-batches-{group.Key}={group.Count()} items={group.Sum(x => x.RequestedQuantity)}"));
        }

        var printed = batches.Count(x => x.Printed);
        output.WriteLine($"label-world-history-batches-total={batches.Count}");
        output.WriteLine($"label-world-history-batches-printed={printed}");
        output.WriteLine($"label-world-history-batches-failed={batches.Count - printed}");
        output.WriteLine($"label-world-history-batches-with-voided-item={batches.Count(x => x.VoidedSequenceNo is not null)}");
        output.WriteLine($"label-world-history-batches-with-reprinted-item={batches.Count(x => x.ReprintedSequenceNo is not null)}");
        output.WriteLine($"label-world-history-print-items={batches.Sum(x => x.RequestedQuantity)}");
        output.WriteLine($"label-world-history-epcis-events={batches.Where(x => x.Family == WorldHistoryLabelFamily.Carton).Sum(x => x.RequestedQuantity)}");
        output.WriteLine($"label-world-history-work-centers={WorldHistoryLabelSpec.WorkCenterCodes.Count}");

        // 设定集 §7：打印批次约 900。目标由规格精确算出，这里断言等值而不是区间。
        Assert.Equal(WorldHistoryLabelSpec.PrintBatchTarget, batches.Count);
        Assert.Equal(4, batches.Select(x => x.Family).Distinct().Count());
        Assert.Equal(
            WorldHistoryLabelSpec.WorkCenterCodes.Count,
            batches.Count(x => x.Family == WorldHistoryLabelFamily.Station));
        Assert.All(batches, batch => Assert.InRange(batch.RequestedQuantity, 1, 8));

        // 标签明细总量必须留在可演示量级，不能因为「一单一标签」而膨胀到万级。
        Assert.InRange(batches.Sum(x => x.RequestedQuantity), 1_000, 6_000);
        Assert.True(printed > 0 && printed < batches.Count, "打印批次必须既有成功也有失败。");
    }

    [Fact]
    public void Full_scale_scan_records_cover_every_material_movement_workflow()
    {
        var scans = WorldHistoryLabelSpec.BuildScanFacts(AsOfDate, 1.0d);

        foreach (var group in scans.GroupBy(x => x.SourceWorkflow).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"label-world-history-scans-{group.Key}={group.Count()}");
        }

        output.WriteLine($"label-world-history-scans-total={scans.Count}");
        output.WriteLine($"label-world-history-scans-accepted={scans.Count(x => x.IsAccepted)}");
        output.WriteLine($"label-world-history-scans-rejected={scans.Count(x => !x.IsAccepted)}");
        output.WriteLine($"label-world-history-scan-devices={scans.Select(x => x.DeviceCode).Distinct(StringComparer.Ordinal).Count()}");

        Assert.Equal(WorldHistoryLabelSpec.ScanRecordTarget, scans.Count);
        Assert.Equal(
            "inventory.issue,inventory.receipt,production.report,quality.inspection,wms.receiving",
            string.Join(',', scans.Select(x => x.SourceWorkflow).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));
        Assert.True(scans.Count(x => !x.IsAccepted) > 0, "历史扫码必须留下被拒记录。");
        Assert.All(scans.Where(x => !x.IsAccepted), scan => Assert.False(string.IsNullOrWhiteSpace(scan.RejectionReason)));

        // 领料是扫码量最大的一族（设定集 §7「与领料 / 入库 / 出库动作对应」）。
        Assert.True(scans.Count(x => x.SourceWorkflow == "inventory.issue") >
            scans.Count(x => x.SourceWorkflow == "inventory.receipt"));
    }

    [Fact]
    public void Accepted_inventory_scans_carry_the_full_inventory_dimension()
    {
        foreach (var scan in WorldHistoryLabelSpec.BuildScanFacts(AsOfDate, 0.2d)
                     .Where(x => x.IsAccepted && x.RequiresInventoryContext))
        {
            Assert.False(string.IsNullOrWhiteSpace(scan.SkuCode));
            Assert.False(string.IsNullOrWhiteSpace(scan.UomCode));
            Assert.Equal(WorldHistorySpec.SiteCode, scan.SiteCode);
            Assert.False(string.IsNullOrWhiteSpace(scan.LocationCode));
            Assert.False(string.IsNullOrWhiteSpace(scan.QualityStatus));
            Assert.Equal(WorldHistoryLabelSpec.OwnerType, scan.OwnerType);
            Assert.True(scan.Quantity > 0m, $"{scan.IdempotencyKey} 的库存扫码数量必须为正。");
        }
    }

    [Fact]
    public void All_fact_timestamps_stay_inside_the_history_window_and_off_sunday()
    {
        var lowerBound = new DateTimeOffset(WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var upperBound = new DateTimeOffset(AsOfDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        foreach (var batch in WorldHistoryLabelSpec.BuildPrintBatchFacts(AsOfDate, 0.2d))
        {
            AssertMoment(batch.IdempotencyKey, batch.CreatedAtUtc, lowerBound, upperBound);
            AssertMoment(batch.IdempotencyKey, batch.CompletedAtUtc, lowerBound, upperBound);
            Assert.True(batch.CompletedAtUtc >= batch.CreatedAtUtc);
            if (batch.SentToPrinterAtUtc is { } sentAtUtc)
            {
                AssertMoment(batch.IdempotencyKey, sentAtUtc, lowerBound, upperBound);
                Assert.True(sentAtUtc >= batch.CreatedAtUtc && sentAtUtc <= batch.CompletedAtUtc);
            }
        }

        foreach (var scan in WorldHistoryLabelSpec.BuildScanFacts(AsOfDate, 0.2d))
        {
            AssertMoment(scan.IdempotencyKey, scan.SourceMomentUtc, lowerBound, upperBound);
            AssertMoment(scan.IdempotencyKey, scan.ScannedAtUtc, lowerBound, upperBound);

            // 「时间戳与源单据一致」：扫码不早于源单据动作时刻，且不超过同班次走动时间。
            Assert.True(scan.ScannedAtUtc >= scan.SourceMomentUtc, $"{scan.IdempotencyKey} 扫码早于源单据动作。");
            Assert.True(
                scan.ScannedAtUtc - scan.SourceMomentUtc <= WorldHistoryConsistencyValidator.MaxScanDelay,
                $"{scan.IdempotencyKey} 扫码晚于源单据动作太久。");
        }
    }

    [Fact]
    public void Fact_streams_are_deterministic_for_the_same_inputs()
    {
        Assert.Equal(
            WorldHistoryLabelSpec.BuildPrintBatchFacts(AsOfDate, 0.2d),
            WorldHistoryLabelSpec.BuildPrintBatchFacts(AsOfDate, 0.2d));
        Assert.Equal(
            WorldHistoryLabelSpec.BuildScanFacts(AsOfDate, 0.2d),
            WorldHistoryLabelSpec.BuildScanFacts(AsOfDate, 0.2d));
    }

    [Fact]
    public void A_print_batchs_content_is_independent_of_the_scale()
    {
        var small = WorldHistoryLabelSpec.BuildPrintBatchFacts(AsOfDate, 0.2d)
            .ToDictionary(x => x.IdempotencyKey, StringComparer.Ordinal);
        var large = WorldHistoryLabelSpec.BuildPrintBatchFacts(AsOfDate, 1.0d)
            .ToDictionary(x => x.IdempotencyKey, StringComparer.Ordinal);

        // 补产工单 WO-2026-R#### 在不同 Scale 下挂的源订单本就不同（一期 WorldHistoryPhase2Spec），
        // 因此不参与缩放无关性比对。
        var shared = small.Keys
            .Intersect(large.Keys, StringComparer.Ordinal)
            .Where(key => !key.Contains("WO-2026-R", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(shared);
        foreach (var key in shared)
        {
            Assert.Equal(small[key].TemplateCode, large[key].TemplateCode);
            Assert.Equal(small[key].RuleCode, large[key].RuleCode);
            Assert.Equal(small[key].Printed, large[key].Printed);
            Assert.Equal(small[key].PrinterId, large[key].PrinterId);
        }
    }

    [Fact]
    public void Idempotency_keys_are_unique_across_both_fact_streams()
    {
        var batches = WorldHistoryLabelSpec.BuildPrintBatchFacts(AsOfDate, 1.0d);
        var scans = WorldHistoryLabelSpec.BuildScanFacts(AsOfDate, 1.0d);

        Assert.Equal(batches.Count, batches.Select(x => x.IdempotencyKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(scans.Count, scans.Select(x => x.IdempotencyKey).Distinct(StringComparer.Ordinal).Count());

        // 扫码的第二自然键（值 + 工作流 + 源单据）也必须唯一，否则库里的唯一索引会直接拒绝。
        Assert.Equal(
            scans.Count,
            scans
                .Select(x => $"{x.RuleCode}|{x.ValueSourceDocumentType}|{x.ValueSourceDocumentId}|{x.ValueSequence}|{x.SourceWorkflow}")
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public async Task Seed_writes_the_full_chain_and_reruns_without_writing_anything()
    {
        await using var db = CreateDbContext();
        var seed = new WorldHistorySeedService(db);

        var first = await seed.SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);
        var second = await seed.SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var batchFacts = WorldHistoryLabelSpec.BuildPrintBatchFacts(AsOfDate, SmallScale);
        var scanFacts = WorldHistoryLabelSpec.BuildScanFacts(AsOfDate, SmallScale);

        output.WriteLine($"small-scale-templates={first.LabelTemplatesWritten}");
        output.WriteLine($"small-scale-rules={first.BarcodeRulesWritten}");
        output.WriteLine($"small-scale-batches={first.PrintBatchesWritten}");
        output.WriteLine($"small-scale-items={first.PrintItemsWritten}");
        output.WriteLine($"small-scale-epcis={first.EpcisEventsWritten}");
        output.WriteLine($"small-scale-scans={first.ScanRecordsWritten}");
        foreach (var line in first.Validation.Sample)
        {
            output.WriteLine($"small-scale-sample: {line}");
        }

        Assert.Equal(4, first.LabelTemplatesWritten);
        Assert.Equal(4, first.BarcodeRulesWritten);
        Assert.Equal(batchFacts.Count, first.PrintBatchesWritten);
        Assert.Equal(batchFacts.Sum(x => x.RequestedQuantity), first.PrintItemsWritten);
        Assert.Equal(scanFacts.Count, first.ScanRecordsWritten);

        // 只有成品箱贴走 GS1 序列化，因此 EPCIS 建档事件数等于箱贴标签张数。
        Assert.Equal(
            batchFacts.Where(x => x.Family == WorldHistoryLabelFamily.Carton).Sum(x => x.RequestedQuantity),
            first.EpcisEventsWritten);

        Assert.Equal(0, second.LabelTemplatesWritten);
        Assert.Equal(0, second.BarcodeRulesWritten);
        Assert.Equal(0, second.PrintBatchesWritten);
        Assert.Equal(0, second.ScanRecordsWritten);
        Assert.Equal(batchFacts.Count, await db.LabelPrintBatches.CountAsync());
        Assert.Equal(scanFacts.Count, await db.ScanRecords.CountAsync());
        Assert.Equal(4, await db.LabelTemplates.CountAsync());
        Assert.Equal(4, await db.BarcodeRules.CountAsync());
    }

    [Fact]
    public async Task Seeded_print_batches_reach_a_terminal_status_with_matching_item_counts()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var batches = await db.LabelPrintBatches.Include(x => x.Items).ToArrayAsync();
        Assert.NotEmpty(batches);
        foreach (var batch in batches)
        {
            Assert.Contains(batch.Status, new[] { "printed", "failed" });
            Assert.Equal(batch.RequestedQuantity, batch.Items.Count);
            Assert.NotNull(batch.CompletedAtUtc);
        }

        Assert.Contains(batches, batch => batch.Status == "failed");
        Assert.Contains(batches, batch => batch.Items.Any(item => item.Status == "voided"));
    }

    [Fact]
    public async Task Seeded_scans_reconcile_with_the_shared_source_document_shape()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var index = WorldHistorySourceDocumentIndex.Build(AsOfDate, SmallScale);
        var scans = await db.ScanRecords.ToArrayAsync();
        Assert.NotEmpty(scans);
        foreach (var scan in scans)
        {
            Assert.True(
                index.Contains(scan.SourceWorkflow, scan.SourceDocumentId),
                $"{scan.IdempotencyKey} 挂在共享形状并不产出的单据 {scan.SourceDocumentId} 上。");
            Assert.True(scan.ScannedAtUtc >= index.MomentFor(scan.SourceWorkflow, scan.SourceDocumentId));
        }
    }

    [Fact]
    public async Task Seeded_documents_stay_isolated_from_the_reserved_number_segments()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var values = (await db.LabelPrintBatches.Select(x => x.SourceDocumentId).ToArrayAsync())
            .Concat(await db.LabelPrintItems.Select(x => x.LabelValue).ToArrayAsync())
            .Concat(await db.ScanRecords.Select(x => x.SourceDocumentId).ToArrayAsync())
            .Concat(await db.ScanRecords.Select(x => x.ScannedValue).ToArrayAsync())
            .Concat(await db.LabelTemplates.Select(x => x.TemplateCode).ToArrayAsync())
            .Concat(await db.BarcodeRules.Select(x => x.RuleCode).ToArrayAsync())
            .ToArray();

        Assert.NotEmpty(values);
        foreach (var value in values)
        {
            Assert.DoesNotContain("-DEMO-", value, StringComparison.Ordinal);
            Assert.DoesNotContain("-SCALE-", value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Validator_fails_closed_when_a_planned_scan_disappears()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        db.ScanRecords.Remove(await db.ScanRecords.FirstAsync());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorldHistoryLabelConsistencyException>(() =>
            new WorldHistoryConsistencyValidator(db).ValidateAsync("org-001", "env-dev", AsOfDate, SmallScale));

        Assert.NotEmpty(exception.Failures);
        Assert.Contains(exception.Failures, failure => failure.Contains("未落库", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validator_fails_closed_when_a_print_batch_loses_a_label_item()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var batch = await db.LabelPrintBatches.Include(x => x.Items).FirstAsync(x => x.Items.Count > 1);
        db.LabelPrintItems.Remove(batch.Items[0]);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorldHistoryLabelConsistencyException>(() =>
            new WorldHistoryConsistencyValidator(db).ValidateAsync("org-001", "env-dev", AsOfDate, SmallScale));

        Assert.Contains(exception.Failures, failure => failure.Contains("标签明细数", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validator_fails_closed_when_a_label_template_is_deactivated()
    {
        await using var db = CreateDbContext();
        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", AsOfDate, SmallScale);

        var template = await db.LabelTemplates.FirstAsync();
        template.Update(template.TemplateName, template.TemplateFileId, template.VariableSchemaJson, "inactive");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<WorldHistoryLabelConsistencyException>(() =>
            new WorldHistoryConsistencyValidator(db).ValidateAsync("org-001", "env-dev", AsOfDate, SmallScale));

        Assert.Contains(exception.Failures, failure => failure.Contains("必须 active", StringComparison.Ordinal));
    }

    private static void AssertMoment(
        string label,
        DateTimeOffset moment,
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound)
    {
        Assert.InRange(moment, lowerBound, upperBound);
        Assert.True(
            WorldHistoryCalendar.IsWorkingDay(DateOnly.FromDateTime(moment.UtcDateTime)),
            $"{label} 的时间戳 {moment:O} 落在周日（停产保养日）。");
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"barcode-world-history-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new WorldHistoryTestMediator());
    }

    private sealed class WorldHistoryTestMediator : IMediator
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
