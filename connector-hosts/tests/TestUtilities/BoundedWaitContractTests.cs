using System.Reflection;
using Xunit;
using Xunit.Sdk;

namespace Nerv.IIP.ConnectorHost.TestUtilities;

/// <summary>
/// Prevents an asynchronous connector test from silently reintroducing the MAN-799 failure mode.
/// This source is linked into each connector test assembly that owns an external process, socket,
/// or background collection cycle.
/// </summary>
public sealed class BoundedWaitContractTests
{
    private const string CollectionName = ConnectorTimeoutCollection.Name;
    private const int TestTimeoutMilliseconds = ConnectorTimeoutCollection.TestTimeoutMilliseconds;

    [Fact]
    public void Every_asynchronous_test_has_an_enforced_timeout()
    {
        var assemblyTypes = Assembly.GetExecutingAssembly().GetTypes();
        var collectionDefinition = assemblyTypes
            .Select(type => new
            {
                Type = type,
                Definition = type.GetCustomAttribute<CollectionDefinitionAttribute>()
            })
            .SingleOrDefault(candidate => candidate.Definition is not null
                && candidate.Type.GetCustomAttributesData()
                    .Single(attribute => attribute.AttributeType == typeof(CollectionDefinitionAttribute))
                    .ConstructorArguments.SingleOrDefault().Value as string == CollectionName);
        Assert.True(
            collectionDefinition?.Definition?.DisableParallelization,
            $"Collection '{CollectionName}' must exist and set DisableParallelization=true so xUnit v2 honours Timeout.");

        var violations = assemblyTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Select(method => new
            {
                Method = method,
                Fact = method.GetCustomAttributes(inherit: true).OfType<FactAttribute>().SingleOrDefault()
            })
            .Where(test => test.Fact is not null && typeof(Task).IsAssignableFrom(test.Method.ReturnType))
            .Select(test =>
            {
                var collection = test.Method.DeclaringType!
                    .GetCustomAttributesData()
                    .SingleOrDefault(attribute => attribute.AttributeType == typeof(CollectionAttribute));
                var collectionName = collection?.ConstructorArguments.SingleOrDefault().Value as string;
                var fact = test.Fact!;
                return collectionName == CollectionName
                    && fact.Timeout == TestTimeoutMilliseconds
                    ? null
                    : $"{test.Method.DeclaringType.FullName}.{test.Method.Name}";
            })
            .Where(violation => violation is not null)
            .OrderBy(violation => violation, StringComparer.Ordinal)
            .ToArray();

        if (violations.Length > 0)
        {
            throw new XunitException(
                $"Async tests must use collection '{CollectionName}' and Timeout={TestTimeoutMilliseconds}: "
                + string.Join(", ", violations));
        }
    }
}
