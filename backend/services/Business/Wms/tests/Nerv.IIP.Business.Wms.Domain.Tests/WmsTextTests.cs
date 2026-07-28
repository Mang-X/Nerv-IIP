using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Business.Wms.Domain;

namespace Nerv.IIP.Business.Wms.Domain.Tests;

public sealed class WmsTextTests
{
    [Fact]
    public void Idempotency_key_namespace_cannot_collide_with_a_literal_legacy_hash_key()
    {
        var longKey = new string('x', 150);
        var legacyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(longKey)))
            .ToLowerInvariant();
        var literalLegacyKey = $"wms-key:{legacyHash}";

        Assert.NotEqual(WmsText.IdempotencyKey(longKey), WmsText.IdempotencyKey(literalLegacyKey));
        Assert.Contains(literalLegacyKey, WmsText.ReplayIdempotencyKeys(literalLegacyKey));
    }

    [Fact]
    public void Derived_line_keys_remain_stable_and_distinct()
    {
        var baseKey = WmsText.IdempotencyKey("operation-intent-001");

        Assert.Equal(
            WmsText.LineIdempotencyKey(baseKey, "LINE-001"),
            WmsText.LineIdempotencyKey(baseKey, "LINE-001"));
        Assert.NotEqual(
            WmsText.LineIdempotencyKey(baseKey, "LINE-001"),
            WmsText.LineIdempotencyKey(baseKey, "LINE-002"));
    }

    [Fact]
    public void Replay_keys_include_v1_raw_and_hashed_line_forms_while_writes_remain_v2()
    {
        const string rawKey = "legacy-operation-intent";
        var longKey = new string('k', 128);
        var legacyLineKey = WmsText.LineIdempotencyKey(longKey, "LINE-001");

        Assert.Equal(
            [WmsText.IdempotencyKey(rawKey), rawKey],
            WmsText.ReplayIdempotencyKeys(rawKey));
        Assert.StartsWith("wms-key-v2:", WmsText.IdempotencyKey(rawKey), StringComparison.Ordinal);
        Assert.Contains(legacyLineKey, WmsText.ReplayLineIdempotencyKeys(longKey, "LINE-001"));
        Assert.StartsWith("wms-line:", legacyLineKey, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(129)]
    [InlineData(150)]
    public void Replay_keys_cover_origin_main_long_raw_and_line_persistence(int keyLength)
    {
        var rawKey = new string('k', keyLength);
        var legacyLineKey = WmsText.LineIdempotencyKey(rawKey, "LINE-001");

        Assert.Contains(rawKey, WmsText.ReplayIdempotencyKeys(rawKey));
        Assert.Contains(legacyLineKey, WmsText.ReplayLineIdempotencyKeys(rawKey, "LINE-001"));
        Assert.StartsWith("wms-line:", legacyLineKey, StringComparison.Ordinal);
        Assert.StartsWith("wms-key-v2:", WmsText.IdempotencyKey(rawKey), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wms-key:legacy-literal")]
    [InlineData("wms-key-v2:legacy-literal")]
    [InlineData("wms-line:legacy-literal")]
    public void Replay_keys_preserve_origin_main_reserved_prefix_literals(string rawKey)
    {
        Assert.Contains(rawKey, WmsText.ReplayIdempotencyKeys(rawKey));
        Assert.NotEqual(rawKey, WmsText.IdempotencyKey(rawKey));
    }
}
