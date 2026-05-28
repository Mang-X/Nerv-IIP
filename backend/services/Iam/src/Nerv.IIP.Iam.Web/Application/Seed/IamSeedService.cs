using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Domain.AggregatesModel.ConnectorHostCredentialAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.ExternalClientAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.SeedAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.Auth;

namespace Nerv.IIP.Iam.Web.Application.Seed;

public sealed class IamSeedService(
    IServiceProvider serviceProvider,
    IOptions<IamSeedOptions> options,
    IamPasswordService passwordService,
    IamTokenService tokenService)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var seed = options.Value;
        if (!seed.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(seed.AdminPassword))
        {
            throw new InvalidOperationException("Iam:Seed:AdminPassword is required when IAM seed is enabled.");
        }

        if (string.IsNullOrWhiteSpace(seed.ConnectorHostSecret))
        {
            throw new InvalidOperationException("Iam:Seed:ConnectorHostSecret is required when IAM seed is enabled.");
        }

        var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var organizationId = new OrganizationId(seed.OrganizationId);
        var environmentId = new IamEnvironmentId(seed.EnvironmentId);
        var adminUserId = new UserId(seed.AdminUserId);
        var adminRoleId = new RoleId(seed.AdminRoleId);
        var membershipId = new MembershipId($"{seed.AdminUserId}:{seed.OrganizationId}:{seed.EnvironmentId}");
        var credentialId = new ConnectorHostCredentialId(seed.ConnectorHostCredentialId);
        var manifestId = new SeedManifestId("iam-default-seed:v1");
        var seedAlreadyApplied = await dbContext.SeedManifests.FindAsync([manifestId], cancellationToken) is not null;
        var now = DateTimeOffset.UtcNow;

        if (await dbContext.Organizations.FindAsync([organizationId], cancellationToken) is null)
        {
            dbContext.Organizations.Add(new Organization(organizationId, seed.OrganizationName, "active"));
        }

        if (await dbContext.Environments.FindAsync([environmentId], cancellationToken) is null)
        {
            dbContext.Environments.Add(new IamEnvironment(environmentId, organizationId, seed.EnvironmentName, "active"));
        }

        var role = await dbContext.Roles
            .Include(x => x.Permissions)
            .SingleOrDefaultAsync(x => x.Id == adminRoleId, cancellationToken);
        if (role is null)
        {
            dbContext.Roles.Add(new Role(adminRoleId, "Platform Administrator", NervIipSeedPermissions.All));
        }
        else if (!SetEquals(role.Permissions.Select(x => x.PermissionCode), NervIipSeedPermissions.All))
        {
            role.ReplacePermissions(NervIipSeedPermissions.All);
        }

        var user = await dbContext.Users.FindAsync([adminUserId], cancellationToken);
        if (user is null)
        {
            user = new User(
                adminUserId,
                seed.AdminLoginName,
                seed.AdminEmail,
                passwordService.Hash(seed.AdminPassword),
                true,
                Guid.NewGuid().ToString("n"),
                1);
            dbContext.Users.Add(user);
        }
        else if (!seedAlreadyApplied && !passwordService.Verify(user, seed.AdminPassword))
        {
            user.UpdatePasswordHash(passwordService.Hash(seed.AdminPassword));
        }

        var membership = await dbContext.Memberships
            .Include(x => x.Roles)
            .SingleOrDefaultAsync(x => x.Id == membershipId, cancellationToken);
        if (membership is null)
        {
            dbContext.Memberships.Add(new Membership(membershipId, adminUserId, organizationId, environmentId, [adminRoleId]));
        }
        else if (!SetEquals(membership.Roles.Select(x => x.RoleId.Id), [adminRoleId.Id]))
        {
            membership.ReplaceRoles([adminRoleId]);
        }

        var connectorCapabilities = NervIipSeedPermissions.All
            .Where(x => x.StartsWith("connectors.", StringComparison.Ordinal))
            .ToArray();
        var connectorSecretHash = tokenService.HashSecret(seed.ConnectorHostSecret);
        var credential = await dbContext.ConnectorHostCredentials
            .Include(x => x.Capabilities)
            .SingleOrDefaultAsync(x => x.Id == credentialId || x.ConnectorHostId == seed.ConnectorHostId, cancellationToken);
        if (credential is null)
        {
            dbContext.ConnectorHostCredentials.Add(new ConnectorHostCredential(
                credentialId,
                seed.ConnectorHostId,
                organizationId,
                environmentId,
                connectorSecretHash,
                now.AddDays(-1),
                null,
                connectorCapabilities));
        }
        else
        {
            if (!seedAlreadyApplied && !string.Equals(credential.SecretHash, connectorSecretHash, StringComparison.Ordinal))
            {
                credential.ReplaceSecretHash(connectorSecretHash);
            }

            if (!SetEquals(credential.Capabilities.Select(x => x.CapabilityCode), connectorCapabilities))
            {
                credential.ReplaceCapabilities(connectorCapabilities);
            }
        }

        if (!string.IsNullOrWhiteSpace(seed.ExternalClientSecret))
        {
            var externalClientSecretHash = tokenService.HashSecret(seed.ExternalClientSecret);
            var externalClient = await dbContext.ExternalClients
                .SingleOrDefaultAsync(x => x.ClientId == seed.ExternalClientId, cancellationToken);
            if (externalClient is null)
            {
                dbContext.ExternalClients.Add(new ExternalClient(
                    new ExternalClientId(seed.ExternalClientId),
                    seed.ExternalClientId,
                    seed.ExternalClientDisplayName,
                    organizationId,
                    environmentId,
                    externalClientSecretHash,
                    true,
                    1,
                    now.AddDays(-1),
                    null));
            }
            else if (!seedAlreadyApplied && !string.Equals(externalClient.SecretHash, externalClientSecretHash, StringComparison.Ordinal))
            {
                externalClient.ReplaceSecretHash(externalClientSecretHash);
            }

            foreach (var permissionCode in seed.ExternalClientPermissionCodes.Distinct(StringComparer.Ordinal))
            {
                var grantId = new AuthorizationGrantId($"external-client:{seed.ExternalClientId}:{seed.OrganizationId}:{seed.EnvironmentId}:{permissionCode}");
                if (await dbContext.AuthorizationGrants.FindAsync([grantId], cancellationToken) is null)
                {
                    dbContext.AuthorizationGrants.Add(new AuthorizationGrant(
                        grantId,
                        "external-client",
                        seed.ExternalClientId,
                        organizationId,
                        environmentId,
                        permissionCode,
                        "*",
                        "*",
                        now.AddDays(-1),
                        null));
                }
            }
        }

        if (!seedAlreadyApplied)
        {
            dbContext.SeedManifests.Add(new SeedManifest(manifestId, "iam-default-seed", "v1", "iam", now));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool SetEquals(IEnumerable<string> current, IEnumerable<string> desired)
    {
        return current.ToHashSet(StringComparer.Ordinal).SetEquals(desired);
    }
}
