using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using FluentValidation;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed record SourceDocument(string Path, string Text);

public static class MasterDataUserMessageSourceAnalyzer
{
    private const string KnownExceptionTypeName = "NetCorePal.Extensions.Primitives.KnownException";
    private const string FluentValidationOptionsTypeName = "FluentValidation.DefaultValidatorOptions";
    private const string FluentValidationMessageParameterName = "errorMessage";
    private const string FluentValidationMessageProviderParameterName = "messageProvider";
    private const int InterpolationEstimatedLength = 12;
    private const int MaximumMessageLength = 60;

    public static IReadOnlyList<string> Analyze(IReadOnlyCollection<SourceDocument> documents)
    {
        var syntaxTrees = documents
            .Select(document => CSharpSyntaxTree.ParseText(document.Text, path: document.Path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "MasterDataUserMessageArchitecture",
            syntaxTrees,
            CreateMetadataReferences());
        var violations = new List<Violation>();

        foreach (var syntaxTree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            foreach (var creation in root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
            {
                if (!IsKnownException(semanticModel, creation))
                {
                    continue;
                }

                var firstArgument = creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
                if (firstArgument is not null)
                {
                    AddViolations(syntaxTree, firstArgument, violations);
                }
            }

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!TryGetFluentValidationMessage(semanticModel, invocation, out var messageExpression))
                {
                    continue;
                }

                AddViolations(syntaxTree, messageExpression, violations);
            }
        }

        return violations
            .OrderBy(violation => violation.Path, StringComparer.Ordinal)
            .ThenBy(violation => violation.Line)
            .ThenBy(violation => violation.Position)
            .ThenBy(violation => violation.Reason, StringComparer.Ordinal)
            .Select(violation => $"{violation.Path}:{violation.Line}: {violation.Reason}")
            .ToArray();
    }

    private static bool IsKnownException(
        SemanticModel semanticModel,
        BaseObjectCreationExpressionSyntax creation) =>
        semanticModel.GetTypeInfo(creation).Type?.ToDisplayString() == KnownExceptionTypeName;

    private static bool TryGetFluentValidationMessage(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        out ExpressionSyntax messageExpression)
    {
        messageExpression = null!;
        if (semanticModel.GetOperation(invocation) is IInvocationOperation operation)
        {
            var targetMethod = operation.TargetMethod.ReducedFrom ?? operation.TargetMethod;
            if (targetMethod.Name != "WithMessage"
                || targetMethod.ContainingType.ToDisplayString() != FluentValidationOptionsTypeName)
            {
                return false;
            }

            var messageArgument = operation.Arguments.FirstOrDefault(
                argument => argument.Parameter?.Name is FluentValidationMessageParameterName
                    or FluentValidationMessageProviderParameterName);
            if (messageArgument?.Value.Syntax is not ExpressionSyntax expression)
            {
                return false;
            }

            messageExpression = expression;
            return true;
        }

        return TryGetUsingStaticFluentValidationMessage(semanticModel, invocation, out messageExpression);
    }

    private static bool TryGetUsingStaticFluentValidationMessage(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        out ExpressionSyntax messageExpression)
    {
        messageExpression = null!;
        if (invocation.Expression is not IdentifierNameSyntax { Identifier.ValueText: "WithMessage" })
        {
            return false;
        }

        var importedType = invocation.SyntaxTree.GetCompilationUnitRoot().Usings
            .Where(usingDirective =>
                usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
                && usingDirective.Name is not null)
            .Select(usingDirective => semanticModel.GetSymbolInfo(usingDirective.Name!).Symbol)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault(type => type.ToDisplayString() == FluentValidationOptionsTypeName);
        if (importedType is null)
        {
            return false;
        }

        var messageParameter = importedType.GetMembers("WithMessage")
            .OfType<IMethodSymbol>()
            .SelectMany(method => method.Parameters)
            .FirstOrDefault(parameter =>
                parameter.Name == FluentValidationMessageParameterName
                && parameter.Type.SpecialType == SpecialType.System_String);
        if (messageParameter is null)
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments;
        var messageArgument = arguments.FirstOrDefault(argument =>
                argument.NameColon?.Name.Identifier.ValueText == messageParameter.Name)
            ?? arguments.ElementAtOrDefault(messageParameter.Ordinal);
        if (messageArgument?.Expression is not ExpressionSyntax expression)
        {
            return false;
        }

        messageExpression = expression;
        return true;
    }

    private static void AddViolations(
        SyntaxTree syntaxTree,
        ExpressionSyntax messageExpression,
        ICollection<Violation> violations)
    {
        var line = syntaxTree.GetLineSpan(messageExpression.Span).StartLinePosition.Line + 1;
        var path = syntaxTree.FilePath;

        if (!TryExtractMessage(messageExpression, out var message, out var estimatedLength))
        {
            violations.Add(new Violation(
                path,
                line,
                messageExpression.SpanStart,
                "用户消息必须是可静态分析的字符串字面量或插值字符串。"));
            return;
        }

        if (!ContainsChinese(message))
        {
            violations.Add(new Violation(
                path,
                line,
                messageExpression.SpanStart,
                "用户消息必须包含中文。"));
        }

        if (estimatedLength > MaximumMessageLength)
        {
            violations.Add(new Violation(
                path,
                line,
                messageExpression.SpanStart,
                "用户消息估算长度不能超过 60 个字符。"));
        }
    }

    private static bool TryExtractMessage(
        ExpressionSyntax expression,
        out string message,
        out int estimatedLength)
    {
        if (expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            message = literal.Token.ValueText;
            estimatedLength = message.Length;
            return true;
        }

        if (expression is InterpolatedStringExpressionSyntax interpolated)
        {
            var fixedText = interpolated.Contents
                .OfType<InterpolatedStringTextSyntax>()
                .Select(content => content.TextToken.ValueText)
                .ToArray();
            message = string.Concat(fixedText);
            estimatedLength = fixedText.Sum(text => text.Length)
                + (interpolated.Contents.OfType<InterpolationSyntax>().Count() * InterpolationEstimatedLength);
            return true;
        }

        message = string.Empty;
        estimatedLength = 0;
        return false;
    }

    private static bool ContainsChinese(string message) =>
        message.Any(character => character is >= '\u3400' and <= '\u9fff');

    private static IReadOnlyCollection<MetadataReference> CreateMetadataReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var assemblyPaths = trustedPlatformAssemblies?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(IValidator).Assembly.Location)
            .Append(typeof(KnownException).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            ?? [
                typeof(object).Assembly.Location,
                typeof(IValidator).Assembly.Location,
                typeof(KnownException).Assembly.Location,
            ];

        return assemblyPaths
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private sealed record Violation(string Path, int Line, int Position, string Reason);
}
