using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using System.Globalization;
using System.Text;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§7 一致性校验器的 **工程域侧**。
///
/// 覆盖：号段完整（spec 有的库里都有、库里该号段的都在 spec 内）、状态与生效日语义自洽
/// （已发布必有审批引用且生效日已到、已排期生效日一定在 <c>asOfDate</c> 之后、草稿无生效日）、
/// 状态分布落在容差内、每张变更至少一条受影响版本、受影响版本引用得上 L0 真实版本、
/// 文档类型/文件名/内容类型/SOP 挂载点自洽、时间戳落在历史窗口内且不在周日、与固定演示事实隔离。
/// **fail-closed**：任何一条不成立即抛 <see cref="WorldHistoryConsistencyException"/>。
/// </summary>
public sealed class WorldHistoryConsistencyValidator(ApplicationDbContext dbContext)
{
    /// <summary>分布类校验的相对容差：至少 ±3%，样本小时放宽到 3σ（与其他域同口径）。</summary>
    public const double MinimumRelativeTolerance = 0.03;

    private static readonly string[] ReservedInfixes = ["-DEMO-", "-SCALE-"];

    private static readonly string[] AllowedContentTypes =
    [
        WorldHistoryEngineeringSpec.PdfContentType,
        WorldHistoryEngineeringSpec.DwgContentType,
        WorldHistoryEngineeringSpec.XlsxContentType,
        WorldHistoryEngineeringSpec.DocxContentType,
    ];

    public async Task<WorldHistoryEngineeringValidationReport> ValidateAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var changeFacts = WorldHistoryEngineeringSpec.BuildChangeFacts(asOfDate, scale);
        var documentFacts = WorldHistoryEngineeringSpec.BuildDocumentFacts(asOfDate, scale);
        var failures = new List<string>();

        var changes = await LoadChangesAsync(organizationId, environmentId, cancellationToken);
        var documents = await LoadDocumentsAsync(organizationId, environmentId, cancellationToken);
        var lowerBound = WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var upperBound = asOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        CheckChangePopulation(changeFacts, changes, failures);
        foreach (var fact in changeFacts)
        {
            if (changes.TryGetValue(fact.ChangeNumber, out var change))
            {
                CheckChange(fact, change, asOfDate, lowerBound, upperBound, failures);
            }
        }

        var referencesChecked = await CheckAffectedVersionReferencesAsync(
            organizationId, environmentId, changes.Values, failures, cancellationToken);
        CheckChangeDistribution(changes.Values, failures);

        CheckDocumentPopulation(documentFacts, documents, failures);
        foreach (var fact in documentFacts)
        {
            if (documents.TryGetValue(WorldHistorySeedService.DocumentKey(fact.DocumentNumber, fact.Revision), out var document))
            {
                CheckDocument(fact, document, lowerBound, upperBound, failures);
            }
        }

        CheckDocumentDistribution(documents.Values, failures);
        CheckIsolation(changes.Keys, documents.Values.Select(x => x.DocumentNumber), failures);

        if (failures.Count > 0)
        {
            throw new WorldHistoryConsistencyException(failures);
        }

