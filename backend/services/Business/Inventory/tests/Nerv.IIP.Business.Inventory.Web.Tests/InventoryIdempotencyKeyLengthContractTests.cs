using System.Globalization;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockReservations;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockStatusTransfers;
using Nerv.IIP.Business.Inventory.Web.Application.Validation;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

/// <summary>
/// GitHub #3176：幂等键的有效上界是「列宽 − handler 落库前追加的最长后缀」，不是列宽本身。
///
/// 这个类把**三者关系**钉住，而不是只钉住某一个数字：
/// - <see cref="Idempotency_key_column_width_matches_the_policy_constant"/>：列宽（读 EF 模型元数据，
///   不读代码里的常量）必须等于策略常量 → 单边改 <c>HasMaxLength</c> 即红；
/// - <see cref="Every_suffixing_write_face_rejects_a_base_key_that_would_overflow_the_column"/>：
///   逐个写面跑**真实校验器**，上界那一位放行、+1 位拒绝 → 校验器上界改回列宽即红；
/// - <see cref="Handler_composed_keys_of_a_max_length_base_key_still_fit_the_column"/>：拿真实 handler
///   跑「恰好顶到校验器上界」的基础键，把它**实际落库的键长**与**从 EF 模型读到的列宽**直接对撞
///   → 这条不经过任何策略常量，handler 换个更长的后缀而没有同步上界就红。
/// </summary>
public sealed partial class InventoryIdempotencyKeyLengthContractTests
{
    /// <summary>
    /// <c>idempotency_key</c> 这一列在 Inventory 的 EF 模型里的**具名豁免**：
    /// 该表来自共享的 <c>Nerv.IIP.Messaging.CAP</c> 死信箱，列宽 500、不由本策略管辖。
    /// 豁免必须具名且被计数封闭——「名单里没有」不构成豁免。
    /// </summary>
    private const string DeadLetterEntityName = "Nerv.IIP.Messaging.CAP.IntegrationEventDeadLetter";

    /// <summary>
    /// EF 模型里 <c>idempotency_key</c> 列的总数（含上面那条具名豁免）。真库读数见 PR 正文。
    /// 这条计数是**下界**：新增一张带该列的表、或改列名让值域塌成空集，都会红——
    /// 否则 <c>Assert.All</c> 对空集恒真，护栏会静默缴械。
    /// </summary>
    private const int IdempotencyKeyColumnCount = 6;

    private const string InventoryWebApplicationRelativeRoot =
        "backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application";

    private const string PolicySourceRelativePath = "Validation/InventoryIdempotencyKeyPolicy.cs";

    /// <summary>
    /// <c>Application/</c> 下调用 <c>InventoryIdempotencyKeyPolicy.Compose</c> 的位点闭集（逐文件精确计数）。
    /// 把任一处换成裸 <c>+</c> 拼接，对应计数会掉到 0 而红（实测 M5b）。
    /// </summary>
    private static readonly (string RelativePath, int ComposeCallCount)[] ExpectedComposeSites =
    [
        ("Commands/StockMovements/PostStockMovementCommand.cs", 1),
        ("Commands/StockReservations/ReserveStockCommand.cs", 1),
        ("Commands/StockStatusTransfers/PostStockStatusTransferCommand.cs", 2),
        ("Expiry/ExpiredStockBlockingService.cs", 1),
        ("IntegrationEventHandlers/QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.cs", 2),
    ];

    /// <summary>
    /// 「在幂等键上直接做字符串加法 / 把它嵌进插值再续写」的**具名豁免**闭集。
    /// 当前唯一一条是 FEFO 重放查询的 <c>StartsWith</c> 谓词：它构造的是查询前缀、不落库。
    /// 这个集合非空，因此扫描正则一旦失配也会红。
    /// </summary>
    private static readonly string[] ExpectedBypassExemptions =
    [
        "Commands/StockReservations/ReserveStockCommand.cs:235",
    ];

