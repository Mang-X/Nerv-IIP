using System.Globalization;
using System.Text;
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
    /// **固有代价**：新增一处**合法**的 <c>Compose</c> 调用也会红（计数变了）。这是登记式护栏的
    /// 一体两面，不是缺陷——新增写面时把计数一并更新即可，别当 bug 去「修」。
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
    /// <summary>
    /// 「改写了幂等键但**不落库**」的具名豁免闭集：键是「文件 + **归一化后的语句原文**」。
    /// **为什么带原文**：只记 (文件, 命中数) 更弱——在**已登记文件内部**用一条落库向裸拼接
    /// **顶替**掉合法命中，计数不变、护栏全绿（实测过）。带上原文才是逐条身份。
    /// **原文不会造成假红**：语句已经过剥注释 → 折平 → 空白归一，折行与多余空格都被吸收
    /// （这两个假红格实测在归一化后转绿），所以「怕重排版」不再是放弃原文的理由。
    /// 逐条理由：
    /// - <c>ReserveStockCommand.cs</c>：FEFO 重放查询的 <c>StartsWith</c> 前缀谓词，构造的是查询前缀。
    /// - <c>Seed/*</c>：<c>$"{SourceDocumentId}|{IdempotencyKey}"</c> 形态的**种子比对键**，
    ///   只做内存内去重/差分，**不写进任何 idempotency_key 列**。
    /// </summary>
    private static readonly string[] ExpectedBypassExemptions =
    [
        "Commands/StockReservations/ReserveStockCommand.cs | var existing = await dbContext.StockReservations.Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.SourceService == request.SourceService && x.SourceDocumentId == request.SourceDocumentId &&(x.IdempotencyKey == request.IdempotencyKey || x.IdempotencyKey.StartsWith(request.IdempotencyKey + ReserveFefoStockCommandHandler.PartSuffixPrefix))).OrderBy(x => x.ExpiryDate ?? DateOnly.MaxValue).ThenBy(x => x.LotNo).ToListAsync(cancellationToken)",
        "Seed/WorldHistoryConsistencyValidator.cs | public string Key => $\"{SourceDocumentId}|{IdempotencyKey}\"",
        "Seed/WorldHistoryConsistencyValidator.cs | public string Key => $\"{SourceDocumentId}|{IdempotencyKey}\"",
        "Seed/WorldHistoryInventorySpec.cs | public string MovementKey => string.Create(CultureInfo.InvariantCulture,$\"{SourceDocumentId}|{IdempotencyKey}\")",
        "Seed/WorldHistoryReservationSeedService.cs | ).ToArrayAsync(cancellationToken)).Select(x => $\"{x.SourceDocumentId}|{x.IdempotencyKey}\").ToHashSet(StringComparer.Ordinal)",
        "Seed/WorldHistoryReservationSpec.cs | public string ReservationKey => string.Create(CultureInfo.InvariantCulture,$\"{SourceDocumentId}|{IdempotencyKey}\")",
        "Seed/WorldHistorySeedService.cs | ).ToArrayAsync(cancellationToken)).Select(x => $\"{x.SourceDocumentId}|{x.IdempotencyKey}\").ToHashSet(StringComparer.Ordinal)",
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

    /// <summary>
    /// 落库前改写幂等键的位点必须全部经过 <c>InventoryIdempotencyKeyPolicy.Compose</c>（#3176 B1-b）。
    /// </summary>
    /// <remarks>
    /// **为什么需要这条**：光有 <c>Compose</c> 不算防线——「有人把 <c>Compose(k, s)</c> 换成裸 <c>k + s</c>」
    /// 在纯行为测试上是完全绿的（实测 M5b：5 通过 / 0 失败）。只有源码闭集扫描才让「绕过」变红。
    ///
    /// **这条护栏的值域边界（声明什么被放弃了，别把它读成完备）**：
    /// 1. **扫描面只有** <c>backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/</c>
    ///    这一棵树。同服务的 <c>Domain/</c>、<c>Infrastructure/</c>、<c>Endpoints/</c>，以及**其它服务**，
    ///    都在值域之外——别的服务往 Inventory 的列里写超长键，这条抓不到（那是另一票的事）。
    /// 2. **认六个方向**（每个都有合成样本正向自检 + 负向对照，删掉任一分支即红）：
    ///    ① `key + x`（键在左操作数）② `x + key`（键在右操作数，**前缀同样撑爆列宽**）
    ///    ③ `$"{key}:suffix"` ④ `$"prefix-{key}"` ⑤ `alias + ":literal"` ⑥ `alias += ":literal"`。
    ///    切分按**语句**（剥注释 → 折平换行 → 空白归一 → 按 `;` 断句），**不按行**：
    ///    本仓格式化会把长表达式在 `+` 前折行，逐行匹配对跨行加法在构造上看不见。
    ///
    ///    **以下一律不在视野内——不是「确认过没有」，是扫描面在构造上就看不见**：
    ///    - `string.Concat` / `string.Format` / `StringBuilder.Append` / `Span` 拼接；
    ///    - **局部别名 + 非字面量后缀**：`var k = request.IdempotencyKey; k + OutboundLegSuffix;`
    ///      —— 方向⑤⑥只堵住别名后跟**冒号开头字面量**的那一半；别名后跟**常量**仍然存活。
    ///    - **别名后再插值**：`var k = request.IdempotencyKey; $"{k}:out";` —— 语句里已经没有
    ///      `IdempotencyKey` 这个词，纯文本扫描在构造上追不到。
    ///    要堵死后两类得上数据流分析，本类不做，故如实登记为**已知缺口**。
    ///
    ///    **曾经是缺口、现已闭合的（记下来免得有人当成还开着）**：
    ///    - `$@"…"` / `@$"…"` 逐字插值——正则原先只认 `$"`，现已放宽；
    ///    - 裸拼接与 `Compose(...)` 写在**同一条语句**里——原先整条语句被无条件豁免，
    ///      也就是「越规范地用了 `Compose`，同一语句里的裸拼接越被放行」，
    ///      现改成只剔除 `Compose(...)` 子表达式再判；
    ///    - 原始插值串 `$"""…"""` 判不了，改用 **fail-closed 负空间断言**禁止它出现在扫描面。
    ///
    ///    **仍开着的一条（登记，未修）**：登记键是「文件 + 归一化原文」，**同一文件里两条归一化后
    ///    逐字相同的语句无法互相区分**（现存实例：`WorldHistoryConsistencyValidator.cs` 里两条
    ///    `public string Key => $"{SourceDocumentId}|{IdempotencyKey}"`）。将来其中一条变成落库向，
    ///    登记集零变化、护栏不红。多重度本身是被强制的（同文件再加一条同文原文会红），
    ///    但「哪一条是哪一条」在构造上分不出。**此条为读代码推断，未跑变异**——
    ///    构造不出只动一个变量、且能把两条区分开的判别格。
    /// 3. **看不到「从零构造一把键」的路径**：本类只检查「拿一把已有的幂等键去改写」，
    ///    像 <c>CreateStockCountTaskIdempotency</c> 那样从别的字段现拼出一把新键的位点，
    ///    这条正则命不中（那类余量由 <see cref="Stock_count_task_code_prefix_still_clears_the_idempotency_key_column"/>
    ///    单独按算术钉住，且**只钉了 count-code 这一处**，不是全仓）。
    /// 4. **列宽读的是 EF 模型（<c>IDesignTimeModel</c>）而不是迁移脚本**：若有人只改迁移、不改模型
    ///    （或反之），本类抓不到那种不一致——那是「模型/迁移漂移」另一类护栏的职责。当前两侧一致
    ///    （真库 <c>information_schema</c> 读数与模型读数对得上，见 PR 正文）。
    /// 5. 两个登记集（<see cref="ExpectedComposeSites"/>、<see cref="ExpectedBypassExemptions"/>）都**非空**，
    ///    再加登记文件的存在性断言与源文件数下界，所以「扫描面塌成空集」会红。
    ///    **但「正则失配就会红」这句只在全失配下成立**：曾经把检测写成单条多分支正则时，
    ///    删掉其中**一个分支**（半失配）不会红——树上恰好没有该形状的位点，闭集比对照旧成立，
    ///    等于无声缴械半边护栏。现在改成**每个方向各有一条合成样本正向自检 + 负向对照**
    ///    （见本方法末尾），删任一分支都会被自检打红，不再依赖「树上恰好有那种位点」。
    ///
    /// **改 <see cref="FindRepoRoot"/> 的定位方式前先读这段**：本仓有「<c>/tmp</c> 里的 archive 副本
    /// 破坏 repoRoot 类工具、副本里的红绿都不可信」的前科（符号链接导致扫描排除项全失配）。
    /// 这里刻意从 <c>AppContext.BaseDirectory</c> 逐级上溯、以
    /// <see cref="InventoryWebApplicationRelativeRoot"/> **这棵被构建的树是否存在**为锚，
    /// **不认 <c>.git</c>、不认环境变量、不认当前工作目录**——因为 git worktree 下 <c>.git</c> 是**文件**
    /// 而不是目录（本仓并行审核默认每席位一棵 <c>git worktree add --detach</c> 的独立树），
    /// 认 <c>.git</c> 目录的写法在那里会直接找错根。
    /// **实证**：复审在自己 <c>/tmp</c> 的隔离 worktree 上只摘掉一处 <c>Compose</c>，本用例照样红——
    /// 说明扫描到的是**被构建的那棵树**，不是主检出目录。**换定位方式会破坏这条前提。**
    /// </remarks>
    [Fact]
    public void Every_pre_persist_rewrite_of_an_idempotency_key_goes_through_Compose()
    {
        // 注意：这里不再断言 applicationRoot 存在——FindRepoRoot 就是以这条相对根为锚且 fail-closed，
        // 根不存在时它先抛，那条断言在构造上不可达（死断言）。
        var applicationRoot = Path.Combine(FindRepoRoot(), InventoryWebApplicationRelativeRoot);

        var sources = Directory
            .EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => (
                Relative: Path.GetRelativePath(applicationRoot, path).Replace(Path.DirectorySeparatorChar, '/'),
                Text: File.ReadAllText(path)))
            .ToArray();
        // 下界：不用「文件数 >= N」这种拍脑袋阈值（实测扫描面 55 个文件，写 20 意味着删掉 35 个仍能过），
        // 改成**登记的每个 Compose 位点文件都必须真的在扫描面里被读到**——扫描面塌掉、改目录名、
        // 改后缀名、少读一个登记文件，都会在这一条先红，而不是让后面的全等比较对空集恒真。
        var scannedPaths = sources.Select(source => source.Relative).ToHashSet(StringComparer.Ordinal);
        Assert.All(
            ExpectedComposeSites,
            site => Assert.Contains(site.RelativePath, scannedPaths));
        // 再加一道贴近实测的数量下界：实测扫描面 55 个源文件，写 20 意味着「目录被拆走一半」也照过。
        Assert.True(sources.Length >= 50, $"扫描面只找到 {sources.Length} 个源文件，值域可疑。");

        // ① Compose 调用点闭集：逐文件精确计数。摘掉任一处会把该文件的计数打到 0。
        // 只在**代码行**上计数：注释 / XML doc 里出现 "…Compose(" 既能被用来「补回计数」制造假绿，
        // 也会在写文档时制造假红。这里先剥掉纯注释行再数。
        var actualComposeSites = sources
            .Select(source => (RelativePath: source.Relative, ComposeCallCount: ComposeCallRegex().Matches(CodeLinesOf(source.Text)).Count))
            .Where(site => site.ComposeCallCount > 0)
            .OrderBy(site => site.RelativePath, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            ExpectedComposeSites.OrderBy(site => site.RelativePath, StringComparer.Ordinal).ToArray(),
            actualComposeSites);

        // ② 绕过面闭集。**按语句切分而不是按行**：本仓格式化会把长表达式在 `+` 前折行，
        //    逐行匹配对「跨行加法」在构造上就看不见（实测该形状原本存活）。
        var actualBypassSites = sources
            .Where(source => !string.Equals(source.Relative, PolicySourceRelativePath, StringComparison.Ordinal))
            .SelectMany(source => StatementsOf(source.Text)
                .Where(IsIdempotencyKeyRewrite)
                .Select(statement => $"{source.Relative} | {statement}"))
            .OrderBy(site => site, StringComparer.Ordinal)
            .ToArray();
        // 失败消息给**差集**：xUnit 默认的 Collections differ 会把两串截成 `···`，
        // 每次红都得回源码里翻，而这是一条后来人必须维护的登记式护栏。
        var unregistered = actualBypassSites.Except(ExpectedBypassExemptions, StringComparer.Ordinal).ToArray();
        var vanished = ExpectedBypassExemptions.Except(actualBypassSites, StringComparer.Ordinal).ToArray();
        Assert.True(
            unregistered.Length == 0 && vanished.Length == 0,
            $"绕过面闭集对不上。未登记的新增命中 {unregistered.Length} 条：{Environment.NewLine}"
                + string.Join(Environment.NewLine, unregistered)
                + $"{Environment.NewLine}登记了却不再命中的 {vanished.Length} 条（多为原文变动，需同步登记）：{Environment.NewLine}"
                + string.Join(Environment.NewLine, vanished));
        Assert.Equal(ExpectedBypassExemptions, actualBypassSites);

        // ②b 负空间（fail-closed）：**原始插值串** $""" ... """ 的 body 取不到，
        //     本扫描在构造上判不了它。与其留成潜伏缺口，不如禁止它出现在扫描面里——
        //     一旦有人引入，这条先红，逼着人回来先把扫描补上。（$@" / @$" 已由正则覆盖，不在此禁。）
        var rawInterpolated = sources
            .Where(source => source.Text.Contains("$\"\"\"", StringComparison.Ordinal))
            .Select(source => source.Relative)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            rawInterpolated.Length == 0,
            "扫描面里出现了原始插值串（$\"\"\"），本扫描判不了它的内容，请先扩展扫描再引入："
                + string.Join(", ", rawInterpolated));

        // ③ 分支自检：**每个方向都要有自己的非空锚点**。
        //    否则「只删掉其中一个分支」时，树上恰好没有该形状的位点，闭集比对照旧成立 → 无声缴械半边护栏
        //    （实测：全失配会红，但**半失配曾经全绿**）。这里用合成样本正反两向钉住每一个方向。
        Assert.True(IsIdempotencyKeyRewrite("var k = request.IdempotencyKey + \":out\";"), "方向①右侧加法失配");
        Assert.True(IsIdempotencyKeyRewrite("var k = \":pfx\" + request.IdempotencyKey;"), "方向②左侧加法失配");
        Assert.True(IsIdempotencyKeyRewrite("var k = $\"{request.IdempotencyKey}:out\";"), "方向③插值后缀失配");
        Assert.True(IsIdempotencyKeyRewrite("var k = $\"pfx-{request.IdempotencyKey}\";"), "方向④插值前缀失配");
        Assert.True(IsIdempotencyKeyRewrite("var k = aliased + \":out\";"), "方向⑤别名加冒号字面量失配");
        Assert.True(IsIdempotencyKeyRewrite("aliased += \":out\";"), "方向⑥别名自加冒号字面量失配");
        // 负向对照：不带任何改写的原样传递不得命中，否则整条护栏退化成「见到键就报」。
        Assert.False(IsIdempotencyKeyRewrite("var k = request.IdempotencyKey;"), "原样传递不应命中");
        Assert.False(IsIdempotencyKeyRewrite("var k = $\"{request.IdempotencyKey}\";"), "整串插值不应命中");

        // ④ **走完整管线**的自检：上面那组是直接喂给谓词的，测不到 StatementsOf 这一段。
        //    这里的合成源码刻意带上「在 + 前折行」和「插值串自带花括号」两个形状——
        //    正是切分实现最容易踩坏的两处（实测踩过：按裸花括号断句会把插值串劈碎，方向③④ 静默失效）。
        const string ProbeSource = """
            public sealed class Probe
            {
                public string CrossLine(string idempotencyKey)
                {
                    return idempotencyKey
                        + ":out";
                }

                public string Interpolated(string idempotencyKey)
                {
                    return $"{idempotencyKey}:in";
                }
            }
            """;
        var probeHits = StatementsOf(ProbeSource).Where(IsIdempotencyKeyRewrite).ToArray();
        Assert.Equal(2, probeHits.Length);
    }

    /// <summary>
    /// 把源码切成**语句**：先剥掉纯注释行，再把换行折平、空白归一，最后按 <c>;</c> 断句。
    /// 这样「在 <c>+</c> 前折行」这种格式化器自己就会产生的写法不会逃出视野。
    /// </summary>
    private static IEnumerable<string> StatementsOf(string text)
    {
        var flattened = WhitespaceRegex().Replace(
            string.Join(' ', text.Split('\n').Select(line => CodeOf(line.TrimEnd('\r'))).Where(line => line.Length > 0)),
            " ");

        // 断句符是 `;` 与 `{` `}`，但**必须跳过字符串字面量内部**：插值串 $"{a}|{b}" 自带花括号，
        // 不跳过就会把它劈成碎片，方向③④ 在真实代码上静默失效（实测踩过一次，自检样本抓不到，
        // 因为样本是直接喂给谓词的、没走这段切分——所以下面另有一条**走完整管线**的自检）。
        var statements = new List<string>();
        var buffer = new StringBuilder();
        var inString = false;
        var escaped = false;
        foreach (var character in flattened)
        {
            if (inString)
            {
                buffer.Append(character);
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
                buffer.Append(character);
                continue;
            }

            if (character is ';' or '{' or '}')
            {
                Flush(statements, buffer);
                continue;
            }

            buffer.Append(character);
        }

        Flush(statements, buffer);
        return statements;

        static void Flush(List<string> sink, StringBuilder buffer)
        {
            var statement = TightenPunctuation(buffer.ToString()).Trim();
            if (statement.Length > 0)
            {
                sink.Add(statement);
            }

            buffer.Clear();
        }
    }

    /// <summary>
    /// 判定一条语句是否「在落库前改写了幂等键」。覆盖六个方向，**每个都由上面的自检样本钉住**；
    /// 仍未覆盖的形状见 <see cref="Every_pre_persist_rewrite_of_an_idempotency_key_goes_through_Compose"/> 的 remarks。
    /// </summary>
    private static bool IsIdempotencyKeyRewrite(string statement)
    {
        // 走 Compose 的**子表达式**不算绕过——但只剔掉那一段，不豁免整条语句。
        // 整条豁免是危险的：真实写面本来就常在一条表达式里同时构造两把键，
        // 那种写法下「越是规范地用了 Compose，同一语句里的裸拼接越是被豁免」。
        var scrubbed = WithoutComposeCalls(statement);

        // 方向①②：加法与幂等键**相邻**——左右两侧都算，不再只认键在左操作数。
        if (IdempotencyKeyAdditionRegex().IsMatch(scrubbed))
        {
            return true;
        }

        // 方向⑤⑥：任意标识符 `+` / `+=` 一段以冒号开头的字面量——堵局部别名后跟字面量后缀。
        if (ColonLiteralAppendRegex().IsMatch(scrubbed))
        {
            return true;
        }

        // 方向③④：插值串里带幂等键，且该串**除了这个洞之外还有别的字面内容**（前缀或后缀都算）。
        foreach (Match interpolation in InterpolatedStringRegex().Matches(scrubbed))
        {
            var body = interpolation.Groups["body"].Value;
            if (!body.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (InterpolationHoleRegex().Replace(body, string.Empty).Trim().Length > 0)
            {
                return true;
            }
        }

        return false;
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

    /// <summary>
    /// 去掉标点两侧的空白（<c>. , ( ) [ ]</c>），**只在字符串字面量之外**处理。
    /// 空白归一只压掉「多打的空格」，压不掉「在成员访问处折行」——折行折平后会留下 <c>X .Y</c>，
    /// 与未折行的 <c>X.Y</c> 文本不同，登记原文就会假红（实测 F1 折在 <c>.</c> 上时确实红了）。
    /// 这一步把两者收敛成同一形态。
    /// </summary>
    private static string TightenPunctuation(string text)
    {
        const string Punctuation = ".,()[]";
        var result = new StringBuilder(text.Length);
        var inString = false;
        var escaped = false;
        foreach (var character in text)
        {
            if (inString)
            {
                result.Append(character);
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
                result.Append(character);
                continue;
            }

            if (character == ' ' && result.Length > 0 && Punctuation.Contains(result[^1], StringComparison.Ordinal))
            {
                continue;
            }

            if (Punctuation.Contains(character, StringComparison.Ordinal))
            {
                while (result.Length > 0 && result[^1] == ' ')
                {
                    result.Length--;
                }
            }

            result.Append(character);
        }

        return result.ToString();
    }

    /// <summary>
    /// 剔除语句里的 <c>InventoryIdempotencyKeyPolicy.Compose(...)</c> 子表达式（按括号配平），
    /// 剩下的部分才拿去判绕过。配平**跳过字符串与字符字面量**（后缀字面量里带括号也不会算错），
    /// 且**配不到右括号时 fail-closed：不剔除**——宁可多判一次绕过，也不要让整条语句失明。
    /// </summary>
    private static string WithoutComposeCalls(string statement)
    {
        const string Marker = "InventoryIdempotencyKeyPolicy.Compose(";
        var result = statement;
        int index;
        while ((index = result.IndexOf(Marker, StringComparison.Ordinal)) >= 0)
        {
            var depth = 0;
            var end = -1;
            var quote = '\0';
            var escaped = false;
            for (var cursor = index + Marker.Length - 1; cursor < result.Length; cursor++)
            {
                var character = result[cursor];
                if (quote != '\0')
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (character is '"' or '\'')
                {
                    quote = character;
                    continue;
                }

                if (character == '(')
                {
                    depth++;
                }
                else if (character == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = cursor;
                        break;
                    }
                }
            }

            // **fail-closed**：配不到右括号就**不剔除**，把整条语句原样交给后面的形状判定。
            // 原先这里是 `return result.Remove(index)`——截断到语句尾，那是 fail-open：
            // marker 之后只要出现一个不配平的 `(`（注释里、字面量里都行），
            // 整条语句就对绕过判定失明（实测可让「摘掉 Compose 换裸拼接」全绿）。
            if (end < 0)
            {
                return result;
            }

            result = result.Remove(index, end - index + 1);
        }

        return result;
    }

    /// <summary>
    /// 取一行里的**代码部分**：整行注释返回空串，**行尾注释也要剥掉**。
    /// 剥的时候必须跳过字符串与字符字面量里的 <c>//</c>（例如 URL、正则），所以复用同一套
    /// 字符串感知扫描。**只剥 <c>//</c> 行注释**；块注释 <c>/* */</c> 只按「整行以 * 或 /* 开头」处理，
    /// 行内块注释不剥——这是本助手的已知边界。
    /// </summary>
    /// <remarks>
    /// 为什么必须剥行尾注释：<c>Compose(</c> 的调用点是**逐文件精确计数**的，
    /// 只剥整行注释的话，一句 <c>// …Compose(</c> 行尾注释就能把被摘掉的调用「补回计数」，
    /// 让「摘掉 Compose 换裸拼接」整个假绿（实测过，全量 295 全绿）。
    /// </remarks>
    private static string CodeOf(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                continue;
            }

            if (character == '/' && index + 1 < line.Length && line[index + 1] == '/')
            {
                return line[..index];
            }
        }

        return line;
    }

    private static string CodeLinesOf(string text)
    {
        return string.Join(
            '\n',
            text.Split('\n').Select(line => CodeOf(line.TrimEnd('\r'))).Where(line => line.Length > 0));
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

    /// <summary>
    /// 定位仓库根。**刻意不用仓内多数用例那套「找 <c>README.md</c> + <c>backend/</c>」的写法**：
    /// 那种锚点在**任何**含这两项的目录上都成立（包括 <c>/tmp</c> 里的 archive 副本），
    /// 而本类要保证扫描到的是**当前被构建的这棵树**。这里以
    /// <see cref="InventoryWebApplicationRelativeRoot"/> 这条深路径为锚，更严且 fail-closed
    /// （找不到直接抛，不回落到当前工作目录）。改写法前先读
    /// <see cref="Every_pre_persist_rewrite_of_an_idempotency_key_goes_through_Compose"/> 的 remarks 末段。
    /// </summary>
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

    [GeneratedRegex(@"InventoryIdempotencyKeyPolicy\.Compose\(", RegexOptions.CultureInvariant)]
    private static partial Regex ComposeCallRegex();

    /// <summary>加法与幂等键相邻：**左右两侧都认**（`key + x` 与 `x + key`，前缀同样撑爆列宽）。</summary>
    [GeneratedRegex(@"[A-Za-z_.]*[Ii]dempotencyKey\s*\+|\+\s*[A-Za-z_.]*[Ii]dempotencyKey", RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyKeyAdditionRegex();

    /// <summary>任意标识符 `+` / `+=` 一段以冒号开头的字面量——堵「局部别名 + 字面量后缀」。</summary>
    [GeneratedRegex(@"\w+\s*\+=?\s*""\s*:", RegexOptions.CultureInvariant)]
    private static partial Regex ColonLiteralAppendRegex();

    /// <summary>插值字符串整体，<c>body</c> 组是引号之间的内容。</summary>
    [GeneratedRegex(@"(?:\$@?|@\$)""(?<body>[^""]*)""", RegexOptions.CultureInvariant)]
    private static partial Regex InterpolatedStringRegex();

    /// <summary>插值串里的洞 <c>{...}</c>，用于判断「除了洞之外还有没有别的字面内容」。</summary>
    [GeneratedRegex(@"\{[^}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex InterpolationHoleRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

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
