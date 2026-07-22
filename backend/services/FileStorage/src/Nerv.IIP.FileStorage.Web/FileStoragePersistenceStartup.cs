namespace Nerv.IIP.FileStorage.Web;

internal sealed record FileStoragePersistenceStartup(
    bool UsePostgreSql,
    bool AutoMigrate)
{
    public static FileStoragePersistenceStartup Validate(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var configuredProvider = configuration["Persistence:Provider"]?.Trim();
        var provider = string.IsNullOrWhiteSpace(configuredProvider)
            ? null
            : configuredProvider;
        var connectionConfigured = !string.IsNullOrWhiteSpace(configuration.GetConnectionString("FileStorageDb"))
            || !string.IsNullOrWhiteSpace(configuration.GetConnectionString("PostgreSQL"));
        var autoMigrate = configuration.GetValue<bool>("Persistence:AutoMigrate");
        var usePostgreSql = string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase);
        var useInMemory = string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase);

        if (autoMigrate && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Persistence:AutoMigrate=true is only allowed for FileStorage in Development. Use scripts/install/migrate-file-storage.ps1 or a migration bundle outside Development.");
        }

        if (usePostgreSql)
        {
            if (!connectionConfigured)
            {
                throw InvalidConfiguration(
                    environment,
                    provider,
                    connectionConfigured,
                    autoMigrate,
                    "PostgreSQL requires ConnectionStrings:FileStorageDb.");
            }

            return new FileStoragePersistenceStartup(UsePostgreSql: true, autoMigrate);
        }

        if (useInMemory && environment.IsDevelopment())
        {
            if (autoMigrate)
            {
                throw InvalidConfiguration(
                    environment,
                    provider,
                    connectionConfigured,
                    autoMigrate,
                    "Persistence:AutoMigrate must be false when Persistence:Provider=InMemory.");
            }

            return new FileStoragePersistenceStartup(UsePostgreSql: false, AutoMigrate: false);
        }

        var remedy = environment.IsDevelopment()
            ? "Development requires an explicit Persistence:Provider of InMemory or PostgreSQL."
            : "Non-Development environments require Persistence:Provider=PostgreSQL and ConnectionStrings:FileStorageDb.";
        throw InvalidConfiguration(environment, provider, connectionConfigured, autoMigrate, remedy);
    }

    private static InvalidOperationException InvalidConfiguration(
        IHostEnvironment environment,
        string? provider,
        bool connectionConfigured,
        bool autoMigrate,
        string remedy)
    {
        var providerStatus = provider ?? "<missing>";
        return new InvalidOperationException(
            $"FileStorage persistence configuration is invalid: environment={environment.EnvironmentName}; provider={providerStatus}; connectionConfigured={connectionConfigured}; autoMigrate={autoMigrate}. {remedy}");
    }
}
