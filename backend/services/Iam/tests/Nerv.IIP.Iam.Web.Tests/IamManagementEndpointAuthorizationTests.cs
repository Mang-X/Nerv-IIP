using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamManagementEndpointAuthorizationTests
{
    [Theory]
    [InlineData("GET", "/api/iam/v1/users")]
    [InlineData("POST", "/api/iam/v1/users")]
    [InlineData("PATCH", "/api/iam/v1/users/user-admin")]
    [InlineData("POST", "/api/iam/v1/users/user-admin/disable")]
    [InlineData("GET", "/api/iam/v1/roles")]
    [InlineData("POST", "/api/iam/v1/roles")]
    [InlineData("PATCH", "/api/iam/v1/roles/role-platform-admin/permissions")]
    public async Task Postgres_management_endpoints_reject_anonymous_callers_before_touching_persistence(string method, string path)
    {
        var environment = PreserveEnvironment(
            "Persistence__Provider",
            "ConnectionStrings__IamDb",
            "Iam__Jwt__SigningKey");

        try
        {
            Environment.SetEnvironmentVariable("Persistence__Provider", "PostgreSQL");
            Environment.SetEnvironmentVariable("ConnectionStrings__IamDb", "Host=localhost;Port=1;Database=nerv_iip_iam_unreachable;Username=nerv;Password=nerv");
            Environment.SetEnvironmentVariable("Iam__Jwt__SigningKey", "test-signing-key-that-is-long-enough-for-local-tests");

            await using var factory = new WebApplicationFactory<Program>();
            var client = factory.CreateClient();

            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            RestoreEnvironment(environment);
        }
    }

    private static IReadOnlyDictionary<string, string?> PreserveEnvironment(params string[] names)
    {
        return names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
    }

    private static void RestoreEnvironment(IReadOnlyDictionary<string, string?> environment)
    {
        foreach (var (name, value) in environment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
