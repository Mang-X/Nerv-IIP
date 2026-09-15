using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nerv.IIP.MigrationGovernance.Tests;

/// <summary>
/// #3124：netcorepal 的三张 CAP 存储表（<c>PublishedMessage</c> / <c>ReceivedMessage</c> / <c>CapLock</c>）
/// 由 DbContext 上的标记接口 <c>ICapDataStorage</c>（及其 PostgreSql 派生 <c>IPostgreSqlCapDataStorage</c>）
/// 带进 EF 模型，并随 InitialSchema 迁移在各服务 schema 里建出。
///
/// 但生产 outbox 走的是另一条链：各服务 <c>AddCap(x =&gt; x.UseEntityFramework&lt;ApplicationDbContext&gt;())</c>
/// 里的 <c>UseEntityFramework</c> 出自 <c>DotNetCore.CAP.PostgreSql</c>，它只从 DbContext 取连接串，
/// 存储用 <c>PostgreSqlDataStorage</c> 的默认 schema/表。netcorepal 的 PostgreSql 包在这条链上只提供
/// <c>PostgreSqlCapTransactionFactory</c>（事务），不提供存储。
///
/// 因此各服务 schema 下的这三张表**运行时零写入**：任何读它们的断言恒 0 行、永远通过，是一类现成的假绿源。
///
/// 为什么不从 DbContext 上删掉那三个 <c>DbSet</c> 面：删不掉。<c>ICapDataStorage</c> 把
/// <c>PublishedMessages</c> / <c>ReceivedMessages</c> / <c>CapLocks</c> 写进了接口契约（手工删除会得到
/// CS0535），而实现 <c>IPostgreSqlCapDataStorage</c> 的另外 13 个服务由 netcorepal 的
/// <c>...CAP.SourceGenerators</c> 生成同样的公开成员。公开读面是供应商强制的、不在本仓源码里，
/// 因此防线只能落在「谁去引用这个表名」这一侧。
///
/// 防线三个面：
///   1. 模型面：活模型里的 CAP 表与台账两向比对。
///   2. 源码面：表名不得以字符串字面量出现在 <c>backend</c> 的 C# 源码里（见下方「为什么反着写」）。
///   3. 目录面：Reference schema catalog 与台账机器校验一致，避免文档静默漂移。
///
/// 台账住在 <c>cap-non-runtime-write-faces.json</c> 而不是本文件里，是刻意的：表名一旦作为 C# 字面量
/// 写进防线自身，防线就必须给自己开豁免，而豁免就是一个可以往里塞读取的洞。
///
/// 删除这些表的可行性评估（先要证明摘掉标记接口能编译）另开票，不在本票范围。
/// </summary>
public sealed class CapStorageOrphanTableGovernanceTests
{
    private const string CapDataStorageInterfaceFullName =
        "NetCorePal.Extensions.DistributedTransactions.CAP.Persistence.ICapDataStorage";

    private const string CapPersistenceNamespace =
        "NetCorePal.Extensions.DistributedTransactions.CAP.Persistence";

    private const string LedgerFileName = "cap-non-runtime-write-faces.json";

    private const string LedgerRelativePath =
        "backend/tests/Nerv.IIP.MigrationGovernance.Tests/" + LedgerFileName;

    private const string CatalogRelativePath = "docs/reference/data/database-schema-catalog.md";

    /// <summary>Reference 目录里每一行 CAP 表都必须带的标注，缺了就说明文档漂回「由 CAP 维护」的错误口径。</summary>
    private const string CatalogNonRuntimeWriteMarker = "运行时零写入";

    /// <summary>
    /// 允许出现表名字面量的非查询 API。这份名单是**闭集**，而且漏登记的后果是红不是绿——
    /// 这正是本规则相对「枚举读取形状」的关键差别：枚举读取形状时漏一种形状 = 静默放行，
    /// 反过来枚举非查询 API 时漏一个 API = 报错，需要有人来这里显式加并接受审核。
    ///
    /// <c>ToTable</c> 是这些表名的唯一生产者（各服务 <c>ApplicationDbContext</c> 的映射声明）；
    /// <c>AssertTable</c> / <c>AssertCreateTable</c> 是 Notification 对映射与迁移建表的元数据断言，
    /// 都不执行查询，不可能产生恒 0 行的假绿。
    /// </summary>
    private static readonly string[] NonQueryDeclarationApis = ["ToTable", "AssertTable", "AssertCreateTable"];

