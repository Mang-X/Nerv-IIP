using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkerAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class WorkerEntityTypeConfiguration : IEntityTypeConfiguration<Worker>
{
    public void Configure(EntityTypeBuilder<Worker> builder)
    {
        builder.ToTable("workers", tableBuilder =>
            tableBuilder.HasComment("Business master data factory workers used by team membership, personnel skills, and MES dispatch."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Worker aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the worker record.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the worker record is valid.");
        builder.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(100).HasComment("Human readable employee number shown on shop floor screens.");
        builder.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200).HasComment("Worker display name.");
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired().HasMaxLength(100).HasComment("Stable person identifier shared with team membership, personnel skills, and MES dispatch.");
        builder.Property(x => x.DepartmentCode).HasColumnName("department_code").HasMaxLength(100).HasComment("Optional department code the worker belongs to.");
        builder.Property(x => x.JobTitle).HasColumnName("job_title").HasMaxLength(200).HasComment("Optional job title of the worker.");
        builder.Property(x => x.EmploymentStatus).HasColumnName("employment_status").IsRequired().HasMaxLength(50).HasComment("Duty status of the worker: active, on-leave, or resigned.");
        builder.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(50).HasComment("Optional contact phone number.");
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Soft delete flag for archived workers.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the worker record was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the worker record was last updated.");
        builder.Ignore(x => x.IsDispatchable);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.UserId }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DepartmentCode, x.Disabled });
    }
}