    [Fact]
    public void Idempotency_key_column_width_matches_the_policy_constant()
    {
        using var fixture = CreateModelFixture();
        var model = fixture.DbContext.GetService<IDesignTimeModel>().Model;

        // 从 EF 模型**闭集枚举**，不是手写白名单：新增一张带 idempotency_key 的表会自动进值域。
        var columns = IdempotencyKeyColumns(model);
        Assert.Equal(IdempotencyKeyColumnCount, columns.Length);

        var exempted = columns
            .Where(property => property.DeclaringType.Name == DeadLetterEntityName)
            .ToArray();
        Assert.Single(exempted);
        Assert.Equal(500, Assert.Single(exempted).GetMaxLength());

        var governed = columns.Except(exempted).ToArray();
        Assert.Equal(IdempotencyKeyColumnCount - 1, governed.Length);
        Assert.All(
            governed,
            property => Assert.Equal(
                InventoryIdempotencyKeyPolicy.ColumnMaxLength,
                property.GetMaxLength()));
    }

    [Fact]
    public async Task Every_suffixing_write_face_rejects_a_base_key_that_would_overflow_the_column()
    {
        // 状态调拨：拒绝点在校验器上（:out / :in 是这个写面唯一的行为）。
        AssertBoundary(
            PostStockStatusTransferCommandHandler.BaseIdempotencyKeyMaxLength,
            length => new PostStockStatusTransferCommandValidator()
                .Validate(StatusTransferCommand(new string('k', length)))
                .IsValid);

        // FEFO 预留分配腿：拒绝点也在校验器上。
        AssertBoundary(
            ReserveFefoStockCommandHandler.BaseIdempotencyKeyMaxLength,
            length => new ReserveFefoStockCommandValidator()
                .Validate(FefoCommand(new string('k', length)))
                .IsValid);

        // 库存移动的拒绝点**不在**校验器上：非调拨移动不追加后缀，合法上界仍是整列宽 128，
        // 因此这个写面的收紧只对 movementType=transfer 生效，落在 handler 的 ValidateTransferLegsOrReject 里。
        Assert.True(new PostStockMovementCommandValidator()
            .Validate(InboundMovementCommand(new string('k', InventoryIdempotencyKeyPolicy.ColumnMaxLength), 1m))
            .IsValid);
        Assert.False(new PostStockMovementCommandValidator()
            .Validate(InboundMovementCommand(new string('k', InventoryIdempotencyKeyPolicy.ColumnMaxLength + 1), 1m))
            .IsValid);
        Assert.True(await TransferMovementHandlerAcceptsAsync(
            PostStockMovementCommandHandler.TransferBaseIdempotencyKeyMaxLength));
        Assert.False(await TransferMovementHandlerAcceptsAsync(
            PostStockMovementCommandHandler.TransferBaseIdempotencyKeyMaxLength + 1));
    }

