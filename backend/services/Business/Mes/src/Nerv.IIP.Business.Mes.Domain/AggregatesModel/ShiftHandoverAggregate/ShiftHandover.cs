namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.ShiftHandoverAggregate;

public partial record ShiftHandoverId : IGuidStronglyTypedId;

public sealed class ShiftHandover : Entity<ShiftHandoverId>, IAggregateRoot
{
    public const string OpenStatus = "Open";
    public const string AcceptedStatus = "Accepted";

    private ShiftHandover()
    {
    }

    private ShiftHandover(
        string organizationId,
        string environmentId,
        string handoverNo,
        string shiftId,
        string teamId,
        int openIssueCount,
        DateTimeOffset createdAtUtc)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        HandoverNo = DomainGuard.Required(handoverNo, nameof(handoverNo));
        ShiftId = DomainGuard.Required(shiftId, nameof(shiftId));
        TeamId = DomainGuard.Required(teamId, nameof(teamId));
        OpenIssueCount = openIssueCount >= 0
            ? openIssueCount
            : throw new ArgumentOutOfRangeException(nameof(openIssueCount), "Open issue count cannot be negative.");
        HandoverStatus = OpenStatus;
        CreatedAtUtc = createdAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string HandoverNo { get; private set; } = string.Empty;
    public string ShiftId { get; private set; } = string.Empty;
    public string TeamId { get; private set; } = string.Empty;
    public string HandoverStatus { get; private set; } = string.Empty;
    public int OpenIssueCount { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? AcceptedAtUtc { get; private set; }

    public static ShiftHandover Create(
        string organizationId,
        string environmentId,
        string handoverNo,
        string shiftId,
        string teamId,
        int openIssueCount,
        DateTimeOffset createdAtUtc)
    {
        return new ShiftHandover(
            organizationId,
            environmentId,
            handoverNo,
            shiftId,
            teamId,
            openIssueCount,
            createdAtUtc);
    }

    public void Accept(DateTimeOffset acceptedAtUtc)
    {
        if (HandoverStatus == AcceptedStatus)
        {
            return;
        }

        if (HandoverStatus != OpenStatus)
        {
            throw new InvalidOperationException("Only open shift handover can be accepted.");
        }

        HandoverStatus = AcceptedStatus;
        AcceptedAtUtc = acceptedAtUtc;
    }
}
