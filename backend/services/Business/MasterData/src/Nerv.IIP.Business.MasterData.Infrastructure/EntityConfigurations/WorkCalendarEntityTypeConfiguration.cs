using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCalendarAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class WorkCalendarEntityTypeConfiguration : IEntityTypeConfiguration<WorkCalendar>
{
    public void Configure(EntityTypeBuilder<WorkCalendar> builder)
    {
        builder.ToTable("work_calendars", tableBuilder =>
            tableBuilder.HasComment("Business master data work calendars defining recurring working days, holidays, and exceptions."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Work calendar aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the work calendar.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the work calendar is valid.");
        builder.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(100).HasComment("Business unique work calendar code.");
        builder.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200).HasComment("Work calendar display name.");
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Disabled flag that hides the work calendar from active use.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the work calendar was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the work calendar was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Code }).IsUnique();
        builder.HasIndex(x => x.Disabled);
        builder.OwnsMany(x => x.WorkingTimes, ConfigureWorkingTimes);
        builder.Navigation(x => x.WorkingTimes).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.OwnsMany(x => x.Holidays, ConfigureHolidays);
        builder.Navigation(x => x.Holidays).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.OwnsMany(x => x.Exceptions, ConfigureExceptions);
        builder.Navigation(x => x.Exceptions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureWorkingTimes(OwnedNavigationBuilder<WorkCalendar, WorkCalendarWorkingTime> builder)
    {
        builder.ToTable("work_calendar_working_times", tableBuilder =>
            tableBuilder.HasComment("Recurring working day markers owned by a business master data work calendar."));
        builder.WithOwner().HasForeignKey("WorkCalendarId");
        builder.Property<WorkCalendarId>("WorkCalendarId")
            .HasColumnName("work_calendar_id")
            .HasConversion(id => id.Id, value => new WorkCalendarId(value))
            .HasComment("Owning work calendar aggregate id.");
        builder.Property<Guid>("id").HasColumnName("id").ValueGeneratedOnAdd().HasComment("Work calendar working day row id.");
        builder.HasKey("id");
        builder.Property(x => x.DayOfWeek).HasColumnName("day_of_week").IsRequired().HasComment("Day of week for the recurring working day.");
        builder.HasIndex("WorkCalendarId");
        builder.HasIndex("WorkCalendarId", nameof(WorkCalendarWorkingTime.DayOfWeek)).IsUnique();
    }

    private static void ConfigureHolidays(OwnedNavigationBuilder<WorkCalendar, WorkCalendarHoliday> builder)
    {
        builder.ToTable("work_calendar_holidays", tableBuilder =>
            tableBuilder.HasComment("Holiday dates owned by a business master data work calendar."));
        builder.WithOwner().HasForeignKey("WorkCalendarId");
        builder.Property<WorkCalendarId>("WorkCalendarId")
            .HasColumnName("work_calendar_id")
            .HasConversion(id => id.Id, value => new WorkCalendarId(value))
            .HasComment("Owning work calendar aggregate id.");
        builder.Property<Guid>("id").HasColumnName("id").ValueGeneratedOnAdd().HasComment("Work calendar holiday row id.");
        builder.HasKey("id");
        builder.Property(x => x.Date).HasColumnName("date").IsRequired().HasComment("Local holiday date.");
        builder.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200).HasComment("Holiday display name.");
        builder.HasIndex("WorkCalendarId");
    }

    private static void ConfigureExceptions(OwnedNavigationBuilder<WorkCalendar, WorkCalendarException> builder)
    {
        builder.ToTable("work_calendar_exceptions", tableBuilder =>
            tableBuilder.HasComment("Exception dates owned by a business master data work calendar."));
        builder.WithOwner().HasForeignKey("WorkCalendarId");
        builder.Property<WorkCalendarId>("WorkCalendarId")
            .HasColumnName("work_calendar_id")
            .HasConversion(id => id.Id, value => new WorkCalendarId(value))
            .HasComment("Owning work calendar aggregate id.");
        builder.Property<Guid>("id").HasColumnName("id").ValueGeneratedOnAdd().HasComment("Work calendar exception row id.");
        builder.HasKey("id");
        builder.Property(x => x.Date).HasColumnName("date").IsRequired().HasComment("Local exception date.");
        builder.Property(x => x.IsWorkingDay).HasColumnName("is_working_day").IsRequired().HasComment("Whether the exception date is treated as a working day.");
        builder.Property(x => x.StartsAt).HasColumnName("starts_at").HasComment("Optional local exception start time.");
        builder.Property(x => x.EndsAt).HasColumnName("ends_at").HasComment("Optional local exception end time.");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(300).HasComment("Optional reason for the calendar exception.");
        builder.HasIndex("WorkCalendarId");
    }
}
