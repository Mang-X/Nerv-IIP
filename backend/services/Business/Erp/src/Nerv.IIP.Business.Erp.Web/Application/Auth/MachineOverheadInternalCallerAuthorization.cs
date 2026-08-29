using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Erp.Web.Application.Auth;

public static class MachineOverheadInternalCallerAuthorization
{
    public const string SchemeName = "ErpMachineOverheadInternalCaller";
    public const string ReadPolicyName = "ErpMachineOverheadInternalCaller.Read";
    public const string ManagePolicyName = "ErpMachineOverheadInternalCaller.Manage";

    public static IServiceCollection AddErpMachineOverheadInternalCallerAuthorization(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var scopedCallers = configuration.GetSection(
            "Erp:MachineOverheadReconciliation:ScopedCallers");
        services.AddNervIipScopedCallerAuthentication(scopedCallers, SchemeName);
        services.AddNervIipScopedCallerPolicy(
            ReadPolicyName,
            SchemeName,
            ErpPermissionCodes.FinanceRead);
        services.AddNervIipScopedCallerPolicy(
            ManagePolicyName,
            SchemeName,
            ErpPermissionCodes.FinanceManage);
        return services;
    }
}
