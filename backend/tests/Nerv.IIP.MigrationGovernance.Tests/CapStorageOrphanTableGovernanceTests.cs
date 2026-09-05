using System.Reflection;
using System.Runtime.Loader;
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
/// 存储用 <c>PostgreSqlDataStorage</c> 的默认 schema/表，即 <c>cap."published"</c> / <c>cap."received"</c> /
/// <c>cap."lock"</c>。netcorepal 的 PostgreSql 包在这条链上只提供 <c>PostgreSqlCapTransactionFactory</c>（事务），
/// 不提供存储。
///
/// 因此各服务 schema 下的这三张表**运行时零写入**：任何读它们的断言恒 0 行、永远通过，是一类现成的假绿源。
/// 本文件是防线，两个面缺一不可：
///   1. 模型面：把「非运行时写入面」登记成显式台账，新增/改名/漏项都红。
///   2. 源码面：禁止任何 <c>backend</c> 下的 C# 源码对这些表做行读取——裸 SQL 与强类型访问两种形态都扫。
///
/// 为什么不从 DbContext 上删掉那三个 <c>DbSet</c> 面：删不掉。<c>ICapDataStorage</c> 把
/// <c>PublishedMessages</c> / <c>ReceivedMessages</c> / <c>CapLocks</c> 写进了接口契约（手工删除会得到
/// CS0535），而实现 <c>IPostgreSqlCapDataStorage</c> 的另外 13 个服务由 netcorepal 的
/// <c>...CAP.SourceGenerators</c> 生成同样的公开成员。公开读面是供应商强制的、不在本仓源码里，
/// 因此防线只能落在「谁去读」这一侧，也就是下面的源码面。
///
/// 删除这些表的可行性评估（先要证明摘掉标记接口能编译）另开票，不在本票范围。
/// </summary>
public sealed class CapStorageOrphanTableGovernanceTests
{
    private const string CapDataStorageInterfaceFullName =
        "NetCorePal.Extensions.DistributedTransactions.CAP.Persistence.ICapDataStorage";

    private const string CapPersistenceNamespace =
        "NetCorePal.Extensions.DistributedTransactions.CAP.Persistence";

    /// <summary>
    /// 「声明了但运行时不写入」的 CAP 表台账，键为 Infrastructure 程序集名，值为 <c>schema.table</c> 集合。
    /// 与 <c>docs/reference/data/database-schema-catalog.md</c> 里对应行的标注互为对照。
    /// 集合两向比对（缺、多、改名都红），因此新服务接 CAP 时必须显式登记。
    /// </summary>
    private static readonly Dictionary<string, string[]> NonRuntimeWriteFaces = new(StringComparer.Ordinal)
    {
        ["Nerv.IIP.AppHub.Infrastructure"] =
            ["apphub.cap_locks", "apphub.cap_published_messages", "apphub.cap_received_messages"],
        ["Nerv.IIP.Business.Approval.Infrastructure"] =
        [
            "business_approval.cap_locks",
            "business_approval.cap_published_messages",
            "business_approval.cap_received_messages",
        ],
        ["Nerv.IIP.Business.BarcodeLabel.Infrastructure"] =
            ["barcode.CAPLock", "barcode.CAPPublishedMessage", "barcode.CAPReceivedMessage"],
        ["Nerv.IIP.Business.DemandPlanning.Infrastructure"] =
        [
            "demand_planning.cap_locks",
            "demand_planning.cap_published_messages",
            "demand_planning.cap_received_messages",
        ],
        ["Nerv.IIP.Business.Erp.Infrastructure"] =
            ["erp.cap_locks", "erp.cap_published_messages", "erp.cap_received_messages"],
        ["Nerv.IIP.Business.IndustrialTelemetry.Infrastructure"] =
        [
            "industrial_telemetry.CAPLock",
            "industrial_telemetry.CAPPublishedMessage",
            "industrial_telemetry.CAPReceivedMessage",
        ],
        ["Nerv.IIP.Business.Inventory.Infrastructure"] =
            ["inventory.cap_locks", "inventory.cap_published_messages", "inventory.cap_received_messages"],
        ["Nerv.IIP.Business.Maintenance.Infrastructure"] =
            ["maintenance.CAPLock", "maintenance.CAPPublishedMessage", "maintenance.CAPReceivedMessage"],
        ["Nerv.IIP.Business.MasterData.Infrastructure"] =
        [
            "business_masterdata.cap_locks",
            "business_masterdata.cap_published_messages",
            "business_masterdata.cap_received_messages",
        ],
        ["Nerv.IIP.Business.Mes.Infrastructure"] =
            ["mes.cap_locks", "mes.cap_published_messages", "mes.cap_received_messages"],
        ["Nerv.IIP.Business.ProductEngineering.Infrastructure"] =
        [
            "product_engineering.cap_locks",
            "product_engineering.cap_published_messages",
            "product_engineering.cap_received_messages",
        ],
        ["Nerv.IIP.Business.Quality.Infrastructure"] =
            ["quality.cap_locks", "quality.cap_published_messages", "quality.cap_received_messages"],
        ["Nerv.IIP.Business.Scheduling.Infrastructure"] =
            ["scheduling.cap_locks", "scheduling.cap_published_messages", "scheduling.cap_received_messages"],
        ["Nerv.IIP.Business.Wms.Infrastructure"] =
            ["wms.CAPLock", "wms.CAPPublishedMessage", "wms.CAPReceivedMessage"],
        ["Nerv.IIP.Notification.Infrastructure"] =
            ["notification.cap_locks", "notification.cap_published_messages", "notification.cap_received_messages"],
        ["Nerv.IIP.Ops.Infrastructure"] =
            ["ops.cap_locks", "ops.cap_published_messages", "ops.cap_received_messages"],
    };

