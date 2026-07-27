using System.Globalization;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **工程域侧** 确定性事实流。
///
/// 产出两条历史（设定集 §4 产品与 BOM/工艺 + §7 历史节奏）：
/// <list type="number">
/// <item><b>工程变更 ECO</b>（<c>ECO-2026-####</c>）：周均 2 张，跨上线日至 <c>asOfDate</c> 的全部周次，
/// 状态按 已发布 60 / 已排期 20 / 草稿 15 / 已取消 5 分布；每张挂 1–3 条受影响版本，
/// 优先落在 L0 真实存在的 <c>rev1 → rev2</c> 演进链上（热销 8 款有 V2，见
/// <see cref="WorldBibleSpec.HotPlatformCodes"/>）。</item>
/// <item><b>工程文档</b>（<c>DOC-2026-####</c>）：周均 4 份改版，覆盖图纸 / 作业指导书 /
/// 检验规范 / 工艺卡 / 技术协议五类；SOP 挂在 L0 真实存在的标准工序 + 工作中心 + 工艺路线上；
/// 约 12% 已被后续版本取代而归档。</item>
/// </list>
///
/// 本类型是**纯函数**：同一 <c>(asOfDate, scale)</c> 必得同一张事实表，seed 与校验器共用它，
/// 于是「写入的东西」与「校验的东西」不可能漂移。受影响版本与 SOP 挂载点全部由
/// <see cref="WorldBibleSpec"/> 的字面量推导，因此只要 L0 世界观主数据在库，引用即真实存在，
/// 无需跨聚合查询，也不建任何外键。
/// </summary>
public static class WorldHistoryEngineeringSpec
{
    #region 号段（与固定演示事实 *-DEMO-* / 规模块 *-SCALE-* 完全隔离）

    public const string ChangeNumberPrefix = "ECO-2026-";
    public const string DocumentNumberPrefix = "DOC-2026-";
    public const string ApprovalReferencePrefix = "APR-2026-";

    /// <summary>本引擎产出的全部号段前缀，供隔离性回归测试断言不与固定演示事实相交。</summary>
    public static readonly string[] NumberSegmentPrefixes = [ChangeNumberPrefix, DocumentNumberPrefix];

    public static string ChangeNumber(int index) =>
        ChangeNumberPrefix + index.ToString("D4", CultureInfo.InvariantCulture);

    public static string DocumentNumber(int index) =>
        DocumentNumberPrefix + index.ToString("D4", CultureInfo.InvariantCulture);

    /// <summary>
    /// 审批引用号。FileStorage / Approval 侧不存在同号审批链——L1 审批域只覆盖采购订单与 NCR 处置两条来源，
    /// 这里的号是**工程变更页面可读的引用占位**，点进审批详情不会有对应单据（已知遗留项）。
    /// </summary>
    public static string ApprovalReferenceId(int index) =>
        ApprovalReferencePrefix + index.ToString("D4", CultureInfo.InvariantCulture);

    #endregion

    #region 目标分布（校验器按容差核对）

    public const double PublishedShare = 0.60;
    public const double ScheduledShare = 0.20;
    public const double DraftShare = 0.15;
    public const double CancelledShare = 0.05;

    /// <summary>约 12% 的历史文档已被后续版本取代而归档，制造「版本演进」观感。</summary>
    public const double ArchivedDocumentShare = 0.12;

    /// <summary>周均产出（设定集 §7 的历史节奏，春节周按 <see cref="WorldHistoryCalendar.SpringFestivalFactor"/> 收缩）。</summary>
    public const int WeeklyChangeBase = 2;
    public const int WeeklyDocumentBase = 4;

    #endregion

    #region 变更原因（全中文，覆盖真实工厂的六类来由）

    public const string ReasonCustomerDrawing = "customer-drawing";
    public const string ReasonSupplierSubstitution = "supplier-substitution";
    public const string ReasonProcessOptimization = "process-optimization";
    public const string ReasonQualityCorrection = "quality-correction";
    public const string ReasonRegulatoryCompliance = "regulatory-compliance";
    public const string ReasonDesignDefect = "design-defect";

