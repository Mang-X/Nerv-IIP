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
    private static readonly string[] ScanRecordUniqueIndexNames =
    [
        "UX_scan_records_idempotency",
        "UX_scan_records_accepted_scanned_value",
        "UX_scan_records_epc_uri",
        "UX_scan_records_gtin_lot_serial",
        "UX_scan_records_gtin_serial_no_lot"
    ];

    private static readonly string[] EpcisEventUniqueIndexNames =
    [
        "UX_epcis_events_epc_uri",
        "UX_epcis_events_gtin_lot_serial",
        "UX_epcis_events_gtin_serial_no_lot"
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
            if (IsUniqueConflict(current, ScanRecordUniqueIndexNames, "scan_records"))
            {
                knownException = new KnownException("Duplicate serialized barcode scan is not allowed.", exception);
                return true;
            }

            if (IsUniqueConflict(current, EpcisEventUniqueIndexNames, "epcis_events"))
            {
                knownException = new KnownException("Duplicate BarcodeLabel EPCIS event is not allowed.", exception);
                return true;
            }
        }

        knownException = null!;
        return false;
    }

    private bool IsUniqueConflict(Exception exception, IReadOnlyCollection<string> indexNames, string tableName)
    {
        return IsPostgreSqlUniqueConflict(exception, indexNames) ||
            IsSqliteUniqueConflict(exception, tableName);
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

    private bool IsSqliteUniqueConflict(Exception exception, string tableName)
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

        return exception.Message.Contains(tableName, StringComparison.OrdinalIgnoreCase);
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
}
