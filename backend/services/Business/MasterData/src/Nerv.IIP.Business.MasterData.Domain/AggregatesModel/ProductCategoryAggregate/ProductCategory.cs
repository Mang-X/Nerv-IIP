using Nerv.IIP.Business.MasterData.Domain.DomainEvents;

namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductCategoryAggregate;

public partial record ProductCategoryId : IGuidStronglyTypedId;

public sealed class ProductCategory : Entity<ProductCategoryId>, IAggregateRoot
{
    private ProductCategory()
    {
    }

    private ProductCategory(
        string organizationId,
        string environmentId,
        string categoryCode,
        string categoryName,
        string? parentCode,
        string? description)
    {
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        CategoryCode = Required(categoryCode);
        CategoryName = Required(categoryName);
        ParentCode = NormalizeParent(parentCode, CategoryCode);
        Description = Optional(description);
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        this.AddDomainEvent(new MasterDataAggregateCreatedDomainEvent(nameof(ProductCategory), OrganizationId, EnvironmentId, CategoryCode));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string CategoryCode { get; private set; } = string.Empty;
    public string CategoryName { get; private set; } = string.Empty;
    public string? ParentCode { get; private set; }
    public string? Description { get; private set; }
    public bool Disabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static ProductCategory Create(
        string organizationId,
        string environmentId,
        string categoryCode,
        string categoryName,
        string? parentCode,
        string? description)
    {
        return new ProductCategory(organizationId, environmentId, categoryCode, categoryName, parentCode, description);
    }

    public void Update(string categoryName, string? parentCode, string? description)
    {
        EnsureEnabled();
        CategoryName = Required(categoryName);
        ParentCode = NormalizeParent(parentCode, CategoryCode);
        Description = Optional(description);
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(ProductCategory), OrganizationId, EnvironmentId, CategoryCode));
    }

    public void Disable(string reason)
    {
        var validReason = Required(reason);
        EnsureEnabled();
        Disabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateDisabledDomainEvent(nameof(ProductCategory), OrganizationId, EnvironmentId, CategoryCode, validReason));
    }

    public void Enable(string reason)
    {
        _ = Required(reason);
        if (!Disabled)
        {
            return;
        }

        Disabled = false;
        UpdatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new MasterDataAggregateUpdatedDomainEvent(nameof(ProductCategory), OrganizationId, EnvironmentId, CategoryCode));
    }

    private void EnsureEnabled()
    {
        if (Disabled)
        {
            throw new InvalidOperationException("Disabled product category cannot be changed.");
        }
    }

    private static string? NormalizeParent(string? parentCode, string categoryCode)
    {
        var normalized = Optional(parentCode);
        if (normalized is not null && string.Equals(normalized, categoryCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Product category cannot reference itself as parent.", nameof(parentCode));
        }

        return normalized;
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
