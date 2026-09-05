using System.Text;

namespace Nerv.IIP.MigrationGovernance.Tests;

/// <summary>
/// 极小的 C# 词法扫描：把源码切成「字符串字面量」「注释」「其余代码」三份。
///
/// #3124 的源码面规则是反着写的——不枚举「哪些 SQL 读取形状违规」，而是「表名不得出现在字面量里」。
/// 枚举读取形状时漏一种 = 静默放行：转义引号 <c>FROM x.\"CAPPublishedMessage\"</c>、逐字双写
/// <c>@\"... \"\"CAPPublishedMessage\"\"\"</c>、插值 schema <c>$\"{schema}.cap_published_messages\"</c>
/// 全都能绕过去。按字面量判则无论 SQL 怎么写，表名本身仍然是字面量文本。
///
/// 注释里提到表名仍然允许（说明「运行时不写入」正是我们希望后人写的），所以必须真正区分二者。
/// 另一方面强类型访问面（<c>DbSet</c> 属性 / 泛型 <c>Set</c> 调用）根本不出现表名字面量，只能在
/// 「剔掉注释与字面量之后的代码文本」上判，否则注释里举例说明就会误报。
/// </summary>
internal static class CSharpLiterals
{
    internal readonly record struct Literal(int Start, string Value);

    /// <summary><see cref="CodeOnly"/> 与输入等长：注释与字面量内容被替换成空格，因此下标与行号仍然对齐。</summary>
    internal sealed record ScanResult(IReadOnlyList<Literal> Literals, string CodeOnly);

    internal static ScanResult Scan(string text)
    {
        var literals = new List<Literal>();
        var codeOnly = new StringBuilder(text);
        var index = 0;

        void Blank(int from, int to)
        {
            for (var position = from; position < to && position < codeOnly.Length; position++)
            {
                if (codeOnly[position] != '\n')
                {
                    codeOnly[position] = ' ';
                }
            }
        }

        while (index < text.Length)
        {
            var current = text[index];

            if (current == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                var newLine = text.IndexOf('\n', index);
                var end = newLine < 0 ? text.Length : newLine;
                Blank(index, end);
                index = end;
                continue;
            }

            if (current == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                var close = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                var end = close < 0 ? text.Length : close + 2;
                Blank(index, end);
                index = end;
                continue;
            }

            if (current == '\'')
            {
                var end = SkipCharLiteral(text, index);
                Blank(index, end);
                index = end;
                continue;
            }

            if (current == '"')
            {
                var end = ReadStringLiteral(text, index, literals);
                Blank(index, end);
                index = end;
                continue;
            }

            index++;
        }

        return new ScanResult(literals, codeOnly.ToString());
    }

    private static int SkipCharLiteral(string text, int start)
    {
        var index = start + 1;
        while (index < text.Length)
        {
            if (text[index] == '\\')
            {
                index += 2;
                continue;
            }

            if (text[index] == '\'')
            {
                return index + 1;
            }

            index++;
        }

        return index;
    }

    private static int ReadStringLiteral(string text, int start, List<Literal> literals)
    {
        var isVerbatim = false;
        for (var prefix = start - 1; prefix >= 0 && (text[prefix] == '@' || text[prefix] == '$'); prefix--)
        {
            if (text[prefix] == '@')
            {
                isVerbatim = true;
            }
        }

        var quoteCount = 0;
        while (start + quoteCount < text.Length && text[start + quoteCount] == '"')
        {
            quoteCount++;
        }

        // raw string literal: """ ... """（含 $"""…"""；插值洞按字面文本处理，更 fail-closed）
        if (quoteCount >= 3)
        {
            var terminator = new string('"', quoteCount);
            var contentStart = start + quoteCount;
            var close = text.IndexOf(terminator, contentStart, StringComparison.Ordinal);
            if (close < 0)
            {
                literals.Add(new Literal(contentStart, text[contentStart..]));
                return text.Length;
            }

            literals.Add(new Literal(contentStart, text[contentStart..close]));
            return close + quoteCount;
        }

        if (quoteCount == 2 && !isVerbatim)
        {
            literals.Add(new Literal(start + 1, string.Empty));
            return start + 2;
        }

        var builder = new StringBuilder();
        var index = start + 1;

        if (isVerbatim)
        {
            while (index < text.Length)
            {
                if (text[index] == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        builder.Append('"');
                        index += 2;
                        continue;
                    }

                    index++;
                    break;
                }

                builder.Append(text[index]);
                index++;
            }

            literals.Add(new Literal(start + 1, builder.ToString()));
            return index;
        }

        while (index < text.Length)
        {
            if (text[index] == '\\')
            {
                if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    builder.Append('"');
                }

                index += 2;
                continue;
            }

            if (text[index] == '"')
            {
                index++;
                break;
            }

            if (text[index] == '\n')
            {
                index++;
                break;
            }

            builder.Append(text[index]);
            index++;
        }

        literals.Add(new Literal(start + 1, builder.ToString()));
        return index;
    }
}
