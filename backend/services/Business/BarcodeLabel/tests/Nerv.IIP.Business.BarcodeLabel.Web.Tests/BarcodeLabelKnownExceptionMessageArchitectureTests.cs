namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class BarcodeLabelKnownExceptionMessageArchitectureTests
{
    private const string BarcodeLabelSourceRoot =
        "backend/services/Business/BarcodeLabel/src";

    private static readonly IReadOnlyCollection<string> SourcePaths =
    [
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Infrastructure/ApplicationDbContext.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/BarcodeRules/CreateOrUpdateBarcodeRuleCommand.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/LabelTemplates/CreateOrUpdateLabelTemplateCommand.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/CreateLabelPrintBatchCommand.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/Scans/RecordScanCommand.cs",
        $"{BarcodeLabelSourceRoot}/Nerv.IIP.Business.BarcodeLabel.Web/Application/Queries/PrintBatches/GetLabelPrintBatchQuery.cs",
    ];

    private static readonly IReadOnlyCollection<BarcodeLabelKnownExceptionSite> ExpectedSites =
    [
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Infrastructure/ApplicationDbContext.cs"), "ApplicationDbContext", "TryMapUniqueConflict", 2),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/BarcodeRules/CreateOrUpdateBarcodeRuleCommand.cs"), "CreateOrUpdateBarcodeRuleCommandHandler", "Handle", 4),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/LabelTemplates/CreateOrUpdateLabelTemplateCommand.cs"), "CreateOrUpdateLabelTemplateCommandHandler", "Handle", 2),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/CreateLabelPrintBatchCommand.cs"), "CreateLabelPrintBatchCommandHandler", "Handle", 4),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "DispatchLabelPrintBatchCommandHandler", "Handle", 0),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "ReprintLabelCommandHandler", "Handle", 0),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "VoidLabelCommandHandler", "Handle", 0),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "LabelPrintLifecycleKnownExceptionMapper", "Create", 9),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "LabelPrintLifecycle", "LoadBatchAsync", 1),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/PrintBatches/PrintLabelLifecycleCommands.cs"), "LabelPrintLifecycle", "CompileFrozenBatchAsync", 1),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/Scans/RecordScanCommand.cs"), "RecordScanCommandHandler", "Handle", 3),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/Scans/RecordScanCommand.cs"), "RecordScanCommandHandler", "CreateCandidateOrThrow", 3),
        Target(SourcePath("Nerv.IIP.Business.BarcodeLabel.Web/Application/Queries/PrintBatches/GetLabelPrintBatchQuery.cs"), "GetLabelPrintBatchQueryHandler", "Handle", 1),
    ];

    [Fact]
    public void BarcodeLabel_transport_visible_known_exceptions_have_a_closed_target_and_exclusion_ledger()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, BarcodeLabelSourceRoot.Replace('/', Path.DirectorySeparatorChar));
        var sourceFiles = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();
        var documents = sourceFiles
            .Select(file => new BarcodeLabelSourceDocument(
                Path.GetRelativePath(repositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(file)))
            .ToArray();

        var documentPaths = documents.Select(document => document.Path).ToHashSet(StringComparer.Ordinal);
        Assert.All(SourcePaths, path => Assert.Contains(path, documentPaths));
        Assert.All(documents, document => Assert.False(string.IsNullOrEmpty(document.Text), $"BarcodeLabel 源文件缺失或为空：{document.Path}"));

        var expectedKeys = ExpectedSites.Select(site => site.Key).ToArray();
        Assert.Equal(expectedKeys.Length, expectedKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(0, ExpectedSites.Count(site => site.Kind == BarcodeLabelKnownExceptionSiteKind.Excluded));
        Assert.Equal(30, ExpectedSites.Where(site => site.Kind == BarcodeLabelKnownExceptionSiteKind.Target)
            .Sum(site => site.DirectKnownExceptionCount));
        Assert.Equal(0, ExpectedSites.Where(site => site.Kind == BarcodeLabelKnownExceptionSiteKind.Excluded)
            .Sum(site => site.DirectKnownExceptionCount));

        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(documents);
        var duplicateDiscoveredKeys = discovered
            .GroupBy(site => site.Key, StringComparer.Ordinal)
            .Where(group => group.Count() != 1)
            .Select(group => $"{group.Key} ({group.Count()})")
            .ToArray();
        Assert.Empty(duplicateDiscoveredKeys);

        var ledgerViolations = BarcodeLabelUserMessageSourceAnalyzer.ValidateLedger(
            discovered,
            ExpectedSites,
            expectedExclusionCount: 0);
        Assert.True(
            ledgerViolations.Count == 0,
            "BarcodeLabel KnownException 位点台账未闭合："
            + Environment.NewLine
            + string.Join(Environment.NewLine, ledgerViolations));

        foreach (var expected in ExpectedSites)
        {
            var matches = discovered.Where(site => site.Key == expected.Key).ToArray();
            Assert.Single(matches);
            Assert.Equal(expected.DirectKnownExceptionCount, matches[0].DirectKnownExceptionCount);
        }

        var violations = BarcodeLabelUserMessageSourceAnalyzer.Analyze(documents);

        Assert.True(
            violations.Count == 0,
            "BarcodeLabel transportVisible 用户消息必须是中文、静态可分析、长度不超过 60 且不含危险字符。违规项："
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Analyzer_reports_dynamic_unsafe_and_internal_helper_messages()
    {
        const string englishSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"Barcode failed.\"); } }";
        Assert.Equal(
            ["Probe.cs:1: 用户消息必须包含中文。"],
            BarcodeLabelUserMessageSourceAnalyzer.Analyze(
                [new BarcodeLabelSourceDocument("Probe.cs", englishSource)]));

        const string dynamicSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle(string message) { throw new KnownException(message); } }";
        Assert.Equal(
            ["Probe.cs:1: 用户消息必须是可静态分析的字符串字面量或插值字符串。"],
            BarcodeLabelUserMessageSourceAnalyzer.Analyze(
                [new BarcodeLabelSourceDocument("Probe.cs", dynamicSource)]));

        const string unsafeSource =
            "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"无法保存 <内容>。\"); } }";
        Assert.Equal(
            ["Probe.cs:1: 用户消息不能包含不安全字符。"],
            BarcodeLabelUserMessageSourceAnalyzer.Analyze(
                [new BarcodeLabelSourceDocument("Probe.cs", unsafeSource)]));

        var internalHelperViolations = BarcodeLabelUserMessageSourceAnalyzer.Analyze(
            [new BarcodeLabelSourceDocument(
                "Internal.cs",
                "using NetCorePal.Extensions.Primitives; class Probe { void Handle() { throw new KnownException(\"internal-code\"); } }")]);

        Assert.Equal(["Internal.cs:1: 用户消息必须包含中文。"], internalHelperViolations);
    }

    [Fact]
    public void Discovery_includes_command_handlers_that_have_no_known_exception_site()
    {
        const string source = """
            using NetCorePal.Extensions.Primitives;
            sealed record ProbeCommand : ICommand<string>;
            sealed class ProbeHandler : ICommandHandler<ProbeCommand, string>
            {
                public Task<string> Handle(ProbeCommand command, CancellationToken cancellationToken) =>
                    Task.FromResult(command.ToString());
            }
            """;

        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(
            [new BarcodeLabelSourceDocument("Probe.cs", source)]);

        var handler = Assert.Single(discovered);
        Assert.Equal("ProbeHandler", handler.TypeName);
        Assert.Equal("Handle", handler.MethodName);
        Assert.Equal(0, handler.DirectKnownExceptionCount);
    }

    [Fact]
    public void Discovery_recognizes_difficult_command_handler_shapes()
    {
        const string source = """
            using NetCorePal.Extensions.Primitives;
            partial class Outer
            {
                public partial class NestedHandler<TCommand>(int seed)
                    : ICommandHandler<TCommand, string>
                    where TCommand : ICommand<string>
                {
                    public Task<string> Handle(TCommand command, CancellationToken cancellationToken) =>
                        Task.FromResult($"{seed}:{command}");
                }
            }
            """;

        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(
            [new BarcodeLabelSourceDocument("Probe.cs", source)]);

        var handler = Assert.Single(discovered);
        Assert.Equal("NestedHandler", handler.TypeName);
        Assert.Equal("Handle", handler.MethodName);
        Assert.Equal(0, handler.DirectKnownExceptionCount);
    }

    [Fact]
    public void Discovery_merges_a_cross_file_partial_handler_into_the_file_that_declares_handle()
    {
        const string declarationSource = """
            using NetCorePal.Extensions.Primitives;
            sealed record SplitCommand : ICommand<string>;
            partial class SplitHandler : ICommandHandler<SplitCommand, string> { }
            """;
        const string handleSource = """
            using System.Threading;
            using System.Threading.Tasks;
            partial class SplitHandler
            {
                public Task<string> Handle(SplitCommand command, CancellationToken cancellationToken) =>
                    throw new KnownException("处理失败。");
            }
            """;

        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(
            [
                new BarcodeLabelSourceDocument("A_declaration.cs", declarationSource),
                new BarcodeLabelSourceDocument("B_handle.cs", handleSource),
            ]);

        var handler = Assert.Single(discovered);
        Assert.Equal("B_handle.cs|SplitHandler|Handle", handler.Key);
        Assert.Equal(1, handler.DirectKnownExceptionCount);
        Assert.True(handler.IsCommandHandler);
    }

    [Fact]
    public void Discovery_merges_a_cross_file_partial_handler_with_an_explicit_interface_implementation()
    {
        const string declarationSource = """
            using NetCorePal.Extensions.Primitives;
            sealed record ExplicitCommand : ICommand<string>;
            partial class ExplicitHandler : ICommandHandler<ExplicitCommand, string> { }
            """;
        const string handleSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using NetCorePal.Extensions.Primitives;
            partial class ExplicitHandler
            {
                Task<string> MediatR.IRequestHandler<ExplicitCommand, string>.Handle(
                    ExplicitCommand command,
                    CancellationToken cancellationToken) =>
                    throw new KnownException("显式处理失败。");
            }
            """;

        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(
            [
                new BarcodeLabelSourceDocument("A_declaration.cs", declarationSource),
                new BarcodeLabelSourceDocument("B_explicit_handle.cs", handleSource),
            ]);

        var handler = Assert.Single(discovered);
        Assert.Equal("B_explicit_handle.cs|ExplicitHandler|Handle", handler.Key);
        Assert.Equal(1, handler.DirectKnownExceptionCount);
        Assert.True(handler.IsCommandHandler);
    }

    [Theory]
    [InlineData("A_contract.cs", "Z_overload.cs")]
    [InlineData("Z_contract.cs", "A_overload.cs")]
    public void Discovery_marks_only_the_interface_contract_handle_when_an_unrelated_overload_sorts_on_either_side(
        string contractPath,
        string overloadPath)
    {
        const string declarationSource = """
            using NetCorePal.Extensions.Primitives;
            sealed record OverloadedCommand : ICommand<string>;
            partial class OverloadedHandler : ICommandHandler<OverloadedCommand, string> { }
            """;
        const string contractSource = """
            using System.Threading;
            using System.Threading.Tasks;
            partial class OverloadedHandler
            {
                public Task<string> Handle(OverloadedCommand command, CancellationToken cancellationToken) =>
                    throw new KnownException("合同处理失败。");
            }
            """;
        const string overloadSource = """
            partial class OverloadedHandler
            {
                public string Handle(string value) =>
                    throw new KnownException("重载处理失败。");
            }
            """;

        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(
            [
                new BarcodeLabelSourceDocument("M_declaration.cs", declarationSource),
                new BarcodeLabelSourceDocument(contractPath, contractSource),
                new BarcodeLabelSourceDocument(overloadPath, overloadSource),
            ]);

        Assert.Collection(
            discovered.Where(site => site.TypeName == "OverloadedHandler"),
            first =>
            {
                var expectedIsContract = first.Path == contractPath;
                Assert.Equal(expectedIsContract, first.IsCommandHandler);
                Assert.Equal(1, first.DirectKnownExceptionCount);
            },
            second =>
            {
                var expectedIsContract = second.Path == contractPath;
                Assert.Equal(expectedIsContract, second.IsCommandHandler);
                Assert.Equal(1, second.DirectKnownExceptionCount);
            });
    }

    [Fact]
    public void Ledger_rejects_command_handler_exclusion_for_a_difficult_handler_shape()
    {
        const string source = """
            using NetCorePal.Extensions.Primitives;
            partial class Outer
            {
                public partial class NestedHandler<TCommand>(int seed)
                    : ICommandHandler<TCommand, string>
                    where TCommand : ICommand<string>
                {
                    public Task<string> Handle(TCommand command, CancellationToken cancellationToken) =>
                        Task.FromResult($"{seed}:{command}");
                }
            }
            """;
        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(
            [new BarcodeLabelSourceDocument("Probe.cs", source)]);
        var handler = Assert.Single(discovered);
        var expected = new BarcodeLabelKnownExceptionSite(
            handler.Path,
            handler.TypeName,
            handler.MethodName,
            0,
            BarcodeLabelKnownExceptionSiteKind.Excluded,
            "探针豁免");

        var violations = BarcodeLabelUserMessageSourceAnalyzer.ValidateLedger(
            discovered,
            [expected],
            expectedExclusionCount: 1);

        Assert.Contains(violations, violation => violation.Contains("ICommandHandler 不得豁免", StringComparison.Ordinal));
    }

    [Fact]
    public void Ledger_rejects_every_exclusion_even_when_the_expected_count_matches()
    {
        const string source =
            "using NetCorePal.Extensions.Primitives; class Probe { void Helper() { throw new KnownException(\"内部失败。\"); } }";
        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(
            [new BarcodeLabelSourceDocument("Probe.cs", source)]);
        var site = Assert.Single(discovered);
        var expected = site with
        {
            Kind = BarcodeLabelKnownExceptionSiteKind.Excluded,
            Reason = "探针豁免",
        };

        var violations = BarcodeLabelUserMessageSourceAnalyzer.ValidateLedger(
            discovered,
            [expected],
            expectedExclusionCount: 1);

        Assert.Contains(violations, violation => violation.Contains("不允许豁免", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyzer_discovers_and_validates_known_exception_subclass_invocations()
    {
        const string source = """
            using NetCorePal.Extensions.Primitives;
            sealed class PrimaryKnown(string message) : KnownException(message);
            sealed class ClassicKnown : KnownException
            {
                public ClassicKnown(string message) : base(message) { }
            }
            sealed class Probe
            {
                public void Handle()
                {
                    _ = new PrimaryKnown("Primary failed.");
                    _ = new ClassicKnown("Classic <failed>.");
                }
            }
            """;
        var documents = new[] { new BarcodeLabelSourceDocument("Derived.cs", source) };

        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(documents);
        var violations = BarcodeLabelUserMessageSourceAnalyzer.Analyze(documents);

        Assert.Equal(4, discovered.Sum(site => site.DirectKnownExceptionCount));
        Assert.Contains(discovered, site => site.Key == "Derived.cs|PrimaryKnown|.ctor" && site.DirectKnownExceptionCount == 1);
        Assert.Contains(discovered, site => site.Key == "Derived.cs|ClassicKnown|.ctor" && site.DirectKnownExceptionCount == 1);
        Assert.Contains(discovered, site => site.Key == "Derived.cs|Probe|Handle" && site.DirectKnownExceptionCount == 2);
        Assert.DoesNotContain(discovered, site => site.MethodName == ".type");
        Assert.Contains(violations, violation => violation.Contains("用户消息必须包含中文", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("用户消息不能包含不安全字符", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("用户消息必须是可静态分析", StringComparison.Ordinal));
    }

    [Fact]
    public void Discovery_uses_type_identity_for_a_known_exception_created_in_a_primary_base_argument()
    {
        const string source = """
            using NetCorePal.Extensions.Primitives;
            class Parent(Exception reason);
            sealed class Probe() : Parent(new KnownException("类型初始化失败。"));
            """;

        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(
            [new BarcodeLabelSourceDocument("Type.cs", source)]);

        var site = Assert.Single(discovered);
        Assert.Equal("Type.cs|Probe|.type", site.Key);
        Assert.Equal(1, site.DirectKnownExceptionCount);
    }

    [Fact]
    public void Discovery_recognizes_a_handler_whose_handle_method_is_inherited()
    {
        const string source = """
            using NetCorePal.Extensions.Primitives;
            namespace ProbeNamespace
            {
                sealed record ProbeCommand : ICommand<string>;
                class Worker
                {
                    public Task<string> Handle(ProbeCommand command, CancellationToken cancellationToken) =>
                        Task.FromResult(command.ToString());
                }
                sealed class Executor : Worker, ICommandHandler<ProbeCommand, string> { }
            }
            """;

        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(
            [new BarcodeLabelSourceDocument("Inherited.cs", source)]);

        var handler = Assert.Single(discovered);
        Assert.Equal("Executor", handler.TypeName);
        Assert.Equal("Handle", handler.MethodName);
        Assert.True(handler.IsCommandHandler);
    }

    [Fact]
    public void Discovery_does_not_treat_an_unrelated_same_named_interface_as_a_command_handler()
    {
        const string source = """
            namespace Unrelated
            {
                interface ICommandHandler<TCommand, TResult> { }
                sealed class FakeHandler : ICommandHandler<int, string>
                {
                    public string Handle(int command) => command.ToString();
                }
            }
            """;

        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(
            [new BarcodeLabelSourceDocument("Fake.cs", source)]);

        Assert.Empty(discovered);
    }

    [Fact]
    public void Discovery_fails_closed_when_command_handler_contract_symbols_cannot_be_resolved()
    {
        const string source = "sealed class Probe { }";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BarcodeLabelUserMessageSourceAnalyzer.Discover(
                [new BarcodeLabelSourceDocument("Probe.cs", source)],
                ["Missing.ICommandHandler`1", "Missing.ICommandHandler`2"]));

        Assert.Equal("BarcodeLabel ICommandHandler 合同类型无法完整解析。", exception.Message);
    }

    [Fact]
    public void Discovery_owns_known_exception_creations_by_their_member_declaration()
    {
        const string source = """
            using NetCorePal.Extensions.Primitives;
            class Probe(string value = "default")
            {
                private readonly Exception field = new KnownException("字段失败。");
                public Probe() : this("value") { throw new KnownException("构造失败。"); }
                public string Property => throw new KnownException("属性失败。");
                public string this[int index] => throw new KnownException("索引失败。");
                public static Probe operator +(Probe left, Probe right) =>
                    throw new KnownException("运算失败。");
                public event Action Changed
                {
                    add => throw new KnownException("订阅失败。");
                    remove => throw new KnownException("退订失败。");
                }
                public void Method()
                {
                    Func<string> nested = () => throw new KnownException("局部失败。");
                    _ = nested();
                }
            }
            """;

        var discovered = BarcodeLabelUserMessageSourceAnalyzer.Discover(
            [new BarcodeLabelSourceDocument("Members.cs", source)]);

        Assert.Equal(7, discovered.Count);
        Assert.Equal(8, discovered.Sum(site => site.DirectKnownExceptionCount));
    }

    [Fact]
    public void Discovery_fails_closed_when_a_known_exception_member_symbol_cannot_be_resolved()
    {
        const string source = """
            using NetCorePal.Extensions.Primitives;
            throw new KnownException("顶层失败。");
            """;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BarcodeLabelUserMessageSourceAnalyzer.Discover(
                [new BarcodeLabelSourceDocument("Global.cs", source)]));

        Assert.Contains("无法归属到成员", exception.Message, StringComparison.Ordinal);
    }

    private static string SourcePath(string relativePath) => $"{BarcodeLabelSourceRoot}/{relativePath}";

    private static BarcodeLabelKnownExceptionSite Target(
        string path,
        string typeName,
        string methodName,
        int directKnownExceptionCount) =>
        new(path, typeName, methodName, directKnownExceptionCount, BarcodeLabelKnownExceptionSiteKind.Target, "受 BarcodeLabel transportVisible 消息合同约束");

    private static BarcodeLabelKnownExceptionSite Excluded(
        string path,
        string typeName,
        string methodName,
        int directKnownExceptionCount,
        string reason) =>
        new(path, typeName, methodName, directKnownExceptionCount, BarcodeLabelKnownExceptionSiteKind.Excluded, reason);

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
