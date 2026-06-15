using MediatR;
using Microsoft.EntityFrameworkCore;
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
}