    private static readonly string[] ReasonCategories =
    [
        ReasonCustomerDrawing,
        ReasonSupplierSubstitution,
        ReasonProcessOptimization,
        ReasonQualityCorrection,
        ReasonRegulatoryCompliance,
        ReasonDesignDefect,
    ];

    private static readonly int[] ReasonWeights = [22, 20, 20, 18, 8, 12];

    private static readonly string[] DefectPhenomena =
    [
        "油封渗漏",
        "活塞杆镀层划伤",
        "阻尼力曲线超差",
        "缸筒焊缝气孔",
    ];

    #endregion

    #region 文档类型

    public const string DocumentTypeDrawing = "drawing";
    public const string DocumentTypeSop = "sop";
    public const string DocumentTypeInspectionSpec = "inspection-spec";
    public const string DocumentTypeProcessCard = "process-card";
    public const string DocumentTypeTechAgreement = "tech-agreement";

    public static readonly string[] DocumentTypes =
    [
        DocumentTypeDrawing,
        DocumentTypeSop,
        DocumentTypeProcessCard,
        DocumentTypeInspectionSpec,
        DocumentTypeTechAgreement,
    ];

    private static readonly int[] DocumentTypeWeights = [30, 25, 20, 15, 10];

    public const string PdfContentType = "application/pdf";
    public const string DwgContentType = "application/acad";
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    #endregion

    #region 受影响版本

    public const string VersionKindEngineeringBom = "engineering-bom";
    public const string VersionKindManufacturingBom = "manufacturing-bom";
    public const string VersionKindRouting = "routing";

    #endregion

    /// <summary>工程变更事实流：按周次推进，编号全局连续，早期周次的内容不随 <paramref name="asOfDate"/> 增长而改变。</summary>
    public static IReadOnlyList<WorldHistoryEngineeringChangeFact> BuildChangeFacts(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var facts = new List<WorldHistoryEngineeringChangeFact>();
        var weeks = WorldHistoryCalendar.WeekCount(asOfDate);
        var index = 0;

        for (var weekIndex = 0; weekIndex < weeks; weekIndex++)
        {
            var volume = WeeklyVolume("eco", weekIndex, WeeklyChangeBase, scale);
            for (var slot = 0; slot < volume; slot++)
            {
                var openedOn = SlotWorkingDay("eco", weekIndex, slot);
                if (openedOn > asOfDate)
                {
                    continue;
                }

                index++;
                facts.Add(BuildChangeFact(index, openedOn, asOfDate));
            }
        }

        return facts;
    }

    /// <summary>工程文档事实流：同样按周次推进，编号全局连续。</summary>
    public static IReadOnlyList<WorldHistoryEngineeringDocumentFact> BuildDocumentFacts(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var facts = new List<WorldHistoryEngineeringDocumentFact>();
        var weeks = WorldHistoryCalendar.WeekCount(asOfDate);
        var index = 0;

        for (var weekIndex = 0; weekIndex < weeks; weekIndex++)
        {
            var volume = WeeklyVolume("edoc", weekIndex, WeeklyDocumentBase, scale);
            for (var slot = 0; slot < volume; slot++)
            {
                var registeredOn = SlotWorkingDay("edoc", weekIndex, slot);
                if (registeredOn > asOfDate)
                {
                    continue;
                }

                index++;
                facts.Add(BuildDocumentFact(index, registeredOn));
            }
        }

        return facts;
    }

    #region 周次节奏

