using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

public sealed class ProductionReportSerialNumberTests
{
    [Fact]
    public void CreateForReport_preserves_barcode_order_and_normalizes_boundaries()
    {
        var serialNumbers = ProductionReportSerialNumber.CreateForReport(
            "  org-001  ",
            "  env-dev  ",
            "  PR-001  ",
            ["  SN-B  ", "SN-A"]);

        Assert.Collection(
            serialNumbers,
            first =>
            {
                Assert.Equal("org-001", first.OrganizationId);
                Assert.Equal("env-dev", first.EnvironmentId);
                Assert.Equal("PR-001", first.ReportNo);
                Assert.Equal(1, first.SequenceNo);
                Assert.Equal("SN-B", first.SerialNumber);
            },
            second =>
            {
                Assert.Equal(2, second.SequenceNo);
                Assert.Equal("SN-A", second.SerialNumber);
            });
    }

    [Fact]
    public void CreateForReport_rejects_a_duplicate_after_trimming()
    {
        var failure = Assert.Throws<ArgumentException>(() => ProductionReportSerialNumber.CreateForReport(
            "org-001",
            "env-dev",
            "PR-001",
            ["SN-001", "  SN-001  "]));

        Assert.Equal("serialNumbers", failure.ParamName);
    }

    [Fact]
    public void CreateForReport_compares_serial_numbers_with_ordinal_case_sensitivity()
    {
        var serialNumbers = ProductionReportSerialNumber.CreateForReport(
            "org-001",
            "env-dev",
            "PR-001",
            ["SN-001", "sn-001"]);

        Assert.Equal(["SN-001", "sn-001"], serialNumbers.Select(x => x.SerialNumber));
    }

    [Fact]
    public void CreateForReport_rejects_blank_or_overlong_serial_numbers()
    {
        Assert.Throws<ArgumentException>(() => ProductionReportSerialNumber.CreateForReport(
            "org-001",
            "env-dev",
            "PR-001",
            [" "]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductionReportSerialNumber.CreateForReport(
            "org-001",
            "env-dev",
            "PR-001",
            [new string('S', ProductionReportSerialNumber.SerialNumberMaxLength + 1)]));
    }
}
