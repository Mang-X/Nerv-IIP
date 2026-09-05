using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Auth;
using Nerv.IIP.Contracts.BarcodeLabel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using Nerv.IIP.Business.BarcodeLabel.Web.Endpoints.BarcodeLabel;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

// PublicContract：#2101 合并报告的第一跳 v1 字段表与时间边界；测试编码独立于生产 helper。
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class TemplateAssetRetirementProofTests
{
    [Theory]
    [InlineData("SecretBase64", "")]
    [InlineData("SecretBase64", "not-base64")]
    [InlineData("SecretBase64", "YWJj")]
    [InlineData("Issuer", "")]
    [InlineData("Audience", "")]
    public void Missing_or_invalid_proof_configuration_fails_host_start(string key, string value)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["InternalService:BearerToken"] = "retirement-startup-token",
                    ["TemplateAssetRetirementProof:" + key] = value,
                }));
        });
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task Openapi_and_endpoint_contract_declare_retirement_permission_and_proof_error()
    {
        var contract = BarcodeLabelEndpointContracts.Get<RetireTemplateAssetEndpoint>();
        Assert.Equal("business.barcodes.template-assets.retire", contract.PermissionCode);
        Assert.Equal("POST", contract.HttpMethod);
        Assert.Equal("retireBusinessBarcodeTemplateAsset", contract.OperationId);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("InternalService:BearerToken", "retirement-openapi-token");
        });
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var operation = document.RootElement.GetProperty("paths")
            .GetProperty("/api/business/v1/barcodes/template-assets/retire").GetProperty("post");
        Assert.Equal("retireBusinessBarcodeTemplateAsset", operation.GetProperty("operationId").GetString());
        Assert.True(operation.GetProperty("responses").TryGetProperty("403", out var forbidden));
        Assert.Contains("TemplateAssetRetirementProofError", forbidden.GetRawText());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var reference = operation.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()!;
        var schema = schemas.GetProperty(reference.Split('/')[^1]).GetProperty("properties");
        Assert.True(schema.TryGetProperty("proof", out _));
    }

    [Fact]
    public void Valid_proof_binds_unicode_newlines_and_every_request_field()
    {
        var request = RetirementProofCases.Request();
        Assert.Equal(RetirementProofCases.Digest(request), TemplateAssetRetirementProofV1.RequestDigest(request));
        Assert.Equal(RetirementProofCases.Payload(RetirementProofCases.Fields(request)),
            TemplateAssetRetirementProofV1.EncodePayload("business-gateway-test", "barcode-label-test",
                RetirementProofCases.Now, RetirementProofCases.Now + 300,
                "user-3042", "business.barcodes.template-assets.retire", RetirementProofCases.Digest(request)));
        Assert.Equal("user-3042", Verifier().Verify(request));
        foreach (var (name, invalid) in RetirementProofCases.InvalidRequests(request))
            Assert.True(Verifier().Verify(invalid) is null, name);
    }

    [Theory]
    [InlineData(-598, -299, true)]
    [InlineData(-599, -300, false)]
    [InlineData(-600, -301, false)]
    [InlineData(299, 300, true)]
    [InlineData(300, 301, false)]
    [InlineData(301, 302, false)]
    [InlineData(0, 1, true)]
    [InlineData(0, 300, true)]
    [InlineData(0, 301, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, -1, false)]
    public void Clock_and_ttl_boundaries_are_independent_of_signature(int issued, int expires, bool accepted)
    {
        var request = RetirementProofCases.Request();
        var fields = RetirementProofCases.Fields(request);
        fields[4] = (RetirementProofCases.Now + issued).ToString(CultureInfo.InvariantCulture);
        fields[5] = (RetirementProofCases.Now + expires).ToString(CultureInfo.InvariantCulture);
        Assert.Equal(accepted, Verifier().Verify(request with { Proof = RetirementProofCases.Sign(fields) }) is not null);
    }

    internal static TemplateAssetRetirementProofVerifier Verifier() => new(
        Options.Create(new TemplateAssetRetirementProofOptions
        {
            Issuer = "business-gateway-test", Audience = "barcode-label-test",
            SecretBase64 = Convert.ToBase64String(RetirementProofCases.Secret),
        }), new RetirementProofCases.Clock());
}

