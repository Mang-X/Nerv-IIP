using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class ProductionReportSerialNumberEntityTypeConfiguration
    : IEntityTypeConfiguration<ProductionReportSerialNumber>
{
    public void Configure(EntityTypeBuilder<ProductionReportSerialNumber> builder)
    {
        builder.ToTable("production_report_serial_numbers", table =>
        {
            table.HasComment("Immutable MES unit serial facts assigned to forward production reports.");
            table.HasCheckConstraint(
                "ck_production_report_serial_numbers_sequence_positive",
                "sequence_no > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseGuidVersion7ValueGenerator()
            .HasComment("Production report serial fact id.");
        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .IsRequired()
            .HasMaxLength(100)
            .HasComment("Organization tenant scope.");
        builder.Property(x => x.EnvironmentId)
            .HasColumnName("environment_id")
            .IsRequired()
            .HasMaxLength(100)
            .HasComment("Environment scope.");
        builder.Property(x => x.ReportNo)
            .HasColumnName("report_no")
            .IsRequired()
            .HasMaxLength(100)
            .HasComment("Forward MES production report number owning the serial assignment.");
        builder.Property(x => x.SequenceNo)
            .HasColumnName("sequence_no")
            .IsRequired()
            .HasComment("One-based BarcodeLabel allocation order within the production report.");
        builder.Property(x => x.SerialNumber)
            .HasColumnName("serial_number")
            .IsRequired()
            .HasMaxLength(ProductionReportSerialNumber.SerialNumberMaxLength)
            .HasComment("Trimmed unit serial number compared with ordinal case-sensitive semantics.");
        builder.HasOne<ProductionReport>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.ReportNo })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.ReportNo })
            .HasConstraintName("fk_production_report_serial_numbers_reports")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ReportNo, x.SequenceNo })
            .IsUnique()
            .HasDatabaseName("ux_production_report_serial_numbers_scope_report_sequence");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SerialNumber })
            .IsUnique()
            .UseCollation("C", "C", "C")
            .HasDatabaseName("ux_production_report_serial_numbers_scope_serial");
    }
}
