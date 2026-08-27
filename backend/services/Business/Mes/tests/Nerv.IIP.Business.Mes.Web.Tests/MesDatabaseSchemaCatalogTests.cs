namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesDatabaseSchemaCatalogTests
{
    [Fact]
    public void Catalog_preserves_oee_and_actual_time_settlement_schema_semantics()
    {
        var catalog = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "architecture",
            "database-schema-catalog.md"));
        var mesSection = catalog[catalog.IndexOf("## BusinessMES 数据库 Schema", StringComparison.Ordinal)..catalog.IndexOf("## BusinessDemandPlanning 数据库 Schema", StringComparison.Ordinal)];

        Assert.Contains("20260825180417_AddProductionReportOeeHistoricalDimensionSnapshot", mesSection, StringComparison.Ordinal);
        Assert.Contains("20260826053334_AddMesOperationActualTimeSettlement", mesSection, StringComparison.Ordinal);
        Assert.Contains("20260826144938_MergeProductionReportOeeAndActualTimeSettlement", mesSection, StringComparison.Ordinal);
        Assert.Contains("| `operation_actual_time_settlements`", mesSection, StringComparison.Ordinal);
        Assert.Contains("| `operation_actual_time_settlement_reports`", mesSection, StringComparison.Ordinal);

        var operationTaskRow = Assert.Single(
            mesSection.Split('\n'),
            line => line.StartsWith("| `operation_tasks`", StringComparison.Ordinal));
        Assert.Contains("actual_time_settlement_revision", operationTaskRow, StringComparison.Ordinal);
        Assert.Contains("row_version", operationTaskRow, StringComparison.Ordinal);
        Assert.Contains("再次完工产生更高 revision", operationTaskRow, StringComparison.Ordinal);

        var productionReportRow = Assert.Single(
            mesSection.Split('\n'),
            line => line.StartsWith("| `production_reports`", StringComparison.Ordinal));
        Assert.Contains("OEE 字段", productionReportRow, StringComparison.Ordinal);
        Assert.Contains("冲销复用原报工快照", productionReportRow, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
