using Nerv.IIP.Business.Erp.Domain.AggregatesModel;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.GLAccountAggregate;

public partial record GLAccountId : IGuidStronglyTypedId;

public enum GLAccountType
{
    Asset,
    Liability,
    Equity,
    Revenue,
    Expense,
}

public sealed class GLAccount : Entity<GLAccountId>, IAggregateRoot
{
    private GLAccount() { }

    private GLAccount(string organizationId, string environmentId, string code, string name, GLAccountType type, string? parentCode)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        Code = ErpText.Required(code, nameof(code));
        Name = ErpText.Required(name, nameof(name));
        Type = type;
        ParentCode = string.IsNullOrWhiteSpace(parentCode) ? null : parentCode.Trim();
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public GLAccountType Type { get; private set; }
    public string? ParentCode { get; private set; }

    public static GLAccount Create(string organizationId, string environmentId, string code, string name, GLAccountType type, string? parentCode = null)
        => new(organizationId, environmentId, code, name, type, parentCode);
}
