using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

public sealed class ProductionReportSerialNumberTests
{
    [Fact]
    public void Assignment_for_on_production_normalizes_order_and_matches_integer_good_quantity()
    {
        var assignment = ProductionReportSerialNumberAssignment.Create(
            "on-production",
            2m,
            ["  SN-B  ", "SN-A"]);

        Assert.Equal(["SN-B", "SN-A"], assignment.SerialNumbers);
        Assert.Empty(ProductionReportSerialNumberAssignment.Create("on-production", 0m, []).SerialNumbers);
        Assert.Empty(ProductionReportSerialNumberAssignment.Create("on-receipt", 1.5m, []).SerialNumbers);
    }

    [Fact]
    public void Assignment_maps_the_legacy_single_serial_wire_without_silently_losing_it()
    {
        var assignment = ProductionReportSerialNumberAssignment.Create(
            ProductionSerialTrackingPolicies.None,
            1m,
            null,
            "  SN-LEGACY-001  ");

        Assert.Equal(ProductionSerialTrackingPolicies.OnProduction, assignment.SerialTrackingPolicy);
        Assert.Equal(["SN-LEGACY-001"], assignment.SerialNumbers);
    }

    [Theory]
    [InlineData("on-production", 1.5, "SN-001")]
    [InlineData("on-production", 2, "SN-001")]
    [InlineData("on-production", 1, " ")]
    [InlineData("on-production", 2, "SN-001| SN-001 ")]
    [InlineData("none", 1, "SN-001")]
    [InlineData("on-receipt", 1, "SN-001")]
    [InlineData("on-shipment", 1, "SN-001")]
    [InlineData("optional", 1, "SN-001")]
    public void Assignment_rejects_inputs_that_violate_the_frozen_policy(
        string policy,
        decimal goodQuantity,
        string serializedInputs)
    {
        var serialNumbers = serializedInputs.Split('|');

        Assert.Throws<InvalidOperationException>(() =>
            ProductionReportSerialNumberAssignment.Create(policy, goodQuantity, serialNumbers));
    }

    [Fact]
    public void Assignment_rejects_mixing_legacy_and_collection_wires()
    {
        Assert.Throws<InvalidOperationException>(() => ProductionReportSerialNumberAssignment.Create(
            ProductionSerialTrackingPolicies.OnProduction,
            1m,
            ["SN-001"],
            "SN-001"));
    }

    [Fact]
    public void CreateForReport_preserves_barcode_order_and_normalizes_boundaries()
    {
        var serialNumbers = ProductionReportSerialNumber.CreateForReport(
            CreateReport("  org-001  ", "  env-dev  ", "  PR-001  "),
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
            CreateReport(),
            ["SN-001", "  SN-001  "]));

        Assert.Equal("serialNumbers", failure.ParamName);
    }

    [Fact]
    public void CreateForReport_compares_serial_numbers_with_ordinal_case_sensitivity()
    {
        var serialNumbers = ProductionReportSerialNumber.CreateForReport(
            CreateReport(),
            ["SN-001", "sn-001"]);

        Assert.Equal(["SN-001", "sn-001"], serialNumbers.Select(x => x.SerialNumber));
    }

    [Fact]
    public void CreateForReport_rejects_blank_or_overlong_serial_numbers()
    {
        Assert.Throws<ArgumentException>(() => ProductionReportSerialNumber.CreateForReport(
            CreateReport(),
            [" "]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductionReportSerialNumber.CreateForReport(
            CreateReport(),
            [new string('S', ProductionReportSerialNumber.SerialNumberMaxLength + 1)]));
    }

    [Fact]
    public void CreateForReport_rejects_a_reversal_owner()
    {
        var original = ProductionReport.Record(
            "org-001",
            "env-dev",
            "PR-001",
            "WO-001",
            "OP-001",
            1m,
            0m,
            false,
            DateTimeOffset.Parse("2026-08-30T09:00:00Z"));
        var reversal = ProductionReport.Reverse(
            original,
            "PR-002",
            DateTimeOffset.Parse("2026-08-30T09:30:00Z"),
            "wrong report",
            "operator-1");

        Assert.Throws<InvalidOperationException>(() =>
            ProductionReportSerialNumber.CreateForReport(reversal, ["SN-001"]));
    }

    private static ProductionReport CreateReport(
        string organizationId = "org-001",
        string environmentId = "env-dev",
        string reportNo = "PR-001") =>
        ProductionReport.Record(
            organizationId,
            environmentId,
            reportNo,
            "WO-001",
            "OP-001",
            1m,
            0m,
            false,
            DateTimeOffset.Parse("2026-08-30T09:00:00Z"));
}
