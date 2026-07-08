using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.BarcodeLabel.Domain;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TraceabilityAggregate;
using NetCorePal.Extensions.DistributedTransactions.CAP.Persistence;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure;

public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IMediator mediator)
    : AppDbContextBase(options, mediator), IPostgreSqlCapDataStorage
{
    private static readonly UniqueConflictMapping[] ScanRecordUniqueConflicts =
    [
        new(
            ["UX_scan_records_idempotency"],
            ["scan_records.organization_id", "scan_records.environment_id", "scan_records.idempotency_key"],
            "Duplicate barcode scan idempotency key is not allowed."),
        new(
            ["UX_scan_records_accepted_scan_natural_key"],
            [
                "scan_records.organization_id",
                "scan_records.environment_id",
                "scan_records.scanned_value",
                "scan_records.source_workflow",
                "scan_records.source_document_id"
            ],
            "Duplicate accepted barcode scan natural key is not allowed."),
        new(
            ["UX_scan_records_epc_uri"],
            ["scan_records.organization_id", "scan_records.environment_id", "scan_records.epc_uri"],
            "Duplicate serialized barcode scan is not allowed."),
        new(
            ["UX_scan_records_gtin_lot_serial"],
            [
                "scan_records.organization_id",
                "scan_records.environment_id",
                "scan_records.gtin",
                "scan_records.lot_no",
                "scan_records.serial_number"
            ],
            "Duplicate serialized barcode scan is not allowed."),
        new(
            ["UX_scan_records_gtin_serial_no_lot"],
            ["scan_records.organization_id", "scan_records.environment_id", "scan_records.gtin", "scan_records.serial_number"],
            "Duplicate serialized barcode scan is not allowed.")
    ];

    private static readonly UniqueConflictMapping[] EpcisEventUniqueConflicts =
    [
        new(
            ["UX_epcis_events_epc_uri"],
            ["epcis_events.organization_id", "epcis_events.environment_id", "epcis_events.event_type", "epcis_events.epc_uri"],
            "Duplicate BarcodeLabel EPCIS event is not allowed."),
        new(
            ["UX_epcis_events_gtin_lot_serial"],
            [
                "epcis_events.organization_id",
                "epcis_events.environment_id",
                "epcis_events.event_type",
                "epcis_events.gtin",
                "epcis_events.lot_no",
                "epcis_events.serial_number"
            ],
            "Duplicate BarcodeLabel EPCIS event is not allowed."),
        new(
            ["UX_epcis_events_gtin_serial_no_lot"],
            ["epcis_events.organization_id", "epcis_events.environment_id", "epcis_events.event_type", "epcis_events.gtin", "epcis_events.serial_number"],
            "Duplicate BarcodeLabel EPCIS event is not allowed.")
    ];

    public DbSet<BarcodeRule> BarcodeRules => Set<BarcodeRule>();

    public DbSet<LabelTemplate> LabelTemplates => Set<LabelTemplate>();

    public DbSet<LabelPrintBatch> LabelPrintBatches => Set<LabelPrintBatch>();

    public DbSet<LabelPrintItem> LabelPrintItems => Set<LabelPrintItem>();

    public DbSet<ScanRecord> ScanRecords => Set<ScanRecord>();

    public DbSet<EpcisEvent> EpcisEvents => Set<EpcisEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(BarcodeLabelFacts.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ConfigureStronglyTypedIdValueConverter(configurationBuilder);
        base.ConfigureConventions(configurationBuilder);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException exception) when (TryMapUniqueConflict(exception, out var knownException))
        {
            throw knownException;
        }
    }

    private bool TryMapUniqueConflict(DbUpdateException exception, out KnownException knownException)
    {
        foreach (var current in EnumerateExceptions(exception))
        {
            foreach (var mapping in ScanRecordUniqueConflicts)
            {
                if (IsUniqueConflict(current, mapping))
                {
                    knownException = new KnownException(mapping.Message, exception);
                    return true;
                }
            }

            foreach (var mapping in EpcisEventUniqueConflicts)
            {
                if (IsUniqueConflict(current, mapping))
                {
                    knownException = new KnownException(mapping.Message, exception);
                    return true;
                }
            }
        }

        knownException = null!;
        return false;
    }

    private bool IsUniqueConflict(Exception exception, UniqueConflictMapping mapping)
    {
        return IsPostgreSqlUniqueConflict(exception, mapping.IndexNames) ||
            IsSqliteUniqueConflict(exception, mapping.SqliteColumnNames);
    }

    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static bool IsPostgreSqlUniqueConflict(Exception exception, IReadOnlyCollection<string> indexNames)
    {
        if (!string.Equals(exception.GetType().FullName, "Npgsql.PostgresException", StringComparison.Ordinal))
        {
            return false;
        }

        var sqlState = exception.GetType().GetProperty("SqlState")?.GetValue(exception) as string;
        if (sqlState != "23505")
        {
            return false;
        }

        var constraintName = exception.GetType().GetProperty("ConstraintName")?.GetValue(exception) as string;
        return !string.IsNullOrWhiteSpace(constraintName) &&
            indexNames.Contains(constraintName, StringComparer.Ordinal);
    }

    private bool IsSqliteUniqueConflict(Exception exception, IReadOnlyCollection<string> columnNames)
    {
        var providerName = Database.ProviderName ?? string.Empty;
        var typeName = exception.GetType().FullName ?? string.Empty;
        if (!providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) &&
            !typeName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extendedErrorCode = GetIntProperty(exception, "SqliteExtendedErrorCode");
        if (extendedErrorCode is not (1555 or 2067))
        {
            return false;
        }

        return IsSqliteUniqueConflictMessageForColumns(exception.Message, columnNames);
    }

    private static bool IsSqliteUniqueConflictMessageForColumns(string message, IReadOnlyCollection<string> columnNames)
    {
        const string marker = "UNIQUE constraint failed:";
        var markerIndex = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        var columnList = message[(markerIndex + marker.Length)..]
            .Trim()
            .Trim('\'', '.');

        var actualColumnNames = columnList
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return actualColumnNames.Length == columnNames.Count &&
            columnNames.All(columnName => actualColumnNames.Contains(columnName, StringComparer.OrdinalIgnoreCase));
    }

    private static int? GetIntProperty(Exception exception, string propertyName)
    {
        var value = exception.GetType().GetProperty(propertyName)?.GetValue(exception);
        return value switch
        {
            int intValue => intValue,
            uint uintValue when uintValue <= int.MaxValue => (int)uintValue,
            _ => null
        };
    }

    private sealed record UniqueConflictMapping(
        IReadOnlyCollection<string> IndexNames,
        IReadOnlyCollection<string> SqliteColumnNames,
        string Message);
}