    private static readonly Lazy<IReadOnlyDictionary<string, string[]>> Ledger = new(LoadLedger);

    /// <summary>台账里出现过的全部表名（去 schema）。两种命名都在里面，只扫一种会漏掉一半服务。</summary>
    private static IReadOnlyList<string> TableNames => Ledger.Value.Values
        .SelectMany(faces => faces)
        .Select(qualified => qualified[(qualified.IndexOf('.', StringComparison.Ordinal) + 1)..])
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    [Fact]
    public void Cap_storage_tables_match_the_registered_non_runtime_write_faces()
    {
        var ledger = Ledger.Value;

        var infrastructureAssemblies = Directory
            .EnumerateFiles(AppContext.BaseDirectory, "Nerv.IIP.*.Infrastructure.dll")
            .Select(LoadAssembly)
            .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .ToArray();

        // 下界是 1：这个 Fact 的强度不靠文件数，靠台账的 16 条两向比对——少加载一个程序集会表现为
        // 「登记了但模型里没有」而变红。与源码面的 1500 下界不对称是有意的。
        Assert.NotEmpty(infrastructureAssemblies);

        var failures = new List<string>();
        var observed = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var assembly in infrastructureAssemblies)
        {
            var assemblyName = assembly.GetName().Name!;
            var factory = assembly.GetTypes()
                .Select(type => new
                {
                    FactoryType = type,
                    InterfaceType = type.GetInterfaces().SingleOrDefault(
                        candidate => candidate.IsGenericType
                            && candidate.GetGenericTypeDefinition() == typeof(IDesignTimeDbContextFactory<>)),
                })
                .SingleOrDefault(candidate => candidate.InterfaceType is not null);

            if (factory is null)
            {
                failures.Add($"{assemblyName}: no IDesignTimeDbContextFactory found; CAP storage face is unknown.");
                continue;
            }

            var dbContextType = factory.InterfaceType!.GetGenericArguments()[0];
            var implementsCapDataStorage = dbContextType.GetInterfaces()
                .Any(candidate => string.Equals(
                    candidate.FullName,
                    CapDataStorageInterfaceFullName,
                    StringComparison.Ordinal));

            if (!implementsCapDataStorage)
            {
                continue;
            }

            try
            {
                var factoryInstance = Activator.CreateInstance(factory.FactoryType);
                var createDbContext = factory.InterfaceType.GetMethod(
                    nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext));
                using var dbContext = Assert.IsAssignableFrom<DbContext>(
                    createDbContext!.Invoke(factoryInstance, [Array.Empty<string>()]));

                observed[assemblyName] = dbContext.Model.GetEntityTypes()
                    .Where(entity => string.Equals(
                        entity.ClrType.Namespace,
                        CapPersistenceNamespace,
                        StringComparison.Ordinal))
                    .Select(entity => $"{entity.GetSchema()}.{entity.GetTableName()}")
                    .OrderBy(qualified => qualified, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception exception)
            {
                failures.Add(
                    $"{assemblyName}: CAP storage model inspection failed: {exception.GetBaseException().Message}");
            }
        }

