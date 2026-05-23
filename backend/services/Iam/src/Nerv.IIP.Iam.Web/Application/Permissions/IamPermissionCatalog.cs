using Nerv.IIP.Iam.Domain;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Permissions;

public sealed record PermissionCatalogResponse(IReadOnlyList<PermissionCatalogItemResponse> Items);
public sealed record PermissionCatalogItemResponse(string Code, string Domain, string Description, bool Seeded);

public static class IamPermissionCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["iam.users.read"] = "Read IAM users.",
        ["iam.users.manage"] = "Create, update, disable and reset IAM users.",
        ["iam.roles.read"] = "Read IAM roles and permission catalog.",
        ["iam.roles.manage"] = "Create IAM roles and update role permissions.",
        ["iam.sessions.read"] = "Read IAM user sessions.",
        ["iam.sessions.revoke"] = "Revoke IAM user sessions.",
        ["connectors.registrations.write"] = "Register connector hosts.",
        ["connectors.heartbeats.write"] = "Write connector host heartbeats.",
        ["connectors.state-snapshots.write"] = "Write connector host state snapshots.",
        ["apphub.instances.read"] = "Read AppHub application instances.",
        ["files.upload"] = "Upload files.",
        ["files.read"] = "Read file metadata.",
        ["files.download-grants.create"] = "Create file download grants.",
        ["files.archive"] = "Archive files.",
        ["ops.tasks.create"] = "Create operation tasks.",
        ["ops.tasks.read"] = "Read operation tasks.",
        ["ops.results.write"] = "Write operation results.",
        ["ops.audit.read"] = "Read operation audit records.",
        ["notifications.intents.submit"] = "Submit notification intents.",
        ["notifications.messages.read"] = "Read notification messages.",
        ["notifications.messages.mark-read"] = "Mark notification messages as read.",
        ["notifications.tasks.read"] = "Read notification tasks.",
        ["business.quality.inspection-plans.manage"] = "Create, activate and supersede quality inspection plans.",
        ["business.quality.inspection-records.create"] = "Create quality inspection records.",
        ["business.quality.inspection-records.read"] = "Read quality inspection plans and records.",
        ["business.quality.ncr.read"] = "Read quality nonconformance reports.",
        ["business.quality.ncr.manage"] = "Create, disposition and close quality nonconformance reports."
    };

    private static readonly HashSet<string> SeedCodes = NervIipSeedPermissions.All.ToHashSet(StringComparer.Ordinal);

    public static PermissionCatalogResponse List()
    {
        var items = NervIipSeedPermissions.All
            .Select(code => new PermissionCatalogItemResponse(
                code,
                GetDomain(code),
                Descriptions.GetValueOrDefault(code, code),
                true))
            .ToArray();
        return new PermissionCatalogResponse(items);
    }

    public static string[] EnsureSeeded(IEnumerable<string> permissionCodes)
    {
        var codes = permissionCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var unknown = codes
            .Where(code => !SeedCodes.Contains(code))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new KnownException($"Unknown permission code '{unknown[0]}'.");
        }

        return codes;
    }

    private static string GetDomain(string code)
    {
        var separator = code.IndexOf('.', StringComparison.Ordinal);
        return separator > 0 ? code[..separator] : code;
    }
}
