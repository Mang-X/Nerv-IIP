using System.Net;
using System.Text;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

internal sealed class S3FileStorageOptions
{
    private const int MaximumObjectKeyBytes = 1024;
    // 最短 ObjectKey 为 1 字节组织标识、斜杠、file_ 和 32 位 GUID。
    private const int MinimumGeneratedObjectKeyBytes = 1 + 1 + 5 + 32;

    public required Uri Endpoint { get; init; }
    public required string Bucket { get; init; }
    public required string AccessKey { get; init; }
    public required string SecretKey { get; init; }
    public required string Region { get; init; }
    public required bool ForcePathStyle { get; init; }
    public required int ReadIdleTimeoutSeconds { get; init; }
    public string? KeyPrefix { get; init; }

    public static S3FileStorageOptions? Load(IConfiguration configuration)
    {
        switch (configuration["FileStorage:StorageProvider"])
        {
            case null:
            case "local":
                return null;
            case "s3":
                break;
            default:
                throw new InvalidOperationException("FileStorage:StorageProvider must be local or s3.");
        }

        var section = configuration.GetSection("FileStorage:S3");
        var endpointValue = Required("Endpoint");
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https") || string.IsNullOrEmpty(endpoint.Host)
            || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0
            || endpoint.AbsolutePath != "/")
        {
            throw Invalid("Endpoint");
        }

        var bucket = Required("Bucket");
        if (bucket.Length is < 3 or > 63 || IPAddress.TryParse(bucket, out _)
            || bucket.Split('.').Any(label => label.Length == 0 || label.StartsWith('-') || label.EndsWith('-'))
            || !bucket.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.'))
        {
            throw Invalid("Bucket");
        }

        var accessKey = Required("AccessKey");
        if (!accessKey.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            throw Invalid("AccessKey");
        }

        var region = section["Region"] ?? "us-east-1";
        if (region.Length == 0 || !region.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            throw Invalid("Region");
        }

        var pathStyleValue = section["ForcePathStyle"] ?? "true";
        if (!bool.TryParse(pathStyleValue, out var pathStyle)
            || (!pathStyle && endpoint.HostNameType != UriHostNameType.Dns))
        {
            throw Invalid("ForcePathStyle");
        }

        var prefix = section["KeyPrefix"];
        if (prefix is not null && (!IsSafeKey(prefix)
            || Encoding.UTF8.GetByteCount(prefix) + 1 + MinimumGeneratedObjectKeyBytes > MaximumObjectKeyBytes))
        {
            throw Invalid("KeyPrefix");
        }

        if (!int.TryParse(section["ReadIdleTimeoutSeconds"] ?? "30", out var idleSeconds)
            || idleSeconds is < 1 or > 3600)
        {
            throw Invalid("ReadIdleTimeoutSeconds");
        }

        return new()
        {
            Endpoint = endpoint,
            Bucket = bucket,
            AccessKey = accessKey,
            SecretKey = Required("SecretKey"),
            Region = region,
            ForcePathStyle = pathStyle,
            ReadIdleTimeoutSeconds = idleSeconds,
            KeyPrefix = prefix
        };

        string Required(string name)
        {
            var value = section[name];
            return !string.IsNullOrWhiteSpace(value) && value == value.Trim() && !value.Any(char.IsControl)
                ? value
                : throw Invalid(name);
        }
    }

    public static bool IsSafeKey(string key) =>
        !string.IsNullOrWhiteSpace(key)
        && !key.Any(c => char.IsControl(c) || c is '\\' or '%')
        && key.Split('/').All(segment => !string.IsNullOrWhiteSpace(segment)
            && segment == segment.Trim() && segment is not ("." or ".."))
        && IsValidUtf8Key(key);

    private static bool IsValidUtf8Key(string key)
    {
        try
        {
            return new UTF8Encoding(false, true).GetByteCount(key) <= MaximumObjectKeyBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static InvalidOperationException Invalid(string name) =>
        new($"FileStorage:S3:{name} is missing or invalid.");
}
