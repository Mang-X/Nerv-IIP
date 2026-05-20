using Nerv.IIP.Iam.Web.Application.Commands.Users;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamCommandSafetyTests
{
    [Fact]
    public void Reset_password_command_does_not_print_plaintext_password()
    {
        var password = SensitivePassword.From("PlainText123!");
        var command = new ResetUserPasswordCommand("user-1", password);

        Assert.Equal("[redacted]", password.ToString());
        Assert.DoesNotContain("PlainText123!", command.ToString(), StringComparison.Ordinal);
        Assert.Contains("[redacted]", command.ToString(), StringComparison.Ordinal);
    }
}
