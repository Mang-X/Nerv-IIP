using System.Security.Cryptography;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Nerv.IIP.Testing.Xunit;

public static class SeededTestOrdering
{
    public const string DefaultSeed = "nerv-iip-default";

    public static IReadOnlyList<string> OrderFullyQualifiedNames(
        IEnumerable<string> fullyQualifiedNames,
        string? seed = null) =>
        OrderByFullyQualifiedName(fullyQualifiedNames, static name => name, seed).ToArray();

    internal static IEnumerable<T> OrderByFullyQualifiedName<T>(
        IEnumerable<T> values,
        Func<T, string> fullyQualifiedName,
        string? seed = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(fullyQualifiedName);

        var effectiveSeed = string.IsNullOrWhiteSpace(seed) ? DefaultSeed : seed;

        return values
            .Select(value =>
            {
                var name = fullyQualifiedName(value)
                    ?? throw new InvalidOperationException("A test fully-qualified name cannot be null.");
                var input = Encoding.UTF8.GetBytes(effectiveSeed + name);
                var hash = Convert.ToHexString(SHA256.HashData(input));
                return new { Value = value, Name = name, Hash = hash };
            })
            .OrderBy(static entry => entry.Hash, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Name, StringComparer.Ordinal)
            .Select(static entry => entry.Value)
            .ToArray();
    }
}

public sealed class SeededTestCaseOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase =>
        SeededTestOrdering.OrderByFullyQualifiedName(
            testCases,
            static testCase => testCase.DisplayName,
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_ORDER_SEED"));
}

public sealed class SeededTestCollectionOrderer : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(
        IEnumerable<ITestCollection> testCollections) =>
        SeededTestOrdering.OrderByFullyQualifiedName(
            testCollections,
            static testCollection => testCollection.DisplayName,
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_ORDER_SEED"));
}
