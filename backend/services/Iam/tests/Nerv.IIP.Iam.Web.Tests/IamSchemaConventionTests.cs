using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Iam.Domain.AggregatesModel.ConnectorHostCredentialAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.SeedAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserSessionAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Testing.EntityFramework;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamSchemaConventionTests
{
    [Fact]
    public void Iam_schema_metadata_follows_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(Organization),
            typeof(IamEnvironment),
            typeof(User),
            typeof(Role),
            typeof(RolePermission),
            typeof(Membership),
            typeof(MembershipRole),
            typeof(UserSession),
            typeof(ConnectorHostCredential),
            typeof(ConnectorHostCredentialCapability),
            typeof(SeedManifest),
        };

        var stringKeys = new[]
        {
            new StringKeyRule(typeof(Organization), nameof(Organization.Id)),
            new StringKeyRule(typeof(IamEnvironment), nameof(IamEnvironment.Id)),
            new StringKeyRule(typeof(User), nameof(User.Id)),
            new StringKeyRule(typeof(Role), nameof(Role.Id)),
            new StringKeyRule(typeof(RolePermission), nameof(RolePermission.Id)),
            new StringKeyRule(typeof(Membership), nameof(Membership.Id)),
            new StringKeyRule(typeof(MembershipRole), nameof(MembershipRole.Id)),
            new StringKeyRule(typeof(UserSession), nameof(UserSession.Id)),
            new StringKeyRule(typeof(ConnectorHostCredential), nameof(ConnectorHostCredential.Id)),
            new StringKeyRule(typeof(ConnectorHostCredentialCapability), nameof(ConnectorHostCredentialCapability.Id)),
            new StringKeyRule(typeof(SeedManifest), nameof(SeedManifest.Id)),
        };

        var failures = new List<string>();
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, "IAM", businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, "IAM", businessEntities));
        failures.AddRange(SchemaConventionAssertions.StringStronglyTypedKeysAreExplicit(fixture.DbContext, "IAM", stringKeys));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, "IAM", "iam"));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Role_name_uniqueness_uses_normalized_database_index()
    {
        using var fixture = CreateFixture();
        var roleEntity = fixture.DbContext.Model.FindEntityType(typeof(Role));

        Assert.NotNull(roleEntity);
        var normalizedRoleName = roleEntity.FindProperty("NormalizedRoleName");
        Assert.NotNull(normalizedRoleName);
        Assert.True(normalizedRoleName.IsNullable == false);
        Assert.Equal(128, normalizedRoleName.GetMaxLength());

        Assert.Contains(roleEntity.GetIndexes(), index =>
            index.IsUnique
            && index.Properties.Count == 1
            && index.Properties[0].Name == "NormalizedRoleName");
        Assert.DoesNotContain(roleEntity.GetIndexes(), index =>
            index.IsUnique
            && index.Properties.Count == 1
            && index.Properties[0].Name == nameof(Role.RoleName));
    }

    private static SchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "PostgreSQL",
                ["ConnectionStrings:IamDb"] = "Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv",
            })
            .Build();
        services.AddIamPersistence(configuration);

        return new SchemaFixture(services.BuildServiceProvider());
    }

    private sealed class SchemaFixture : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly IServiceScope scope;

        public SchemaFixture(ServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
            scope = serviceProvider.CreateScope();
            DbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        }

        public ApplicationDbContext DbContext { get; }

        public void Dispose()
        {
            DbContext.Dispose();
            scope.Dispose();
            serviceProvider.Dispose();
        }
    }
}
