using System.Net;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Approvals;
using Nerv.IIP.Business.Quality.Web.Application.Commands.CorrectiveActions;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityNcrEvidenceTests
{
    [Theory]
    [InlineData("missing", false)]
    [InlineData("unavailable", false)]
    [InlineData("purpose", false)]
    [InlineData("organization", false)]
    [InlineData("environment", false)]
    [InlineData("owner-service", false)]
    [InlineData("owner-type", false)]
    [InlineData("owner-id", false)]
    [InlineData("file-id", false)]
    [InlineData("metadata-failure", false)]
    [InlineData("purpose", true)]
    [InlineData("owner-id", true)]
    public async Task Invalid_new_or_existing_evidence_rejects_disposition_without_mutating_ncr(
        string failure, bool existing)
    {
        await using var db = CreateDb();
        var ncr = NewNcr(existing ? ["bad-file"] : []);
        db.NonconformanceReports.Add(ncr);
        await db.SaveChangesAsync();
        ncr.ClearDomainEvents();
        var id = ncr.Id;
        var files = new QualityEvidenceTestFiles();
        var metadata = QualityEvidenceTestFiles.Metadata(ncr, "bad-file");
        metadata = failure switch
        {
            "unavailable" => metadata with { Status = "uploading" },
            "purpose" => metadata with { FilePurpose = "engineering-document" },
            "organization" => metadata with { OrganizationId = "other-org" },
            "environment" => metadata with { EnvironmentId = "other-env" },
            "owner-service" => metadata with { Owner = metadata.Owner with { OwnerService = "business-mes" } },
            "owner-type" => metadata with { Owner = metadata.Owner with { OwnerType = "inspection-record" } },
            "owner-id" => metadata with { Owner = metadata.Owner with { OwnerId = "other-ncr" } },
            "file-id" => metadata with { FileId = "another-file" },
            _ => metadata,
        };
        if (failure is not ("missing" or "metadata-failure")) files.Files.Add("bad-file", metadata);
        if (failure == "metadata-failure") files.MissingStatus = HttpStatusCode.ServiceUnavailable;
        files.Files.Add("good-file", QualityEvidenceTestFiles.Metadata(ncr, "good-file"));
        var automation = new RecordingAutomation();
        var handler = new SubmitNonconformanceReportDispositionCommandHandler(
            new NonconformanceReportRepository(db), new UnexpectedApproval(), automation, db, files.CreateClient());
        var command = new SubmitNonconformanceReportDispositionCommand(
            id, ncr.OrganizationId, ncr.EnvironmentId, "sort-and-screen", null,
            existing ? ["good-file"] : ["bad-file"], []);

        var exception = await Record.ExceptionAsync(() => handler.Handle(command, CancellationToken.None));
        if (failure is "missing" or "metadata-failure") Assert.IsType<HttpRequestException>(exception);
        else Assert.IsType<KnownException>(exception);
        Assert.Equal(0, automation.Calls);
        Assert.Empty(ncr.GetDomainEvents());
        // 即使调用方随后保存同一 DbContext，拒绝路径也不能留下部分变更。
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var persisted = await db.NonconformanceReports.SingleAsync(x => x.Id == id);
        Assert.Equal("open", persisted.Status);
        Assert.Null(persisted.DispositionType);
        Assert.Null(persisted.DispositionApprovalChainId);
        Assert.Empty(persisted.MrbReviews);
        Assert.Equal(existing ? ["bad-file"] : Array.Empty<string>(), persisted.AttachmentFileIds);
    }

    [Fact]
    public async Task Sort_and_screen_validates_existing_and_new_files_before_accepting_without_approval()
    {
        await using var db = CreateDb();
        var ncr = NewNcr(["existing-file"]);
        db.NonconformanceReports.Add(ncr);
        await db.SaveChangesAsync();
        var files = new QualityEvidenceTestFiles();
        foreach (var id in new[] { "existing-file", "new-file" })
            files.Files.Add(id, QualityEvidenceTestFiles.Metadata(ncr, id));
        var handler = new SubmitNonconformanceReportDispositionCommandHandler(
            new NonconformanceReportRepository(db), new UnexpectedApproval(), new RecordingAutomation(), db, files.CreateClient());

        await handler.Handle(new(ncr.Id, ncr.OrganizationId, ncr.EnvironmentId,
            "sort-and-screen", null, ["new-file"], []), CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var persisted = await db.NonconformanceReports.SingleAsync();
        Assert.Equal(new[] { "existing-file", "new-file" }, files.Reads);
        Assert.Equal(new[] { "existing-file", "new-file" }, persisted.AttachmentFileIds);
        Assert.Equal("disposition-in-progress", persisted.Status);
        Assert.Empty(persisted.MrbReviews);
        Assert.Null(persisted.DispositionApprovalChainId);
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Mediator(new ServiceCollection().BuildServiceProvider()));

    private static NonconformanceReport NewNcr(IReadOnlyCollection<string> files) =>
        NonconformanceReport.Open("org-001", "env-dev", "NCR-EVIDENCE", "in-process", "DEFECT-001",
            "SKU-001", 1m, "scratch", null, null, files);

    private sealed class UnexpectedApproval : IApprovalChainStatusClient
    {
        public Task<bool> IsApprovedForNcrDispositionAsync(string chainId, string organizationId, string environmentId,
            string ncrCode, CancellationToken ct) => throw new InvalidOperationException("Sort-and-screen does not require approval.");
        public Task<bool> IsApprovedForCapaClosureAsync(string chainId, string organizationId, string environmentId,
            string capaCode, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingAutomation : ICapaAutomationService
    {
        public int Calls { get; private set; }
        public Task OpenForDispositionIfRequiredAsync(NonconformanceReport ncr, CancellationToken ct)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
