using MediatR;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTemplateAggregate;
using Nerv.IIP.Ops.Infrastructure;
using Nerv.IIP.Ops.Infrastructure.Migrations;
using Nerv.IIP.Testing.EntityFramework;

namespace Nerv.IIP.Ops.Web.Tests;

public sealed class OpsSchemaConventionTests
{
    [Fact]
    public void Ops_schema_metadata_follows_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(OperationTask),
            typeof(OperationAttempt),
            typeof(AuditRecord),
            typeof(OperationTemplate),
        };

        var jsonColumns = new[]
        {
            new JsonColumnRule(typeof(OperationTask), nameof(OperationTask.ParametersJson)),
            new JsonColumnRule(typeof(OperationAttempt), nameof(OperationAttempt.FailureJson)),
            new JsonColumnRule(typeof(OperationTemplate), nameof(OperationTemplate.ParameterSchemaJson)),
        };

        var stringKeys = new[]
        {
            new StringKeyRule(typeof(OperationTask), nameof(OperationTask.Id)),
            new StringKeyRule(typeof(OperationAttempt), nameof(OperationAttempt.Id)),
            new StringKeyRule(typeof(AuditRecord), nameof(AuditRecord.Id)),
            new StringKeyRule(typeof(OperationTemplate), nameof(OperationTemplate.Id)),
        };

        var failures = new List<string>();
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, "Ops", businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, "Ops", businessEntities));
        failures.AddRange(SchemaConventionAssertions.JsonColumnsHaveCompatibilityComments(fixture.DbContext, "Ops", jsonColumns));
        failures.AddRange(SchemaConventionAssertions.StringStronglyTypedKeysAreExplicit(fixture.DbContext, "Ops", stringKeys));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, "Ops", "ops"));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Operation_attempt_lease_claim_columns_allow_legacy_nulls()
    {
        using var fixture = CreateFixture();
        var entity = fixture.DbContext.Model.FindEntityType(typeof(OperationAttempt));
        Assert.NotNull(entity);

        var nullableLeaseClaimColumns = new[]
        {
            nameof(OperationAttempt.LeaseId),
            nameof(OperationAttempt.LeasedAtUtc),
            nameof(OperationAttempt.LeasedUntilUtc),
            nameof(OperationAttempt.AttemptNo),
            nameof(OperationAttempt.MaxAttempts),
        };

        foreach (var column in nullableLeaseClaimColumns)
        {
            var property = entity.FindProperty(column);
            Assert.NotNull(property);
            Assert.True(property.IsNullable, $"{column} must remain nullable so legacy operation_attempts are not treated as expired leases.");
        }

        var migration = new AddOpsLeaseClaimFields();
        var migrationBuilder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AddOpsLeaseClaimFields)
            .GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [migrationBuilder]);
        var addedColumns = migrationBuilder.Operations
            .OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>()
            .ToDictionary(x => x.Name);

        foreach (var column in nullableLeaseClaimColumns)
        {
            Assert.True(addedColumns[column].IsNullable, $"{column} migration column must be nullable for existing rows.");
            Assert.Null(addedColumns[column].DefaultValue);
            Assert.Null(addedColumns[column].DefaultValueSql);
        }
    }

    [Fact]
    public void Audit_records_have_chain_scope_and_unique_sequence_index()
    {
        using var fixture = CreateFixture();
        var entity = fixture.DbContext.Model.FindEntityType(typeof(AuditRecord));
        Assert.NotNull(entity);

        Assert.NotNull(entity.FindProperty(nameof(AuditRecord.OrganizationId)));
        Assert.NotNull(entity.FindProperty(nameof(AuditRecord.EnvironmentId)));

        var uniqueScopeIndex = entity.GetIndexes().SingleOrDefault(index =>
            index.IsUnique
            && index.Properties.Select(x => x.Name).SequenceEqual([
                nameof(AuditRecord.OrganizationId),
                nameof(AuditRecord.EnvironmentId),
                nameof(AuditRecord.SequenceNo)
            ]));

        Assert.NotNull(uniqueScopeIndex);
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
                ["ConnectionStrings:OpsDb"] = "Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv",
            })
            .Build();
        services.AddOpsPersistence(configuration, usePostgreSql: true);

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