    /// <summary>第 <paramref name="weekIndex"/> 周的产出量：基准 ± 1 抖动，春节周收缩，再按 <paramref name="scale"/> 缩放（至少 1）。</summary>
    private static int WeeklyVolume(string kind, int weekIndex, int baseCount, double scale)
    {
        var weekStart = WorldHistoryCalendar.WeekStart(weekIndex);
        var factor = WorldHistoryCalendar.WeekOverlapsSpringFestival(weekStart)
            ? WorldHistoryCalendar.SpringFestivalFactor
            : 1.0d;
        var random = new WorldHistoryRandom($"{kind}:week:{weekIndex:D3}");
        var shaped = Math.Max(1, baseCount + random.NextInt(-1, 2));
        var scaled = (int)Math.Round(shaped * factor * scale, MidpointRounding.AwayFromZero);
        return Math.Max(scaled, 1);
    }

    /// <summary>周内第 <paramref name="slot"/> 个槽位落在周一至周六的哪一天（周日停产，不排工程动作）。</summary>
    private static DateOnly SlotWorkingDay(string kind, int weekIndex, int slot)
    {
        var random = new WorldHistoryRandom($"{kind}:slot:{weekIndex:D3}:{slot:D2}");
        return WorldHistoryCalendar.WeekStart(weekIndex).AddDays(random.NextInt(0, 6));
    }

    private static DateTimeOffset SlotMoment(string streamKey, DateOnly day)
    {
        var random = new WorldHistoryRandom(streamKey);
        // 工程动作集中在早班（评审、发图都在白天），少量落到中班。
        var shiftIndex = random.Chance(0.85) ? 0 : 1;
        return WorldHistoryCalendar.ShiftMoment(day, shiftIndex, random.NextInt(0, WorldHistoryCalendar.ShiftLengthHours * 60));
    }

    #endregion

    #region 工程变更

    private static WorldHistoryEngineeringChangeFact BuildChangeFact(int index, DateOnly openedOn, DateOnly asOfDate)
    {
        var changeNumber = ChangeNumber(index);
        var random = new WorldHistoryRandom(changeNumber);
        var product = PickProduct(random);
        var reasonCategory = random.PickWeighted(ReasonCategories, ReasonWeights);
        var state = StateFor(index);
        var affectedVersions = BuildAffectedVersions(random, product);
        var openedAtUtc = SlotMoment($"{changeNumber}:opened", openedOn);

        // 草稿还没走完评审：既没有审批引用，也没有生效日——这正是「待审批」看板要展示的那一档。
        if (state == WorldHistoryEngineeringChangeState.Draft)
        {
            return new WorldHistoryEngineeringChangeFact(
                changeNumber,
                BuildReasonText(reasonCategory, product, random),
                ReasonCategory: reasonCategory,
                ApprovalReferenceId: null,
                State: state,
                EffectiveDate: null,
                OpenedAtUtc: openedAtUtc,
                DecidedAtUtc: openedAtUtc,
                AffectedVersions: affectedVersions);
        }

        var decidedOn = WorldHistoryCalendar.AddWorkingDays(openedOn, random.NextInt(1, 6));
        if (decidedOn > asOfDate)
        {
            decidedOn = openedOn;
        }

        var decidedAtUtc = decidedOn == openedOn ? openedAtUtc : SlotMoment($"{changeNumber}:decided", decidedOn);
        var leadDays = random.NextInt(7, 29);
        var effectiveDate = state switch
        {
            // 已排期：生效日一定在 asOfDate 之后，否则定时发布任务一启动就会把它推成已发布，分布随之失真。
            WorldHistoryEngineeringChangeState.Scheduled =>
                WorldHistoryCalendar.SnapToWorkingDay(asOfDate.AddDays(random.NextInt(7, 61))),
            // 已发布：生效日已经到达，夹在 asOfDate 之内。
            WorldHistoryEngineeringChangeState.Published =>
                ClampToWindow(WorldHistoryCalendar.SnapToWorkingDay(openedOn.AddDays(leadDays)), openedOn, asOfDate),
            // 已取消：保留取消前最后一次排期的生效日。
            _ => WorldHistoryCalendar.SnapToWorkingDay(openedOn.AddDays(leadDays)),
        };

        return new WorldHistoryEngineeringChangeFact(
            changeNumber,
            BuildReasonText(reasonCategory, product, random),
            ReasonCategory: reasonCategory,
            ApprovalReferenceId: ApprovalReferenceId(index),
            State: state,
            EffectiveDate: effectiveDate,
            OpenedAtUtc: openedAtUtc,
            DecidedAtUtc: decidedAtUtc,
            AffectedVersions: affectedVersions);
    }

