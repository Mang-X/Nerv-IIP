using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.SpcControlChartAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Queries;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Queries.Spc;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityListQueryCompositionTests
{
    [Theory]
    [InlineData(-1, 0, 500, 0, 1)]
    [InlineData(0, 501, 500, 0, 500)]
    [InlineData(0, 201, 200, 0, 200)]
    public void Offset_page_normalizes_to_the_query_specific_bounds(
        int skip,
        int take,
        int maxTake,
        int expectedSkip,
        int expectedTake)
    {
        var page = OffsetPage.From(skip, take, maxTake);

        Assert.Equal(expectedSkip, page.Skip);
        Assert.Equal(expectedTake, page.Take);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Search_term_treats_blank_keywords_as_absent(string? keyword)
    {
        Assert.Null(SearchTerm.From(keyword).Value);
    }

    [Fact]
    public void Tenant_scope_and_search_term_normalize_public_query_values()
    {
        var tenant = TenantScope.From(" org-001 ", " env-dev ");
        var keyword = SearchTerm.From("  PuMp ");

        Assert.Equal("org-001", tenant.OrganizationId);
        Assert.Equal("env-dev", tenant.EnvironmentId);
        Assert.Equal("pump", keyword.Value);
    }

    [Fact]
    public void Quality_validators_keep_existing_page_bounds_and_domain_rules()
    {
        var recordResult = new ListInspectionRecordsQueryValidator().Validate(
            new ListInspectionRecordsQuery("org-001", "env-dev", null, null, null, null, null, -1, 0));
        var taskResult = new ListInspectionTasksQueryValidator().Validate(
            new ListInspectionTasksQuery("org-001", "env-dev", null, null, Take: 201, ScopeKind: "invalid"));
        var spcResult = new ListSpcControlChartsQueryValidator().Validate(
            new ListSpcControlChartsQuery("org-001", "env-dev", Keyword: new string('x', 201), Take: 501));

        Assert.Contains(recordResult.Errors, error => error.PropertyName == "Skip");
        Assert.Contains(recordResult.Errors, error => error.PropertyName == "Take");
        Assert.Contains(taskResult.Errors, error => error.PropertyName == "Take");
        Assert.Contains(taskResult.Errors, error => error.PropertyName == "ScopeKind");
        Assert.Contains(spcResult.Errors, error => error.PropertyName == "Keyword");
        Assert.Contains(spcResult.Errors, error => error.PropertyName == "Take");
    }

    [Fact]
    public async Task Inspection_record_list_uses_normalized_tenant_and_page()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionRecords.AddRange(
            NewInspectionRecord("org-001", "env-dev", "REC-A"),
            NewInspectionRecord("org-001", "env-dev", "REC-B"),
            NewInspectionRecord("org-other", "env-dev", "REC-CROSS-TENANT"));
        await dbContext.SaveChangesAsync();

        var result = await new ListInspectionRecordsQueryHandler(dbContext).Handle(
            new ListInspectionRecordsQuery(
                " org-001 ",
                " env-dev ",
                null,
                null,
                null,
                null,
                null,
                Skip: 1,
                Take: 1),
            CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Single(result.Items);
        Assert.DoesNotContain(result.Items, item => item.SourceDocumentId == "REC-CROSS-TENANT");
    }

    [Fact]
    public async Task Inspection_task_list_uses_normalized_tenant_page_and_keyword()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionTasks.AddRange(
            NewInspectionTask("org-001", "env-dev", "WO-PUMP-A", DateTimeOffset.Parse("2026-08-30T08:00:00Z")),
            NewInspectionTask("org-001", "env-dev", "WO-PUMP-B", DateTimeOffset.Parse("2026-08-30T09:00:00Z")),
            NewInspectionTask("org-other", "env-dev", "WO-PUMP-CROSS-TENANT", DateTimeOffset.Parse("2026-08-30T10:00:00Z")));
        await dbContext.SaveChangesAsync();

        var result = await new ListInspectionTasksQueryHandler(dbContext).Handle(
            new ListInspectionTasksQuery(
                " org-001 ",
                " env-dev ",
                null,
                null,
                Skip: 1,
                Take: 1,
                Keyword: "  pUmP ",
                AsOfUtc: DateTimeOffset.Parse("2026-08-30T07:00:00Z")),
            CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Equal("WO-PUMP-B", Assert.Single(result.Items).SourceDocumentId);
    }

    [Fact]
    public async Task Spc_catalog_uses_normalized_tenant_page_and_keyword()
    {
        await using var dbContext = CreateDbContext();
        dbContext.SpcControlCharts.AddRange(
            SpcControlChart.Create("org-001", "env-dev", "SKU-PUMP-A", "length", "WC-01", 5),
            SpcControlChart.Create("org-001", "env-dev", "SKU-PUMP-B", "length", "WC-01", 5),
            SpcControlChart.Create("org-other", "env-dev", "SKU-PUMP-CROSS-TENANT", "length", "WC-01", 5));
        await dbContext.SaveChangesAsync();

        var result = await new ListSpcControlChartsQueryHandler(dbContext).Handle(
            new ListSpcControlChartsQuery(
                " org-001 ",
                " env-dev ",
                Keyword: "  pUmP ",
                Skip: 1,
                Take: 1),
            CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Equal("SKU-PUMP-B", Assert.Single(result.Items).SkuCode);
        Assert.Equal(0, result.LockedCount);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static InspectionRecord NewInspectionRecord(
        string organizationId,
        string environmentId,
        string sourceDocumentId) =>
        InspectionRecord.Create(
            organizationId,
            environmentId,
            null,
            "receiving",
            "wms",
            sourceDocumentId,
            "SKU-PUMP",
            1m,
            null,
            null,
            [InspectionResultLineInput.Pass("appearance", "ok", null, [])],
            null,
            []);

    private static InspectionTask NewInspectionTask(
        string organizationId,
        string environmentId,
        string sourceDocumentId,
        DateTimeOffset createdAtUtc) =>
        InspectionTask.CreatePending(
            organizationId,
            environmentId,
            new InspectionPlanId(Guid.CreateVersion7()),
            "operation",
            "mes",
            sourceDocumentId,
            "OP-10",
            "SKU-PUMP",
            1m,
            "pcs",
            null,
            null,
            createdAtUtc,
            createdAtUtc.AddHours(1),
            $"quality:test:{sourceDocumentId}");

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
