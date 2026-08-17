using System.Text.RegularExpressions;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class KnownExceptionMessageArchitectureTests
{
    private static readonly Regex DirectMessagePattern = new(
        "new\\s+KnownException\\s*\\(\\s*\\$?\"(?<message>(?:\\\\.|[^\"\\\\])*)\"",
        RegexOptions.CultureInvariant);

    private static readonly Regex ConstructorPattern = new(
        "new\\s+KnownException\\s*\\(",
        RegexOptions.CultureInvariant);

    private static readonly Regex TargetTypedConstructorPattern = new(
        "\\bKnownException\\s+\\w+\\s*\\([^;{}]*\\)\\s*=>\\s*new\\s*\\(",
        RegexOptions.CultureInvariant);

    private static readonly Regex ChineseCharacterPattern = new(
        "[\\u3400-\\u9fff]",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Direct_known_exception_messages_contain_chinese_user_guidance()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(
            repositoryRoot,
            "backend",
            "services",
            "Business",
            "MasterData",
            "src");

        var violations = Directory
            .GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !HasPathSegment(file, "bin") && !HasPathSegment(file, "obj"))
            .SelectMany(file => FindViolations(repositoryRoot, file))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "MasterData KnownException messages must contain Chinese user guidance. Offenders:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> FindViolations(string repositoryRoot, string sourceFile)
    {
        var source = File.ReadAllText(sourceFile);
        var directMessages = DirectMessagePattern.Matches(source);
        var constructorCount = ConstructorPattern.Count(source);

        foreach (Match match in TargetTypedConstructorPattern.Matches(source))
        {
            var line = source.AsSpan(0, match.Index).Count('\n') + 1;
            yield return $"{Path.GetRelativePath(repositoryRoot, sourceFile)}:{line}: "
                + "KnownException 构造必须显式写为 new KnownException(...)，以便验证用户消息。";
        }

        if (directMessages.Count != constructorCount)
        {
            yield return $"{Path.GetRelativePath(repositoryRoot, sourceFile)}: KnownException 必须以直接字符串作为第一参数，"
                + $"但 {constructorCount} 个构造中仅识别到 {directMessages.Count} 个。";
        }

        foreach (Match match in directMessages)
        {
            var message = match.Groups["message"].Value;
            if (!ChineseCharacterPattern.IsMatch(message))
            {
                var line = source.AsSpan(0, match.Index).Count('\n') + 1;
                yield return $"{Path.GetRelativePath(repositoryRoot, sourceFile)}:{line}: {message}";
            }
        }
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
