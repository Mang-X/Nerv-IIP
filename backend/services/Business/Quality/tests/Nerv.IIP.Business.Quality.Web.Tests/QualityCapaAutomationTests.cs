using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MediatR;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.CorrectiveActionAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.QualityReasonAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Approvals;
using Nerv.IIP.Business.Quality.Web.Application.Commands.CorrectiveActions;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Contracts.Quality;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityCapaAutomationTests
{
    [Fact]
    public async Task High_severity_scrap_disposition_automatically_opens_linked_capa()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.QualityReasons.Add(QualityReason.Create(
            "org-001",
            "env-dev",
            "dimension-out-of-spec",
            "Dimension out of spec",
            "dimensional",
            "critical",
            "scrap",
            enabled: true));
        var ncr = NewNcr("NCR-AUTO-CAPA-001", "dimension-out-of-spec");
        db.NonconformanceReports.Add(ncr);
        await db.SaveChangesAsync(CancellationToken.None);
        var handler = new SubmitNonconformanceReportDispositionCommandHandler(
            new NonconformanceReportRepository(db),
            new FixedApprovalChainStatusClient(true),
            new CapaAutomationService(
                db,
                new CorrectiveActionRepository(db),
                new FixedCorrectiveActionCodeGenerator("CAPA-AUTO-001"),
                Options.Create(new CapaAutomationOptions
                {
                    Enabled = true,
                    MinimumSeverity = "major",
                    Dispositions = [QualityNcrDispositionTypes.Rework, QualityNcrDispositionTypes.Scrap],
                    OwnerUserId = "qa-manager-001",
                    DueDays = 14,
                })));

        await handler.Handle(
            new SubmitNonconformanceReportDispositionCommand(
                ncr.Id,
                QualityNcrDispositionTypes.Scrap,
                "approval-chain-approved",
                [],
                [MrbReviewInput.Approve("qa-manager-001", "MRB accepted", DateTimeOffset.Parse("2026-07-07T08:00:00Z"))]),
            CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        var capa = await db.CorrectiveActions.Include(x => x.Actions).SingleAsync(CancellationToken.None);
        Assert.Equal("CAPA-AUTO-001", capa.CapaCode);
        Assert.Equal(ncr.Id.ToString(), capa.SourceNcrId);
        Assert.Equal("qa-manager-001", capa.OwnerUserId);
        Assert.Contains(capa.Actions, x => x.ActionType == "corrective");
        Assert.Contains(capa.Actions, x => x.ActionType == "preventive");
    }

    [Fact]
    public async Task Major_disposition_does_not_auto_open_capa_when_severity_is_below_configured_threshold()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.QualityReasons.Add(QualityReason.Create(
            "org-001",
            "env-dev",
            "label-smudge",
            "Label smudge",
            "appearance",
            "minor",
            QualityNcrDispositionTypes.Rework,
            enabled: true));
        var ncr = NewNcr("NCR-NO-AUTO-CAPA-001", "label-smudge");
        db.NonconformanceReports.Add(ncr);
        await db.SaveChangesAsync(CancellationToken.None);
        var handler = new SubmitNonconformanceReportDispositionCommandHandler(
            new NonconformanceReportRepository(db),
            new FixedApprovalChainStatusClient(true),
            new CapaAutomationService(
                db,
                new CorrectiveActionRepository(db),
                new FixedCorrectiveActionCodeGenerator("CAPA-SHOULD-NOT-OPEN"),
                Options.Create(new CapaAutomationOptions
                {
                    Enabled = true,
                    MinimumSeverity = "major",
                    Dispositions = [QualityNcrDispositionTypes.Rework, QualityNcrDispositionTypes.Scrap],
                    OwnerUserId = "qa-manager-001",
                    DueDays = 14,
                })));

        await handler.Handle(
            new SubmitNonconformanceReportDispositionCommand(
                ncr.Id,
                QualityNcrDispositionTypes.Rework,
                "approval-chain-approved",
                [],
                [MrbReviewInput.Approve("qa-manager-001", "MRB accepted", DateTimeOffset.Parse("2026-07-07T08:00:00Z"))]),
            CancellationToken.None);

        Assert.Empty(db.CorrectiveActions);
    }

    [Fact]
    public async Task Capa_effectiveness_verification_must_reference_passed_inspection_before_close()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rejectedInspection = NewInspectionRecord("RCV-CAPA-VERIFY-REJECTED", passed: false);
        var passedInspection = NewInspectionRecord("RCV-CAPA-VERIFY-PASSED", passed: true);
        var capa = NewCompletedCapa();
        db.InspectionRecords.AddRange(rejectedInspection, passedInspection);
        db.CorrectiveActions.Add(capa);
        await db.SaveChangesAsync(CancellationToken.None);
        var verifyHandler = new VerifyCorrectiveActionEffectivenessCommandHandler(
            new CorrectiveActionRepository(db),
            new NonconformanceReportRepository(db),
            db);

        var rejectedException = await Assert.ThrowsAsync<KnownException>(() => verifyHandler.Handle(
            new VerifyCorrectiveActionEffectivenessCommand(
                capa.Id,
                "qa-manager-001",
                "Rejected verification lot",
                DateTimeOffset.Parse("2026-07-15T00:00:00Z"),
                rejectedInspection.Id),
            CancellationToken.None));
        Assert.Contains("passed", rejectedException.Message, StringComparison.OrdinalIgnoreCase);

        await verifyHandler.Handle(
            new VerifyCorrectiveActionEffectivenessCommand(
                capa.Id,
                "qa-manager-001",
                "Passed verification lot",
                DateTimeOffset.Parse("2026-07-16T00:00:00Z"),
                passedInspection.Id),
            CancellationToken.None);

        var closeHandler = new CloseCorrectiveActionCommandHandler(
            new CorrectiveActionRepository(db),
            new NonconformanceReportRepository(db),
            new FixedApprovalChainStatusClient(true),
            Options.Create(new CapaCloseApprovalOptions { Required = true }));
        await closeHandler.Handle(
            new CloseCorrectiveActionCommand(capa.Id, "qa-manager-001", "approval-chain-approved"),
            CancellationToken.None);

        Assert.Equal(passedInspection.Id, capa.EffectivenessInspectionRecordId);
        Assert.Equal("closed", capa.Status);
    }

    [Fact]
    public async Task Capa_close_approval_reject_path_keeps_capa_effectiveness_verified()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inspection = NewInspectionRecord("RCV-CAPA-CLOSE-REJECT", passed: true);
        var capa = NewCompletedCapa();
        db.InspectionRecords.Add(inspection);
        db.CorrectiveActions.Add(capa);
        await db.SaveChangesAsync(CancellationToken.None);
        var verifyHandler = new VerifyCorrectiveActionEffectivenessCommandHandler(
            new CorrectiveActionRepository(db),
            new NonconformanceReportRepository(db),
            db);
        await verifyHandler.Handle(
            new VerifyCorrectiveActionEffectivenessCommand(
                capa.Id,
                "qa-manager-001",
                "Passed verification lot",
                DateTimeOffset.Parse("2026-07-16T00:00:00Z"),
                inspection.Id),
            CancellationToken.None);
        var closeHandler = new CloseCorrectiveActionCommandHandler(
            new CorrectiveActionRepository(db),
            new NonconformanceReportRepository(db),
            new FixedApprovalChainStatusClient(false),
            Options.Create(new CapaCloseApprovalOptions { Required = true }));

        var exception = await Assert.ThrowsAsync<KnownException>(() => closeHandler.Handle(
            new CloseCorrectiveActionCommand(capa.Id, "qa-manager-001", "approval-chain-rejected"),
            CancellationToken.None));

        Assert.Contains("approval", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("effectiveness-verified", capa.Status);
        Assert.Null(capa.ClosedAtUtc);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"quality-capa-automation-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    private static NonconformanceReport NewNcr(string ncrCode, string defectReason)
    {
        return NonconformanceReport.Open(
            "org-001",
            "env-dev",
            ncrCode,
            "receiving",
            $"RCV-{ncrCode}",
            "SKU-RM-1000",
            10m,
            defectReason,
            null,
            null,
            []);
    }

    private static CorrectiveAction NewCompletedCapa()
    {
        var capa = CorrectiveAction.OpenStandalone(
            "org-001",
            "env-dev",
            $"CAPA-{Guid.CreateVersion7():N}",
            "Root cause confirmed",
            "Contain affected material",
            "qa-engineer-001",
            DateTimeOffset.Parse("2026-07-30T00:00:00Z"));
        capa.AddAction("corrective", "Fix supplier process", "supplier-quality-001", DateTimeOffset.Parse("2026-07-12T00:00:00Z"));
        var action = capa.Actions.Single();
        capa.CompleteAction(action.Id, action.OwnerUserId, DateTimeOffset.Parse("2026-07-13T00:00:00Z"));
        return capa;
    }

    private static InspectionRecord NewInspectionRecord(string sourceDocumentId, bool passed)
    {
        return InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            "receiving",
            "purchase-receipt",
            sourceDocumentId,
            "SKU-RM-1000",
            10m,
            null,
            null,
            passed
                ? [InspectionResultLineInput.Pass("appearance", "ok", null, [])]
                : [InspectionResultLineInput.Fail("appearance", "bad", "dimension-out-of-spec", 1m, [])],
            passed ? null : "Verification lot rejected",
            []);
    }

    private sealed class FixedCorrectiveActionCodeGenerator(string code) : ICorrectiveActionCodeGenerator
    {
        public Task<string> NextAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(code);
        }
    }

    private sealed class FixedApprovalChainStatusClient(bool approved) : IApprovalChainStatusClient
    {
        public Task<bool> IsApprovedForNcrDispositionAsync(
            string chainId,
            string organizationId,
            string environmentId,
            string ncrCode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(approved);
        }

        public Task<bool> IsApprovedForCapaClosureAsync(
            string chainId,
            string organizationId,
            string environmentId,
            string capaCode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(approved);
        }
    }
}
