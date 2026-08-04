using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: InternalsVisibleTo("Nerv.IIP.Testing.Tests")]

namespace Nerv.IIP.Testing.Xunit;

public static class SeededTestOrdering
{
    public const string DefaultSeed = "nerv-iip-default";

    /// <summary>
    /// Orders items by <c>SHA-256(seed + displayName)</c>. The key is the xUnit display name, which is
    /// the only stable identity both <see cref="ITestCase"/> and <see cref="ITestCollection"/> expose;
    /// for a <c>[Theory]</c> it includes the argument text, which is itself fixed per data row.
    /// </summary>
    internal static IEnumerable<T> OrderByDisplayName<T>(
        IEnumerable<T> values,
        Func<T, string> displayName,
        string? seed = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(displayName);

        var effectiveSeed = string.IsNullOrWhiteSpace(seed) ? DefaultSeed : seed;

        return values
            .Select(value =>
            {
                var name = displayName(value)
                    ?? throw new InvalidOperationException("A test display name cannot be null.");
                var input = Encoding.UTF8.GetBytes(effectiveSeed + name);
                var hash = Convert.ToHexString(SHA256.HashData(input));
                return new { Value = value, Name = name, Hash = hash };
            })
            .OrderBy(static entry => entry.Hash, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Name, StringComparer.Ordinal)
            .Select(static entry => entry.Value)
            .ToArray();
    }

    internal static IReadOnlyList<string> OrderDisplayNames(
        IEnumerable<string> displayNames,
        string? seed = null) =>
        OrderByDisplayName(displayNames, static name => name, seed).ToArray();
}

public sealed class SeededTestCaseOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase =>
        SeededTestOrdering.OrderByDisplayName(
            testCases,
            static testCase => testCase.DisplayName,
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_ORDER_SEED"));
}

public sealed class SeededTestCollectionOrderer : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(
        IEnumerable<ITestCollection> testCollections) =>
        SeededTestOrdering.OrderByDisplayName(
            testCollections,
            static testCollection => testCollection.DisplayName,
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_ORDER_SEED"));
}