    /// <summary>
    /// 状态按 **20 张一个配额块** 分层，而不是逐张独立抽签：
    /// 每块固定 12 已发布 / 4 已排期 / 3 草稿 / 1 已取消（正好 60/20/15/5），块内顺序按块号确定性洗牌。
    /// 独立抽签在几十张的样本量上很容易抽出「一张已取消都没有」的历史（0.95^57 ≈ 5%），
    /// 演示页面上就少掉一整档状态；配额分层保证任何纵深下四档都在场，且前缀比例稳定。
    /// </summary>
    public static WorldHistoryEngineeringChangeState StateFor(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(index);
        var block = (index - 1) / StateQuotaBlockSize;
        var position = (index - 1) % StateQuotaBlockSize;
        return BuildStateBlock(block)[position];
    }

    /// <summary>配额块大小：20 = 12 + 4 + 3 + 1，与设定集的 60/20/15/5 精确对齐。</summary>
    public const int StateQuotaBlockSize = 20;

    private static WorldHistoryEngineeringChangeState[] BuildStateBlock(int block)
    {
        var slots = new WorldHistoryEngineeringChangeState[StateQuotaBlockSize];
        var cursor = 0;
        Fill(slots, ref cursor, WorldHistoryEngineeringChangeState.Published, 12);
        Fill(slots, ref cursor, WorldHistoryEngineeringChangeState.Scheduled, 4);
        Fill(slots, ref cursor, WorldHistoryEngineeringChangeState.Draft, 3);
        Fill(slots, ref cursor, WorldHistoryEngineeringChangeState.Cancelled, 1);

        // Fisher–Yates，随机源只由块号决定——同一块在任何 asOfDate / scale 下顺序相同。
        var random = new WorldHistoryRandom($"eco:state-block:{block:D4}");
        for (var i = slots.Length - 1; i > 0; i--)
        {
            var j = random.NextInt(0, i + 1);
            (slots[i], slots[j]) = (slots[j], slots[i]);
        }

        return slots;
    }

    private static void Fill(
        WorldHistoryEngineeringChangeState[] slots,
        ref int cursor,
        WorldHistoryEngineeringChangeState state,
        int count)
    {
        for (var offset = 0; offset < count; offset++)
        {
            slots[cursor++] = state;
        }
    }

    private static DateOnly ClampToWindow(DateOnly candidate, DateOnly openedOn, DateOnly asOfDate) =>
        candidate > asOfDate ? openedOn : candidate;

    /// <summary>热销 8 款权重 3、其余 16 款权重 1——六成变更落在有 V2 演进链的产品上。</summary>
    private static WorldBibleProduct PickProduct(WorldHistoryRandom random) =>
        random.PickWeighted(
            WorldBibleSpec.Products,
            [.. WorldBibleSpec.Products.Select(product => product.IsHotSelling ? 3 : 1)]);

    /// <summary>
    /// 1–3 条受影响版本。热销款用 L0 真实存在的 <c>rev1 → rev2</c> 链（<c>supersededByVersionId</c> 指向后继版本），
    /// 其余款只声明被影响的 rev1（工艺路线在 L0 只有 rev1，永远没有后继）。
    /// </summary>
    private static IReadOnlyList<WorldHistoryAffectedVersionFact> BuildAffectedVersions(
        WorldHistoryRandom random,
        WorldBibleProduct product)
    {
        var successorRevision = product.IsHotSelling ? WorldBibleSpec.V2Revision : null;
        var candidates = new List<WorldHistoryAffectedVersionFact>
        {
            BuildAffectedVersion(VersionKindEngineeringBom, WorldBibleSpec.EngineeringBomCode(product.SkuCode), successorRevision),
            BuildAffectedVersion(VersionKindManufacturingBom, WorldBibleSpec.ManufacturingBomCode(product.SkuCode), successorRevision),
            BuildAffectedVersion(VersionKindRouting, WorldBibleSpec.RoutingCode(product.SkuCode), null),
        };

        return [.. candidates.Take(random.NextInt(1, candidates.Count + 1))];
    }

