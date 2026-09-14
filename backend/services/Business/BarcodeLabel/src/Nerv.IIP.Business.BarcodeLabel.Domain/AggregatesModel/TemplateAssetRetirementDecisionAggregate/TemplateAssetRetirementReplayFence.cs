using System.Security.Cryptography;
using System.Text;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;

/// <summary>Permanent non-sensitive facts remaining after the detailed decision expires.</summary>
public sealed class TemplateAssetRetirementReplayFence : Entity<TemplateAssetRetirementDecisionId>, IAggregateRoot
{
    private TemplateAssetRetirementReplayFence() { }

    public TemplateAssetRetirementReplayFence(TemplateAssetRetirementDecision decision, DateTimeOffset replayUntilUtc)
    {
        Id = decision.Id;
        OrganizationId = decision.OrganizationId;
        EnvironmentId = decision.EnvironmentId;
        TemplateFileId = decision.TemplateFileId;
        IdempotencyKeyDigest = DigestKey(decision.IdempotencyKey);
        ReplayUntilUtc = replayUntilUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string TemplateFileId { get; private set; } = string.Empty;
    public string IdempotencyKeyDigest { get; private set; } = string.Empty;
    public DateTimeOffset ReplayUntilUtc { get; private set; }

    public static string DigestKey(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
