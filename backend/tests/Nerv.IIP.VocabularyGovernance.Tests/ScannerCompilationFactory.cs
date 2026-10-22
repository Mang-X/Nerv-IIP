using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Nerv.IIP.VocabularyGovernance.Tests;

/// <summary>
/// 为扫描器造 <see cref="CSharpCompilation"/>。三件本仓此前没有先例的事都在这里：
/// <list type="number">
/// <item><b>加载并运行源生成器</b>：强类型 Id（<c>IStringStronglyTypedId</c> 等）的 <c>Id</c> 成员是
/// netcorepal 源生成器产的，源码里没有；不跑生成器则该成员在语义模型里根本不存在。
/// 生成器清单来自 MSBuild 导出的 <c>nerv-source-generators.txt</c>（见 csproj 的
/// ExportSourceGeneratorsForScanner 目标），⛔ 不写死包名/版本/NuGet 缓存路径。</item>
/// <item><b>合成 global usings 语法树</b>：SDK 隐式 using 不会自动进入手工构造的编译，
/// 缺了会多出数万条 CS0246/CS0103。</item>
/// <item><b>metadata 引用取宿主自己的 TRUSTED_PLATFORM_ASSEMBLIES</b>：宿主 csproj 因此必须持有
/// 「backend 全部非测试项目 PackageReference 的并集」（漏包 ⇒ 目标类型解析不出来 ⇒ 记进 unresolved ⇒ 报红）。</item>
/// </list>
/// </summary>
public static class ScannerCompilationFactory
{
    public const string SourceGeneratorManifestFileName = "nerv-source-generators.txt";

    private static readonly CSharpParseOptions ParseOptions =
        new CSharpParseOptions(LanguageVersion.Latest);

    private static readonly Lazy<ImmutableArray<MetadataReference>> HostReferences =
        new(CreateHostMetadataReferences);

    private static readonly Lazy<GeneratorCatalog> Generators = new(LoadGeneratorCatalog);

    /// <summary>SDK 隐式 using（<c>Microsoft.NET.Sdk</c>）。</summary>
    private static readonly string[] BaseImplicitUsings =
    [
        "System",
        "System.Collections.Generic",
        "System.IO",
        "System.Linq",
        "System.Net.Http",
        "System.Threading",
        "System.Threading.Tasks",
    ];

    /// <summary>SDK 隐式 using（<c>Microsoft.NET.Sdk.Web</c> 在上面基础上追加）。</summary>
    private static readonly string[] WebImplicitUsings =
    [
        "System.Net.Http.Json",
        "Microsoft.AspNetCore.Builder",
        "Microsoft.AspNetCore.Hosting",
        "Microsoft.AspNetCore.Http",
        "Microsoft.AspNetCore.Routing",
        "Microsoft.Extensions.Configuration",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.Logging",
    ];

    public sealed record GeneratorCatalog(
        ImmutableArray<ISourceGenerator> Generators,
        IReadOnlyList<string> AnalyzerAssemblies,
        IReadOnlyList<string> LoadFailures);

    public static GeneratorCatalog SourceGenerators => Generators.Value;

    public static IReadOnlyList<MetadataReference> MetadataReferences => HostReferences.Value;

