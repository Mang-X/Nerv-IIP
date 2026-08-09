using System.Reflection;
using System.Runtime.CompilerServices;
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

        var violations = FindViolatingAsyncTests(
                assemblyTypes.Where(type => type != typeof(AsyncVoidRegressionFixture)
                    && type != typeof(StaticAsyncRegressionFixture)))
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .OrderBy(violation => violation, StringComparer.Ordinal)
            .ToArray();

        if (violations.Length > 0)
        {
            throw new XunitException(
                $"Async tests must use collection '{CollectionName}' and Timeout={TestTimeoutMilliseconds}: "
                + string.Join(", ", violations));
        }
    }

    [Fact]
    public void Unbounded_async_void_fact_and_theory_are_reported()
    {
        var violations = FindViolatingAsyncTests([typeof(AsyncVoidRegressionFixture)])
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Async_void_fact_without_timeout", "Async_void_theory_without_timeout"],
            violations);
    }

    [Fact]
    public void Unbounded_static_async_fact_and_theory_are_reported()
    {
        var violations = FindViolatingAsyncTests([typeof(StaticAsyncRegressionFixture)])
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Static_async_task_fact_without_timeout", "Static_async_void_theory_without_timeout"],
            violations);
    }

    private static MethodInfo[] FindViolatingAsyncTests(IEnumerable<Type> types) =>
        types
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Select(method => new
            {
                Method = method,
                Fact = method.GetCustomAttributes(inherit: true).OfType<FactAttribute>().SingleOrDefault()
            })
            .Where(test => test.Fact is not null && IsAsynchronousTest(test.Method))
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
                    : test.Method;
            })
            .Where(violation => violation is not null)
            .Cast<MethodInfo>()
            .ToArray();

    private static bool IsAsynchronousTest(MethodInfo method) =>
        typeof(Task).IsAssignableFrom(method.ReturnType)
        || method.ReturnType == typeof(void)
        && method.GetCustomAttribute<AsyncStateMachineAttribute>() is not null;

#pragma warning disable xUnit1000, xUnit1048 // Deliberately undiscoverable xUnit v2 async-void regression fixture.
    private sealed class AsyncVoidRegressionFixture
    {
        [Fact]
        public async void Async_void_fact_without_timeout() => await Task.CompletedTask;

        [Theory]
        [InlineData(0)]
        public async void Async_void_theory_without_timeout(int _) => await Task.CompletedTask;
    }

    private sealed class StaticAsyncRegressionFixture
    {
        [Fact]
        public static async Task Static_async_task_fact_without_timeout() => await Task.CompletedTask;

        [Theory]
        [InlineData(0)]
        public static async void Static_async_void_theory_without_timeout(int _) => await Task.CompletedTask;
    }
#pragma warning restore xUnit1000, xUnit1048
}
