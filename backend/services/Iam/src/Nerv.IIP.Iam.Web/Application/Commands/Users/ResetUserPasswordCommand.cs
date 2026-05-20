using Nerv.IIP.Iam.Web.Application.Users;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Commands.Users;

public sealed record ResetUserPasswordCommand(string UserId, SensitivePassword NewPassword) : ICommand
{
    public override string ToString() => $"{nameof(ResetUserPasswordCommand)} {{ UserId = {UserId}, NewPassword = [redacted] }}";
}

public sealed class SensitivePassword
{
    private SensitivePassword(string value)
    {
        Value = value;
    }

    internal string Value { get; }

    public static SensitivePassword From(string value) => new(value);

    public override string ToString() => "[redacted]";
}

public sealed class ResetUserPasswordCommandHandler(IIamUserApplicationService users)
    : ICommandHandler<ResetUserPasswordCommand>
{
    public async Task Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        await users.ResetPasswordAsync(request.UserId, request.NewPassword.Value, cancellationToken);
    }
}
