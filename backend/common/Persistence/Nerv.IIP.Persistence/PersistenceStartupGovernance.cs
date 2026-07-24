using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Nerv.IIP.Persistence;

public sealed record PersistenceStartupRequirements(
    string ServiceName,
    IReadOnlyList<string> PostgreSqlConnectionStringNames)
{
    public string NonDevelopmentMigrationRemedy { get; init; } =
        "Use an explicit migrator, release script or migration bundle outside Development.";
}

public sealed record PersistenceStartupDecision(
    bool UsePostgreSql,
    bool AutoMigrate,
    string? PostgreSqlConnectionStringName);

public static class PersistenceStartupGovernance
{
    public static PersistenceStartupDecision Resolve(
        IConfiguration configuration,
        IHostEnvironment environment,
        PersistenceStartupRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(requirements);
        ValidateRequirements(requirements);

        var configuredProvider = configuration["Persistence:Provider"]?.Trim();
        var provider = string.IsNullOrWhiteSpace(configuredProvider) ? null : configuredProvider;
        var usePostgreSql = string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase);
        var useInMemory = string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase);
        var providerStatus = usePostgreSql
            ? "PostgreSQL"
            : useInMemory
                ? "InMemory"
                : provider ?? "<missing>";
        var postgreSqlConnectionStringName = requirements.PostgreSqlConnectionStringNames.FirstOrDefault(
            name => !string.IsNullOrWhiteSpace(configuration.GetConnectionString(name)));
        var connectionConfigured = postgreSqlConnectionStringName is not null;
        var autoMigrate = configuration.GetValue<bool>("Persistence:AutoMigrate");

        if (autoMigrate && !environment.IsDevelopment())
        {
            throw InvalidConfiguration(
                requirements.ServiceName,
                environment.EnvironmentName,
                providerStatus,
                connectionConfigured,
                autoMigrate,
                $"Persistence:AutoMigrate=true is only allowed for {requirements.ServiceName} in Development. {requirements.NonDevelopmentMigrationRemedy}");
        }

        if (usePostgreSql)
        {
            if (!connectionConfigured)
            {
                throw InvalidConfiguration(
                    requirements.ServiceName,
                    environment.EnvironmentName,
                    providerStatus,
                    connectionConfigured,
                    autoMigrate,
                    $"PostgreSQL requires ConnectionStrings:{requirements.PostgreSqlConnectionStringNames[0]}.");
            }

            return new PersistenceStartupDecision(
                UsePostgreSql: true,
                autoMigrate,
                postgreSqlConnectionStringName);
        }

        if (useInMemory && environment.IsDevelopment())
        {
            if (autoMigrate)
            {
                throw InvalidConfiguration(
                    requirements.ServiceName,
                    environment.EnvironmentName,
                    providerStatus,
                    connectionConfigured,
                    autoMigrate,
                    "Persistence:AutoMigrate must be false when Persistence:Provider=InMemory.");
            }

            return new PersistenceStartupDecision(
                UsePostgreSql: false,
                AutoMigrate: false,
                PostgreSqlConnectionStringName: null);
        }

        var remedy = environment.IsDevelopment()
            ? "Development requires an explicit Persistence:Provider of InMemory or PostgreSQL."
            : $"Non-Development environments require Persistence:Provider=PostgreSQL and ConnectionStrings:{requirements.PostgreSqlConnectionStringNames[0]}.";
        throw InvalidConfiguration(
            requirements.ServiceName,
            environment.EnvironmentName,
            providerStatus,
            connectionConfigured,
            autoMigrate,
            remedy);
    }

    private static void ValidateRequirements(PersistenceStartupRequirements requirements)
    {
        if (string.IsNullOrWhiteSpace(requirements.ServiceName))
        {
            throw new ArgumentException("Persistence service name is required.", nameof(requirements));
        }

        if (requirements.PostgreSqlConnectionStringNames.Count == 0
            || requirements.PostgreSqlConnectionStringNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "At least one non-empty PostgreSQL connection-string name is required.",
                nameof(requirements));
        }
    }

    private static InvalidOperationException InvalidConfiguration(
        string serviceName,
        string environmentName,
        string providerStatus,
        bool connectionConfigured,
        bool autoMigrate,
        string remedy)
    {
        return new InvalidOperationException(
            $"{serviceName} persistence configuration is invalid: environment={environmentName}; provider={providerStatus}; connectionConfigured={connectionConfigured}; autoMigrate={autoMigrate}. {remedy}");
    }
}
