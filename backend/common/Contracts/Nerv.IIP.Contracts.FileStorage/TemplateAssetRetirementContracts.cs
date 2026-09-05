namespace Nerv.IIP.Contracts.FileStorage;

/// <summary>
/// Second-hop v1 capability. Payload is unpadded Base64URL of UTF-8 length-prefixed fields,
/// separated by LF: version, algorithm, issuer, audience, issuedAt, expiresAt, decisionId,
/// replayPolicyVersion, clientWindowSeconds, barcodeLeaseSeconds, barcodeMaxBackoffSeconds,
/// organizationId, environmentId, fileId, checksum, ownerService, ownerType, ownerId, purpose.
/// Numeric fields are invariant decimal integers; timestamps are Unix seconds. Signature is
/// unpadded Base64URL HMAC-SHA-256 over the decoded payload, using the active second-hop key.
/// </summary>
public sealed record RetireTemplateAssetRequest(string Payload, string Signature);

/// <summary>Acceptance releases quota but does not certify physical deletion or start terminal retention.</summary>
public sealed record RetireTemplateAssetResponse(
    string DecisionId,
    string FileId,
    string Status,
    DateTimeOffset QuotaReleasedAtUtc,
    long ReplayHorizonSeconds);
