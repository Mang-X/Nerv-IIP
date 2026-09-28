using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Nerv.IIP.Iam.Web.Tests;

/// <summary>
/// 没有成员关系的账号登录会被拒（<c>iam-no-membership</c>）。只想验证登录、改密、会话等行为的用例，
/// 建完用户后经管理员把它加进管理员当前的组织环境。
/// </summary>
internal static class IamTestMembership
{
    public static async Task AssignAsync(HttpClient client, string userId)
    {
        var login = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
        login.EnsureSuccessStatusCode();
        var admin = (await login.Content.ReadFromJsonAsync<Envelope>())!.Data!;

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/iam/v1/users/{userId}/membership")
        {
            Content = JsonContent.Create(new { roleIds = new[] { "role-erp-sales" } })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private sealed record Envelope(AdminSession? Data);

    private sealed record AdminSession(string AccessToken);
}
