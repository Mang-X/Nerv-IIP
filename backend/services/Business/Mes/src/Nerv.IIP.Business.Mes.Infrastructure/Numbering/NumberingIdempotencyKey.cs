#pragma warning disable S1144 // EF Core sets surrogate identifiers through materialization.
namespace Nerv.IIP.Business.Mes.Infrastructure.Numbering;

public sealed class NumberingIdempotencyKey
{
    private NumberingIdempotencyKey() { }

    public NumberingIdempotencyKey(string organizationId, string environmentId, string documentType, string idempotencyKey, string number, string payloadFingerprint, DateTimeOffset createdAtUtc)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        DocumentType = documentType;
        IdempotencyKey = idempotencyKey;
        Number = number;
        PayloadFingerprint = payloadFingerprint;
        CreatedAtUtc = createdAtUtc;
    }

    public long Id { get; private set; }
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DocumentType { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string Number { get; private set; } = string.Empty;
    public string PayloadFingerprint { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
