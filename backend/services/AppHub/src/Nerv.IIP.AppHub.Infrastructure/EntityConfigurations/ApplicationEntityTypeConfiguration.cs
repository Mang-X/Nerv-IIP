using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate;
using NetCorePal.Extensions.Domain;
using AppHubApplication = Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate.Application;
using AppHubApplicationId = Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate.ApplicationId;

namespace Nerv.IIP.AppHub.Infrastructure.EntityConfigurations;

public sealed class ApplicationEntityTypeConfiguration : IEntityTypeConfiguration<AppHubApplication>
{
    public void Configure(EntityTypeBuilder<AppHubApplication> builder)
    {
        builder.ToTable("applications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(id => id.Id, value => new AppHubApplicationId(value)).ValueGeneratedNever().HasComment("Application aggregate id");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(100).HasComment("Organization id");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(100).HasComment("Environment id");
        builder.Property(x => x.ApplicationKey).IsRequired().HasMaxLength(160).HasComment("Application protocol key");
        builder.Property(x => x.ApplicationName).IsRequired().HasMaxLength(200).HasComment("Application display name");
        builder.Property(x => x.Deleted).HasConversion(x => x.Value, x => new Deleted(x)).HasComment("Soft delete flag");
        builder.Property(x => x.RowVersion).HasConversion(x => x.VersionNumber, x => new RowVersion(x)).HasComment("Optimistic row version");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ApplicationKey }).IsUnique();
        builder.HasMany(x => x.Versions).WithOne().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ApplicationVersionEntityTypeConfiguration : IEntityTypeConfiguration<ApplicationVersion>
{
    public void Configure(EntityTypeBuilder<ApplicationVersion> builder)
    {
        builder.ToTable("application_versions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(id => id.Id, value => new ApplicationVersionId(value)).ValueGeneratedNever().HasComment("Application version id");
        builder.Property(x => x.ApplicationId).HasConversion(id => id.Id, value => new AppHubApplicationId(value)).IsRequired().HasComment("Application aggregate id");
        builder.Property(x => x.Version).IsRequired().HasMaxLength(100).HasComment("Application version");
        builder.HasIndex(x => new { x.ApplicationId, x.Version }).IsUnique();
    }
}
