using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nerv.IIP.Testing;

internal static class UserMessageText
{
    internal const int InterpolationEstimatedLength = 12;

    internal static bool TryExtractMessage(
        ExpressionSyntax expression,
        out string message,
        out int estimatedLength,
        Func<InterpolationSyntax, int>? estimateInterpolationLength = null)
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
            var interpolations = interpolated.Contents.OfType<InterpolationSyntax>();
            var interpolationLength = estimateInterpolationLength is null
                ? interpolations.Count() * InterpolationEstimatedLength
                : interpolations.Sum(estimateInterpolationLength);
            estimatedLength = fixedText.Sum(text => text.Length) + interpolationLength;
            return true;
        }

        message = string.Empty;
        estimatedLength = 0;
        return false;
    }

    internal static bool ContainsChinese(string message) =>
        message.Any(character => character is >= '\u3400' and <= '\u9fff');
}
