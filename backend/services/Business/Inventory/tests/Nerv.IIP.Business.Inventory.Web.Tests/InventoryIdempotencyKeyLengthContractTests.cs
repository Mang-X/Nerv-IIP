using System.Globalization;
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
/// - <see cref="Idempotency_key_column_width_matches_the_policy_constant"/>：列宽从 **EF 模型闭集枚举**
///   （<c>IDesignTimeModel</c>）读，不读代码里的常量 → 单边改 <c>HasMaxLength</c> 或漏登记一列即红；
/// - <see cref="Every_suffixing_write_face_rejects_a_base_key_that_would_overflow_the_column"/>：
///   逐个写面跑**真实校验器 / 真实 handler**，上界那一位放行、+1 位拒绝；
/// - <see cref="Handler_composed_keys_of_a_max_length_base_key_still_fit_the_column"/>：拿真实 handler
///   跑「恰好顶到上界」的基础键，把它**实际落库的键长**与**从 EF 模型读到的列宽**直接对撞
///   → 这条不经过任何策略常量，是三条腿里唯一的真对撞。
///
/// **值域边界（声明放弃了什么，别读成完备）**：
/// 1. 列宽读的是 **EF 模型**而不是迁移脚本。只改迁移不改模型（或反之）本类抓不到，
///    那是「模型/迁移漂移」另一类护栏的职责；当前两侧一致（真库 <c>information_schema</c> 读数与
///    模型读数对得上，见 PR 正文）。
/// 2. **本类不证明「所有拼接都走 <c>InventoryIdempotencyKeyPolicy.Compose</c>」**。
///    曾经有一套源码闭集扫描试图证明它，三轮下来每轮都能找出新的绕法
///    （跨行 <c>+</c>、左加法、<c>$@"</c>、<c>$"""</c>、同语句 <c>Compose(</c>、行尾注释补计数、
///    字面量里的括号），本质是在用文本近似手搓一个 C# 词法分析器，**不收敛**，已按裁定移除。
///    结构性封闭见 #3231：把键换成不可拼接的包装类型，届时靠**类型不可表达**而不是靠扫描证明。
///
///    **这条移除有代价，写在这里免得它悄悄消失**：被删掉的是**真跑过的鉴别力**，不是死代码——
///    「摘掉 <c>Compose</c> 换裸拼接」「新增未登记的绕过位点」「同语句里 <c>Compose</c> 与裸拼接并存」
///    「用一句行尾注释补回调用计数」等格此前都实测能打红，此后**都不再红**。
///    也就是说：**在 #3231 落地前，「有人绕开 <c>Compose</c> 直接拼接幂等键」处于零防线状态**，
///    唯一的约束是 <c>Compose</c> 的 doc 里那句约定。**别把本类的绿读成「拼接方式已被看住」**——
///    本类看住的是「上界算得对不对、顶格键塞不塞得进列」，看不住「有没有人走别的路拼这把键」。
/// </summary>
public sealed class InventoryIdempotencyKeyLengthContractTests
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

    /// <summary>
    /// 「在幂等键上直接做字符串加法 / 把它嵌进插值再续写」的**具名豁免**闭集。
    /// 当前唯一一条是 FEFO 重放查询的 <c>StartsWith</c> 谓词：它构造的是查询前缀、不落库。
    /// 这个集合非空，因此扫描正则一旦失配也会红。

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

    /// <summary>
    /// **注意：这条与 <see cref="Every_suffixing_write_face_bound_leaves_room_for_its_own_longest_suffix"/>
    /// 的 FEFO 段代数等价**，实测在单变量变异下从不单独变红（真缺陷形状下它绿而后者红）。
    /// 保留是因为它把「最长后缀确实在序号上限处」这个前提写成了可读的断言，
    /// **但不要把它当成一条独立防线计入鉴别力**。
    /// </summary>
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
        // #3176 S6：盘点任务未给幂等键时，键由 "count-code:" + CountTaskCode 现拼，是**无守卫的余量**。
        // 注意两点，都是被实测纠正过的：
        // 1. **不借道 Compose**——Compose 在 >128 时先抛 KnownException，而列宽恰好也是 128，
        //    于是原来那条断言的失败消息根本走不到，是死代码，红的理由也不对。
        // 2. **不只比两个常量的算术**——那样钉的是常量之间的关系，跟真实构造路径脱钩
        //    （实测：把 Resolve 的前缀换成一段 51 字内联字面量、真实键长 151 必炸 22001，旧断言照样绿）。
        //    这里直接对**真实入口 Resolve(...)** 用顶格 CountTaskCode 求值再量长度。
        using var fixture = CreateModelFixture();
        var columnWidth = ColumnWidthOf(
            fixture.DbContext.GetService<IDesignTimeModel>().Model,
            typeof(StockCountTask),
            nameof(StockCountTask.IdempotencyKey));

        var longestCode = new string('k', CreateStockCountTaskIdempotency.CountTaskCodeMaxLength);
        var resolved = CreateStockCountTaskIdempotency.Resolve(StockCountTaskCommand(longestCode));
        Assert.StartsWith(CreateStockCountTaskIdempotency.CountCodePrefix, resolved, StringComparison.Ordinal);
        Assert.True(
            resolved.Length <= columnWidth,
            $"顶格盘点单号派生出的幂等键 {resolved.Length} 位，超出列宽 {columnWidth}。");

        // 常量侧的算术也钉一道：校验器上界与前缀长度任一放宽，这条先红，不必等真实构造路径。
        Assert.True(
            CreateStockCountTaskIdempotency.CountCodePrefix.Length
                + CreateStockCountTaskIdempotency.CountTaskCodeMaxLength <= columnWidth,
            "count-code 前缀与盘点单号上界之和超出幂等键列宽。");
    }

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

    private static void AssertBoundary(int baseMaxLength, Func<int, bool> validate)
    {
        Assert.True(validate(baseMaxLength), $"长度 {baseMaxLength} 的基础幂等键应当通过校验。");
        Assert.False(validate(baseMaxLength + 1), $"长度 {baseMaxLength + 1} 的基础幂等键应当被校验器拒绝。");
    }

    private static CreateStockCountTaskCommand StockCountTaskCommand(string countTaskCode)
    {
        return new CreateStockCountTaskCommand(
            "org-001",
            "env-dev",
            countTaskCode,
            "SKU-IDEM",
            "EA",
            "SITE-001",
            "LOC-A-01",
            "LOT-001",
            null,
            "unrestricted",
            "owned",
            null);
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
