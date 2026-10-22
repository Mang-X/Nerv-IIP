using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SharedResponseDataEndpointResults = Nerv.IIP.Web.ResponseDataEndpointResults;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class ResponseDataEndpointResultsTests
{
    [Fact]
    public async Task WriteDataAsync_writes_public_success_contract()
    {
        var context = CreateContext();

        await SharedResponseDataEndpointResults.WriteDataAsync(
            context,
            StatusCodes.Status201Created,
            new SampleResponse("sample"),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);
        Assert.Equal(
            "{\"data\":{\"value\":\"sample\"},\"success\":true,\"message\":\"\",\"code\":0,\"errorData\":[]}",
            await ReadBodyAsync(context));
    }

    [Fact]
    public async Task WriteErrorAsync_writes_public_error_contract()
    {
        var context = CreateContext();

        await SharedResponseDataEndpointResults.WriteErrorAsync(
            context,
            StatusCodes.Status422UnprocessableEntity,
            "invalid-sample",
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);
        Assert.Equal(
            "{\"success\":false,\"message\":\"invalid-sample\",\"code\":422,\"errorData\":[]}",
            await ReadBodyAsync(context));
    }

    [Fact]
    public async Task WriteDataAsync_uses_explicit_json_options()
    {
        var context = CreateContext();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        await SharedResponseDataEndpointResults.WriteDataAsync(
            context,
            StatusCodes.Status200OK,
            new SampleResponse("sample"),
            CancellationToken.None,
            options);

        using var document = JsonDocument.Parse(await ReadBodyAsync(context));
        Assert.Equal("sample", document.RootElement.GetProperty("data").GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("error_data").ValueKind);
        Assert.False(document.RootElement.TryGetProperty("errorData", out _));
    }

    [Fact]
    public async Task WriteDataAsync_propagates_cancellation()
    {
        var context = CreateContext();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SharedResponseDataEndpointResults.WriteDataAsync(
                context,
                StatusCodes.Status200OK,
                new SampleResponse("sample"),
                cancellation.Token));
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync(CancellationToken.None);
    }

    private sealed record SampleResponse(string Value);
}
