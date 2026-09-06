using Xunit;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nerv.IIP.Messaging.CAP.Tests;

/// <summary>
/// #3122：CAP consumer selector 的替换实现全仓只允许有一份，住在共享基础设施里。
///
/// 判据是**身份**不是名字：任何位置只要把 CAP 的 consumer selector 接口注册/替换进 DI，
/// 或者派生 CAP 的 selector 基类，就必须在台账登记的共享入口文件里。改类名、改注册形态
/// （类型注册 → 工厂 lambda）、换目录都绕不过这条 —— 本票收敛掉的第三份副本正是靠这三招
/// 从「按类名 grep」里消失的。
///
/// 允许形态是**闭集**：只有「从容器里取出来读」这几种消费形态被允许出现在共享入口之外。
/// 漏登记一种合法形态的后果是红（有人来这里显式加并接受审核），不是静默放行 —— 这与
/// 「枚举违规形状」相反，后者漏一种就是假绿。闭集本身的强度声明见
/// <see cref="Allowed_consumption_forms_are_structurally_read_only"/>，那里明写了它覆盖什么、不覆盖什么。
///
/// 被扫描的标识符住在 <c>consumer-service-selector-governance.json</c>：守卫源码里不写这个
/// 标识符，就不需要给自己开豁免，也就没有那条可以往里塞注册的自指的洞。
/// </summary>
public sealed class ConsumerServiceSelectorConsolidationGovernanceTests
{
    private const string LedgerRelativePath =
        "backend/tests/Nerv.IIP.Messaging.CAP.Tests/consumer-service-selector-governance.json";

    private sealed record GovernanceLedger(
        string InterfaceName,
        string BaseClassName,
        string[] SharedEntryFiles,
        string[] AllowedConsumptionForms,
        int MinimumScannedFiles);

    private static readonly Lazy<GovernanceLedger> Ledger = new(LoadLedger);

    [Fact]
    public void Only_the_shared_entry_may_register_the_cap_consumer_selector()
    {
        var ledger = Ledger.Value;
        var root = FindRepositoryRoot();
        var sources = ScanFace(root);

        Assert.True(
            sources.Length >= ledger.MinimumScannedFiles,
            $"Expected the repository-wide C# scan face to cover at least {ledger.MinimumScannedFiles} files, "
            + $"found {sources.Length}. A glob that misses the tree would make this gate silently green.");

        // 票面验收条件第一句是「全仓该接口的替换实现只有一份」。
        // 没有这条 Assert.Single，sharedEntryFiles 就是一个无上界白名单：
        // 把一份完整本地副本的路径追加进台账即可全绿（#3122 复审实测）。
        // 「一」必须在门禁里被表达出来，而不是靠台账写的人自觉。
        var sharedEntryRelative = Assert.Single(ledger.SharedEntryFiles);
        var sharedEntry = Path.GetFullPath(
            Path.Combine(root, sharedEntryRelative.Replace('/', Path.DirectorySeparatorChar)));
        Assert.True(File.Exists(sharedEntry), $"Shared entry file was not found at {sharedEntry}.");
        var sharedEntries = new HashSet<string>(StringComparer.Ordinal) { sharedEntry };

        var violations = new List<string>();

        foreach (var path in sources)
        {
            if (sharedEntries.Contains(path))
            {
                continue;
            }

            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                if (CountRegistrationOccurrences(lines[index], ledger) > 0)
                {
                    violations.Add($"{Relative(root, path)}:{index + 1}: {lines[index].Trim()}");
                }
            }

            var derivations = BaseTypeRegex(ledger).Matches(File.ReadAllText(path));
            if (derivations.Count > 0)
            {
                violations.Add(
                    $"{Relative(root, path)}: derives from the CAP consumer selector base type "
                    + $"({derivations.Count} declaration(s)).");
            }
        }

        Assert.True(
            violations.Count == 0,
            "CAP consumer selector replacements must live in the shared messaging entry only (#3122). "
            + "Offending locations:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations.OrderBy(x => x, StringComparer.Ordinal)));
    }

