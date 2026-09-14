using System.Xml.Linq;

namespace Nerv.IIP.VocabularyGovernance.Tests;

/// <summary>backend 下的一个 C# 项目（仅取扫描需要的三项事实：路径、SDK、ProjectReference）。</summary>
public sealed record BackendProject(
    string RelativePath,
    string AbsolutePath,
    string Sdk,
    IReadOnlyList<string> ProjectReferences)
{
    public string Directory => System.IO.Path.GetDirectoryName(AbsolutePath)!;

    public bool IsWebSdk => Sdk.Contains("Sdk.Web", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// backend 非测试项目的 ProjectReference 有向图。
///
/// 扫描单位是**图的根**（没有被任何其它非测试项目引用的项目），一根一次编译：
/// <list type="bullet">
/// <item>根集合覆盖全部非测试项目（<see cref="UncoveredProjects"/> 必须为空，
/// 由测试断言；新增一个谁都不引用的孤儿项目会自动成为新根而不是被漏掉）。</item>
/// <item>⛔ 不能一锅编：把多个服务的源码并进同一个 <c>CSharpCompilation</c> 会撞
/// <c>CS0104 ApplicationDbContext 二义</c>（实测 867 条），语义模型随之失真。</item>
/// </list>
/// ⚠️ 项目集合是**结构性枚举**（`backend` 下全部 `*.csproj` 减去路径段含 `tests` 的），
/// 不是按文本命中挑出来的白名单 —— 新服务、新项目自动进入扫描面。
/// </summary>
public sealed class BackendProjectGraph
{
    private BackendProjectGraph(
        string backendRoot,
        IReadOnlyList<BackendProject> projects,
        IReadOnlyList<BackendProject> roots,
        IReadOnlyList<string> uncoveredProjects)
    {
        BackendRoot = backendRoot;
        Projects = projects;
        Roots = roots;
        UncoveredProjects = uncoveredProjects;
        this.projectsByPath = projects.ToDictionary(project => project.AbsolutePath, StringComparer.Ordinal);
    }

    private readonly Dictionary<string, BackendProject> projectsByPath;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<string>> sourceFiles = new(StringComparer.Ordinal);

    public string BackendRoot { get; }

    /// <summary>backend 下全部非测试项目。</summary>
    public IReadOnlyList<BackendProject> Projects { get; }

    /// <summary>没有被任何其它非测试项目引用的项目，每个对应一次编译。</summary>
    public IReadOnlyList<BackendProject> Roots { get; }

    /// <summary>不在任何根的引用闭包里的项目（正常必须为空）。</summary>
    public IReadOnlyList<string> UncoveredProjects { get; }

    public static BackendProjectGraph Load()
    {
        var backendRoot = LocateBackendRoot();
        var projects = Directory
            .EnumerateFiles(backendRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(file => !HasAnySegment(Path.GetRelativePath(backendRoot, file), "obj", "bin", "tests"))
            .Select(file => ReadProject(backendRoot, file))
            .OrderBy(project => project.RelativePath, StringComparer.Ordinal)
            .ToArray();

        var byPath = projects.ToDictionary(project => project.AbsolutePath, StringComparer.Ordinal);
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in projects.SelectMany(project => project.ProjectReferences))
        {
            if (byPath.ContainsKey(reference))
            {
                referenced.Add(reference);
            }
        }

        var roots = projects.Where(project => !referenced.Contains(project.AbsolutePath)).ToArray();

        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            CollectClosure(root, byPath, covered);
        }

        var uncovered = projects
            .Where(project => !covered.Contains(project.AbsolutePath))
            .Select(project => project.RelativePath)
            .ToArray();

        return new BackendProjectGraph(backendRoot, projects, roots, uncovered);
    }

    /// <summary>某个根的 ProjectReference 传递闭包（含自身），按相对路径序返回。</summary>
    public IReadOnlyList<BackendProject> ClosureOf(BackendProject root)
    {
        var byPath = this.projectsByPath;
        var closure = new HashSet<string>(StringComparer.Ordinal);
        CollectClosure(root, byPath, closure);
        return closure
            .Select(path => byPath[path])
            .OrderBy(project => project.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 归属于 <paramref name="project"/> 的 <c>*.cs</c>：目录下递归，但**最近的上级 csproj 必须是它自己**，
    /// 以免把嵌套项目的源码重复算进上级。排除路径段 obj / bin。
    /// </summary>
    public IReadOnlyList<string> SourceFilesOf(BackendProject project) =>
        this.sourceFiles.GetOrAdd(project.AbsolutePath, _ => EnumerateSourceFiles(project));

    private static IReadOnlyList<string> EnumerateSourceFiles(BackendProject project)
    {
        var directory = project.Directory;
        return Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !HasAnySegment(Path.GetRelativePath(directory, file), "obj", "bin"))
            .Where(file => string.Equals(NearestProjectDirectory(file, directory), directory, StringComparison.Ordinal))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();
    }

    public string ToRelativePath(string absolutePath) =>
        Path.GetRelativePath(BackendRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    private static string? NearestProjectDirectory(string file, string stopAt)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(file)!);
        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.csproj").Any())
            {
                return directory.FullName;
            }

            if (string.Equals(directory.FullName, stopAt, StringComparison.Ordinal))
            {
                return null;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static void CollectClosure(
        BackendProject project,
        IReadOnlyDictionary<string, BackendProject> byPath,
        HashSet<string> collected)
    {
        if (!collected.Add(project.AbsolutePath))
        {
            return;
        }

        foreach (var reference in project.ProjectReferences)
        {
            if (byPath.TryGetValue(reference, out var referenced))
            {
                CollectClosure(referenced, byPath, collected);
            }
        }
    }

    private static BackendProject ReadProject(string backendRoot, string file)
    {
        var document = XDocument.Load(file);
        var sdk = document.Root?.Attribute("Sdk")?.Value ?? string.Empty;
        var directory = Path.GetDirectoryName(file)!;
        var references = document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(directory, include!.Replace('\\', Path.DirectorySeparatorChar))))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return new BackendProject(
            Path.GetRelativePath(backendRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
            Path.GetFullPath(file),
            sdk,
            references);
    }

    private static bool HasAnySegment(string relativePath, params string[] segments)
    {
        var pathSegments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => pathSegments.Contains(segment, StringComparer.Ordinal));
    }

    /// <summary>从测试输出目录向上定位 backend 根，不依赖 CWD（与 VocabularyDriftGovernanceTests 同姿势）。</summary>
    private static string LocateBackendRoot()
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
