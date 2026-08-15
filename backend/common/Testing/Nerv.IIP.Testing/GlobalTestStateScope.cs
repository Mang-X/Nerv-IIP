using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using FluentValidation;

namespace Nerv.IIP.Testing;

/// <summary>
/// Raised when process-global test state remains owned by another scope for the full acquisition
/// budget, either because that earlier scope was not disposed or because it legitimately held the
/// state longer than the budget.
/// </summary>
public sealed class GlobalTestStateScopeAcquisitionTimeoutException : TimeoutException
{
    public GlobalTestStateScopeAcquisitionTimeoutException(TimeSpan acquisitionTimeout)
        : base(
            $"Timed out after {acquisitionTimeout} waiting to capture process-global test state. "
            + "An earlier GlobalTestStateScope was likely not disposed, or it legitimately held "
            + "process-global test state longer than the acquisition budget. Ensure every captured "
            + "scope is disposed with await using or try/finally and keep its lifetime bounded.")
    {
        AcquisitionTimeout = acquisitionTimeout;
    }

    public TimeSpan AcquisitionTimeout { get; }
}

/// <summary>
/// Serialises the tests that take a scope against each other and restores the exact prior value on
/// dispose, including the difference between "was never set", "was set to the empty string" and
/// "had a value". It cannot stop a test that never takes a scope from observing a mutated value
/// while a scope is open — process-global state has no other owner.
/// </summary>
/// <remarks>
/// The mutators are instance methods rather than something the caller writes inline for two reasons.
/// First, <see cref="SetEnvironmentVariable"/> captures a variable's prior value the moment it is first
/// written, so a caller can never mutate a variable it forgot to name in <see cref="CaptureAsync"/> —
/// the previous shape made that omission silent and unrecoverable. Second, it keeps every raw
/// <c>Environment.SetEnvironmentVariable</c> / <c>CultureInfo.Current*</c> write inside this one
/// audited type instead of scattered across test bodies, which is what the backend test-determinism
/// gate (<c>scripts/check-backend-test-determinism.ps1</c>) is looking for.
/// <para>
/// Capture and restore deliberately cover more statics than the mutator surface does: the
/// FluentValidation global resolvers and the default-thread cultures have no mutator because nothing
/// currently needs one, yet a scope still puts them back, so a test that reaches past the scope is
/// still cleaned up after. A mutator is only added when a caller exists to exercise it.
/// </para>
/// </remarks>
public sealed class GlobalTestStateScope : IAsyncDisposable
{
    private static readonly TimeSpan DefaultAcquisitionTimeout = TimeSpan.FromSeconds(60);
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

    /// <summary>
    /// Captures process-global test state after acquiring the shared gate within the default
    /// 60-second budget.
    /// </summary>
    /// <exception cref="GlobalTestStateScopeAcquisitionTimeoutException">
    /// The gate remained owned for the full acquisition budget, either because an earlier scope was
    /// not disposed or because it legitimately held process-global state longer than the budget.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The caller-provided <paramref name="cancellationToken"/> was cancelled before acquisition.
    /// </exception>
    public static ValueTask<GlobalTestStateScope> CaptureAsync(
        IEnumerable<string>? environmentVariables = null,
        CancellationToken cancellationToken = default) =>
        CaptureAsync(environmentVariables, DefaultAcquisitionTimeout, cancellationToken);

    internal static async ValueTask<GlobalTestStateScope> CaptureAsync(
        IEnumerable<string>? environmentVariables,
        TimeSpan acquisitionTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(acquisitionTimeout, TimeSpan.Zero);

        var names = (environmentVariables ?? [])
            .Prepend("TZ")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var acquired = await Gate.WaitAsync(acquisitionTimeout, cancellationToken).ConfigureAwait(false);
        if (!acquired)
        {
            throw new GlobalTestStateScopeAcquisitionTimeoutException(acquisitionTimeout);
        }

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