    /// <summary>造一次编译。<paramref name="runGenerators"/> 为 false 是哨兵用法：证明探针不是恒真。</summary>
    public static CSharpCompilation Create(
        string assemblyName,
        IReadOnlyCollection<SourceDocument> documents,
        bool web,
        bool runGenerators = true)
    {
        var syntaxTrees = documents
            .Select(Parse)
            .ToList();
        syntaxTrees.Insert(0, CSharpSyntaxTree.ParseText(ImplicitUsingsSource(web), ParseOptions, path: "__ImplicitUsings.g.cs"));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            HostReferences.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: true));

        if (!runGenerators)
        {
            return compilation;
        }

        var catalog = Generators.Value;
        var driver = CSharpGeneratorDriver.Create(
            catalog.Generators,
            parseOptions: ParseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        return (CSharpCompilation)updated;
    }

    /// <summary>
    /// 语法树按 (路径, 文本) 缓存复用：25 次编译里 <c>common/*</c> 的源码会被反复用到，
    /// SyntaxTree 不可变且解析选项相同，复用不改变任何读数，只省解析时间。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Path, string Text), SyntaxTree> ParsedTrees = new();

    private static SyntaxTree Parse(SourceDocument document) =>
        ParsedTrees.GetOrAdd(
            (document.Path, document.Text),
            key => CSharpSyntaxTree.ParseText(key.Text, ParseOptions, path: key.Path));

    private static string ImplicitUsingsSource(bool web)
    {
        var namespaces = web ? BaseImplicitUsings.Concat(WebImplicitUsings) : BaseImplicitUsings;
        return string.Join(Environment.NewLine, namespaces.Select(name => $"global using global::{name};"));
    }

    private static ImmutableArray<MetadataReference> CreateHostMetadataReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var assemblyPaths = trustedPlatformAssemblies?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? [typeof(object).Assembly.Location];

        return assemblyPaths
            .Where(File.Exists)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }

    private static GeneratorCatalog LoadGeneratorCatalog()
    {
        var manifest = Path.Combine(AppContext.BaseDirectory, SourceGeneratorManifestFileName);
        if (!File.Exists(manifest))
        {
            throw new FileNotFoundException(
                $"源生成器清单 {SourceGeneratorManifestFileName} 不存在（应由 csproj 的 "
                + "ExportSourceGeneratorsForScanner 目标写出）。没有生成器就判不出强类型 Id 的 Id 成员。",
                manifest);
        }

        var assemblies = File
            .ReadAllLines(manifest)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && File.Exists(line))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToArray();

        var failures = new List<string>();
        var generators = ImmutableArray.CreateBuilder<ISourceGenerator>();
        foreach (var assembly in assemblies)
        {
            AnalyzerAssemblyLoader.Instance.AddDependencyLocation(assembly);
            var reference = new AnalyzerFileReference(assembly, AnalyzerAssemblyLoader.Instance);
            reference.AnalyzerLoadFailed += (_, args) =>
                failures.Add($"{assembly}: {args.ErrorCode} {args.Message}");
            generators.AddRange(reference.GetGenerators(LanguageNames.CSharp));
        }

        var catalog = new GeneratorCatalog(generators.ToImmutable(), assemblies, failures);
        if (catalog.Generators.Length == 0)
        {
            throw new InvalidOperationException(
                $"{SourceGeneratorManifestFileName} 里 {assemblies.Length} 个 analyzer 程序集一个源生成器都没有。"
                + "强类型 Id 的 Id 成员由 netcorepal 源生成器产出，拿不到生成器就判不出任何位点 —— "
                + "这里直接抛，⛔ 不允许退化成「扫到 0 处违例」的假绿。");
        }

        return catalog;
    }

    /// <summary>
    /// 生成器程序集加载器。<see cref="AddDependencyLocation"/> 登记的同目录依赖必须真的参与解析，
    /// 否则像 <c>Microsoft.Interop.ComInterfaceGenerator</c> 这种拆成多个程序集的生成器会加载失败
    /// （失败会被记进 <see cref="GeneratorCatalog.LoadFailures"/> 并由门禁断言为空）。
    /// </summary>
    private sealed class AnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
    {
        public static readonly AnalyzerAssemblyLoader Instance = new();

        private readonly HashSet<string> dependencyDirectories = new(StringComparer.Ordinal);

        private AnalyzerAssemblyLoader() =>
            System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                lock (this.dependencyDirectories)
                {
                    foreach (var directory in this.dependencyDirectories)
                    {
                        var candidate = Path.Combine(directory, name.Name + ".dll");
                        if (File.Exists(candidate))
                        {
                            return context.LoadFromAssemblyPath(candidate);
                        }
                    }
                }

                return null;
            };

        public void AddDependencyLocation(string fullPath)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(fullPath));
            if (directory is null)
            {
                return;
            }

            lock (this.dependencyDirectories)
            {
                this.dependencyDirectories.Add(directory);
            }
        }

        public System.Reflection.Assembly LoadFromPath(string fullPath)
        {
            this.AddDependencyLocation(fullPath);
            return System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
        }
    }
}