    private static WorldHistoryAffectedVersionFact BuildAffectedVersion(string versionKind, string code, string? successorRevision) =>
        new(
            versionKind,
            WorldBibleSpec.VersionId(code, WorldBibleSpec.V1Revision),
            successorRevision is null ? null : WorldBibleSpec.VersionId(code, successorRevision));

    private static string BuildReasonText(string category, WorldBibleProduct product, WorldHistoryRandom random)
    {
        var operation = random.Pick(WorldBibleSpec.StandardOperations);
        return category switch
        {
            ReasonCustomerDrawing =>
                $"客户图纸升版：{product.SkuName}按主机厂 {product.PlatformCode} 平台新版图纸调整安装点与行程尺寸，设计 BOM 与制造 BOM 同步升版。",
            ReasonSupplierSubstitution =>
                $"供应商物料替换：{product.SkuName}弹簧由 {product.SpringSkuCode} 切换为二供 {product.SecondSourceSpringSkuCode}，等效性验证与首件确认通过后执行。",
            ReasonProcessOptimization =>
                $"工艺优化降本：{product.SkuName}的{operation.OperationName}工序节拍由 {operation.RunMinutes + 3} 分钟压缩至 {operation.RunMinutes} 分钟，取消一道中间转序。",
            ReasonQualityCorrection =>
                $"质量问题纠正：{product.SkuName}批量出现{random.Pick(DefectPhenomena)}，按不合格纠正措施加严{operation.OperationName}工序参数并同步更新检验规范。",
            ReasonRegulatoryCompliance =>
                $"法规与标准符合性：{product.SkuName}按乘用车悬架减振器行业标准修订版补充盐雾与耐久试验要求，更新技术协议与检验规范。",
            _ =>
                $"设计缺陷修正：{product.SkuName}防尘罩与缸筒配合过盈量偏大导致装配困难，修正图纸公差带并调整{operation.OperationName}工序作业要求。",
        };
    }

    #endregion

    #region 工程文档

    private static WorldHistoryEngineeringDocumentFact BuildDocumentFact(int index, DateOnly registeredOn)
    {
        var documentNumber = DocumentNumber(index);
        var random = new WorldHistoryRandom(documentNumber);
        var product = PickProduct(random);
        var documentType = random.PickWeighted(DocumentTypes, DocumentTypeWeights);
        var revision = random.PickWeighted<string>([WorldBibleSpec.V1Revision, WorldBibleSpec.V2Revision, "3"], [65, 25, 10]);
        var registeredAtUtc = SlotMoment($"{documentNumber}:registered", registeredOn);
        var isArchived = random.Chance(ArchivedDocumentShare);
        var archiveReason = isArchived ? "已被后续版本取代，按文档管理规程转归档。" : null;
        var fileId = FileId(documentNumber, revision);

        if (documentType == DocumentTypeSop)
        {
            var stageIndex = random.NextInt(0, WorldBibleSpec.StandardOperations.Count);
            var stage = product.RoutingStages()[stageIndex];
            return new WorldHistoryEngineeringDocumentFact(
                documentNumber,
                revision,
                DocumentTypeSop,
                ItemCode: null,
                FileId: fileId,
                FileName: $"{stage.Operation.OperationName}作业指导书_{stage.WorkCenterCode}_Rev{revision}.pdf",
                ContentType: PdfContentType,
                OperationCode: stage.Operation.OperationCode,
                WorkCenterCode: stage.WorkCenterCode,
                RoutingCode: WorldBibleSpec.RoutingCode(product.SkuCode),
                RoutingRevision: WorldBibleSpec.V1Revision,
                EffectiveDate: registeredOn,
                RegisteredAtUtc: registeredAtUtc,
                IsArchived: isArchived,
                ArchiveReason: archiveReason);
        }

        var (fileName, contentType) = BuildNonSopFile(documentType, product, revision, random);
        return new WorldHistoryEngineeringDocumentFact(
            documentNumber,
            revision,
            documentType,
            ItemCode: product.SkuCode,
            FileId: fileId,
            FileName: fileName,
            ContentType: contentType,
            OperationCode: null,
            WorkCenterCode: null,
            RoutingCode: null,
            RoutingRevision: null,
            EffectiveDate: null,
            RegisteredAtUtc: registeredAtUtc,
            IsArchived: isArchived,
            ArchiveReason: archiveReason);
    }