    /// <summary>
    /// 台账里出现过的全部表名（去 schema），源码面扫描按这个集合展开——
    /// 两种命名（netcorepal 默认名与本仓重命名）都在里面，只扫一种会漏掉一半服务。
    /// </summary>
    private static readonly string[] NonRuntimeWriteTableNames = NonRuntimeWriteFaces
        .SelectMany(entry => entry.Value)
        .Select(qualified => qualified[(qualified.IndexOf('.', StringComparison.Ordinal) + 1)..])
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    [Fact]
    public void Cap_storage_tables_match_the_registered_non_runtime_write_faces()
    {
        var infrastructureAssemblies = Directory
            .EnumerateFiles(AppContext.BaseDirectory, "Nerv.IIP.*.Infrastructure.dll")
            .Select(LoadAssembly)
            .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .ToArray();

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
                failures.Add($"{assemblyName}: CAP storage model inspection failed: {exception.GetBaseException().Message}");
            }
        }

        foreach (var assemblyName in observed.Keys.Concat(NonRuntimeWriteFaces.Keys).Distinct(StringComparer.Ordinal)
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            var hasObserved = observed.TryGetValue(assemblyName, out var actual);
            var hasRegistered = NonRuntimeWriteFaces.TryGetValue(assemblyName, out var expected);

            if (!hasRegistered)
            {
                failures.Add(
                    $"{assemblyName}: maps CAP storage tables [{string.Join(", ", actual!)}] but is not registered "
                    + "as a non-runtime-write face. Register it here and annotate it in the Reference schema catalog.");
                continue;
            }

            if (!hasObserved)
            {
                failures.Add(
                    $"{assemblyName}: registered as a non-runtime-write face with [{string.Join(", ", expected!)}], "
                    + "but no CAP storage entity was found in its model. Update the ledger and the catalog together.");
                continue;
            }

            if (!actual!.SequenceEqual(expected!, StringComparer.Ordinal))
            {
                failures.Add(
                    $"{assemblyName}: CAP storage tables are [{string.Join(", ", actual!)}], "
                    + $"registered ledger says [{string.Join(", ", expected!)}].");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void No_backend_source_reads_rows_from_non_runtime_cap_tables()
    {
        var root = FindRepositoryRoot();
        var backend = Path.Combine(root, "backend");
        Assert.True(Directory.Exists(backend), $"Backend source root was not found at {backend}.");

        // 扫描面：backend 下全部 C# 源码，排除构建产物与 Migrations。
        // Migrations 是历史模型快照与建表脚本，本来就必须写出这些表名；活模型面由上一个 Fact 承担。
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

        var tableAlternatives = string.Join('|', NonRuntimeWriteTableNames.Select(Regex.Escape));

        // 裸 SQL：表名紧跟 FROM/JOIN/INTO/UPDATE，允许可选的 schema 限定与双引号。
        // 只认紧邻，注释里提到表名（例如说明「运行时不写入」）不会误报。
        var sqlRead = new Regex(
            $"\\b(?:FROM|JOIN|INTO|UPDATE)\\s+(?:\"?[A-Za-z0-9_]+\"?\\s*\\.\\s*)?\"?(?:{tableAlternatives})\"?\\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // 强类型：DbContext 泛型 Set 调用与 DbSet 属性访问两种形态；本文件的正则本身写成不自匹配，
        // 因此扫描面不需要给自己开豁免（开了就等于留一个可以往里塞读取的洞）。
        // 要求有接收者（context.Set<...>()）：DbContext 自身实现 ICapDataStorage 时的裸 Set<...>() 是声明
        // 惯用法，不是读取；从外部读一定带接收者。
        var typedSetRead = new Regex(
            "[\\w\\)\\]]\\s*\\.\\s*Set<\\s*(?:PublishedMessage|ReceivedMessage|CapLock)\\s*>\\s*\\(",
            RegexOptions.CultureInvariant);
        var typedMemberRead = new Regex(
            "\\.\\s*(?:PublishedMessages|ReceivedMessages|CapLocks)\\b",
            RegexOptions.CultureInvariant);

        var failures = new List<string>();

        foreach (var source in sources)
        {
            var text = File.ReadAllText(source);
            var relative = Path.GetRelativePath(root, source).Replace('\\', '/');

            foreach (var (probe, shape) in new[]
                     {
                         (sqlRead, "raw SQL read"),
                         (typedSetRead, "DbContext.Set<T>() read"),
                         (typedMemberRead, "DbSet property read"),
                     })
            {
                foreach (Match match in probe.Matches(text))
                {
                    failures.Add(
                        $"{relative}:{LineOf(text, match.Index)}: {shape} '{match.Value.Trim()}' targets a CAP table "
                        + "that is never written at runtime; the production outbox lives in cap.\"published\" / "
                        + "cap.\"received\" / cap.\"lock\". Reading it always yields zero rows.");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static bool ContainsSegment(string path, string segment) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => string.Equals(part, segment, StringComparison.Ordinal));

    private static int LineOf(string text, int index) =>
        text.AsSpan(0, index).Count('\n') + 1;

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