        foreach (var assemblyName in observed.Keys.Concat(ledger.Keys).Distinct(StringComparer.Ordinal)
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            var hasObserved = observed.TryGetValue(assemblyName, out var actual);
            var hasRegistered = ledger.TryGetValue(assemblyName, out var expected);

            if (!hasRegistered)
            {
                failures.Add(
                    $"{assemblyName}: maps CAP storage tables [{string.Join(", ", actual!)}] but is not registered "
                    + $"in {LedgerRelativePath}. Register it there and annotate it in {CatalogRelativePath}.");
                continue;
            }

            if (!hasObserved)
            {
                failures.Add(
                    $"{assemblyName}: registered in {LedgerRelativePath} with [{string.Join(", ", expected!)}], "
                    + "but no CAP storage entity was found in its model. Update the ledger and the catalog together.");
                continue;
            }

            if (!actual!.SequenceEqual(expected!, StringComparer.Ordinal))
            {
                failures.Add(
                    $"{assemblyName}: CAP storage tables are [{string.Join(", ", actual!)}], "
                    + $"{LedgerRelativePath} says [{string.Join(", ", expected!)}].");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void No_backend_source_names_a_non_runtime_cap_table_in_a_string_literal()
    {
        var root = FindRepositoryRoot();
        var backend = Path.Combine(root, "backend");
        Assert.True(Directory.Exists(backend), $"Backend source root was not found at {backend}.");

        // 扫描面：backend 下全部 C# 源码，排除构建产物与 Migrations。
        // Migrations 是历史模型快照与建表脚本，本来就必须写出这些表名；活模型面由第一个 Fact 承担。
        var sources = Directory
            .EnumerateFiles(backend, "*.cs", SearchOption.AllDirectories)
            .Where(path => !ContainsSegment(path, "obj")
                && !ContainsSegment(path, "bin")
                && !ContainsSegment(path, "Migrations"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        // 阴性对照没有意义除非先证明扫描面非空：glob 打偏时这个 Fact 会静默全绿。
        Assert.True(
            sources.Length >= 1500,
            $"Expected the backend C# scan face to cover at least 1500 files, found {sources.Length} under {backend}.");

        var tableNames = TableNames;
        var contract = ResolveCapDataStorageContract(Directory
            .EnumerateFiles(AppContext.BaseDirectory, "Nerv.IIP.*.Infrastructure.dll")
            .Select(LoadAssembly));
        var dbSetPropertyNames = contract.Select(property => property.Name).ToArray();
        var capEntityTypeNames = contract
            .Select(property => property.PropertyType.GetGenericArguments()[0].Name)
            .ToArray();
        var failures = new List<string>();

        foreach (var source in sources)
        {
            var relative = Path.GetRelativePath(root, source).Replace('\\', '/');
            var findings = CapStorageSourceProbe.Analyze(
                relative,
                File.ReadAllText(source),
                tableNames,
                dbSetPropertyNames,
                capEntityTypeNames,
                NonQueryDeclarationApis);

            foreach (var finding in findings)
            {
                failures.Add(
                    $"{relative}:{finding.Line}: {finding.Shape} '{finding.Detail}' reaches a CAP table that is "
                    + "never written at runtime; the production outbox/inbox/lock live in the cap schema, so any "
                    + "query against it is always zero rows. "
                    + $"Only these non-query APIs may name it: {string.Join(", ", NonQueryDeclarationApis)} "
                    + $"(registered in {nameof(NonQueryDeclarationApis)} in "
                    + "backend/tests/Nerv.IIP.MigrationGovernance.Tests/CapStorageOrphanTableGovernanceTests.cs; "
                    + "adding one there is a reviewable act).");
            }
        }

        Assert.True(
            failures.Count == 0,
            string.Join(Environment.NewLine, failures)
            + Environment.NewLine
            + CoverageBoundaryNotice);
    }

    [Fact]
    public void Reference_schema_catalog_matches_the_registered_non_runtime_write_faces()
    {
        var root = FindRepositoryRoot();
        var catalog = Path.Combine(root, CatalogRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(catalog), $"Reference schema catalog was not found at {catalog}.");

        var lines = File.ReadAllLines(catalog);
        var expectedCounts = Ledger.Value.Values
            .SelectMany(faces => faces)
            .Select(qualified => qualified[(qualified.IndexOf('.', StringComparison.Ordinal) + 1)..])
            .GroupBy(name => name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var failures = new List<string>();

        foreach (var (tableName, expectedCount) in expectedCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var rows = lines
                .Select((line, index) => (Line: line, Number: index + 1))
                .Where(row => row.Line.StartsWith($"| `{tableName}`", StringComparison.Ordinal))
                .ToArray();

            if (rows.Length != expectedCount)
            {
                failures.Add(
                    $"{CatalogRelativePath}: found {rows.Length} row(s) for '{tableName}', "
                    + $"{LedgerRelativePath} registers {expectedCount}.");
            }

            foreach (var row in rows.Where(
                         row => !row.Line.Contains(CatalogNonRuntimeWriteMarker, StringComparison.Ordinal)))
            {
                failures.Add(
                    $"{CatalogRelativePath}:{row.Number}: the row for '{tableName}' does not carry the "
                    + $"'{CatalogNonRuntimeWriteMarker}' annotation; the catalog would claim it is a live CAP face.");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// 强类型访问面的词表：<c>db.PublishedMessages</c> 与 <c>db.Set&lt;PublishedMessage&gt;()</c> 里根本没有
    /// 表名字面量，字面量规则抓不到，必须按 CLR 名字判。名字**从 <c>ICapDataStorage</c> 的接口成员反射穷举**，
    /// 不手打——手打会让本文件自己出现表名字面量（实体名大小写不敏感地就等于其中一张表的名字），
    /// 进而逼防线给自己开豁免。
    ///
    /// 两条已知局限（有意接受，写在这里以免被当成完备）：
    ///   1. <c>Type.GetProperties()</c> 对接口**不返回继承来的成员**。若 netcorepal 把新的 DbSet 加在派生接口
    ///      <c>IPostgreSqlCapDataStorage</c> 上，这里拿不到；<c>Assert.Equal(3, ...)</c> 只保证「基接口自己
    ///      增删成员即红」。
    ///   2. 只覆盖属性形态。若供应商改成方法（例如 <c>GetPublishedMessages()</c>），这里同样拿不到。
    /// 两种情况都属于升级 netcorepal 时需要人工复核的范围，由第一个 Fact 的台账兜底。
    /// </summary>
    private static IReadOnlyList<PropertyInfo> ResolveCapDataStorageContract(IEnumerable<Assembly> assemblies)
    {
        var contract = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetInterfaces())
            .FirstOrDefault(candidate => string.Equals(
                candidate.FullName,
                CapDataStorageInterfaceFullName,
                StringComparison.Ordinal));

        Assert.True(
            contract is not null,
            $"{CapDataStorageInterfaceFullName} was not found in the loaded Infrastructure assemblies; "
            + "the typed-access face would silently cover nothing.");

        var properties = contract!.GetProperties();
        Assert.Equal(3, properties.Length);
        return properties;
    }

    /// <summary>
    /// 明说覆盖边界，避免后人把这个门禁当完备。措辞与实现逐条对齐过：声称覆盖的都跑过变异，
    /// 声称放弃的都确认真的放弃。
    ///
    /// **已覆盖**：Roslyn 语法树里的字符串字面量（普通、逐字 <c>@""</c>、raw <c>"""</c>、UTF-8、
    /// 转义序列已按 <c>ValueText</c> 还原）、插值字符串的文本段、以及插值洞内的嵌套字面量；
    /// 带接收者的 <c>DbSet</c> 属性访问与泛型 <c>Set</c> 调用（类型实参取最右标识符，**全限定写法也命中**）。
    /// 在 <c>backend</c> 内用字面量定义常量同样命中——常量的**定义点**是字面量。
    ///
    /// **明确放弃**：跨字符串拼接出表名；引用在 <c>backend</c> 之外定义的常量或嵌入资源；
    /// <c>using</c> 类型别名把实体重命名后再用；反射按名字取 <c>DbSet</c>；<c>backend</c> 之外的源码。
    /// 这些放弃项由第一个 Fact 的台账与人工审核兜底。
    /// </summary>
    private const string CoverageBoundaryNotice =
        "Coverage boundary: parsed with Roslyn, this probe covers (a) string literals — regular, verbatim, raw, "
        + "UTF-8, with escape sequences resolved — plus interpolated-string text segments and literals nested "
        + "inside interpolation holes, and (b) DbSet property access / generic Set<T>() calls with a receiver, "
        + "including fully qualified type arguments. Defining a constant from such a literal inside backend is "
        + "covered too. It deliberately does NOT cover names assembled by string concatenation, constants or "
        + "embedded resources defined outside backend, using-alias renames, reflection-by-name, or sources "
        + "outside backend/**/*.cs (obj, bin and Migrations are excluded). Both faces share one syntax tree, so "
        + "they are complementary judgements over a common parse, not two independent defences. Do not treat a "
        + "green result as proof that nothing reads these tables.";

    private static IReadOnlyDictionary<string, string[]> LoadLedger()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, LedgerRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"CAP non-runtime-write ledger was not found at {path}.");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var faces = document.RootElement.GetProperty("faces");
        var ledger = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var entry in faces.EnumerateObject())
        {
            ledger[entry.Name] = entry.Value
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        Assert.NotEmpty(ledger);
        return ledger;
    }

    private static bool ContainsSegment(string path, string segment) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => string.Equals(part, segment, StringComparison.Ordinal));

    private static Assembly LoadAssembly(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return AssemblyLoadContext.Default.Assemblies.FirstOrDefault(
                   assembly => string.Equals(assembly.Location, fullPath, StringComparison.OrdinalIgnoreCase))
               ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
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
