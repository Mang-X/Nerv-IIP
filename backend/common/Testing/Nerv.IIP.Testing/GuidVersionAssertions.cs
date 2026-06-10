namespace Nerv.IIP.Testing;

public static class GuidVersionAssertions
{
    public static IReadOnlyList<string> Version7GuidSuffixFailures(string id, string prefix)
    {
        var failures = new List<string>();
        if (!id.StartsWith(prefix, StringComparison.Ordinal))
        {
            failures.Add($"Expected '{id}' to start with '{prefix}'.");
            return failures;
        }

        var suffix = id[prefix.Length..];
        if (!Guid.TryParseExact(suffix, "N", out _))
        {
            failures.Add($"Expected '{id}' to end with an N-formatted GUID suffix after prefix '{prefix}'.");
            return failures;
        }

        if (suffix.Length <= 12 || suffix[12] != '7')
        {
            var version = suffix.Length > 12 ? suffix[12].ToString() : "<missing>";
            failures.Add($"Expected GUID suffix in '{id}' to be version 7, but version nibble was '{version}'.");
        }

        return failures;
    }
}
