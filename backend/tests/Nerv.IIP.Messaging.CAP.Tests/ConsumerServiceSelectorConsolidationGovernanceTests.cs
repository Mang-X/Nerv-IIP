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
/// 允许形态是**闭集**：只有「从容器里取出来读」这两种消费形态被允许出现在共享入口之外。
/// 漏登记一种合法形态的后果是红（有人来这里显式加并接受审核），不是静默放行 —— 这与
/// 「枚举违规形状」相反，后者漏一种就是假绿。
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
            $"Expected the backend C# scan face to cover at least {ledger.MinimumScannedFiles} files, "
            + $"found {sources.Length}. A glob that misses the tree would make this gate silently green.");

        var sharedEntries = ledger.SharedEntryFiles
            .Select(relative => Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var sharedEntry in sharedEntries)
        {
            Assert.True(File.Exists(sharedEntry), $"Shared entry file was not found at {sharedEntry}.");
        }

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
    /// 允许形态是闭集，闭集本身必须真的把该标识符盖住 —— 否则 <c>allowedConsumptionForms</c>
    /// 写错一个字符就会把「消费形态」误判成「注册形态」（红，可发现），或者更糟：
    /// 写成一个空串 / 过宽的串把所有注册都掩盖掉（绿，不可发现）。这条钉住后者。
    /// </summary>
    [Fact]
    public void Allowed_consumption_forms_cannot_mask_a_registration()
    {
        var ledger = Ledger.Value;

        Assert.NotEmpty(ledger.AllowedConsumptionForms);
        Assert.All(ledger.AllowedConsumptionForms, form =>
            Assert.Contains(ledger.InterfaceName, form, StringComparison.Ordinal));

        var registrationLike =
            $"services.Replace(ServiceDescriptor.Singleton<{ledger.InterfaceName}, Whatever>());";
        var factoryLike =
            $"services.Replace(ServiceDescriptor.Singleton<{ledger.InterfaceName}>(sp => new Whatever(sp)));";
        var consumptionLike =
            $"var selector = provider.GetRequiredService<{ledger.InterfaceName}>();";

        Assert.Equal(1, CountRegistrationOccurrences(registrationLike, ledger));
        Assert.Equal(1, CountRegistrationOccurrences(factoryLike, ledger));
        Assert.Equal(0, CountRegistrationOccurrences(consumptionLike, ledger));
    }

    private static int CountRegistrationOccurrences(string line, GovernanceLedger ledger)
    {
        var stripped = line;
        foreach (var form in ledger.AllowedConsumptionForms)
        {
            stripped = stripped.Replace(form, string.Empty, StringComparison.Ordinal);
        }

        var count = 0;
        var offset = 0;
        while (true)
        {
            var found = stripped.IndexOf(ledger.InterfaceName, offset, StringComparison.Ordinal);
            if (found < 0)
            {
                return count;
            }

            count++;
            offset = found + ledger.InterfaceName.Length;
        }
    }

    private static Regex BaseTypeRegex(GovernanceLedger ledger) => new(
        $@":\s*(?:[\w.]+\.)?(?<![\w]){Regex.Escape(ledger.BaseClassName)}\b",
        RegexOptions.CultureInvariant);

    private static string[] ScanFace(string root)
    {
        var backend = Path.Combine(root, "backend");
        Assert.True(Directory.Exists(backend), $"Backend source root was not found at {backend}.");

        return Directory
            .EnumerateFiles(backend, "*.cs", SearchOption.AllDirectories)
            .Where(path => !ContainsSegment(path, "obj") && !ContainsSegment(path, "bin"))
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
