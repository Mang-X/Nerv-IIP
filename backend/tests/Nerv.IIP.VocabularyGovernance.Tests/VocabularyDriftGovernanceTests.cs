namespace Nerv.IIP.VocabularyGovernance.Tests;

/// <summary>
/// #1703 · 词表漂移扫描门禁（#1370「防第 19 例」的治本机制）。
///
/// 守护目标：<c>backend/services/**/Application/**</c> 与 <c>backend/services/**/Seed/**</c> 下，
/// 凡与 <c>Nerv.IIP.Contracts.*</c> 任一词表常量同值的字符串字面量，必须以常量引用形式出现；
/// 违例集合为空，或每条违例在 <see cref="VocabularyDriftExemptions"/> 白名单里附中文裁决注释。
/// 词表集合从类型系统穷举（见 <see cref="ContractsVocabularyExtractor"/>），
/// 新增词表常量自动进入守护范围，不存在「要查哪些值」的手写清单。
///
/// 扫描覆盖率声明（不许静默截断）：
/// <list type="bullet">
/// <item>已扫：<c>backend/services</c> 下路径段含 <c>Application</c> 或 <c>Seed</c> 的全部 <c>*.cs</c>
/// （当前仓库所有 Seed 目录都在 Application 之下，单独列出 Seed 段是为将来出现独立 Seed 目录时不漏扫）；
/// 排除路径段 <c>obj</c> / <c>bin</c> / <c>tests</c>（按 segment 精确匹配，不误伤名字含 obj 的目录）。</item>
/// <item>未扫（票面 DoD 之外，明示不覆盖）：Domain / Infrastructure / Endpoints 层、gateway、
/// connector-hosts、前端、EF migration，以及测试代码本身。</item>
/// <item>匹配口径：完整字面量取值的序数相等（含 verbatim / raw / UTF-8 / 无插值洞的插值字符串）；
/// 词表值作为插值或拼接**片段**出现时不在守护范围（无静态完整取值，无法零误报判定）。</item>
/// <item>注册在 <see cref="ReplicaFileNames"/> 的跨服务逐字副本文件不做字面量判定，
/// 改由 <see cref="World_history_replica_files_stay_identical_member_by_member"/> 断言副本一致性。</item>
/// </list>
/// </summary>
public sealed class VocabularyDriftGovernanceTests
{
    /// <summary>
    /// 跨服务逐字副本圈（票面 (b) 类裁决：这些文件是「设计如此」的复制，
    /// 断言「副本间逐字相同」而不是「禁止重复」）。只登记 ≥2 个服务持有的同名 Seed 规格文件；
    /// 服务专属的单份 Spec（如 <c>WorldHistoryFloorEventsSpec.cs</c>）不属于副本圈，走正常字面量门禁。
    /// </summary>
    private static readonly IReadOnlyList<string> ReplicaFileNames =
    [
        // 8 服务共享的世界史主规格（#1703 票面点名）。
        "WorldHistorySpec.cs",
        // 4 服务共享的世界史二期规格（#1703 票面点名）。
        "WorldHistoryPhase2Spec.cs",
        // 以下同为跨服务复制圈：多服务各存一份、须逐字同步（与票面点名的两份同一设计）。
        "WorldHistoryMesSpec.cs",
        "WorldHistoryProcurementSpec.cs",
        "WorldHistoryCountSpec.cs",
        "WorldHistoryDeviceSpec.cs",
        "WorldHistoryQualitySpec.cs",
        "WorldHistoryCalendar.cs",
        "WorldHistoryTimeline.cs",
        "WorldHistoryRandom.cs",
        "WorldHistoryConfiguration.cs",
    ];

    [Fact]
    public void Contracts_vocabulary_extraction_is_clean_and_non_empty()
    {
        var extraction = ContractsVocabularyExtractor.Extract(LoadContractsDocuments());

        Assert.True(
            extraction.Errors.Count == 0,
            "词表常量抽取失败（守护集合不允许静默缩小）：" + Environment.NewLine
            + string.Join(Environment.NewLine, extraction.Errors));
        Assert.NotEmpty(extraction.Constants);
    }

    [Fact]
    public void Application_and_seed_literals_reference_contracts_vocabulary_constants()
    {
        var extraction = ContractsVocabularyExtractor.Extract(LoadContractsDocuments());
        Assert.Empty(extraction.Errors);

        var result = VocabularyLiteralScanner.Scan(
            extraction.Constants,
            LoadScannedDocuments(),
            VocabularyDriftExemptions.Entries,
            ReplicaFileNames);

        Assert.True(
            result.Violations.Count == 0,
            $"发现 {result.Violations.Count} 条词表裸字面量违例（同义改常量引用，同值不同义登记白名单裁决）："
            + Environment.NewLine + string.Join(Environment.NewLine, result.Violations));
        Assert.True(
            result.StaleExemptions.Count == 0,
            "白名单存在未命中的过期豁免（违例修掉后必须同步删除豁免条目）：" + Environment.NewLine
            + string.Join(Environment.NewLine, result.StaleExemptions));
    }

