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
/// </list>
/// </summary>
public sealed class VocabularyDriftGovernanceTests
{
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
            VocabularyDriftExemptions.Entries);

        Assert.True(
            result.Violations.Count == 0,
            $"发现 {result.Violations.Count} 条词表裸字面量违例（同义改常量引用，同值不同义登记白名单裁决）："
            + Environment.NewLine + string.Join(Environment.NewLine, result.Violations));
        Assert.True(
            result.StaleExemptions.Count == 0,
            "白名单存在未命中的过期豁免（违例修掉后必须同步删除豁免条目）：" + Environment.NewLine
            + string.Join(Environment.NewLine, result.StaleExemptions));
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
