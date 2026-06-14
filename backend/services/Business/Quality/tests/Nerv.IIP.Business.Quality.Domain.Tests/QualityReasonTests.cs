using Nerv.IIP.Business.Quality.Domain.AggregatesModel.QualityReasonAggregate;

namespace Nerv.IIP.Business.Quality.Domain.Tests;

public sealed class QualityReasonTests
{
    [Fact]
    public void Quality_reason_captures_group_severity_and_default_disposition()
    {
        var reason = QualityReason.Create(
            "org-001",
            "env-dev",
            "QR-SURFACE",
            "Surface scratch",
            "Appearance",
            "minor",
            "rework",
            true);

        Assert.Equal("QR-SURFACE", reason.ReasonCode);
        Assert.Equal("Appearance", reason.GroupName);
        Assert.Equal("minor", reason.Severity);
        Assert.Equal("rework", reason.DefaultDisposition);
        Assert.True(reason.Enabled);

        reason.Update("Deep scratch", "Appearance", "major", "scrap");

        Assert.Equal("Deep scratch", reason.ReasonName);
        Assert.Equal("major", reason.Severity);
        Assert.Equal("scrap", reason.DefaultDisposition);
    }

    [Theory]
    [InlineData("low")]
    [InlineData("")]
    public void Quality_reason_rejects_unknown_severity(string severity)
    {
        Assert.Throws<ArgumentException>(() => QualityReason.Create(
            "org-001",
            "env-dev",
            "QR-BAD",
            "Bad reason",
            "Appearance",
            severity,
            null,
            true));
    }

    [Fact]
    public void Quality_reason_default_disposition_follows_ncr_supported_dispositions()
    {
        var reason = QualityReason.Create(
            "org-001",
            "env-dev",
            "QR-MISSING",
            "Missing part",
            "Assembly",
            "critical",
            null,
            true);

        Assert.Null(reason.DefaultDisposition);
        Assert.Throws<ArgumentException>(() => reason.Update("Missing part", "Assembly", "critical", "use-as-is"));
    }

    [Fact]
    public void Quality_reason_cannot_be_updated_after_archive()
    {
        var reason = QualityReason.Create(
            "org-001",
            "env-dev",
            "QR-SURFACE",
            "Surface scratch",
            "Appearance",
            "minor",
            "rework",
            true);

        reason.SetEnabled(false);

        Assert.Throws<InvalidOperationException>(() =>
            reason.Update("Deep scratch", "Appearance", "major", "scrap"));
    }
}
