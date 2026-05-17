using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ManagedNodeAggregate;
using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.AppHub.Infrastructure.EntityConfigurations;

public sealed class ManagedNodeEntityTypeConfiguration : IEntityTypeConfiguration<ManagedNode>
{
    public void Configure(EntityTypeBuilder<ManagedNode> builder)
    {
        builder.ToTable("managed_nodes", tableBuilder =>
            tableBuilder.HasComment("AppHub managed connector host or runtime node catalog entries."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Managed node aggregate id");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(100).HasComment("Organization id");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(100).HasComment("Environment id");
        builder.Property(x => x.NodeKey).IsRequired().HasMaxLength(160).HasComment("Node protocol key");
        builder.Property(x => x.NodeName).IsRequired().HasMaxLength(200).HasComment("Node display name");
        builder.Property(x => x.DeploymentKind).IsRequired().HasMaxLength(100).HasComment("Node deployment kind");
        builder.Property(x => x.Deleted).HasConversion(x => x.Value, x => new Deleted(x)).HasComment("Soft delete flag");
        builder.Property(x => x.RowVersion).HasConversion(x => x.VersionNumber, x => new RowVersion(x)).HasComment("Optimistic row version");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.NodeKey }).IsUnique();
    }
}
