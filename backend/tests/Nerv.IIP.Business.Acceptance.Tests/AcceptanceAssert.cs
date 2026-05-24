namespace Nerv.IIP.Business.Acceptance.Tests;

internal static class AcceptanceAssert
{
    public static string RequiredFact(
        IReadOnlyDictionary<string, string> facts,
        string key,
        string eventType)
    {
        Assert.True(
            facts.TryGetValue(key, out var value),
            $"Acceptance event '{eventType}' must expose visible fact '{key}'.");

        return value!;
    }
}