internal static class RetirementProofCases
{
    internal const long Now = 1800000000;
    internal static readonly byte[] Secret = Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef");
    internal sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(Now);
    }

    internal static RetireTemplateAssetRequest Request(Guid? templateId = null)
    {
        var request = new RetireTemplateAssetRequest("org-3042", "env-3042",
            templateId ?? Guid.Parse("11111111-2222-3333-4444-555555555555"), "file-3042",
            "sha256:" + new string('a', 64), "旧模板\n不再使用:é", "retire-3042", "");
        return request with { Proof = Sign(Fields(request)) };
    }

    internal static string[] Fields(RetireTemplateAssetRequest request) =>
        ["1", "HMAC-SHA256", "business-gateway-test", "barcode-label-test", Now.ToString(CultureInfo.InvariantCulture),
            (Now + 300).ToString(CultureInfo.InvariantCulture), "user-3042", "business.barcodes.template-assets.retire", Digest(request)];

    internal static string Digest(RetireTemplateAssetRequest request, string action = "retire-template-asset") =>
        Url(SHA256.HashData(Payload([action, request.OrganizationId, request.EnvironmentId,
            request.TemplateId.ToString("D"), request.FileId, request.Checksum, request.Reason, request.IdempotencyKey])));

    internal static byte[] Payload(string[] fields)
    {
        using var stream = new MemoryStream();
        for (var index = 0; index < fields.Length; index++)
        {
            if (index != 0) stream.WriteByte(10);
            var value = Encoding.UTF8.GetBytes(fields[index]);
            stream.Write(Encoding.ASCII.GetBytes(value.Length.ToString(CultureInfo.InvariantCulture) + ":"));
            stream.Write(value);
        }
        return stream.ToArray();
    }

    internal static string Url(byte[] bytes) => Convert.ToBase64String(bytes).Replace("=", "").Replace("+", "-").Replace("/", "_");
    internal static string Sign(string[] fields) => SignBytes(Payload(fields));
    internal static string SignBytes(byte[] payload, byte[]? key = null) =>
        Url(payload) + "." + Url(HMACSHA256.HashData(key ?? Secret, payload));

    internal static IEnumerable<(string Name, RetireTemplateAssetRequest Request)> InvalidRequests(RetireTemplateAssetRequest request)
    {
        var fields = Fields(request);
        foreach (var (index, value, name) in new (int, string, string)[]
        {
            (0, "2", "version"), (1, "SHA256", "algorithm"), (2, "wrong", "issuer"), (3, "wrong", "audience"),
            (5, (Now + 301).ToString(), "over-ttl"),
            (5, Now.ToString(), "zero-ttl"), (5, (Now - 1).ToString(), "negative-ttl"),
            (6, " ", "blank-subject"), (6, new string('u', 201), "oversized-subject"),
            (7, "business.barcodes.templates.manage", "permission"), (8, "wrong", "digest"),
            (4, "+1800000000", "noncanonical-time"), (4, long.MinValue.ToString(), "time-overflow"),
        })
        {
            var changed = (string[])fields.Clone(); changed[index] = value;
            yield return (name, request with { Proof = Sign(changed) });
        }
        var expired = (string[])fields.Clone(); expired[4] = (Now - 599).ToString(); expired[5] = (Now - 300).ToString();
        yield return ("expired", request with { Proof = Sign(expired) });
        foreach (var delta in new[] { 300, 301 })
        {
            var future = (string[])fields.Clone();
            future[4] = (Now + delta).ToString(CultureInfo.InvariantCulture);
            future[5] = (Now + delta + 1).ToString(CultureInfo.InvariantCulture);
            yield return ($"future-issued-{delta}", request with { Proof = Sign(future) });
        }
        var action = (string[])fields.Clone(); action[8] = Digest(request, "other-action");
        yield return ("action", request with { Proof = Sign(action) });
        yield return ("organization", request with { OrganizationId = "other-org" });
        yield return ("environment", request with { EnvironmentId = "other-env" });
        yield return ("template", request with { TemplateId = Guid.Parse("22222222-2222-3333-4444-555555555555") });
        yield return ("file", request with { FileId = "other-file" });
        yield return ("checksum", request with { Checksum = "sha256:" + new string('b', 64) });
        yield return ("reason", request with { Reason = "其他原因" });
        yield return ("idempotency-key", request with { IdempotencyKey = "other-key" });
        yield return ("wrong-key", request with { Proof = SignBytes(Payload(fields), new byte[32]) });
        var tampered = (string[])fields.Clone(); tampered[6] = "other-user";
        yield return ("tampered-subject", request with { Proof = Url(Payload(tampered)) + "." + request.Proof.Split('.')[1] });
        yield return ("missing-signature", request with { Proof = Url(Payload(fields)) });
        yield return ("wrong-signature", request with { Proof = Url(Payload(fields)) + "." + Url(new byte[32]) });
        yield return ("missing-field", request with { Proof = Sign(fields[..8]) });
        yield return ("duplicate-field", request with { Proof = Sign([..fields, fields[8]]) });
        var reordered = (string[])fields.Clone(); (reordered[2], reordered[3]) = (reordered[3], reordered[2]);
        yield return ("field-order", request with { Proof = Sign(reordered) });
        var text = Encoding.UTF8.GetString(Payload(fields));
        yield return ("crlf", request with { Proof = SignBytes(Encoding.UTF8.GetBytes(text.Replace("\n", "\r\n"))) });
        yield return ("bom", request with { Proof = SignBytes([0xef, 0xbb, 0xbf, ..Payload(fields)]) });
        yield return ("length", request with { Proof = SignBytes(Encoding.UTF8.GetBytes("01" + text[1..])) });
        yield return ("bad-length", request with { Proof = SignBytes(Encoding.UTF8.GetBytes("2" + text[1..])) });
        yield return ("utf8", request with { Proof = SignBytes([..Payload(fields)[..^1], 0xff]) });
        yield return ("padding", request with { Proof = request.Proof + "=" });
        yield return ("base64-whitespace", request with { Proof = " " + request.Proof });
        yield return ("bad-base64", request with { Proof = "!.!" });
    }
}