    /// <summary>
    /// 存量副本分裂登记（按「类型名.成员键」）。本票（#1703）只落地门禁不修存量：
    /// 以下成员在 main 上已经分裂，待 #1388 分批销账（裁决「同步副本」还是「承认服务专属分叉并改名」）；
    /// 分裂消失后本登记必须同步删除，否则 stale 检查红。
    ///
    /// 承接票改判（2026-08-20 owner 裁决）：本组原挂 #1370 ③，实为副本间逐字对拍，与 ③ 的
    /// 「裸字面量改引常量」不是一类活；#1370 ③ 已于 2026-08-19 四批（#1830/#1829/#1827/#1858）
    /// 收口，正主是 #1388（8 份副本实为三变体、BuildOrderPlans 无黄金向量）。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KnownReplicaDrifts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Erp / DemandPlanning / Scheduling 三份多出 PRQ-/RFQ-/SQ-/OPP-/COST- 五个号段前缀，
        // 其余五个服务未跟上——号段口径分叉，待 #1388 销账。
        ["WorldHistorySpec.NumberSegmentPrefixes"] = "待 #1388 销账：8 份副本 5 vs 3 分裂（号段前缀清单不一致）。",
        // Mes 一侧的 BuildOperators 多出 EmployeeName / TeamName / 班组名单，其余四份未跟上，待 #1388 销账。
        ["WorldHistoryMesSpec.BuildOperators()"] = "待 #1388 销账：Mes 副本多出班组字段，其余 4 份未同步。",
        // Scheduling 一侧的 StandardOperations 缺少 Workshop 归属参数（记录形状已分叉），待 #1388 销账。
        ["WorldHistoryMesSpec.StandardOperations"] = "待 #1388 销账：Scheduling 副本缺 Workshop 车间归属，其余 5 份已带。",
        // 同一分叉在 record 主构造参数表上的另一面：WorldHistoryOperation 少 Workshop 参数、
        // WorldHistoryOperator 是 Mes 扩展形状——与上两条同根，待 #1388 一并销账。
        ["WorldHistoryOperation.<类型头>"] = "待 #1388 销账：Scheduling 副本的 record 参数表缺 Workshop，与 StandardOperations 同根分叉。",
        ["WorldHistoryOperator.<类型头>"] = "待 #1388 销账：Mes 副本的 record 参数表多出班组字段，与 BuildOperators() 同根分叉。",
        // Approval 一侧的 BuildPurchasePlan 缺 PurchaseReceiptNo 与在途分支（旧版快照），待 #1388 销账。
        ["WorldHistoryProcurementSpec.BuildPurchasePlan(int index, DateOnly orderDate, DateOnly asOfDate)"] =
            "待 #1388 销账：Approval 副本是旧版（缺收货号与在途分支），其余 4 份已演进。",
        ["WorldHistoryPurchasePlan.<类型头>"] = "待 #1388 销账：Approval 副本的 record 参数表是旧版，与 BuildPurchasePlan 同根分叉。",
    };

    [Fact]
    public void World_history_replica_files_stay_identical_member_by_member()
    {
        var documentsByFileName = LoadScannedDocuments()
            .GroupBy(
                document => document.Path[(document.Path.LastIndexOf('/') + 1)..],
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var violations = new List<string>();
        var observedDriftKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fileName in ReplicaFileNames)
        {
            if (!documentsByFileName.TryGetValue(fileName, out var replicas) || replicas.Length < 2)
            {
                violations.Add(
                    $"{fileName}: 副本圈登记要求至少 2 份同名副本，实际 {documentsByFileName.GetValueOrDefault(fileName)?.Length ?? 0} 份；"
                    + "复制圈已收敛时必须同步删除登记。");
                continue;
            }

            foreach (var drift in ReplicaConsistencyChecker.Check(replicas))
            {
                observedDriftKeys.Add(drift.MemberKey);
                if (!KnownReplicaDrifts.ContainsKey(drift.MemberKey))
                {
                    violations.Add(drift.Message);
                }
            }
        }

        violations.AddRange(KnownReplicaDrifts.Keys
            .Where(key => !observedDriftKeys.Contains(key))
            .Select(key => $"{key}: 存量分裂登记未命中（分裂已消失时必须同步删除登记）。裁决原文：{KnownReplicaDrifts[key]}"));

        Assert.True(
            violations.Count == 0,
            "世界史副本圈一致性被破坏（一侧修改必须同步全部副本）：" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static IReadOnlyList<SourceDocument> LoadContractsDocuments()
    {
        var backendRoot = BackendRoot();
        var contractsRoot = Path.Combine(backendRoot, "common", "Contracts");

        return LoadDocuments(
            backendRoot,
            Directory
                .EnumerateFiles(contractsRoot, "*.cs", SearchOption.AllDirectories)
                .Where(file => !HasAnySegment(Path.GetRelativePath(backendRoot, file), "obj", "bin")));
    }

    private static IReadOnlyList<SourceDocument> LoadScannedDocuments()
    {
        var backendRoot = BackendRoot();
        var servicesRoot = Path.Combine(backendRoot, "services");

        return LoadDocuments(
            backendRoot,
            Directory
                .EnumerateFiles(servicesRoot, "*.cs", SearchOption.AllDirectories)
                .Where(file =>
                {
                    var relative = Path.GetRelativePath(backendRoot, file);
                    return !HasAnySegment(relative, "obj", "bin", "tests")
                        && HasAnySegment(relative, "Application", "Seed");
                }));
    }

    private static IReadOnlyList<SourceDocument> LoadDocuments(string backendRoot, IEnumerable<string> files) =>
        files
            .Select(file => new SourceDocument(
                Path.GetRelativePath(backendRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(file)))
            .OrderBy(document => document.Path, StringComparer.Ordinal)
            .ToArray();

    private static bool HasAnySegment(string relativePath, params string[] segments)
    {
        var pathSegments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => pathSegments.Contains(segment, StringComparer.Ordinal));
    }

    /// <summary>从测试输出目录向上定位 backend 根，不依赖 CWD（照抄 ApprovalChainSeededIdentityContractTests）。</summary>
    private static string BackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "backend", "common", "Contracts");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(directory.FullName, "backend");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the backend directory from the test output directory.");
    }
}
