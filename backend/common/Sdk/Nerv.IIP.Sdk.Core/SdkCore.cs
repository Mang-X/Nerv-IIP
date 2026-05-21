namespace Nerv.IIP.Sdk.Core;

public sealed record PlatformApiOptions(Uri BaseAddress, string SdkVersion = "1.0");

public sealed record PlatformRequestContext(
    string OrganizationId,
    string EnvironmentId,
    string CorrelationId,
    string? IdempotencyKey = null,
    string? TraceParent = null);

public sealed record PlatformApiError(string Code, string Message);

public sealed record PlatformApiResult<T>(T? Value, PlatformApiError? Error)
{
    public bool Succeeded => Error is null;

    public static PlatformApiResult<T> Success(T value) => new(value, null);
    public static PlatformApiResult<T> Failure(string code, string message) => new(default, new PlatformApiError(code, message));
}