    private static (string FileName, string ContentType) BuildNonSopFile(
        string documentType,
        WorldBibleProduct product,
        string revision,
        WorldHistoryRandom random) => documentType switch
        {
            // 已发布的图纸多为 PDF 出图，少量保留 DWG 原稿。
            DocumentTypeDrawing => random.Chance(0.7)
                ? ($"{product.SkuName}装配图_Rev{revision}.pdf", PdfContentType)
                : ($"{product.SkuName}装配图_Rev{revision}.dwg", DwgContentType),
            DocumentTypeInspectionSpec => ($"{product.SkuName}检验规范_Rev{revision}.xlsx", XlsxContentType),
            DocumentTypeProcessCard => ($"{product.SkuName}工艺卡_Rev{revision}.docx", DocxContentType),
            _ => ($"{product.SkuName}技术协议_Rev{revision}.pdf", PdfContentType),
        };

    /// <summary>
    /// 确定性伪 fileId。**FileStorage 里没有对应对象**：L1 工程文档不上传真实文件，
    /// 因此工程文档页的「下载 / 预览」会 404。这是已知遗留项，演示讲稿里不要点下载。
    /// </summary>
    public static string FileId(string documentNumber, string revision) =>
        "file-edoc-" + WorldHistoryRandom.Fnv1a64($"{documentNumber}:{revision}").ToString("x16", CultureInfo.InvariantCulture);

    #endregion
}

/// <summary>工程变更的历史终局状态。</summary>
public enum WorldHistoryEngineeringChangeState
{
    Draft,
    Scheduled,
    Published,
    Cancelled,
}

/// <summary>一张历史工程变更单的完整事实。</summary>
public sealed record WorldHistoryEngineeringChangeFact(
    string ChangeNumber,
    string Reason,
    string ReasonCategory,
    string? ApprovalReferenceId,
    WorldHistoryEngineeringChangeState State,
    DateOnly? EffectiveDate,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset DecidedAtUtc,
    IReadOnlyList<WorldHistoryAffectedVersionFact> AffectedVersions);

/// <summary>一条受影响版本引用（<c>CODE:REVISION</c> 形态，与发布命令的 versionId 同一口径）。</summary>
public sealed record WorldHistoryAffectedVersionFact(string VersionKind, string VersionId, string? SupersededByVersionId);

/// <summary>一份历史工程文档的完整事实。</summary>
public sealed record WorldHistoryEngineeringDocumentFact(
    string DocumentNumber,
    string Revision,
    string DocumentType,
    string? ItemCode,
    string FileId,
    string FileName,
    string ContentType,
    string? OperationCode,
    string? WorkCenterCode,
    string? RoutingCode,
    string? RoutingRevision,
    DateOnly? EffectiveDate,
    DateTimeOffset RegisteredAtUtc,
    bool IsArchived,
    string? ArchiveReason)
{
    public bool IsSop => string.Equals(DocumentType, WorldHistoryEngineeringSpec.DocumentTypeSop, StringComparison.Ordinal);
}
