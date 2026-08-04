using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using FluentValidation;

namespace Nerv.IIP.Testing;

public sealed class GlobalTestStateScope : IAsyncDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly Func<Type, MemberInfo, LambdaExpression, string> _propertyNameResolver;
    private readonly Func<Type, MemberInfo, LambdaExpression, string> _displayNameResolver;
    private readonly CultureInfo _currentCulture;
    private readonly CultureInfo _currentUiCulture;
    private readonly CultureInfo? _defaultThreadCurrentCulture;
    private readonly CultureInfo? _defaultThreadCurrentUiCulture;
    private readonly IReadOnlyDictionary<string, string?> _environmentVariables;
    private int _disposed;

    private GlobalTestStateScope(IReadOnlyList<string> environmentVariables)
    {
        _propertyNameResolver = ValidatorOptions.Global.PropertyNameResolver;
        _displayNameResolver = ValidatorOptions.Global.DisplayNameResolver;
        _currentCulture = CultureInfo.CurrentCulture;
        _currentUiCulture = CultureInfo.CurrentUICulture;
        _defaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentCulture;
        _defaultThreadCurrentUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        _environmentVariables = environmentVariables.ToDictionary(
            name => name,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);
    }

    public static async ValueTask<GlobalTestStateScope> CaptureAsync(
        IEnumerable<string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        var names = (environmentVariables ?? [])
            .Prepend("TZ")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return new GlobalTestStateScope(names);
        }
        catch
        {
            Gate.Release();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            ValidatorOptions.Global.PropertyNameResolver = _propertyNameResolver;
            ValidatorOptions.Global.DisplayNameResolver = _displayNameResolver;
            CultureInfo.DefaultThreadCurrentCulture = _defaultThreadCurrentCulture;
            CultureInfo.DefaultThreadCurrentUICulture = _defaultThreadCurrentUiCulture;
            CultureInfo.CurrentCulture = _currentCulture;
            CultureInfo.CurrentUICulture = _currentUiCulture;

            foreach (var (name, value) in _environmentVariables)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
        finally
        {
            Gate.Release();
        }

        return ValueTask.CompletedTask;
    }
}