    private static async Task<bool> TransferMovementHandlerAcceptsAsync(int idempotencyKeyLength)
    {
        await using var dbContext = CreateContext($"movement-boundary-{idempotencyKeyLength}");
        var handler = new PostStockMovementCommandHandler(dbContext);
        await handler.Handle(InboundMovementCommand("seed-inbound", 10m), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        try
        {
            await handler.Handle(
                TransferMovementCommand(new string('k', idempotencyKeyLength)),
                CancellationToken.None);
            return true;
        }
        catch (InventoryPostingRejectedException exception)
            when (exception.Message.Contains("幂等键过长", StringComparison.Ordinal))
        {
            return false;
        }
    }

    [Fact]
    public void Every_suffixing_write_face_bound_leaves_room_for_its_own_longest_suffix()
    {
        // 后缀字面量取自 handler 自己，不是测试里另抄一份。
        Assert.Equal(
            InventoryIdempotencyKeyPolicy.ColumnMaxLength,
            PostStockStatusTransferCommandHandler.BaseIdempotencyKeyMaxLength
                + Math.Max(
                    PostStockStatusTransferCommandHandler.OutboundLegSuffix.Length,
                    PostStockStatusTransferCommandHandler.InboundLegSuffix.Length));

        Assert.Equal(
            InventoryIdempotencyKeyPolicy.ColumnMaxLength,
            PostStockMovementCommandHandler.TransferBaseIdempotencyKeyMaxLength
                + Math.Max(
                    PostStockMovementCommandHandler.TransferOutLegSuffix.Length,
                    PostStockMovementCommandHandler.TransferInLegSuffix.Length));

        // FEFO 后缀是变长的：上界由候选台账数上限决定，逐个求值取最大，不靠「序号越大后缀越长」的推断。
        var longestPartSuffixLength = Enumerable
            .Range(2, ReserveFefoStockCommandHandler.MaxFefoCandidateLedgers - 1)
            .Max(index => ReserveFefoStockCommandHandler.PartSuffix(index).Length);
        Assert.Equal(
            InventoryIdempotencyKeyPolicy.ColumnMaxLength,
            ReserveFefoStockCommandHandler.BaseIdempotencyKeyMaxLength + longestPartSuffixLength);
    }

    [Fact]
    public async Task Handler_composed_keys_of_a_max_length_base_key_still_fit_the_column()
    {
        using var fixture = CreateModelFixture();
        var model = fixture.DbContext.GetService<IDesignTimeModel>().Model;
        var movementColumnWidth = ColumnWidthOf(model, typeof(StockMovement), nameof(StockMovement.IdempotencyKey));
        var reservationColumnWidth = ColumnWidthOf(model, typeof(StockReservation), nameof(StockReservation.IdempotencyKey));

        // 1) 状态调拨：基础键恰好顶到校验器上界，handler 拼出的两腿键都必须还塞得进列。
        await using (var dbContext = CreateContext("status-transfer"))
        {
            var statusKey = new string('k', PostStockStatusTransferCommandHandler.BaseIdempotencyKeyMaxLength);
            await SeedLedgerAsync(dbContext, "unrestricted", 10m);
            await new PostStockStatusTransferCommandHandler(dbContext)
                .Handle(StatusTransferCommand(statusKey), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var keys = dbContext.StockMovements
                .Where(x => x.IdempotencyKey.StartsWith(statusKey))
                .Select(x => x.IdempotencyKey)
                .ToList();
            Assert.Equal(2, keys.Count);
            Assert.All(keys, key => Assert.True(
                key.Length <= movementColumnWidth,
                $"状态调拨腿键 {key.Length} 位超出 stock_movements.idempotency_key 列宽 {movementColumnWidth}。"));
            // 顶格键必须真的顶到列宽：否则说明上界被放松成了「远小于列宽」，这条读数就不再有鉴别力。
            Assert.Equal(movementColumnWidth, keys.Max(key => key.Length));
        }

        // 2) 库存移动调拨腿：同样的对撞。
        await using (var dbContext = CreateContext("movement-transfer"))
        {
            var transferKey = new string('k', PostStockMovementCommandHandler.TransferBaseIdempotencyKeyMaxLength);
            var handler = new PostStockMovementCommandHandler(dbContext);
            await handler.Handle(InboundMovementCommand("seed-inbound", 10m), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await handler.Handle(TransferMovementCommand(transferKey), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var keys = dbContext.StockMovements
                .Where(x => x.MovementType == "transfer")
                .Select(x => x.IdempotencyKey)
                .ToList();
            Assert.Equal(2, keys.Count);
            Assert.All(keys, key => Assert.True(
                key.Length <= movementColumnWidth,
                $"调拨腿键 {key.Length} 位超出 stock_movements.idempotency_key 列宽 {movementColumnWidth}。"));
            Assert.Equal(movementColumnWidth, keys.Max(key => key.Length));
        }

        // 3) FEFO 预留：两条台账 → 第二腿带 :part-2 后缀。
        await using (var dbContext = CreateContext("fefo-reservation"))
        {
            var fefoKey = new string('k', ReserveFefoStockCommandHandler.BaseIdempotencyKeyMaxLength);
            // 真实 UtcNow 参与 FEFO 的过期过滤，锚点必须取「相对今天的未来」而不是写死年份。
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            await SeedLedgerAsync(dbContext, "unrestricted", 4m, "LOC-A-01", today.AddYears(1));
            await SeedLedgerAsync(dbContext, "unrestricted", 6m, "LOC-A-02", today.AddYears(2));
            await new ReserveFefoStockCommandHandler(dbContext)
                .Handle(FefoCommand(fefoKey) with { Quantity = 10m }, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var keys = dbContext.StockReservations.Select(x => x.IdempotencyKey).ToList();
            Assert.Equal(2, keys.Count);
            Assert.Contains(keys, key => key == fefoKey + ReserveFefoStockCommandHandler.PartSuffix(2));
            Assert.All(keys, key => Assert.True(
                key.Length <= reservationColumnWidth,
                $"FEFO 分配腿键 {key.Length} 位超出 stock_reservations.idempotency_key 列宽 {reservationColumnWidth}。"));
        }
    }

    [Fact]
    public void Compose_refuses_to_overflow_the_column_instead_of_letting_the_database_throw()
    {
        // 后缀取自 handler 自己，不在测试里另抄一份（#3176 S4）。
        var suffix = PostStockStatusTransferCommandHandler.OutboundLegSuffix;
        var atColumnWidth = new string('k', InventoryIdempotencyKeyPolicy.ColumnMaxLength);

        // 落库前拼接必须就地拒绝（KnownException → 400），而不是把越界值送进库换一个
        // DbUpdateException(22001)。注意：这只改变**失败形态**，两种异常在 CAP 消费者内都会逃逸（#877 同族）。
        var exception = Assert.Throws<KnownException>(
            () => InventoryIdempotencyKeyPolicy.Compose(atColumnWidth, suffix));
        Assert.Contains("超出长度上限", exception.Message, StringComparison.Ordinal);

        // 恰好塞满不拒绝，也绝不截断。
        var exact = InventoryIdempotencyKeyPolicy.Compose(
            new string('k', InventoryIdempotencyKeyPolicy.ColumnMaxLength - suffix.Length),
            suffix);
        Assert.Equal(InventoryIdempotencyKeyPolicy.ColumnMaxLength, exact.Length);
        Assert.EndsWith(suffix, exact, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_pre_persist_rewrite_of_an_idempotency_key_goes_through_Compose()
    {
        // #3176 B1-b：光有 Compose 不算防线——「有人把 Compose(k, s) 换成裸 k + s」在纯行为测试上
        // 是完全绿的（复审实测 M5b：5 通过 / 0 失败）。这条是**源码闭集扫描**，它才让绕过变红。
        var applicationRoot = Path.Combine(FindRepoRoot(), InventoryWebApplicationRelativeRoot);
        Assert.True(Directory.Exists(applicationRoot), applicationRoot);

        var sources = Directory
            .EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => (
                Relative: Path.GetRelativePath(applicationRoot, path).Replace(Path.DirectorySeparatorChar, '/'),
                Text: File.ReadAllText(path)))
            .ToArray();
        // 下界：扫描面塌成空集时（改目录名 / 改后缀名）这条先红，而不是让后面的比较对空集恒真。
        Assert.True(sources.Length >= 20, $"扫描面只找到 {sources.Length} 个源文件，值域可疑。");

        // ① Compose 调用点闭集：逐文件精确计数。摘掉任一处会把该文件的计数打到 0。
        var actualComposeSites = sources
            .Select(source => (RelativePath: source.Relative, ComposeCallCount: ComposeCallRegex().Matches(source.Text).Count))
            .Where(site => site.ComposeCallCount > 0)
            .OrderBy(site => site.RelativePath, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            ExpectedComposeSites.OrderBy(site => site.RelativePath, StringComparer.Ordinal).ToArray(),
            actualComposeSites);

        // ② 绕过面闭集：任何「在幂等键上直接做字符串加法 / 把幂等键嵌进插值再续写」的位点，
        //    都必须是登记过的那一条查询谓词。登记集非空，因此正则失效也会红，不会静默放行。
        var actualBypassSites = sources
            .Where(source => !string.Equals(source.Relative, PolicySourceRelativePath, StringComparison.Ordinal))
            .SelectMany(source => source.Text
                .Split('\n')
                .Select((line, index) => (Site: $"{source.Relative}:{index + 1}", Line: line))
                .Where(entry => IdempotencyKeyRewriteRegex().IsMatch(entry.Line))
                .Select(entry => entry.Site))
            .OrderBy(site => site, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ExpectedBypassExemptions, actualBypassSites);
    }

    [Fact]
    public void Fefo_longest_part_suffix_is_the_one_at_the_leg_index_cap()
    {
        // #3176 S3 上半：118 完全建立在「腿序号 ≤ MaxFefoCandidateLedgers」之上，
        // 逐个求值取最大，不靠「序号越大后缀越长」的推断。
        var suffixLengths = Enumerable
            .Range(2, ReserveFefoStockCommandHandler.MaxFefoCandidateLedgers - 1)
            .Select(index => ReserveFefoStockCommandHandler.PartSuffix(index).Length)
            .ToArray();
        Assert.Equal(
            ReserveFefoStockCommandHandler.PartSuffix(ReserveFefoStockCommandHandler.MaxFefoCandidateLedgers).Length,
            suffixLengths.Max());
    }

    [Fact]
    public async Task Fefo_rejects_more_candidate_ledgers_than_the_leg_index_cap()
    {
        // #3176 S3 下半：上一条只证明「若序号 ≤ 上限则后缀不超长」；这条证明**上限真的被执行**——
        // 该守卫此前在全仓零覆盖（删掉 Take 与超限 throw 后契约测试全绿）。
        await using var dbContext = CreateContext("fefo-cap");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        for (var index = 0; index <= ReserveFefoStockCommandHandler.MaxFefoCandidateLedgers; index++)
        {
            dbContext.StockLedgers.Add(SeedLedger("unrestricted", 1m, $"LOC-CAP-{index:D5}", today.AddYears(1)));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<KnownException>(() => new ReserveFefoStockCommandHandler(dbContext)
            .Handle(FefoCommand(new string('k', 10)) with { Quantity = 1m }, CancellationToken.None));
        Assert.Contains(
            ReserveFefoStockCommandHandler.MaxFefoCandidateLedgers.ToString(CultureInfo.InvariantCulture),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Fefo_top_out_leg_key_fills_the_reservation_column_exactly()
    {
        // #3176 S8/S9：118 是由 :part-1000 决定的，而 handler 驱动的夹具只跑到 :part-2（125 位）。
        // 顶格那一格必须有直接读数，且要有**反松弛等式**——只写 <= 抓不住「上界被手抄成更小的数」。
        using var fixture = CreateModelFixture();
        var reservationColumnWidth = ColumnWidthOf(
            fixture.DbContext.GetService<IDesignTimeModel>().Model,
            typeof(StockReservation),
            nameof(StockReservation.IdempotencyKey));
        var longestPartSuffix = ReserveFefoStockCommandHandler.PartSuffix(
            ReserveFefoStockCommandHandler.MaxFefoCandidateLedgers);

        var topOutKey = InventoryIdempotencyKeyPolicy.Compose(
            new string('k', ReserveFefoStockCommandHandler.BaseIdempotencyKeyMaxLength),
            longestPartSuffix);
        Assert.Equal(reservationColumnWidth, topOutKey.Length);

        // 再宽一位就必须被拒——证明 118 是顶格值，不是随手留的余量。
        Assert.Throws<KnownException>(() => InventoryIdempotencyKeyPolicy.Compose(
            new string('k', ReserveFefoStockCommandHandler.BaseIdempotencyKeyMaxLength + 1),
            longestPartSuffix));
    }

    [Fact]
    public void Stock_count_task_code_prefix_still_clears_the_idempotency_key_column()
    {
        // #3176 S6：盘点任务的键是 "count-code:" + CountTaskCode，当前 11 + 100 = 111 ≤ 128，
        // 是**无守卫的余量**。CountTaskCode 一旦放宽到 118 就静默变缺陷，这条把余量钉住。
        using var fixture = CreateModelFixture();
        var columnWidth = ColumnWidthOf(
            fixture.DbContext.GetService<IDesignTimeModel>().Model,
            typeof(StockCountTask),
            nameof(StockCountTask.IdempotencyKey));

        var longestKey = InventoryIdempotencyKeyPolicy.Compose(
            CreateStockCountTaskIdempotency.CountCodePrefix,
            new string('k', CreateStockCountTaskIdempotency.CountTaskCodeMaxLength));
        Assert.True(
            longestKey.Length <= columnWidth,
            $"count-code 前缀键最长 {longestKey.Length} 位，超出列宽 {columnWidth}。");
    }

    [GeneratedRegex(@"InventoryIdempotencyKeyPolicy\.Compose\(", RegexOptions.CultureInvariant)]
    private static partial Regex ComposeCallRegex();

    /// <summary>匹配「拿幂等键做字符串加法」与「把幂等键嵌进插值后还有后续内容」两种改写形状。</summary>
    [GeneratedRegex(@"[Ii]dempotencyKey\s*\+\s*|\$""\{[^}]*[Ii]dempotencyKey[^}]*\}[^""]", RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyKeyRewriteRegex();

    private static IProperty[] IdempotencyKeyColumns(IModel model)
    {
        return model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => string.Equals(property.GetColumnName(), "idempotency_key", StringComparison.Ordinal))
            .ToArray();
    }

    private static int ColumnWidthOf(IModel model, Type entityType, string propertyName)
    {
        return model.FindEntityType(entityType)!.FindProperty(propertyName)!.GetMaxLength()!.Value;
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, InventoryWebApplicationRelativeRoot)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Cannot locate the repository root from the test output directory.");
    }

    private static void AssertBoundary(int baseMaxLength, Func<int, bool> validate)
    {
        Assert.True(validate(baseMaxLength), $"长度 {baseMaxLength} 的基础幂等键应当通过校验。");
        Assert.False(validate(baseMaxLength + 1), $"长度 {baseMaxLength + 1} 的基础幂等键应当被校验器拒绝。");
    }

    private static PostStockStatusTransferCommand StatusTransferCommand(string idempotencyKey)
    {
        return new PostStockStatusTransferCommand(
            "org-001",
            "env-dev",
            "unrestricted",
            "blocked",
            "quality",
            "DOC-IDEM-001",
            "LINE-001",
            idempotencyKey,
            "SKU-IDEM",
            "EA",
            "SITE-001",
            "LOC-A-01",
            "LOT-001",
            null,
            "owned",
            null,
            1m);
    }

    private static PostStockMovementCommand InboundMovementCommand(string idempotencyKey, decimal quantity)
    {
        return new PostStockMovementCommand(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "DOC-IDEM-001",
            "LINE-001",
            idempotencyKey,
            "SKU-IDEM",
            "EA",
            "SITE-001",
            "LOC-A-01",
            "LOT-001",
            null,
            "unrestricted",
            "owned",
            null,
            quantity,
            UnitCost: 5m);
    }

    private static PostStockMovementCommand TransferMovementCommand(string idempotencyKey)
    {
        return InboundMovementCommand(idempotencyKey, -1m) with
        {
            MovementType = "transfer",
            TransferInLocationCode = "LOC-B-01",
            TransferInQuantity = 1m,
        };
    }

    private static ReserveFefoStockCommand FefoCommand(string idempotencyKey)
    {
        return new ReserveFefoStockCommand(
            "org-001",
            "env-dev",
            "wms",
            "DOC-IDEM-001",
            "LINE-001",
            idempotencyKey,
            "SKU-IDEM",
            "EA",
            "SITE-001",
            "unrestricted",
            "owned",
            null,
            1m,
            LocationCode: null);
    }

    private static async Task SeedLedgerAsync(
        ApplicationDbContext dbContext,
        string qualityStatus,
        decimal quantity,
        string locationCode = "LOC-A-01",
        DateOnly? expiryDate = null)
    {
        dbContext.StockLedgers.Add(SeedLedger(qualityStatus, quantity, locationCode, expiryDate));
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static StockLedger SeedLedger(
        string qualityStatus,
        decimal quantity,
        string locationCode,
        DateOnly? expiryDate)
    {
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-IDEM",
            "EA",
            "SITE-001",
            locationCode,
            "LOT-001",
            null,
            qualityStatus,
            "owned",
            null,
            null,
            expiryDate);
        var movement = StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "DOC-IDEM-SEED",
            "LINE-001",
            $"seed-{locationCode}",
            "SKU-IDEM",
            "EA",
            "SITE-001",
            locationCode,
            "LOT-001",
            null,
            qualityStatus,
            "owned",
            null,
            quantity,
            5m,
            null,
            expiryDate);
        ledger.ApplyMovement(movement);
        return ledger;
    }

    private static ApplicationDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"inventory-idempotency-key-{name}-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static ModelFixture CreateModelFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddInventoryPostgreSqlPersistence("Host=localhost;Database=nerv_iip_idempotency_key_contract;Username=nerv;Password=nerv");
        return new ModelFixture(services.BuildServiceProvider());
    }

    private sealed class ModelFixture : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly IServiceScope scope;

        public ModelFixture(ServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
            scope = serviceProvider.CreateScope();
            DbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        }

        public ApplicationDbContext DbContext { get; }

        public void Dispose()
        {
            DbContext.Dispose();
            scope.Dispose();
            serviceProvider.Dispose();
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException("This test mediator only supports publish.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");
    }
}
