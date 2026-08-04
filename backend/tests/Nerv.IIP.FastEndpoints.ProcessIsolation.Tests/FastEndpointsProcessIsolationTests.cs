using System.Text.Json;
using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.FastEndpoints.ProcessIsolation.Tests;

public sealed class FastEndpointsProcessIsolationTests
{
    [Fact]
    public void NewConfig_SeesDeliberateMutationsMadeAfterTestHostStartup()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var firstConfig = new Config();
        var serializer = firstConfig.Serializer;
        var validation = firstConfig.Validation;
        var converter = new TestOnlyJsonConverter();
        var mutatedValidationSetting = !validation.UsePropertyNamingPolicy;

        serializer.Options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        serializer.Options.Converters.Add(converter);
        validation.UsePropertyNamingPolicy = mutatedValidationSetting;

        var secondConfig = new Config();

        Assert.Same(serializer, secondConfig.Serializer);
        Assert.Same(validation, secondConfig.Validation);
        Assert.Same(JsonNamingPolicy.SnakeCaseLower, secondConfig.Serializer.Options.PropertyNamingPolicy);
        Assert.Contains(converter, secondConfig.Serializer.Options.Converters);
        Assert.Equal(mutatedValidationSetting, secondConfig.Validation.UsePropertyNamingPolicy);
    }

    private sealed class TestOnlyJsonConverter : JsonConverter<object>
    {
        public override object? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(
            Utf8JsonWriter writer,
            object value,
            JsonSerializerOptions options) =>
            throw new NotSupportedException();
    }
}
