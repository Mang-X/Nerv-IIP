using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using FluentValidation;

namespace Nerv.IIP.Testing;

/// <summary>
/// Serialises process-global mutations across an assembly's tests and restores the exact prior value
/// on dispose, including the difference between "was never set", "was set to the empty string" and
/// "had a value".
/// </summary>
/// <remarks>
/// The mutators are instance methods rather than something the caller writes inline for two reasons.
/// First, <see cref="SetEnvironmentVariable"/> captures a variable's prior value the moment it is first
/// written, so a caller can never mutate a variable it forgot to name in <see cref="CaptureAsync"/> —
/// the previous shape made that omission silent and unrecoverable. Second, it keeps every raw
/// <c>Environment.SetEnvironmentVariable</c> / <c>CultureInfo.Current*</c> write inside this one
/// audited type instead of scattered across test bodies, which is what the backend test-determinism
/// gate (<c>scripts/check-backend-test-determinism.ps1</c>) is looking for.
/// </remarks>
public sealed class GlobalTestStateScope : IAsyncDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly Func<Type, MemberInfo, LambdaExpression, string> _propertyNameResolver;
    private readonly Func<Type, MemberInfo, LambdaExpression, string> _displayNameResolver;
    private readonly CultureInfo _currentCulture;
    private readonly CultureInfo _currentUiCulture;
    private readonly CultureInfo? _defaultThreadCurrentCulture;
    private readonly CultureInfo? _defaultThreadCurrentUiCulture;
    private readonly Dictionary<string, string?> _environmentVariables;
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

    /// <summary>Sets both the current culture and the current UI culture for the running thread.</summary>
    public GlobalTestStateScope UseCulture(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return UseCulture(CultureInfo.GetCultureInfo(name));
    }

    /// <summary>Sets both the current culture and the current UI culture for the running thread.</summary>
    public GlobalTestStateScope UseCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ThrowIfDisposed();

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return this;
    }

    public GlobalTestStateScope UseCurrentCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ThrowIfDisposed();

        CultureInfo.CurrentCulture = culture;
        return this;
    }

    public GlobalTestStateScope UseCurrentUiCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ThrowIfDisposed();

        CultureInfo.CurrentUICulture = culture;
        return this;
    }

    public GlobalTestStateScope UseDefaultThreadCulture(CultureInfo? culture)
    {
        ThrowIfDisposed();

        CultureInfo.DefaultThreadCurrentCulture = culture;
        return this;
    }

    public GlobalTestStateScope UseDefaultThreadUiCulture(CultureInfo? culture)
    {
        ThrowIfDisposed();

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        return this;
    }

    public GlobalTestStateScope UsePropertyNameResolver(
        Func<Type, MemberInfo, LambdaExpression, string> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ThrowIfDisposed();

        ValidatorOptions.Global.PropertyNameResolver = resolver;
        return this;
    }

    public GlobalTestStateScope UseDisplayNameResolver(
        Func<Type, MemberInfo, LambdaExpression, string> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ThrowIfDisposed();

        ValidatorOptions.Global.DisplayNameResolver = resolver;
        return this;
    }

    /// <summary>
    /// Writes a process environment variable, capturing its prior value on first write so dispose can
    /// restore it whether it was absent, empty, or set. Names never passed to
    /// <see cref="CaptureAsync"/> are therefore still restored.
    /// </summary>
    public GlobalTestStateScope SetEnvironmentVariable(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ThrowIfDisposed();

        if (!_environmentVariables.ContainsKey(name))
        {
            _environmentVariables[name] = Environment.GetEnvironmentVariable(name);
        }

        Environment.SetEnvironmentVariable(name, value);
        return this;
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
