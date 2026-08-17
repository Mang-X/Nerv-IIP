using Microsoft.AspNetCore.Http;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

public sealed record FileStorageError(string Message);

public sealed record FileStorageResult<T>(T? Value, FileStorageError? Error, int StatusCode)
{
    public static FileStorageResult<T> Ok(T value) => new(value, null, StatusCodes.Status200OK);
    public static FileStorageResult<T> BadRequest(string message) => new(default, new FileStorageError(message), StatusCodes.Status400BadRequest);
    public static FileStorageResult<T> NotFound(string message) => new(default, new FileStorageError(message), StatusCodes.Status404NotFound);
    public static FileStorageResult<T> Conflict(string message) => new(default, new FileStorageError(message), StatusCodes.Status409Conflict);
    public static FileStorageResult<T> ServiceUnavailable(string message) => new(default, new FileStorageError(message), StatusCodes.Status503ServiceUnavailable);
    internal static FileStorageResult<T> Failure(int statusCode, string message) => new(default, new FileStorageError(message), statusCode);
}