    /// <summary>
    /// 这条 Fact 让上面那条不可能因为「共享入口被搬走 / 被删空」而空转成绿：
    /// 台账里的共享入口必须真的既注册了那个接口，又派生了那个基类。
    /// </summary>
    [Fact]
    public void Shared_entry_actually_registers_and_derives_the_cap_consumer_selector()
    {
        var ledger = Ledger.Value;
        var root = FindRepositoryRoot();

        foreach (var relative in ledger.SharedEntryFiles)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            var text = File.ReadAllText(path);

            var registrations = text
                .Split('\n')
                .Sum(line => CountRegistrationOccurrences(line, ledger));

            Assert.True(
                registrations > 0,
                $"{relative} no longer registers the CAP consumer selector; the consolidation gate would be vacuous.");
            Assert.True(
                BaseTypeRegex(ledger).IsMatch(text),
                $"{relative} no longer derives from the CAP consumer selector base type.");
        }
    }

    /// <summary>
    /// 允许形态闭集是 <see cref="CountRegistrationOccurrences"/> 的**剥离**输入：写进去的每条串
    /// 都会先从行里被删掉，再去找接口标识符。因此一条「过宽」的 form 能把真实注册整段剥掉，
    /// 表现为绿——不可发现。
    ///
    /// **强度声明（本条覆盖什么、不覆盖什么）**：
    ///   * 覆盖：闭集的**结构**——每条 form 必须是取值动作（以 <c>Get</c> 开头）、必须恰好含一次
    ///     接口标识符、且不得含任何 DI 注册记号（见 <see cref="RegistrationMarkers"/>）。
    ///     这三条一起排除掉「以注册动词开头 / 中途夹带注册片段 / 一条吃掉两次出现」这三类过宽写法。
    ///     #3122 首轮复审用的绕法是往闭集里追加一条「以 <c>AddSingleton&lt;</c> 开头、后接接口标识符」的串，
    ///     它被第一条（不以 <c>Get</c> 开头）和第三条（含注册记号）各挡一次。
    ///     注意本文件通篇不写那个接口标识符的字面量——写了就会被上面那条 Fact 判成违规注册，
    ///     而给自己开豁免就是重新打开那个自指的洞。
    ///   * **不覆盖**：任意可能的过宽写法。<c>RegistrationMarkers</c> 是枚举，枚举天然可能漏。
    ///     它只是在结构约束之外多加一道，不构成完备性声明——本仓的教训是「护栏自称完备比有洞更坏」，
    ///     所以这里明说它不完备，请不要因为这条 Fact 存在就停止怀疑闭集。
    ///
    /// 下面三条构造串是**例子不是判据**：判据是上面的结构约束，探针只演示两类注册写法确实还能被数到。
    /// </summary>
    [Fact]
    public void Allowed_consumption_forms_are_structurally_read_only()
    {
        var ledger = Ledger.Value;

        Assert.NotEmpty(ledger.AllowedConsumptionForms);

        Assert.All(ledger.AllowedConsumptionForms, form =>
        {
            // 结构约束 1：取值动作。以注册动词开头的 form 一律不合法。
            Assert.StartsWith("Get", form, StringComparison.Ordinal);

            // 结构约束 2：恰好含一次接口标识符，避免一次剥离吃掉两次出现。
            Assert.Equal(1, CountOccurrences(form, ledger.InterfaceName));

            // 结构约束 3：不得夹带任何 DI 注册记号（非完备枚举，见上）。
            foreach (var marker in RegistrationMarkers)
            {
                Assert.DoesNotContain(marker, form, StringComparison.Ordinal);
            }
        });

        var registrationLike =
            $"services.Replace(ServiceDescriptor.Singleton<{ledger.InterfaceName}, Whatever>());";
        var factoryLike =
            $"services.Replace(ServiceDescriptor.Singleton<{ledger.InterfaceName}>(sp => new Whatever(sp)));";
        var addSingletonLike =
            $"services.AddSingleton<{ledger.InterfaceName}, Whatever>();";
        var consumptionLike =
            $"var selector = provider.GetRequiredService<{ledger.InterfaceName}>();";

        Assert.Equal(1, CountRegistrationOccurrences(registrationLike, ledger));
        Assert.Equal(1, CountRegistrationOccurrences(factoryLike, ledger));
        Assert.Equal(1, CountRegistrationOccurrences(addSingletonLike, ledger));
        Assert.Equal(0, CountRegistrationOccurrences(consumptionLike, ledger));
    }

    /// <summary>
    /// DI 注册记号。**这是枚举，不是闭集**：漏一个就少一道，所以它只作为结构约束 1 / 3 之外的补充，
    /// 承重的是「必须以 Get 开头」这条结构约束。
    /// </summary>
    private static readonly string[] RegistrationMarkers =
    [
        "Add", "TryAdd", "Replace", "Describe", "ServiceDescriptor",
        "Singleton", "Scoped", "Transient",
    ];

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var offset = 0;
        while (true)
        {
            var found = text.IndexOf(needle, offset, StringComparison.Ordinal);
            if (found < 0)
            {
                return count;
            }

            count++;
            offset = found + needle.Length;
        }
    }

    private static int CountRegistrationOccurrences(string line, GovernanceLedger ledger)
    {
        var stripped = line;
        foreach (var form in ledger.AllowedConsumptionForms)
        {
            stripped = stripped.Replace(form, string.Empty, StringComparison.Ordinal);
        }

        return CountOccurrences(stripped, ledger.InterfaceName);
    }

    private static Regex BaseTypeRegex(GovernanceLedger ledger) => new(
        $@":\s*(?:[\w.]+\.)?(?<![\w]){Regex.Escape(ledger.BaseClassName)}\b",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// 扫描面 = **仓库根**下全部 C# 源码，不是 <c>backend/</c>。
    ///
    /// 票面验收条件说的是「全仓」；扫 <c>backend/</c> 就与验收条件的面不一致，
    /// 把完整副本放进 <c>connector-hosts/</c> 即可绕过（#3122 复审实测：3/3 全绿）。
    /// 排除集只有构建产物与依赖目录，不含任何按内容/用途的豁免——豁免就是洞。
    /// </summary>
    private static readonly string[] ExcludedPathSegments = ["obj", "bin", "node_modules"];

    private static string[] ScanFace(string root)
    {
        Assert.True(Directory.Exists(root), $"Repository root was not found at {root}.");

        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !ExcludedPathSegments.Any(segment => ContainsSegment(path, segment)))
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ContainsSegment(string path, string segment) => path
        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(part => string.Equals(part, segment, StringComparison.Ordinal));

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static GovernanceLedger LoadLedger()
    {
        var path = Path.Combine(FindRepositoryRoot(), LedgerRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Governance ledger was not found at {path}.");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var rootElement = document.RootElement;

        return new GovernanceLedger(
            rootElement.GetProperty("interfaceName").GetString()!,
            rootElement.GetProperty("baseClassName").GetString()!,
            [.. rootElement.GetProperty("sharedEntryFiles").EnumerateArray().Select(x => x.GetString()!)],
            [.. rootElement.GetProperty("allowedConsumptionForms").EnumerateArray().Select(x => x.GetString()!)],
            rootElement.GetProperty("minimumScannedFiles").GetInt32());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
