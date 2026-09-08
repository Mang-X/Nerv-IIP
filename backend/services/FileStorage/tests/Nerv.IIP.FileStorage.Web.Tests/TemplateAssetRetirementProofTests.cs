using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Domain;
using Nerv.IIP.FileStorage.Web.Application.Files;

namespace Nerv.IIP.FileStorage.Web.Tests;

// Oracle: #3028 retirement report, v1 second-hop field table and replay formula; independent wire producer.
public sealed class TemplateAssetRetirementProofTests
{
    internal const string Secret = "ZmlsZXN0b3JhZ2UtcmV0aXJlbWVudC10ZXN0LWtleS0zMDQ0";
    internal static readonly DateTimeOffset Epoch = new(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(2592000, 300, 300, 604800, 300, 300, 300, 2592000)]
    [InlineData(1, 300, 300, 800000, 300, 300, 300, 800600)]
    [InlineData(1, 900000, 300, 0, 1, 300, 400, 900800)]
    [InlineData(691199, 1, 1, 0, 1, 1, 1, 691200)]
    [InlineData(691200, 1, 1, 0, 1, 1, 1, 691200)]
    [InlineData(691201, 1, 1, 0, 1, 1, 1, 691201)]
    [InlineData(7775999, 1, 1, 0, 1, 1, 1, 7775999)]
    [InlineData(7776000, 1, 1, 0, 1, 1, 1, 7776000)]
    [InlineData(7776001, 1, 1, 0, 1, 1, 1, 7776000)]
    [InlineData(1, 1, 1, 7775998, 1, 1, 1, 7776000)]
    [InlineData(1, 7775998, 1, 0, 1, 1, 1, 7776000)]
    public void Horizon_uses_frozen_maxima_and_only_clamps_client_demand(long client, long blLease,
        long blBackoff, long grace, long interval, long fsLease, long fsBackoff, long expected) =>
        Assert.Equal(expected, RetirementReplayPolicy.Resolve(client, blLease, blBackoff,
            new(grace, interval, fsLease, fsBackoff)));

    [Theory]
    [InlineData("FileStorage:TemplateAssetRetirement:Secret", "")]
    [InlineData("FileStorage:TemplateAssetRetirement:Secret", "not-base64")]
    [InlineData("FileStorage:TemplateAssetRetirement:Secret", "YQ==")]
    [InlineData("FileStorage:TemplateAssetRetirement:Issuer", "")]
    [InlineData("FileStorage:TemplateAssetRetirement:Audience", "")]
    [InlineData("FileStorage:TemplateAssetRetirement:LeaseSeconds", "0")]
    [InlineData("FileStorage:TemplateAssetRetirement:LeaseSeconds", "-1")]
    [InlineData("FileStorage:TemplateAssetRetirement:MaxBackoffSeconds", "0")]
    [InlineData("FileStorage:TemplateAssetRetirement:MaxBackoffSeconds", "-1")]
    [InlineData("FileStorage:GarbageCollection:IntervalSeconds", "0")]
    [InlineData("FileStorage:GarbageCollection:PhysicalDeleteGraceSeconds", "-1")]
    [InlineData("FileStorage:GarbageCollection:PhysicalDeleteGraceSeconds", "7776000")]
    [InlineData("FileStorage:TemplateAssetRetirement:LeaseSeconds", "7776000")]
    [InlineData("FileStorage:TemplateAssetRetirement:MaxBackoffSeconds", "9223372036854775807")]
    public void Invalid_retirement_configuration_fails_host_startup(string key, string value)
    {
        using var factory = new FileStorageWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.UseSetting(key, value));
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains(TemplateAssetRetirementOptions.Section, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(0, 300, true)]
    [InlineData(0, 0, false)]
    [InlineData(0, -1, false)]
    [InlineData(0, 301, false)]
    [InlineData(299, 300, true)]
    [InlineData(300, 301, false)]
    [InlineData(301, 302, false)]
    [InlineData(-301, -299, true)]
    [InlineData(-301, -300, false)]
    [InlineData(-302, -301, false)]
    public void Clock_and_ttl_have_independent_inclusive_rejection_edges(int issuedOffset, int expiresOffset, bool accepted)
    {
        var fields = Fields();
        fields[4] = (Epoch.ToUnixTimeSeconds() + issuedOffset).ToString(CultureInfo.InvariantCulture);
        fields[5] = (Epoch.ToUnixTimeSeconds() + expiresOffset).ToString(CultureInfo.InvariantCulture);
        var proof = new TemplateAssetRetirementProof(Options(), new FakeTimeProvider(Epoch));
        Assert.Equal(accepted, proof.Verify(Sign(fields)) is not null);
    }

    internal static TemplateAssetRetirementOptions Options() => new(Convert.FromBase64String(Secret),
        "business-barcode-label", "file-storage", new(604800, 300, 300, 300));

    internal static string[] Fields(string fileId = "retirement-file") =>
    [
        "1", "HMAC-SHA-256", "business-barcode-label", "file-storage",
        Epoch.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        (Epoch.ToUnixTimeSeconds() + 300).ToString(CultureInfo.InvariantCulture),
        "01991000-0000-7000-8000-000000003044", "1", "2592000", "300", "300",
        "retirement-org", "retirement-env", fileId, $"sha256:{new string('c', 64)}",
        "business-barcode-label", "label-template", "模板甲", "barcode-label-template"
    ];

    internal static byte[] Wire(IEnumerable<string> fields) => Encoding.UTF8.GetBytes(
        string.Join('\n', fields.Select(value => $"{Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture)}:{value}")));

    internal static RetireTemplateAssetRequest Sign(string[] fields) => SignBytes(Wire(fields));
    internal static RetireTemplateAssetRequest SignBytes(byte[] payload, byte[]? key = null) =>
        new(Url(payload), Url(HMACSHA256.HashData(key ?? Convert.FromBase64String(Secret), payload)));
    internal static string Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
