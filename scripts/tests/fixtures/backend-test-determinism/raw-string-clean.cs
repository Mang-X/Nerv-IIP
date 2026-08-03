public static class RawStringCleanFixture
{
    private const string PlainRawText = """Task.Delay(900)""";
    private static readonly string InterpolatedRawText = $$"""{Task.Delay(901)} {{1 + 1}}""";
    private static readonly string MixedBraceRawText = $$"""{{{1 + 1}}} Task.Delay(902)""";

    public static string Read() => PlainRawText + InterpolatedRawText + MixedBraceRawText;
}