        return new WorldHistoryEngineeringValidationReport(
            EngineeringChangesChecked: changes.Count,
            AffectedVersionsChecked: changes.Values.Sum(x => x.AffectedVersions.Count),
            PublishedChanges: CountByStatus(changes.Values, EngineeringVersionStatus.Published),
            ScheduledChanges: CountByStatus(changes.Values, EngineeringVersionStatus.Scheduled),
            DraftChanges: CountByStatus(changes.Values, EngineeringVersionStatus.Draft),
            CancelledChanges: CountByStatus(changes.Values, EngineeringVersionStatus.Cancelled),
            EngineeringDocumentsChecked: documents.Count,
            SopDocumentsChecked: documents.Values.Count(x => x.OperationCode is not null),
            ArchivedDocumentsChecked: documents.Values.Count(x => x.Status == EngineeringVersionStatus.Archived),
            AffectedVersionReferencesChecked: referencesChecked);
    }

    #region 工程变更校验

    private static void CheckChangePopulation(
        IReadOnlyList<WorldHistoryEngineeringChangeFact> facts,
        IReadOnlyDictionary<string, ChangeProjection> changes,
        List<string> failures)
    {
        var expected = facts.Select(x => x.ChangeNumber).ToHashSet(StringComparer.Ordinal);
        var missing = expected.Except(changes.Keys, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            failures.Add($"工程变更缺失 {missing.Length} 张（首条 {missing.Order(StringComparer.Ordinal).First()}）。");
        }

        var unexpected = changes.Keys.Except(expected, StringComparer.Ordinal).ToArray();
        if (unexpected.Length > 0)
        {
            failures.Add($"{WorldHistoryEngineeringSpec.ChangeNumberPrefix} 号段被本次计划之外的 {unexpected.Length} 张变更占用（首条 {unexpected.Order(StringComparer.Ordinal).First()}）。");
        }
    }

    private static void CheckChange(
        WorldHistoryEngineeringChangeFact fact,
        ChangeProjection change,
        DateOnly asOfDate,
        DateTime lowerBound,
        DateTime upperBound,
        List<string> failures)
    {
        if (!IsNumberedSegment(change.ChangeNumber, WorldHistoryEngineeringSpec.ChangeNumberPrefix))
        {
            failures.Add($"{change.ChangeNumber} 不符合 {WorldHistoryEngineeringSpec.ChangeNumberPrefix}#### 号段格式。");
        }

        if (string.IsNullOrWhiteSpace(change.Reason))
        {
            failures.Add($"{change.ChangeNumber} 缺少变更原因。");
        }

        var expectedStatus = ToStatus(fact.State);
        if (change.Status != expectedStatus)
        {
            failures.Add($"{change.ChangeNumber} 状态为 {change.Status}，与历史事实 {expectedStatus} 不符。");
        }

        if (change.AffectedVersions.Count == 0)
        {
            failures.Add($"{change.ChangeNumber} 没有受影响版本——工程变更页会显示成一张空单。");
        }

        var expectedVersions = fact.AffectedVersions
            .Select(x => $"{x.VersionKind}|{x.VersionId}|{x.SupersededByVersionId ?? string.Empty}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var actualVersions = change.AffectedVersions
            .Select(x => $"{x.VersionKind}|{x.VersionId}|{x.SupersededByVersionId ?? string.Empty}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        if (!expectedVersions.SequenceEqual(actualVersions, StringComparer.Ordinal))
        {
            failures.Add($"{change.ChangeNumber} 的受影响版本与历史事实不一致。");
        }

        switch (fact.State)
        {
            case WorldHistoryEngineeringChangeState.Draft:
                if (change.EffectiveDate is not null)
                {
                    failures.Add($"{change.ChangeNumber} 是草稿却已经有生效日 {change.EffectiveDate}。");
                }

                break;

            case WorldHistoryEngineeringChangeState.Scheduled:
                if (change.EffectiveDate is not { } scheduledDate || scheduledDate <= asOfDate)
                {
                    failures.Add($"{change.ChangeNumber} 已排期，生效日必须晚于 {asOfDate:yyyy-MM-dd}，否则定时发布任务一启动就会把它推成已发布。");
                }

                break;

            case WorldHistoryEngineeringChangeState.Published:
                if (change.EffectiveDate is not { } publishedDate || publishedDate > asOfDate)
                {
                    failures.Add($"{change.ChangeNumber} 已发布，但生效日 {change.EffectiveDate} 尚未到达 {asOfDate:yyyy-MM-dd}。");
                }

                break;

            default:
                if (change.EffectiveDate is null)
                {
                    failures.Add($"{change.ChangeNumber} 已取消，应保留取消前最后一次排期的生效日。");
                }

                break;
        }

        var approvalRequired = fact.State != WorldHistoryEngineeringChangeState.Draft;
        if (approvalRequired && string.IsNullOrWhiteSpace(change.ApprovalReferenceId))
        {
            failures.Add($"{change.ChangeNumber} 状态为 {change.Status} 却没有审批引用。");
        }

        if (!approvalRequired && !string.IsNullOrWhiteSpace(change.ApprovalReferenceId))
        {
            failures.Add($"{change.ChangeNumber} 还是草稿却已经带上审批引用 {change.ApprovalReferenceId}。");
        }

        CheckMoment(change.ChangeNumber, "创建时间", change.CreatedAtUtc, fact.OpenedAtUtc.UtcDateTime, lowerBound, upperBound, failures);
        CheckMoment(change.ChangeNumber, "更新时间", change.UpdatedAtUtc, fact.DecidedAtUtc.UtcDateTime, lowerBound, upperBound, failures);
        if (change.UpdatedAtUtc < change.CreatedAtUtc)
        {
            failures.Add($"{change.ChangeNumber} 的更新时间早于创建时间。");
        }
    }

    /// <summary>
    /// 受影响版本必须指得上 L0 真实存在的版本。
    /// L1 允许在没有 L0 的环境（单元测试、只跑历史的排障场景）里单独执行，
    /// 因此仅当对应 L0 表在本租户下非空时才做引用完整性校验——空表意味着 L0 压根没铺，不是引用坏了。
    /// </summary>
    private async Task<bool> CheckAffectedVersionReferencesAsync(
        string organizationId,
        string environmentId,
        IEnumerable<ChangeProjection> changes,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        var engineeringBoms = await LoadVersionKeysAsync(
            dbContext.EngineeringBoms
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                .Select(x => new VersionKeyProjection(x.BomCode, x.Revision)),
            cancellationToken);
        var manufacturingBoms = await LoadVersionKeysAsync(
            dbContext.ManufacturingBoms
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                .Select(x => new VersionKeyProjection(x.BomCode, x.Revision)),
            cancellationToken);
        var routings = await LoadVersionKeysAsync(
            dbContext.Routings
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
                .Select(x => new VersionKeyProjection(x.RoutingCode, x.Revision)),
            cancellationToken);

        if (engineeringBoms.Count == 0 || manufacturingBoms.Count == 0 || routings.Count == 0)
        {
            return false;
        }

        foreach (var change in changes)
        {
            foreach (var affectedVersion in change.AffectedVersions)
            {
                var pool = affectedVersion.VersionKind switch
                {
                    WorldHistoryEngineeringSpec.VersionKindEngineeringBom => engineeringBoms,
                    WorldHistoryEngineeringSpec.VersionKindManufacturingBom => manufacturingBoms,
                    WorldHistoryEngineeringSpec.VersionKindRouting => routings,
                    _ => null,
                };

                if (pool is null)
                {
                    failures.Add($"{change.ChangeNumber} 引用了未支持的受影响版本类型 {affectedVersion.VersionKind}。");
                    continue;
                }

                if (!pool.Contains(affectedVersion.VersionId))
                {
                    failures.Add($"{change.ChangeNumber} 的受影响版本 {affectedVersion.VersionId} 在 L0 工程主数据里不存在。");
                }

                if (affectedVersion.SupersededByVersionId is { } successor && !pool.Contains(successor))
                {
                    failures.Add($"{change.ChangeNumber} 的后继版本 {successor} 在 L0 工程主数据里不存在。");
                }
            }
        }

        return true;
    }

    private static void CheckChangeDistribution(IReadOnlyCollection<ChangeProjection> changes, List<string> failures)
    {
        if (changes.Count == 0)
        {
            return;
        }

        CheckShare(changes, EngineeringVersionStatus.Published, WorldHistoryEngineeringSpec.PublishedShare, "已发布", failures);
        CheckShare(changes, EngineeringVersionStatus.Scheduled, WorldHistoryEngineeringSpec.ScheduledShare, "已排期", failures);
        CheckShare(changes, EngineeringVersionStatus.Draft, WorldHistoryEngineeringSpec.DraftShare, "草稿", failures);
        CheckShare(changes, EngineeringVersionStatus.Cancelled, WorldHistoryEngineeringSpec.CancelledShare, "已取消", failures);
    }

    private static void CheckShare(
        IReadOnlyCollection<ChangeProjection> changes,
        EngineeringVersionStatus status,
        double expectedShare,
        string label,
        List<string> failures)
    {
        var actual = CountByStatus(changes, status);
        if (!WithinTolerance(actual, expectedShare, changes.Count))
        {
            failures.Add(FormattableString.Invariant(
                $"工程变更「{label}」占比 {actual}/{changes.Count} 偏离设定集目标 {expectedShare:P0}。"));
        }
    }

    #endregion

    #region 工程文档校验

    private static void CheckDocumentPopulation(
        IReadOnlyList<WorldHistoryEngineeringDocumentFact> facts,
        IReadOnlyDictionary<string, DocumentProjection> documents,
        List<string> failures)
    {
        var expected = facts
            .Select(x => WorldHistorySeedService.DocumentKey(x.DocumentNumber, x.Revision))
            .ToHashSet(StringComparer.Ordinal);
        var missing = expected.Except(documents.Keys, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            failures.Add($"工程文档缺失 {missing.Length} 份（首条 {missing.Order(StringComparer.Ordinal).First()}）。");
        }

        var unexpected = documents.Keys.Except(expected, StringComparer.Ordinal).ToArray();
        if (unexpected.Length > 0)
        {
            failures.Add($"{WorldHistoryEngineeringSpec.DocumentNumberPrefix} 号段被本次计划之外的 {unexpected.Length} 份文档占用（首条 {unexpected.Order(StringComparer.Ordinal).First()}）。");
        }
    }

    private static void CheckDocument(
        WorldHistoryEngineeringDocumentFact fact,
        DocumentProjection document,
        DateTime lowerBound,
        DateTime upperBound,
        List<string> failures)
    {
        if (!IsNumberedSegment(document.DocumentNumber, WorldHistoryEngineeringSpec.DocumentNumberPrefix))
        {
            failures.Add($"{document.DocumentNumber} 不符合 {WorldHistoryEngineeringSpec.DocumentNumberPrefix}#### 号段格式。");
        }

        if (!WorldHistoryEngineeringSpec.DocumentTypes.Contains(document.DocumentType, StringComparer.Ordinal))
        {
            failures.Add($"{document.DocumentNumber} 的文档类型 {document.DocumentType} 不在设定集范围内。");
        }

        if (!AllowedContentTypes.Contains(document.ContentType, StringComparer.Ordinal))
        {
            failures.Add($"{document.DocumentNumber} 的内容类型 {document.ContentType} 不在设定集范围内。");
        }

        if (!ContainsChinese(document.FileName))
        {
            failures.Add($"{document.DocumentNumber} 的文件名「{document.FileName}」不是中文——演示页面上会露出内部英文字面量。");
        }

        if (fact.IsSop)
        {
            if (string.IsNullOrWhiteSpace(document.OperationCode) || document.EffectiveDate is null)
            {
                failures.Add($"{document.DocumentNumber} 是作业指导书，必须同时挂上工序编码与生效日。");
            }
            else if (!WorldBibleSpec.StandardOperations.Any(operation =>
                string.Equals(operation.OperationCode, document.OperationCode, StringComparison.Ordinal)))
            {
                failures.Add($"{document.DocumentNumber} 挂在 L0 不存在的工序 {document.OperationCode} 上。");
            }
        }
        else
        {
            if (document.OperationCode is not null)
            {
                failures.Add($"{document.DocumentNumber} 不是作业指导书，不应带工序编码。");
            }

            if (document.ItemCode is null ||
                !WorldBibleSpec.Products.Any(product => string.Equals(product.SkuCode, document.ItemCode, StringComparison.Ordinal)))
            {
                failures.Add($"{document.DocumentNumber} 的物料 {document.ItemCode ?? "(空)"} 不在 L0 成品清单内。");
            }
        }

        var expectedStatus = fact.IsArchived ? EngineeringVersionStatus.Archived : EngineeringVersionStatus.Published;
        if (document.Status != expectedStatus)
        {
            failures.Add($"{document.DocumentNumber} 状态为 {document.Status}，与历史事实 {expectedStatus} 不符。");
        }

        if (!string.Equals(document.FileId, fact.FileId, StringComparison.Ordinal))
        {
            failures.Add($"{document.DocumentNumber} 的 fileId 与历史事实不一致。");
        }

        CheckMoment(document.DocumentNumber, "登记时间", document.RegisteredAtUtc, fact.RegisteredAtUtc.UtcDateTime, lowerBound, upperBound, failures);
    }

    private static void CheckDocumentDistribution(IReadOnlyCollection<DocumentProjection> documents, List<string> failures)
    {
        if (documents.Count == 0)
        {
            return;
        }

        var archived = documents.Count(x => x.Status == EngineeringVersionStatus.Archived);
        if (!WithinTolerance(archived, WorldHistoryEngineeringSpec.ArchivedDocumentShare, documents.Count))
        {
            failures.Add(FormattableString.Invariant(
                $"已归档文档占比 {archived}/{documents.Count} 偏离设定集目标 {WorldHistoryEngineeringSpec.ArchivedDocumentShare:P0}。"));
        }
    }

    #endregion

    #region 通用

    private static void CheckIsolation(
        IEnumerable<string> changeNumbers,
        IEnumerable<string> documentNumbers,
        List<string> failures)
    {
        foreach (var number in changeNumbers.Concat(documentNumbers))
        {
            foreach (var infix in ReservedInfixes)
            {
                if (number.Contains(infix, StringComparison.Ordinal))
                {
                    failures.Add($"{number} 落进了固定演示 / 规模块保留号段。");
                }
            }
        }
    }

    private static void CheckMoment(
        string number,
        string label,
        DateTime actual,
        DateTime expected,
        DateTime lowerBound,
        DateTime upperBound,
        List<string> failures)
    {
        if (actual != expected)
        {
            failures.Add($"{number} 的{label}未回填到历史窗口（库内 {actual:O}，历史事实 {expected:O}）。");
            return;
        }

        if (actual < lowerBound || actual > upperBound)
        {
            failures.Add($"{number} 的{label} {actual:O} 落在历史窗口之外。");
        }

        if (!WorldHistoryCalendar.IsWorkingDay(DateOnly.FromDateTime(actual.Add(WorldHistoryCalendar.SiteUtcOffset))))
        {
            failures.Add($"{number} 的{label} {actual:O} 落在周日（停产保养日）。");
        }
    }

    private static int CountByStatus(IEnumerable<ChangeProjection> changes, EngineeringVersionStatus status) =>
        changes.Count(x => x.Status == status);

    /// <summary>号段形如 <c>PREFIX####</c>（4 位十进制序号）。</summary>
    private static bool IsNumberedSegment(string number, string prefix)
    {
        if (!number.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = number.AsSpan(prefix.Length);
        return suffix.Length >= 4 && !suffix.ContainsAnyExcept("0123456789");
    }

    private static bool ContainsChinese(string value) =>
        value.Any(character => character is >= '一' and <= '鿿');

    public static bool WithinTolerance(int actual, double expectedShare, int total)
    {
        var expected = total * expectedShare;
        if (expected <= 0d)
        {
            return actual == 0;
        }

        var sigma = Math.Sqrt(total * expectedShare * (1d - expectedShare));
        var allowed = Math.Max(expected * MinimumRelativeTolerance, 3d * sigma);
        return Math.Abs(actual - expected) <= allowed;
    }

    private static EngineeringVersionStatus ToStatus(WorldHistoryEngineeringChangeState state) => state switch
    {
        WorldHistoryEngineeringChangeState.Draft => EngineeringVersionStatus.Draft,
        WorldHistoryEngineeringChangeState.Scheduled => EngineeringVersionStatus.Scheduled,
        WorldHistoryEngineeringChangeState.Published => EngineeringVersionStatus.Published,
        _ => EngineeringVersionStatus.Cancelled,
    };

    #endregion

    #region 载入紧凑投影

    private async Task<Dictionary<string, ChangeProjection>> LoadChangesAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var prefix = WorldHistoryEngineeringSpec.ChangeNumberPrefix;
        var rows = await dbContext.EngineeringChanges
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.ChangeNumber.StartsWith(prefix))
            .Select(x => new ChangeProjection(
                x.ChangeNumber,
                x.Reason,
                x.ApprovalReferenceId,
                x.Status,
                x.EffectiveDate,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.AffectedVersions
                    .Select(version => new AffectedVersionProjection(version.VersionKind, version.VersionId, version.SupersededByVersionId))
                    .ToArray()))
            .ToArrayAsync(cancellationToken);

        return rows.ToDictionary(x => x.ChangeNumber, StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, DocumentProjection>> LoadDocumentsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var prefix = WorldHistoryEngineeringSpec.DocumentNumberPrefix;
        var rows = await dbContext.EngineeringDocuments
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.DocumentNumber.StartsWith(prefix))
            .Select(x => new DocumentProjection(
                x.DocumentNumber,
                x.Revision,
                x.DocumentType,
                x.ItemCode,
                x.FileId,
                x.FileName,
                x.ContentType,
                x.OperationCode,
                x.EffectiveDate,
                x.Status,
                x.RegisteredAtUtc))
            .ToArrayAsync(cancellationToken);

        return rows.ToDictionary(
            x => WorldHistorySeedService.DocumentKey(x.DocumentNumber, x.Revision),
            StringComparer.Ordinal);
    }

    private static async Task<HashSet<string>> LoadVersionKeysAsync(
        IQueryable<VersionKeyProjection> query,
        CancellationToken cancellationToken)
    {
        var rows = await query.ToArrayAsync(cancellationToken);
        return rows
            .Select(x => WorldBibleSpec.VersionId(x.Code, x.Revision))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ChangeProjection(
        string ChangeNumber,
        string Reason,
        string ApprovalReferenceId,
        EngineeringVersionStatus Status,
        DateOnly? EffectiveDate,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        IReadOnlyList<AffectedVersionProjection> AffectedVersions);

    private sealed record AffectedVersionProjection(string VersionKind, string VersionId, string? SupersededByVersionId);

    private sealed record DocumentProjection(
        string DocumentNumber,
        string Revision,
        string DocumentType,
        string? ItemCode,
        string FileId,
        string FileName,
        string ContentType,
        string? OperationCode,
        DateOnly? EffectiveDate,
        EngineeringVersionStatus Status,
        DateTime RegisteredAtUtc);

    private sealed record VersionKeyProjection(string Code, string Revision);

    #endregion
}

/// <summary>一次 L1 工程域历史一致性校验的摘要。</summary>
public sealed record WorldHistoryEngineeringValidationReport(
    int EngineeringChangesChecked,
    int AffectedVersionsChecked,
    int PublishedChanges,
    int ScheduledChanges,
    int DraftChanges,
    int CancelledChanges,
    int EngineeringDocumentsChecked,
    int SopDocumentsChecked,
    int ArchivedDocumentsChecked,
    bool AffectedVersionReferencesChecked);

/// <summary>L1 工程域历史一致性校验失败——seed 直接 fail-closed，不让「账不圆」的历史进库。</summary>
public sealed class WorldHistoryConsistencyException : InvalidOperationException
{
    public WorldHistoryConsistencyException(IReadOnlyList<string> failures)
        : base(BuildMessage(failures))
    {
        Failures = failures;
    }

    public WorldHistoryConsistencyException()
        : base("World-history consistency validation failed.")
    {
        Failures = [];
    }

    public WorldHistoryConsistencyException(string message)
        : base(message)
    {
        Failures = [message];
    }

    public WorldHistoryConsistencyException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failures = [message];
    }

    public IReadOnlyList<string> Failures { get; }

    private static string BuildMessage(IReadOnlyList<string> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        var builder = new StringBuilder("L1 工程域背景历史一致性校验失败：");
        builder.Append(failures.Count.ToString(CultureInfo.InvariantCulture));
        builder.Append(" 项不成立。");
        foreach (var failure in failures.Take(20))
        {
            builder.Append(Environment.NewLine).Append("- ").Append(failure);
        }

        return builder.ToString();
    }
}
