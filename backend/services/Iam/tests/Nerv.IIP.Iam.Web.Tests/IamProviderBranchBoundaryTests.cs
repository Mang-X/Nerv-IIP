namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamProviderBranchBoundaryTests
{
    [Fact]
    public void Endpoint_sources_do_not_branch_on_persistence_provider_or_touch_store_implementations()
    {
        var violations = SourceFiles("src/Nerv.IIP.Iam.Web/Endpoints")
            .SelectMany(file => ForbiddenEndpointTokens
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Relative(file)} contains '{token}'"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void User_application_handlers_use_persistence_abstractions_instead_of_provider_detection()
    {
        var violations = SourceFiles("src/Nerv.IIP.Iam.Web/Application")
            .Where(file => file.Contains($"{Path.DirectorySeparatorChar}Commands{Path.DirectorySeparatorChar}Users{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}Queries{Path.DirectorySeparatorChar}Users{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => ForbiddenApplicationTokens
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Relative(file)} contains '{token}'"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Auth_roles_and_sessions_application_services_do_not_use_ef_core_or_manual_transactions()
    {
        var applicationDirectories = new[]
        {
            $"Application{Path.DirectorySeparatorChar}Auth",
            $"Application{Path.DirectorySeparatorChar}Roles",
            $"Application{Path.DirectorySeparatorChar}Sessions"
        };

        var violations = SourceFiles("src/Nerv.IIP.Iam.Web/Application")
            .Where(file => applicationDirectories.Any(directory => file.Contains(directory, StringComparison.Ordinal)))
            .SelectMany(file => ForbiddenPostgresApplicationTokens
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Relative(file)} contains '{token}'"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Role_endpoints_do_not_use_exceptions_as_not_implemented_control_flow()
    {
        var violations = SourceFiles("src/Nerv.IIP.Iam.Web/Endpoints/Roles")
            .SelectMany(file => ForbiddenNotImplementedControlFlowTokens
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Relative(file)} contains '{token}'"))
            .ToArray();

        Assert.Empty(violations);
    }

    private static readonly string[] ForbiddenEndpointTokens =
    [
        "Persistence:Provider",
        "IsPostgreSql",
        "IsInMemory",
        "InMemoryIamStore",
        "ApplicationDbContext"
    ];

    private static readonly string[] ForbiddenApplicationTokens =
    [
        "IServiceProvider",
        "GetService<IUserRepository>",
        "GetRequiredService<InMemoryIamStore>",
        "InMemoryIamStore"
    ];

    private static readonly string[] ForbiddenPostgresApplicationTokens =
    [
        "ApplicationDbContext",
        "Microsoft.EntityFrameworkCore",
        "SaveChangesAsync",
        "BeginTransaction",
        "ExecuteUpdateAsync",
        ".Database"
    ];

    private static readonly string[] ForbiddenNotImplementedControlFlowTokens =
    [
        "NotImplementedException"
    ];

    private static IEnumerable<string> SourceFiles(string relativeDirectory)
    {
        return Directory.EnumerateFiles(Path.Combine(IamServiceRoot(), relativeDirectory), "*.cs", SearchOption.AllDirectories);
    }

    private static string IamServiceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "backend", "services", "Iam");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate backend/services/Iam from test output directory.");
    }

    private static string Relative(string file)
    {
        return Path.GetRelativePath(IamServiceRoot(), file);
    }
}
