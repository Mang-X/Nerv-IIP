using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Mes.Web.Application.Andon;

namespace Nerv.IIP.Business.Mes.Web.Tests;

// DomainInvariant: #3652，未配置不得伪造时限/接收人；无效或歧义策略须在配置边界定位。
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class AndonEscalationConfigurationTests
{
    [Theory]
    [InlineData("UnclaimedTimeout", "00:00:00")]
    [InlineData("RecipientId", "")]
    [InlineData("Category", "")]
    [InlineData("EnvironmentId", "")]
    public async Task Invalid_policy_is_rejected_by_host_options_validation(string field, string value)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Mes:AndonEscalation:Policies:0:OrganizationId"] = "org-1",
            ["Mes:AndonEscalation:Policies:0:EnvironmentId"] = "env-1",
            ["Mes:AndonEscalation:Policies:0:Category"] = "Equipment",
            ["Mes:AndonEscalation:Policies:0:UnclaimedTimeout"] = "00:05:00",
            ["Mes:AndonEscalation:Policies:0:RecipientId"] = "supervisor"
        };
        settings[$"Mes:AndonEscalation:Policies:0:{field}"] = value;
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        });
        var error = Assert.Throws<OptionsValidationException>(() => factory.Services.GetRequiredService<IOptions<AndonEscalationOptions>>().Value);
        Assert.Contains("Mes:AndonEscalation", error.Message);
    }
}
