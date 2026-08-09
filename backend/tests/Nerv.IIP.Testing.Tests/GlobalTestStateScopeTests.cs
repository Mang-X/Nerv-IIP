using System.Globalization;
using FluentValidation;

namespace Nerv.IIP.Testing.Tests;

/// <summary>
/// The scope's own tests. Every one of them must release the scope's permit on **every** path,
/// including a failing assertion: <see cref="GlobalTestStateScope.CaptureAsync"/> waits on the gate
/// before a bounded diagnostic failure, so leaking a permit would still stall every following test
/// for the full production budget. Hence no assertion may sit between a bare capture and its
/// disposal; use <c>await using</c> or <c>try/finally</c>.
/// </summary>
public sealed class GlobalTestStateScopeTests
{
    [Fact]
    public async Task DisposeAsync_RestoresPublicMutableGlobalStateIncludingAbsentAndEmptyEnvironmentValues()
    {
        const string environmentVariable = "NERV_IIP_GLOBAL_STATE_SCOPE_TEST";
        var processTimeZone = Environment.GetEnvironmentVariable("TZ");
        var processEnvironmentValue = Environment.GetEnvironmentVariable(environmentVariable);

        try
        {
            Environment.SetEnvironmentVariable("TZ", null);
            Environment.SetEnvironmentVariable(environmentVariable, string.Empty);

            var originalPropertyNameResolver = ValidatorOptions.Global.PropertyNameResolver;
            var originalDisplayNameResolver = ValidatorOptions.Global.DisplayNameResolver;
            var originalCurrentCulture = CultureInfo.CurrentCulture;
            var originalCurrentUiCulture = CultureInfo.CurrentUICulture;
            var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
            var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

            var scope = await GlobalTestStateScope.CaptureAsync([environmentVariable]);
            await using (scope)
            {
                ValidatorOptions.Global.PropertyNameResolver = static (_, _, _) => "mutated-property";
                ValidatorOptions.Global.DisplayNameResolver = static (_, _, _) => "mutated-display";
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
                CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
                Environment.SetEnvironmentVariable("TZ", "Etc/UTC");
                Environment.SetEnvironmentVariable(environmentVariable, null);
            }

            Assert.Same(originalPropertyNameResolver, ValidatorOptions.Global.PropertyNameResolver);
            Assert.Same(originalDisplayNameResolver, ValidatorOptions.Global.DisplayNameResolver);
            Assert.Same(originalCurrentCulture, CultureInfo.CurrentCulture);
            Assert.Same(originalCurrentUiCulture, CultureInfo.CurrentUICulture);
            Assert.Same(originalDefaultCulture, CultureInfo.DefaultThreadCurrentCulture);
            Assert.Same(originalDefaultUiCulture, CultureInfo.DefaultThreadCurrentUICulture);
            Assert.Null(Environment.GetEnvironmentVariable("TZ"));
            Assert.Equal(string.Empty, Environment.GetEnvironmentVariable(environmentVariable));

            // Disposing twice must be a no-op rather than a second Gate.Release(), which would raise
            // the permit count above one and let two scopes run concurrently ever after.
            await scope.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TZ", processTimeZone);
            Environment.SetEnvironmentVariable(environmentVariable, processEnvironmentValue);
        }
    }

    [Fact]
    public async Task SetEnvironmentVariable_RestoresAVariableThatWasNotNamedAtCapture()
    {
        const string environmentVariable = "NERV_IIP_GLOBAL_STATE_SCOPE_LATE_TEST";
        Assert.Null(Environment.GetEnvironmentVariable(environmentVariable));

        await using (var scope = await GlobalTestStateScope.CaptureAsync())
        {
            scope.SetEnvironmentVariable(environmentVariable, "mutated");

            Assert.Equal("mutated", Environment.GetEnvironmentVariable(environmentVariable));
        }

        Assert.Null(Environment.GetEnvironmentVariable(environmentVariable));
    }

