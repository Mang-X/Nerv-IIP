using System;
using System.Globalization;
using FluentValidation;

public static class StaticSetterFixture
{
    public static void MutateProcessState()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        Environment.SetEnvironmentVariable("NERV_TEST", "changed");
        ValidatorOptions.Global.LanguageManager = null!;
        ValidatorOptions.Global.LanguageManager.Culture = CultureInfo.InvariantCulture;
    }
}
