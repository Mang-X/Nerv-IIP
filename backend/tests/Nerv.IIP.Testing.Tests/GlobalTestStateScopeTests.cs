using System.Globalization;
using FluentValidation;

namespace Nerv.IIP.Testing.Tests;

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

            ValidatorOptions.Global.PropertyNameResolver = static (_, _, _) => "mutated-property";
            ValidatorOptions.Global.DisplayNameResolver = static (_, _, _) => "mutated-display";
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            Environment.SetEnvironmentVariable("TZ", "Etc/UTC");
            Environment.SetEnvironmentVariable(environmentVariable, null);

            await scope.DisposeAsync();

            Assert.Same(originalPropertyNameResolver, ValidatorOptions.Global.PropertyNameResolver);
            Assert.Same(originalDisplayNameResolver, ValidatorOptions.Global.DisplayNameResolver);
            Assert.Same(originalCurrentCulture, CultureInfo.CurrentCulture);
            Assert.Same(originalCurrentUiCulture, CultureInfo.CurrentUICulture);
            Assert.Same(originalDefaultCulture, CultureInfo.DefaultThreadCurrentCulture);
            Assert.Same(originalDefaultUiCulture, CultureInfo.DefaultThreadCurrentUICulture);
            Assert.Null(Environment.GetEnvironmentVariable("TZ"));
            Assert.Equal(string.Empty, Environment.GetEnvironmentVariable(environmentVariable));

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

        var scope = await GlobalTestStateScope.CaptureAsync();
        scope.SetEnvironmentVariable(environmentVariable, "mutated");

        Assert.Equal("mutated", Environment.GetEnvironmentVariable(environmentVariable));

        await scope.DisposeAsync();

        Assert.Null(Environment.GetEnvironmentVariable(environmentVariable));
    }

    [Fact]
    public async Task UseCulture_SetsBothCurrentAndUiCultureAndDisposeRestoresThem()
    {
        var originalCurrentCulture = CultureInfo.CurrentCulture;
        var originalCurrentUiCulture = CultureInfo.CurrentUICulture;

        var scope = await GlobalTestStateScope.CaptureAsync();
        scope.UseCulture("fr-FR");

        Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
        Assert.Equal("fr-FR", CultureInfo.CurrentUICulture.Name);

        await scope.DisposeAsync();

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

        Assert.False(secondCapture.IsCompleted);

        await first.DisposeAsync();
        var second = await secondCapture;

        await second.DisposeAsync();
    }
}
