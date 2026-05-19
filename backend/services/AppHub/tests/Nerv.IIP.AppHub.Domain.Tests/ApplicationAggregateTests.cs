using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate;

namespace Nerv.IIP.AppHub.Domain.Tests;

public sealed class ApplicationAggregateTests
{
    [Fact]
    public void Application_registration_creates_active_application_with_initial_version()
    {
        var application = new Application("org-001", "env-dev", "demo-api", "Demo API", "1.0.0");

        Assert.Equal("org-001", application.OrganizationId);
        Assert.Equal("env-dev", application.EnvironmentId);
        Assert.Equal("demo-api", application.ApplicationKey);
        Assert.Equal("Demo API", application.ApplicationName);
        Assert.False(application.Deleted.Value);
        var version = Assert.Single(application.Versions);
        Assert.Equal("1.0.0", version.Version);
    }

    [Fact]
    public void Rename_and_track_version_updates_name_and_ignores_duplicate_versions()
    {
        var application = new Application("org-001", "env-dev", "demo-api", "Demo API", "1.0.0");

        application.RenameAndTrackVersion("Demo API v2", "2.0.0");
        application.RenameAndTrackVersion("Demo API v2", "2.0.0");

        Assert.Equal("Demo API v2", application.ApplicationName);
        Assert.Equal(["1.0.0", "2.0.0"], application.Versions.Select(x => x.Version).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Deactivate_marks_application_as_deleted_idempotently()
    {
        var application = new Application("org-001", "env-dev", "demo-api", "Demo API", "1.0.0");
        var initialDomainEventCount = application.GetDomainEvents().Count;

        application.Deactivate();
        application.Deactivate();

        Assert.True(application.Deleted.Value);
        var deactivatedEvent = Assert.IsType<ApplicationDeactivatedDomainEvent>(application.GetDomainEvents().Last());
        Assert.Equal("org-001", deactivatedEvent.OrganizationId);
        Assert.Equal("env-dev", deactivatedEvent.EnvironmentId);
        Assert.Equal("demo-api", deactivatedEvent.ApplicationKey);
        Assert.Equal(initialDomainEventCount + 1, application.GetDomainEvents().Count);
    }
}
