using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTemplateAggregate;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Ops.Infrastructure.EntityConfigurations;

public sealed class OperationTemplateEntityTypeConfiguration : IEntityTypeConfiguration<OperationTemplate>
{
    public void Configure(EntityTypeBuilder<OperationTemplate> builder)
    {
        builder.ToTable("operation_templates", table =>
            table.HasComment("Ops operation templates registering supported operation codes and execution defaults."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new OperationTemplateId(x))
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("Operation template identifier.");
        builder.Property(x => x.OperationCode).IsRequired().HasMaxLength(128).HasComment("Registered operation code.");
        builder.Property(x => x.DisplayName).IsRequired().HasMaxLength(200).HasComment("Operation display name.");
        builder.Property(x => x.ParameterSchemaJson).IsRequired().HasComment("JSON schema describing accepted operation parameters.");
        builder.Property(x => x.RiskLevel).IsRequired().HasMaxLength(32).HasComment("Operation risk level.");
        builder.Property(x => x.DefaultMaxAttempts).HasComment("Default maximum execution attempts.");
        builder.Property(x => x.DefaultLeaseDurationSeconds).HasComment("Default connector lease duration in seconds.");
        builder.Property(x => x.RequiresApproval).HasComment("Whether this template requires manual approval before execution.");
        builder.Property(x => x.Enabled).HasComment("Whether this operation template can create new tasks.");
        builder.Property(x => x.CreatedAtUtc).HasComment("Template creation time in UTC.");
        builder.Property(x => x.UpdatedAtUtc).HasComment("Template last update time in UTC.");
        builder.Property(x => x.Deleted).HasConversion(x => x.Value, x => new Deleted(x)).HasComment("Soft delete flag.");
        builder.Property(x => x.RowVersion).HasConversion(x => x.VersionNumber, x => new RowVersion(x)).HasComment("Optimistic row version.");

        builder.HasIndex(x => x.OperationCode).IsUnique();
        builder.HasIndex(x => new { x.Enabled, x.RiskLevel });
    }
}