    [Fact]
    public async Task UseCulture_SetsBothCurrentAndUiCultureAndDisposeRestoresThem()
    {
        var originalCurrentCulture = CultureInfo.CurrentCulture;
        var originalCurrentUiCulture = CultureInfo.CurrentUICulture;

        await using (var scope = await GlobalTestStateScope.CaptureAsync())
        {
            scope.UseCulture("fr-FR");

            Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
            Assert.Equal("fr-FR", CultureInfo.CurrentUICulture.Name);
        }

        Assert.Same(originalCurrentCulture, CultureInfo.CurrentCulture);
        Assert.Same(originalCurrentUiCulture, CultureInfo.CurrentUICulture);
    }

    [Fact]
    public async Task Mutators_RejectUseAfterDisposeRatherThanLeakingUnrestorableState()
    {
        var scope = await GlobalTestStateScope.CaptureAsync();
        await scope.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => scope.UseCulture("fr-FR"));
        Assert.Throws<ObjectDisposedException>(
            () => scope.SetEnvironmentVariable("NERV_IIP_GLOBAL_STATE_SCOPE_DISPOSED_TEST", "value"));
    }

    [Fact]
    public async Task CaptureAsync_SerializesConcurrentScopesUntilTheFirstScopeDisposes()
    {
        var first = await GlobalTestStateScope.CaptureAsync();
        var secondCapture = GlobalTestStateScope.CaptureAsync().AsTask();

        try
        {
            Assert.False(secondCapture.IsCompleted);
        }
        finally
        {
            // Both permits must come back even when the assertion above fails. Disposing the first
            // scope is also what unblocks the queued capture, so awaiting it here cannot hang: it is
            // either already completed (the failure case) or completes as soon as the permit frees.
            await first.DisposeAsync();
            await (await secondCapture).DisposeAsync();
        }
    }

    [Fact]
    public async Task CaptureAsync_WhenAnEarlierScopeIsStillHeld_FailsWithinBudgetWithActionableDiagnostic()
    {
        var first = await GlobalTestStateScope.CaptureAsync();

        try
        {
            var exception = await Assert.ThrowsAsync<GlobalTestStateScopeAcquisitionTimeoutException>(
                async () => await GlobalTestStateScope.CaptureAsync(
                        environmentVariables: null,
                        acquisitionTimeout: TimeSpan.FromMilliseconds(25),
                        cancellationToken: default)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(2)));

            Assert.Contains(
                "An earlier GlobalTestStateScope was likely not disposed",
                exception.Message,
                StringComparison.Ordinal);
            Assert.Contains("await using or try/finally", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            await first.DisposeAsync();
        }
    }

    [Fact]
    public async Task CaptureAsync_AfterATimedOutWaiter_StillSerializesUntilTheHolderDisposes()
    {
        var first = await GlobalTestStateScope.CaptureAsync();
        Task<GlobalTestStateScope>? nextCapture = null;

        try
        {
            await Assert.ThrowsAsync<GlobalTestStateScopeAcquisitionTimeoutException>(
                async () => await GlobalTestStateScope.CaptureAsync(
                        environmentVariables: null,
                        acquisitionTimeout: TimeSpan.FromMilliseconds(25),
                        cancellationToken: default)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(2)));

            nextCapture = GlobalTestStateScope.CaptureAsync(
                environmentVariables: null,
                acquisitionTimeout: TimeSpan.FromSeconds(2),
                cancellationToken: default).AsTask();

            Assert.False(nextCapture.IsCompleted);
        }
        finally
        {
            await first.DisposeAsync();

            if (nextCapture is not null)
            {
                await (await nextCapture).DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task CaptureAsync_WhenCallerCancels_PropagatesCallerCancellation()
    {
        await using var first = await GlobalTestStateScope.CaptureAsync();
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await GlobalTestStateScope.CaptureAsync(
                environmentVariables: null,
                acquisitionTimeout: TimeSpan.FromSeconds(2),
                cancellationToken: callerCancellation.Token));

        Assert.IsNotType<GlobalTestStateScopeAcquisitionTimeoutException>(exception);
        Assert.Equal(callerCancellation.Token, exception.CancellationToken);
    }
}
