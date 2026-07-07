using Nerv.IIP.Business.Quality.Domain.AggregatesModel.CorrectiveActionAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityCapaIntegrationEventTests
{
    [Fact]
    public void Capa_lifecycle_events_expose_notification_and_workbench_matrix_payloads()
    {
        var capa = CorrectiveAction.OpenStandalone(
            "org-001",
            "env-dev",
            "CAPA-EVENT-001",
            "Root cause confirmed",
            "Contain affected material",
            "qa-manager-001",
            DateTimeOffset.Parse("2026-07-21T00:00:00Z"));
        var openedEvent = capa.GetDomainEvents().OfType<CorrectiveActionOpenedDomainEvent>().Single();
        capa.AddAction("corrective", "Correct process parameter", "qa-manager-001", DateTimeOffset.Parse("2026-07-12T00:00:00Z"));
        var action = capa.Actions.Single();
        capa.CompleteAction(action.Id, action.OwnerUserId, DateTimeOffset.Parse("2026-07-13T00:00:00Z"));
        var verificationInspectionId = new InspectionRecordId(Guid.CreateVersion7());
        capa.VerifyEffectiveness(
            "qa-manager-001",
            "Verification inspection passed",
            DateTimeOffset.Parse("2026-07-15T00:00:00Z"),
            verificationInspectionId,
            "passed");
        var verifiedEvent = capa.GetDomainEvents().OfType<CorrectiveActionEffectivenessVerifiedDomainEvent>().Single();
        capa.Close("qa-manager-001", "approval-chain-approved");
        var closedEvent = capa.GetDomainEvents().OfType<CorrectiveActionClosedDomainEvent>().Single();
        var context = new StubQualityIntegrationEventContextAccessor();

        var opened = new CapaOpenedIntegrationEventConverter(context).Convert(openedEvent);
        var verified = new CapaEffectivenessVerifiedIntegrationEventConverter(context).Convert(verifiedEvent);
        var closed = new CapaClosedIntegrationEventConverter(context).Convert(closedEvent);

        Assert.Equal(QualityIntegrationEventTypes.CapaOpened, opened.EventType);
        Assert.Equal("CAPA-EVENT-001", opened.Payload.CapaCode);
        Assert.Equal("qa-manager-001", opened.Payload.OwnerUserId);
        Assert.Equal(QualityIntegrationEventTypes.CapaEffectivenessVerified, verified.EventType);
        Assert.Equal(verificationInspectionId.ToString(), verified.Payload.VerificationInspectionRecordId);
        Assert.Equal("qa-manager-001", verified.Payload.VerifiedByUserId);
        Assert.Equal(QualityIntegrationEventTypes.CapaClosed, closed.EventType);
        Assert.Equal("approval-chain-approved", closed.Payload.CloseApprovalChainId);
        Assert.Equal("qa-manager-001", closed.Payload.ClosedByUserId);
    }

    private sealed class StubQualityIntegrationEventContextAccessor : IQualityIntegrationEventContextAccessor
    {
        public QualityIntegrationEventContext GetContext()
        {
            return new QualityIntegrationEventContext(
                "corr-capa-event-001",
                "cause-capa-event-001",
                "system:business-quality");
        }
    }
}
