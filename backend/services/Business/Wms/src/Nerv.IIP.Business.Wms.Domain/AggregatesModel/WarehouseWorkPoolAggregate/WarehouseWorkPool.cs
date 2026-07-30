namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseWorkPoolAggregate;

public partial record WarehouseWorkPoolId : IGuidStronglyTypedId;
public partial record WarehouseWorkPoolMembershipId : IGuidStronglyTypedId;

public sealed class WarehouseWorkPool : Entity<WarehouseWorkPoolId>, IAggregateRoot
{
    private WarehouseWorkPool()
    {
    }

    private WarehouseWorkPool(
        string organizationId,
        string environmentId,
        string poolCode,
        string displayName,
        string siteCode)
    {
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        PoolCode = WmsText.Required(poolCode, nameof(poolCode));
        DisplayName = WmsText.Required(displayName, nameof(displayName));
        SiteCode = WmsText.Required(siteCode, nameof(siteCode));
        Active = true;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string PoolCode { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public bool Active { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? DeactivatedAtUtc { get; private set; }

    public static WarehouseWorkPool Create(
        string organizationId,
        string environmentId,
        string poolCode,
        string displayName,
        string siteCode) =>
        new(organizationId, environmentId, poolCode, displayName, siteCode);

    public void Deactivate(DateTime deactivatedAtUtc)
    {
        if (!Active)
        {
            return;
        }

        Active = false;
        DeactivatedAtUtc = EnsureUtc(deactivatedAtUtc, nameof(deactivatedAtUtc));
    }

    private static DateTime EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Warehouse work-pool timestamps must be UTC.", parameterName);
        }

        return value;
    }
}

public sealed class WarehouseWorkPoolMembership
    : Entity<WarehouseWorkPoolMembershipId>, IAggregateRoot
{
    private WarehouseWorkPoolMembership()
    {
    }

    private WarehouseWorkPoolMembership(
        string organizationId,
        string environmentId,
        string poolCode,
        string principalId,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc)
    {
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        PoolCode = WmsText.Required(poolCode, nameof(poolCode));
        PrincipalId = WmsText.Required(principalId, nameof(principalId));
        EffectiveFromUtc = EnsureUtc(effectiveFromUtc, nameof(effectiveFromUtc));
        EffectiveToUtc = effectiveToUtc is null
            ? null
            : EnsureUtc(effectiveToUtc.Value, nameof(effectiveToUtc));
        if (EffectiveToUtc <= EffectiveFromUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveToUtc),
                effectiveToUtc,
                "Warehouse work-pool membership must end after it starts.");
        }

        Active = true;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string PoolCode { get; private set; } = string.Empty;
    public string PrincipalId { get; private set; } = string.Empty;
    public bool Active { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? DeactivatedAtUtc { get; private set; }

    public static WarehouseWorkPoolMembership Create(
        string organizationId,
        string environmentId,
        string poolCode,
        string principalId,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc = null) =>
        new(
            organizationId,
            environmentId,
            poolCode,
            principalId,
            effectiveFromUtc,
            effectiveToUtc);

    public bool IsEffectiveAt(DateTime instantUtc)
    {
        var instant = EnsureUtc(instantUtc, nameof(instantUtc));
        return Active
            && EffectiveFromUtc <= instant
            && (EffectiveToUtc is null || instant < EffectiveToUtc);
    }

    public void Deactivate(DateTime deactivatedAtUtc)
    {
        if (!Active)
        {
            return;
        }

        var instant = EnsureUtc(deactivatedAtUtc, nameof(deactivatedAtUtc));
        if (instant < EffectiveFromUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deactivatedAtUtc),
                deactivatedAtUtc,
                "A membership cannot be deactivated before it starts.");
        }

        Active = false;
        DeactivatedAtUtc = instant;
        if (EffectiveToUtc is null || instant < EffectiveToUtc)
        {
            EffectiveToUtc = instant;
        }
    }

    private static DateTime EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Warehouse work-pool timestamps must be UTC.", parameterName);
        }

        return value;
    }
}
