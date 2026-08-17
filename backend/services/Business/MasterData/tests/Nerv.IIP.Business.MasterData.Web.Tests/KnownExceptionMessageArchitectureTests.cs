namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class KnownExceptionMessageArchitectureTests
{
    public static TheoryData<string, string> EnglishUserMessageSources => new()
    {
        {
            "using NetCorePal.Extensions.Primitives; class Probe { void Run() { throw new KnownException(\"Unable to save\"); } }",
            "explicit KnownException construction"
        },
        {
            "using NetCorePal.Extensions.Primitives; class Probe { KnownException Create() => new(\"Unable to save\"); }",
            "expression-bodied target-typed construction"
        },
        {
            "using NetCorePal.Extensions.Primitives; class Probe { KnownException Create() { return new(\"Unable to save\"); } }",
            "block return target-typed construction"
        },
        {
            "using NetCorePal.Extensions.Primitives; class Probe { void Run() { KnownException error = new(\"Unable to save\"); } }",
            "local-variable target-typed construction"
        },
        {
            "using NetCorePal.Extensions.Primitives; class Probe { private readonly KnownException _error = new(\"Unable to save\"); }",
            "field target-typed construction"
        },
        {
            "using NetCorePal.Extensions.Primitives; class Probe { KnownException Error { get; } = new(\"Unable to save\"); }",
            "property target-typed construction"
        },
    };

    [Theory]
    [MemberData(nameof(EnglishUserMessageSources))]
    public void English_user_messages_are_reported(string source, string _)
    {
        var violations = MasterDataUserMessageSourceAnalyzer.Analyze([new SourceDocument("Probe.cs", source)]);

        Assert.Equal(["Probe.cs:1: 用户消息必须包含中文。"], violations);
    }

    [Fact]
    public void Non_static_user_messages_are_reported()
    {
        const string source =
            "using NetCorePal.Extensions.Primitives; class Probe { KnownException Create(string message) => new(message); }";

        var violations = MasterDataUserMessageSourceAnalyzer.Analyze([new SourceDocument("Probe.cs", source)]);

        Assert.Equal(["Probe.cs:1: 用户消息必须是可静态分析的字符串字面量或插值字符串。"], violations);
    }

    [Fact]
    public void User_messages_estimated_above_sixty_characters_are_reported()
    {
        const string source =
            "using NetCorePal.Extensions.Primitives; class Probe { KnownException Create(string code) => new($\"一二三四五六七八九十一二三四五六七八九十一二三四五六七八九十一二三四五六七八九十一二三四五六七八九十{code}\"); }";

        var violations = MasterDataUserMessageSourceAnalyzer.Analyze([new SourceDocument("Probe.cs", source)]);

        Assert.Equal(["Probe.cs:1: 用户消息估算长度不能超过 60 个字符。"], violations);
    }

    [Fact]
    public void Chinese_raw_string_user_messages_are_allowed()
    {
        const string source =
            "using NetCorePal.Extensions.Primitives; class Probe { KnownException Create() => new(\"\"\"无法保存，请稍后重试。\"\"\"); }";

        var violations = MasterDataUserMessageSourceAnalyzer.Analyze([new SourceDocument("Probe.cs", source)]);

        Assert.Empty(violations);
    }

    [Fact]
    public void FluentValidation_reduced_WithMessage_is_reported()
    {
        const string source =
            "using FluentValidation; class Request { public string Name { get; set; } = string.Empty; } class Probe : AbstractValidator<Request> { public Probe() { RuleFor(x => x.Name).NotEmpty().WithMessage(\"Unable to save\"); } }";

        var violations = MasterDataUserMessageSourceAnalyzer.Analyze([new SourceDocument("Probe.cs", source)]);

        Assert.Equal(["Probe.cs:1: 用户消息必须包含中文。"], violations);
    }

    [Fact]
    public void FluentValidation_static_WithMessage_uses_the_error_message_argument()
    {
        const string source =
            "using FluentValidation; using DefaultValidatorExtensions = FluentValidation.DefaultValidatorOptions; class Request { public string Name { get; set; } = string.Empty; } class Probe : AbstractValidator<Request> { public Probe() { var rule = RuleFor(x => x.Name).NotEmpty(); DefaultValidatorExtensions.WithMessage(rule, \"无法保存。\"); } }";

        var violations = MasterDataUserMessageSourceAnalyzer.Analyze([new SourceDocument("Probe.cs", source)]);

        Assert.Empty(violations);
    }

    [Fact]
    public void FluentValidation_using_static_WithMessage_is_reported()
    {
        const string source =
            "using FluentValidation; using static FluentValidation.DefaultValidatorOptions; class Request { public string Name { get; set; } = string.Empty; } class Probe : AbstractValidator<Request> { public Probe() { var rule = RuleFor(x => x.Name).NotEmpty(); WithMessage(rule, \"Unable to save\"); } }";

        var violations = MasterDataUserMessageSourceAnalyzer.Analyze([new SourceDocument("Probe.cs", source)]);

        Assert.Equal(["Probe.cs:1: 用户消息必须包含中文。"], violations);
    }

    [Fact]
    public void FluentValidation_named_arguments_use_the_error_message_parameter()
    {
        const string source =
            "using FluentValidation; using DefaultValidatorExtensions = FluentValidation.DefaultValidatorOptions; class Request { public string Name { get; set; } = string.Empty; } class Probe : AbstractValidator<Request> { public Probe() { var rule = RuleFor(x => x.Name).NotEmpty(); DefaultValidatorExtensions.WithMessage(rule: rule, errorMessage: \"Unable to save\"); } }";

        var violations = MasterDataUserMessageSourceAnalyzer.Analyze([new SourceDocument("Probe.cs", source)]);

        Assert.Equal(["Probe.cs:1: 用户消息必须包含中文。"], violations);
    }

    [Fact]
    public void FluentValidation_message_provider_is_reported_as_non_static()
    {
        const string source =
            "using FluentValidation; class Request { public string Name { get; set; } = string.Empty; } class Probe : AbstractValidator<Request> { public Probe() { RuleFor(x => x.Name).NotEmpty().WithMessage(_ => \"Unable to save\"); } }";

        var violations = MasterDataUserMessageSourceAnalyzer.Analyze([new SourceDocument("Probe.cs", source)]);

        Assert.Equal(["Probe.cs:1: 用户消息必须是可静态分析的字符串字面量或插值字符串。"], violations);
    }

    [Fact]
    public void Non_FluentValidation_WithMessage_is_ignored()
    {
        const string source =
            "class Rule { public Rule WithMessage(string message) => this; } class Probe { void Run(Rule rule) { rule.WithMessage(\"Unable to save\"); } }";

        var violations = MasterDataUserMessageSourceAnalyzer.Analyze([new SourceDocument("Probe.cs", source)]);

        Assert.Empty(violations);
    }

    [Fact]
    public void MasterData_user_messages_follow_the_architecture_rules()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(
            repositoryRoot,
            "backend",
            "services",
            "Business",
            "MasterData",
            "src");

        var documents = Directory
            .GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !HasPathSegment(file, "bin") && !HasPathSegment(file, "obj"))
            .Select(file => new SourceDocument(
                Path.GetRelativePath(repositoryRoot, file),
                File.ReadAllText(file)))
            .ToArray();

        var violations = MasterDataUserMessageSourceAnalyzer.Analyze(documents);

        Assert.True(
            violations.Count == 0,
            "MasterData user messages must be static, contain Chinese, and be at most 60 estimated characters. Offenders:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
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
