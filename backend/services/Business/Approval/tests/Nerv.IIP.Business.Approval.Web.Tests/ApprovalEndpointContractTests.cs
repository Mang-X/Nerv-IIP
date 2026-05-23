using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalTemplateAggregate;
using Nerv.IIP.Business.Approval.Infrastructure;
using Nerv.IIP.Business.Approval.Web.Application.Auth;
using Nerv.IIP.Business.Approval.Web.Application.Commands.Chains;
using Nerv.IIP.Business.Approval.Web.Application.Commands.Templates;
using Nerv.IIP.Business.Approval.Web.Application.Queries.Chains;
using Nerv.IIP.Business.Approval.Web.Endpoints.Approvals;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Approval.Web.Tests;

public sealed class ApprovalEndpointContractTests
{
    [Fact]
    public void Approval_endpoints_expose_issue_134_routes_permissions_policies_and_operation_ids()
    {
        var contracts = ApprovalEndpointContracts.All.ToArray();

        Assert.Equal(6, contracts.Length);
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/approvals/templates"
            && x.PermissionCode == ApprovalPermissionCodes.Manage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "createOrUpdateApprovalTemplate");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/approvals/templates"
            && x.PermissionCode == ApprovalPermissionCodes.Read
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "listApprovalTemplates");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/approvals/chains"
            && x.PermissionCode == ApprovalPermissionCodes.Manage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "startApprovalChain");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/approvals/chains/{chainId}"
            && x.PermissionCode == ApprovalPermissionCodes.Read
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "getApprovalChain");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/approvals/tasks"
            && x.PermissionCode == ApprovalPermissionCodes.Read
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "listPendingApprovalTasks");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/approvals/chains/{chainId}/steps/{stepNo}/resolve"
            && x.PermissionCode == ApprovalPermissionCodes.Manage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "resolveApprovalStep");
    }

    [Theory]
    [InlineData(typeof(CreateOrUpdateApprovalTemplateEndpoint))]
    [InlineData(typeof(ListApprovalTemplatesEndpoint))]
    [InlineData(typeof(StartApprovalChainEndpoint))]
    [InlineData(typeof(GetApprovalChainEndpoint))]
    [InlineData(typeof(ListPendingApprovalTasksEndpoint))]
    [InlineData(typeof(ResolveApprovalStepEndpoint))]
    public void Approval_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
        Assert.DoesNotContain(typeof(ApplicationDbContext), parameterTypes);
    }

    [Fact]
    public async Task Pending_tasks_query_only_returns_actor_current_sequence_tasks()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var template = NewTemplate();
        dbContext.ApprovalTemplates.Add(template);
        var chain = ApprovalChain.Start(template, NewDocument(), "system:eco");
        dbContext.ApprovalChains.Add(chain);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ListPendingApprovalTasksQueryHandler(dbContext);
        var engineeringTasks = await handler.Handle(new ListPendingApprovalTasksQuery("org-001", "env-dev", "user", "u-engineering"), CancellationToken.None);
        var qualityTasksBefore = await handler.Handle(new ListPendingApprovalTasksQuery("org-001", "env-dev", "user", "u-quality"), CancellationToken.None);
        chain.ResolveStep(1, "user", "u-engineering", "approve", "ok");
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var qualityTasksAfter = await handler.Handle(new ListPendingApprovalTasksQuery("org-001", "env-dev", "user", "u-quality"), CancellationToken.None);

        Assert.Single(engineeringTasks);
        Assert.Empty(qualityTasksBefore);
        Assert.Single(qualityTasksAfter);
        Assert.Equal(2, qualityTasksAfter.Single().StepNo);
    }

    [Fact]
    public void Validators_reject_invalid_approval_codes_and_decisions()
    {
        var templateResult = new CreateOrUpdateApprovalTemplateCommandValidator().Validate(
            NewTemplateCommand() with { TemplateCode = "ECO;DROP" });
        var resolveResult = new ResolveApprovalStepCommandValidator().Validate(
            new ResolveApprovalStepCommand(new ApprovalChainId(Guid.CreateVersion7()), 1, "user", "u-engineering", "escalate", null));

        Assert.False(templateResult.IsValid);
        Assert.Contains(templateResult.Errors, x => x.ErrorMessage.Contains("letters", StringComparison.OrdinalIgnoreCase));
        Assert.False(resolveResult.IsValid);
        Assert.Contains(resolveResult.Errors, x => x.ErrorMessage.Contains("approve", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Approval_http_endpoints_reject_anonymous_callers_before_persistence()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/business/v1/approvals/templates", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            templateCode = "ECO-DEFAULT",
            documentType = "engineering-change-order",
            version = 1,
            isActive = true,
            steps = new[]
            {
                new
                {
                    stepNo = 1,
                    stepName = "Engineering review",
                    parallelGroupKey = (string?)null,
                    approverType = "user",
                    approverRef = "u-engineering",
                    dueInHours = 24,
                },
            },
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase($"approval-api-contract-{Guid.CreateVersion7():N}"));
        return services.BuildServiceProvider();
    }

    internal static CreateOrUpdateApprovalTemplateCommand NewTemplateCommand()
    {
        return new CreateOrUpdateApprovalTemplateCommand(
            "org-001",
            "env-dev",
            "ECO-DEFAULT",
            "engineering-change-order",
            1,
            true,
            [
                new ApprovalTemplateStepInput(1, "Engineering review", null, "user", "u-engineering", 24),
                new ApprovalTemplateStepInput(2, "Quality review", null, "user", "u-quality", 24),
            ]);
    }

    internal static ApprovalTemplate NewTemplate()
    {
        return ApprovalTemplate.Create(
            "org-001",
            "env-dev",
            "ECO-DEFAULT",
            "engineering-change-order",
            1,
            true,
            [
                new ApprovalTemplateStepDefinition(1, "Engineering review", null, "user", "u-engineering", 24),
                new ApprovalTemplateStepDefinition(2, "Quality review", null, "user", "u-quality", 24),
            ]);
    }

    internal static ApprovalDocumentReference NewDocument()
    {
        return new ApprovalDocumentReference("eco", "engineering-change-order", "ECO-1001", null);
    }
}
