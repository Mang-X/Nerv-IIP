using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;

namespace Nerv.IIP.VocabularyGovernance.Tests;

/// <summary>
/// 对 backend 全部非测试源码跑一次 <see cref="StronglyTypedIdQueryScanner"/>。
/// 每个 ProjectReference 图的根编译一次（⛔ 不能一锅编：多服务并进同一编译会撞
/// <c>CS0104 ApplicationDbContext 二义</c>）；同一个文件出现在多个闭包里时按
/// (路径, 行, 列) 去重，取第一次读数。
/// </summary>
public static class BackendStronglyTypedIdScan
{
    private static readonly Lazy<BackendScanReading> Cached = new(() => Run(injections: null));

    public static BackendScanReading Reading => Cached.Value;

    public sealed record BackendScanReading(
        BackendProjectGraph Graph,
        IReadOnlyList<StronglyTypedIdInnerMemberSite> Sites,
        IReadOnlyList<UnresolvedInnerMemberSite> Unresolved,
        IReadOnlyList<string> UncoveredSyntaxForms,
        int ScannedFileCount,
        int CompilationCount,
        TimeSpan Elapsed);

    /// <summary>
    /// <paramref name="injections"/>：把某个磁盘文件的文本临时改写后再编译（⛔ 只在内存里改，不落盘）。
    /// 这是阳性对照的取数手段——main 上不存在致命位点，只能注入后测判定力。
    /// </summary>
    public static BackendScanReading Run(
        IReadOnlyDictionary<string, Func<string, string>>? injections,
        bool runGenerators = true,
        Func<BackendProject, bool>? rootFilter = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var graph = BackendProjectGraph.Load();
        var roots = graph.Roots.Where(root => rootFilter is null || rootFilter(root)).ToArray();

        // 每个文件**确定性地**归给「闭包里含它的第一个根」（根按相对路径序）。
        // 归属先算好再并行，读数与串行完全一致，且不受线程调度影响。
        var closures = roots.ToDictionary(root => root.RelativePath, graph.ClosureOf, StringComparer.Ordinal);
        var ownedFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        var closureFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            var files = closures[root.RelativePath]
                .SelectMany(graph.SourceFilesOf)
                .Select(graph.ToRelativePath)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            closureFiles[root.RelativePath] = files;
            ownedFiles[root.RelativePath] = files.Where(assigned.Add).ToList();
        }

        var fileTexts = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var siteBag = new System.Collections.Concurrent.ConcurrentBag<StronglyTypedIdInnerMemberSite>();
        var unresolvedBag = new System.Collections.Concurrent.ConcurrentBag<UnresolvedInnerMemberSite>();
        var uncoveredBag = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.ForEach(roots, root =>
        {
            var documents = closureFiles[root.RelativePath]
                .Select(relative =>
                {
                    var text = fileTexts.GetOrAdd(relative, key => File.ReadAllText(Path.Combine(graph.BackendRoot, key)));
                    if (injections is not null && injections.TryGetValue(relative, out var rewrite))
                    {
                        text = rewrite(text);
                    }

                    return new SourceDocument(relative, text);
                })
                .ToArray();

            var compilation = ScannerCompilationFactory.Create(
                root.RelativePath.Replace('/', '_'),
                documents,
                closures[root.RelativePath].Any(project => project.IsWebSdk),
                runGenerators);

            var result = StronglyTypedIdQueryScanner.Scan(compilation, ownedFiles[root.RelativePath]);
            foreach (var site in result.Sites)
            {
                siteBag.Add(site);
            }

            foreach (var site in result.Unresolved)
            {
                unresolvedBag.Add(site);
            }

            foreach (var text in result.UncoveredSyntaxForms)
            {
                uncoveredBag.Add(text);
            }
        });

        stopwatch.Stop();
        return new BackendScanReading(
            graph,
            siteBag.OrderBy(site => site.Path, StringComparer.Ordinal).ThenBy(site => site.Line).ThenBy(site => site.Column).ToArray(),
            unresolvedBag.OrderBy(site => site.Path, StringComparer.Ordinal).ThenBy(site => site.Line).ThenBy(site => site.Column).ToArray(),
            uncoveredBag.Distinct(StringComparer.Ordinal).OrderBy(text => text, StringComparer.Ordinal).ToArray(),
            assigned.Count,
            roots.Length,
            stopwatch.Elapsed);
    }
}
