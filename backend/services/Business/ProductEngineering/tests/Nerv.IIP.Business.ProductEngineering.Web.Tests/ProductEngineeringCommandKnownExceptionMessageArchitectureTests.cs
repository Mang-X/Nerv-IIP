using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class ProductEngineeringCommandKnownExceptionMessageArchitectureTests
{
    private const string ReleaseCommandsPath =
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductEngineeringReleaseCommands.cs";
    private const string ScheduledReleasePath =
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Scheduling/EngineeringChangeScheduledReleaseService.cs";
    private const string StandardOperationCommandsPath =
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/StandardOperations/StandardOperationCommands.cs";
    private const string ArchiveProductionVersionCommandPath =
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductionVersions/ArchiveProductionVersionCommand.cs";
    private const string CreateProductionVersionCommandPath =
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductionVersions/CreateProductionVersionCommand.cs";
    private const string UpdateProductionVersionCommandPath =
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductionVersions/UpdateProductionVersionCommand.cs";

    private static readonly IReadOnlyCollection<string> CommandSourcePaths =
    [
        ReleaseCommandsPath,
        ScheduledReleasePath,
        StandardOperationCommandsPath,
        ArchiveProductionVersionCommandPath,
        CreateProductionVersionCommandPath,
        UpdateProductionVersionCommandPath,
    ];

    private static readonly IReadOnlyCollection<ProductEngineeringExcludedQuerySite> ExcludedSites =
    [
        new(ReleaseCommandsPath, "ProductEngineeringReleaseValidation", "AsKnownException", "dynamic domain-message helper"),
        new(ReleaseCommandsPath, "HttpProductEngineeringMasterDataReferenceValidator", "ValidateActiveReferencesAsync", "base已合规中文 provider boundary"),
        new(ReleaseCommandsPath, "PromoteScheduledEngineeringChangeCommandHandler", "Handle", "scheduled/internal command"),
        new(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "ResolveAffectedVersionAsync", "scheduler/background"),
        new(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "GetSuccessorEngineeringBomAsync", "scheduler/background"),
        new(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "GetSuccessorManufacturingBomAsync", "scheduler/background"),
        new(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "GetSuccessorRoutingAsync", "scheduler/background"),
        new(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "GetSuccessorProductionVersionAsync", "scheduler/background"),
        new(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "GetSuccessorEngineeringDocumentAsync", "scheduler/background"),
        new(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "ArchiveEngineeringBom", "scheduler/background"),
        new(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "ArchiveManufacturingBom", "scheduler/background"),
        new(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "ArchiveRouting", "scheduler/background"),
        new(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "ArchiveProductionVersion", "scheduler/background"),
        new(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "ArchiveEngineeringDocument", "scheduler/background"),
        new(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "GetEngineeringDocumentRepository", "scheduler/background"),
        new(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "EnsurePublishedSuccessor", "scheduler/background"),
        new(ScheduledReleasePath, "ScheduledEngineeringChangeArchiveResolver", "EnsureActiveSuccessor", "scheduler/background"),
        new(ReleaseCommandsPath, "RejectingEngineeringApprovalVerifier", "EnsureApprovedAsync", "六步第1步失败：Program.cs:46 生产注册 HttpEngineeringApprovalVerifier，fallback 同步根不可达"),
    ];

    [Fact]
    public void Command_known_exception_messages_use_static_chinese_text()
    {
        var repositoryRoot = FindRepositoryRoot();
        var programPath = Path.Combine(repositoryRoot, "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Program.cs");
        var programText = File.ReadAllText(programPath);
        var approvalRegistrations = Regex.Matches(
                programText,
                @"Add(?:HttpClient|Scoped|Transient|Singleton)\s*<\s*IEngineeringApprovalVerifier\s*,\s*(?<implementation>[A-Za-z0-9_]+)\s*>")
            .Select(match => match.Groups["implementation"].Value)
            .ToArray();
        Assert.Equal(["HttpEngineeringApprovalVerifier"], approvalRegistrations);
        var documents = CommandSourcePaths
            .Select(path => Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)))
            .Select(file => new ProductEngineeringSourceDocument(
                Path.GetRelativePath(repositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.Exists(file) ? File.ReadAllText(file) : string.Empty))
            .ToArray();

        Assert.Equal(CommandSourcePaths.Count, documents.Length);
        Assert.All(documents, document => Assert.False(string.IsNullOrEmpty(document.Text), $"命令源文件缺失或为空：{document.Path}"));

        var violations = ProductEngineeringUserMessageSourceAnalyzer.Analyze(documents, ExcludedSites);

        Assert.True(
            violations.Count == 0,
            "ProductEngineering command user messages must be static, contain Chinese, and be at most 60 estimated characters. Offenders:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));

        var targetMethods = new HashSet<(string TypeName, string MethodName)>
        {
            ("ReleaseEngineeringChangeCommandHandler", "Handle"),
            ("ReleaseEngineeringChangeCommandHandler", "ArchiveEngineeringBom"),
            ("ReleaseEngineeringChangeCommandHandler", "ArchiveManufacturingBom"),
            ("ReleaseEngineeringChangeCommandHandler", "ArchiveRouting"),
            ("ReleaseEngineeringChangeCommandHandler", "ArchiveProductionVersion"),
            ("ReleaseEngineeringChangeCommandHandler", "ArchiveEngineeringDocument"),
            ("CancelScheduledEngineeringChangeCommandHandler", "Handle"),
            ("RescheduleEngineeringChangeCommandHandler", "Handle"),
        };
        var targetMessages = documents
            .Where(document => document.Path == ReleaseCommandsPath)
            .SelectMany(document => CSharpSyntaxTree.ParseText(document.Text, path: document.Path)
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method.Ancestors().OfType<TypeDeclarationSyntax>()
                    .Any(type => targetMethods.Contains((type.Identifier.ValueText, method.Identifier.ValueText))))
                .SelectMany(method => method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Where(IsProductEngineeringAsKnownException)
                    .Select(invocation => (method, invocation))))
            .ToArray();

        foreach (var (_, invocation) in targetMessages)
        {
            var arguments = invocation.ArgumentList?.Arguments ?? [];
            Assert.True(arguments.Count >= 2, $"{invocation.GetLocation()} 的 AsKnownException 必须提供固定中文外层文案。");
            var message = arguments[1].Expression;
            var isStaticMessage = ProductEngineeringUserMessageSourceAnalyzer.TryExtractMessage(
                message,
                out var messageText,
                out var estimatedLength);
            var containsChinese = isStaticMessage
                && messageText.Any(character => character is >= '\u3400' and <= '\u9fff');
            Assert.True(isStaticMessage, $"{invocation.GetLocation()} 的 AsKnownException 外层文案必须是可静态分析字符串。");
            Assert.True(containsChinese, $"{invocation.GetLocation()} 的 AsKnownException 外层文案必须包含中文。");
            Assert.True(estimatedLength <= 60, $"{invocation.GetLocation()} 的 AsKnownException 外层文案估算长度超过 60 个字符。");
        }
    }

    private static bool IsProductEngineeringAsKnownException(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax memberAccess
        && memberAccess.Name.Identifier.ValueText == "AsKnownException"
        && memberAccess.Expression.ToString().EndsWith("ProductEngineeringReleaseValidation", StringComparison.Ordinal);

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
