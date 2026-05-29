using DotNetCore.CAP;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.Messaging.CAP;
using System.Reflection;
using Xunit;

namespace Nerv.IIP.Messaging.CAP.Tests;

public sealed class CapMessagingConfigurationTests
{
    [Fact]
    public void UseConfiguredTransport_DefaultProvider_RegistersInMemoryMessageQueue()
    {
        var options = new CapOptions();

        options.UseConfiguredTransport(CreateConfiguration(), "Development");

        var extensionTypeNames = GetExtensionTypeNames(options);
        Assert.Contains(extensionTypeNames, name => name.Contains("InMemory", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(extensionTypeNames, name => name.Contains("RabbitMQ", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("InMemory")]
    public void UseConfiguredTransport_InMemoryProviderOutsideDevelopment_FailsFast(string? provider)
    {
        var options = new CapOptions();
        var values = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
        };

        if (provider is not null)
        {
            values["Messaging:Provider"] = provider;
        }

        var exception = Assert.Throws<InvalidOperationException>(() =>
            options.UseConfiguredTransport(CreateConfiguration(values)));

        Assert.Contains("CAP InMemory transport is only allowed in Development", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Messaging:Provider=RabbitMQ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UseConfiguredTransport_RabbitMqProvider_RegistersRabbitMqTransport()
    {
        var options = new CapOptions();

        options.UseConfiguredTransport(CreateConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "RabbitMQ",
            ["RabbitMQ:HostName"] = "rabbitmq.local",
            ["RabbitMQ:Port"] = "5673",
            ["RabbitMQ:UserName"] = "nerv",
            ["RabbitMQ:Password"] = "secret",
        }));

        var extensionTypeNames = GetExtensionTypeNames(options);
        Assert.Contains(extensionTypeNames, name => name.Contains("RabbitMQ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(extensionTypeNames, name => name.Contains("InMemory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UseConfiguredTransport_UnsupportedProvider_FailsFast()
    {
        var options = new CapOptions();
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Kafka",
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            options.UseConfiguredTransport(configuration));

        Assert.Contains("Unsupported Messaging:Provider 'Kafka'", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?>? values = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
    }

    private static string[] GetExtensionTypeNames(CapOptions options)
    {
        // CAP exposes transport selection only through registered extensions; use reflection narrowly
        // here so the tests can assert provider wiring without starting a broker or service provider.
        var extensionsProperty = typeof(CapOptions).GetProperty(
            "Extensions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(extensionsProperty);

        var extensions = (System.Collections.IEnumerable?)extensionsProperty.GetValue(options);
        Assert.NotNull(extensions);

        return extensions.Cast<object>().Select(x => x.GetType().FullName ?? x.GetType().Name).ToArray();
    }
}
