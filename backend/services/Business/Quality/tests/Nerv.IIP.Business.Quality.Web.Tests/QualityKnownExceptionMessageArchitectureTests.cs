using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityKnownExceptionMessageArchitectureTests
{
    private const string QualityDomainRoot =
        "backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain";

    private const string QualityWebRoot =
        "backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web";

    [Fact]
    public void Quality_transport_visible_known_exceptions_use_static_chinese_messages()
    {
        var documents = LoadDocuments();
        var violations = KnownExceptionUserMessageSourceAnalyzer.Analyze(
            documents, [], enforceLengthAndCharacters: false);

        Assert.True(
            violations.Count == 0,
            "Quality transportVisible 用户消息必须是静态中文。违规项："
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Analyzer_rejects_english_and_dynamic_mutations_and_honors_exclusion()
    {
        const string englishSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"Inspection task not found.\"); } }";
        var englishViolations = KnownExceptionUserMessageSourceAnalyzer.Analyze(
            [new UserMessageSourceDocument("Probe.cs", englishSource)],
            []);
        Assert.Equal(["Probe.cs:1: 用户消息必须包含中文。"], englishViolations);

        const string dynamicSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle(string message) { throw new KnownException(message); } }";
        var dynamicViolations = KnownExceptionUserMessageSourceAnalyzer.Analyze(
            [new UserMessageSourceDocument("Probe.cs", dynamicSource)],
            []);
        Assert.Equal(["Probe.cs:1: 用户消息必须是可静态分析的字符串字面量或插值字符串。"], dynamicViolations);

        var excluded = KnownExceptionUserMessageSourceAnalyzer.Analyze(
            [new UserMessageSourceDocument(
                "Internal.cs",
                "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"internal\"); } }")],
            [new ExcludedUserMessageSite("Internal.cs", "Probe", "Handle", "internal/no-facade")]);
        Assert.Empty(excluded);
    }

    private static IReadOnlyCollection<UserMessageSourceDocument> LoadDocuments()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoots = new[]
        {
            Path.Combine(repositoryRoot, QualityDomainRoot.Replace('/', Path.DirectorySeparatorChar)),
            Path.Combine(repositoryRoot, QualityWebRoot.Replace('/', Path.DirectorySeparatorChar)),
        };
        return sourceRoots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !HasPathSegment(file, "bin") && !HasPathSegment(file, "obj"))
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(file => new UserMessageSourceDocument(
                Path.GetRelativePath(repositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(file)))
            .ToArray();
    }

    private static bool HasPathSegment(string path, string segment) =>
        path.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Contains(segment, StringComparer.OrdinalIgnoreCase);

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
