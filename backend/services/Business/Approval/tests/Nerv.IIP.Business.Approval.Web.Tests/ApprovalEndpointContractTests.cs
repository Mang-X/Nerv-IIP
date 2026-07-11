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
using Nerv.IIP.Business.Approval.Web.Application.Commands.Delegations;
using Nerv.IIP.Business.Approval.Web.Application.Commands.Chains;
using Nerv.IIP.Business.Approval.Web.Application.Commands.Templates;
using Nerv.IIP.Business.Approval.Web.Application.Queries.Chains;
using Nerv.IIP.Business.Approval.Web.Application.Queries.Delegations;
using Nerv.IIP.Business.Approval.Web.Endpoints.Approvals;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Approval.Web.Tests;

public sealed class ApprovalEndpointContractTests
{
    [Fact]
    public void Approval_endpoints_expose_issue_134_routes_permissions_policies_and_operation_ids()
    {
        var contracts = ApprovalEndpointContracts.All.ToArray();

        Assert.Equal(16, contracts.Length);
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
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/approvals/chains"
            && x.PermissionCode == ApprovalPermissionCodes.Read
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "listApprovalChains");
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
            && x.Route == "/api/business/v1/approvals/tasks/overdue/check"
            && x.PermissionCode == ApprovalPermissionCodes.Manage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "checkOverdueApprovalSteps");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/approvals/decisions"
            && x.PermissionCode == ApprovalPermissionCodes.Read
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "listApprovalDecisions");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/approvals/chains/{chainId}/steps/{stepNo}/resolve"
            && x.PermissionCode == ApprovalPermissionCodes.Manage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "resolveApprovalStep");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/approvals/chains/{chainId}/withdraw"
            && x.PermissionCode == ApprovalPermissionCodes.Manage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "withdrawApprovalChain");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/approvals/chains/{chainId}/resubmit"
            && x.PermissionCode == ApprovalPermissionCodes.Manage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "resubmitApprovalChain");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/approvals/chains/{chainId}/steps/{stepNo}/add-signer"
            && x.PermissionCode == ApprovalPermissionCodes.Manage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "addApprovalStepSigner");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/approvals/chains/{chainId}/steps/{stepNo}/transfer"
            && x.PermissionCode == ApprovalPermissionCodes.Manage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "transferApprovalStep");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/approvals/delegations"
            && x.PermissionCode == ApprovalPermissionCodes.Read
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "listApprovalDelegations");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/approvals/delegations"
            && x.PermissionCode == ApprovalPermissionCodes.Manage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "createApprovalDelegation");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/approvals/delegations/{delegationId}/revoke"
            && x.PermissionCode == ApprovalPermissionCodes.Manage
            && x.AuthorizationPolicy == InternalServiceAuthorizationPolicy.Name
            && x.OperationId == "revokeApprovalDelegation");
    }

    [Theory]
    [InlineData(typeof(CreateOrUpdateApprovalTemplateEndpoint))]
    [InlineData(typeof(ListApprovalTemplatesEndpoint))]
    [InlineData(typeof(ListApprovalChainsEndpoint))]
    [InlineData(typeof(StartApprovalChainEndpoint))]
    [InlineData(typeof(GetApprovalChainEndpoint))]
    [InlineData(typeof(ListPendingApprovalTasksEndpoint))]
    [InlineData(typeof(CheckOverdueApprovalStepsEndpoint))]
    [InlineData(typeof(ListApprovalDecisionsEndpoint))]
    [InlineData(typeof(ResolveApprovalStepEndpoint))]
    [InlineData(typeof(WithdrawApprovalChainEndpoint))]
    [InlineData(typeof(ResubmitApprovalChainEndpoint))]
    [InlineData(typeof(AddApprovalStepSignerEndpoint))]
    [InlineData(typeof(TransferApprovalStepEndpoint))]
    [InlineData(typeof(ListApprovalDelegationsEndpoint))]
    [InlineData(typeof(CreateApprovalDelegationEndpoint))]
    [InlineData(typeof(RevokeApprovalDelegationEndpoint))]
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
    public async Task Approval_list_queries_return_filtered_paged_totals()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var template = NewTemplate();
        dbContext.ApprovalTemplates.Add(template);
        var first = ApprovalChain.Start(template, NewDocument(), "u-requester");
        var second = ApprovalChain.Start(template, new ApprovalDocumentReference("eco", "engineering-change-order", "ECO-1002", null), "u-requester");
        dbContext.ApprovalChains.AddRange(first, second);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        first.ResolveStep(1, "user", "u-engineering", "approve", "ok");
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var chains = await new ListApprovalChainsQueryHandler(dbContext).Handle(
            new ListApprovalChainsQuery("org-001", "env-dev", "pending", "u-requester", null, "engineering-change-order", null, 1, 1),
            CancellationToken.None);
        var decisions = await new ListApprovalDecisionsQueryHandler(dbContext).Handle(
            new ListApprovalDecisionsQuery("org-001", "env-dev", first.Id.ToString(), "user", "u-engineering", "approve", null, null, 0, 10),
            CancellationToken.None);

        Assert.Equal(2, chains.Total);
        Assert.Single(chains.Items);
        Assert.Contains(chains.Items.Single().DocumentId, new[] { "ECO-1001", "ECO-1002" });
        Assert.Equal(1, decisions.Total);
        Assert.Equal("ECO-1001", decisions.Items.Single().DocumentId);
    }

    [Fact]
    public async Task Approval_delegation_commands_create_query_and_revoke_authorizations()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = new CreateApprovalDelegationCommandHandler(dbContext);
        var revokeHandler = new RevokeApprovalDelegationCommandHandler(dbContext);
        var listHandler = new ListApprovalDelegationsQueryHandler(dbContext);

        var delegationId = await createHandler.Handle(new CreateApprovalDelegationCommand(
            "org-001",
            "env-dev",
            "user",
            "u-manager",
            "user",
            "u-backup",
            "engineering-change-order",
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-30T00:00:00Z"),
            "travel",
            "u-manager"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var activeBeforeRevoke = await listHandler.Handle(new ListApprovalDelegationsQuery(
            "org-001",
            "env-dev",
            "active",
            null,
            "u-backup",
            null,
            0,
            10), CancellationToken.None);
        await revokeHandler.Handle(new RevokeApprovalDelegationCommand(
            delegationId,
            "org-001",
            "env-dev",
            "u-manager"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var activeAfterRevoke = await listHandler.Handle(new ListApprovalDelegationsQuery(
            "org-001",
            "env-dev",
            "active",
            null,
            "u-backup",
            null,
            0,
            10), CancellationToken.None);

        Assert.Single(activeBeforeRevoke.Items);
        Assert.Equal("u-manager", activeBeforeRevoke.Items.Single().DelegatorActorRef);
        Assert.Equal(0, activeAfterRevoke.Total);
    }

    [Fact]
    public async Task Resolve_step_applies_only_active_matching_delegation()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var template = NewTemplate();
        dbContext.ApprovalTemplates.Add(template);
        var chain = ApprovalChain.Start(template, NewDocument(), "system:eco");
        dbContext.ApprovalChains.Add(chain);
        var createDelegation = new CreateApprovalDelegationCommandHandler(dbContext);
        await createDelegation.Handle(new CreateApprovalDelegationCommand(
            "org-001",
            "env-dev",
            "user",
            "u-engineering",
            "user",
            "u-backup",
            "engineering-change-order",
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddHours(1),
            "shift cover",
            "u-engineering"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var decisionId = await new ResolveApprovalStepCommandHandler(dbContext).Handle(new ResolveApprovalStepCommand(
            chain.Id,
            1,
            "user",
            "u-backup",
            "approve",
            "ok"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var decision = await dbContext.ApprovalDecisions.SingleAsync(x => x.Id == decisionId, CancellationToken.None);
        Assert.Equal("u-backup", decision.ActorRef);
        Assert.Equal("u-engineering", decision.OnBehalfOfActorRef);
    }

    [Fact]
    public async Task Concurrent_approval_decisions_on_same_chain_are_rejected()
    {
        await using var provider = CreateInMemoryProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var template = ApprovalTemplate.Create(
                "org-001",
                "env-dev",
                "COUNT-VARIANCE",
                "inventory-count-variance",
                1,
                true,
                [
                    new ApprovalTemplateStepDefinition(1, "Finance review", "finance-qc", "user", "u-finance", 24, "any"),
                    new ApprovalTemplateStepDefinition(1, "Quality review", "finance-qc", "user", "u-quality", 24, "any"),
                ]);
            var chain = ApprovalChain.Start(template, new ApprovalDocumentReference("inventory", "inventory-count-variance", "COUNT-001", null), "system:inventory");
            dbContext.ApprovalTemplates.Add(template);
            dbContext.ApprovalChains.Add(chain);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var firstDbContext = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secondDbContext = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var firstChain = await firstDbContext.ApprovalChains
            .Include(x => x.Steps)
            .Include(x => x.Decisions)
            .SingleAsync(CancellationToken.None);
        var secondChain = await secondDbContext.ApprovalChains
            .Include(x => x.Steps)
            .Include(x => x.Decisions)
            .SingleAsync(CancellationToken.None);

        firstChain.ResolveStep(1, "user", "u-finance", "approve", "ok");
        secondChain.ResolveStep(1, "user", "u-quality", "approve", "ok");
        await firstDbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<KnownException>(() => secondDbContext.SaveChangesAsync(CancellationToken.None));
        Assert.Contains("concurrent", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Approval_action_commands_withdraw_resubmit_add_signer_and_transfer()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var template = NewTemplate();
        dbContext.ApprovalTemplates.Add(template);
        var chain = ApprovalChain.Start(template, NewDocument(), "system:eco");
        dbContext.ApprovalChains.Add(chain);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var clock = new FixedApprovalClock(DateTimeOffset.Parse("2026-06-21T08:00:00Z"));
        await new WithdrawApprovalChainCommandHandler(dbContext, clock).Handle(new WithdrawApprovalChainCommand(
            chain.Id,
            "user",
            "u-requester",
            "duplicate"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var withdrawnChain = await dbContext.ApprovalChains.Include(x => x.Steps).SingleAsync(x => x.Id == chain.Id, CancellationToken.None);
        var withdrawnFirstStep = withdrawnChain.Steps.Single(x => x.StepNo == 1);
        Assert.Equal(ApprovalStepStatuses.Withdrawn, withdrawnFirstStep.Status);
        Assert.NotNull(withdrawnFirstStep.ResolvedAtUtc);

        clock.UtcNow = DateTimeOffset.Parse("2026-06-21T09:00:00Z");
        await new ResubmitApprovalChainCommandHandler(dbContext, clock).Handle(new ResubmitApprovalChainCommand(
            chain.Id,
            "user",
            "u-requester",
            "reworked"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var resubmittedChain = await dbContext.ApprovalChains.Include(x => x.Steps).SingleAsync(x => x.Id == chain.Id, CancellationToken.None);
        var resubmittedFirstStep = resubmittedChain.Steps.Single(x => x.StepNo == 1);
        Assert.Equal(ApprovalStepStatuses.Pending, resubmittedFirstStep.Status);
        Assert.Null(resubmittedFirstStep.ResolvedAtUtc);

        clock.UtcNow = DateTimeOffset.Parse("2026-06-21T09:05:00Z");
        await new TransferApprovalStepCommandHandler(dbContext, clock).Handle(new TransferApprovalStepCommand(
            chain.Id,
            1,
            "user",
            "u-engineering",
            "user",
            "u-engineering-backup",
            "user",
            "u-manager",
            "shift change"), CancellationToken.None);
        clock.UtcNow = DateTimeOffset.Parse("2026-06-21T09:10:00Z");
        var addedStepId = await new AddApprovalStepSignerCommandHandler(dbContext, clock).Handle(new AddApprovalStepSignerCommand(
            chain.Id,
            1,
            "user",
            "u-finance",
            "user",
            "u-engineering-backup",
            "amount threshold"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var stored = await dbContext.ApprovalChains
            .Include(x => x.Steps)
            .Include(x => x.Decisions)
            .SingleAsync(x => x.Id == chain.Id, CancellationToken.None);
        Assert.Equal(ApprovalChainStatuses.Pending, stored.Status);
        Assert.Contains(stored.Steps, x => x.Id == addedStepId
            && x.ApproverRef == "u-finance"
            && x.CompletionPolicy == ApprovalCompletionPolicies.All);
        Assert.Contains(stored.Steps, x => x.StepNo == 1 && x.ApproverRef == "u-engineering-backup");
        Assert.Contains(stored.Decisions, x => x.Decision == ApprovalDecisions.Withdraw && x.ActorRef == "u-requester");
        Assert.Contains(stored.Decisions, x => x.Decision == ApprovalDecisions.Resubmit && x.ActorRef == "u-requester");
        Assert.Contains(stored.Decisions, x => x.Decision == ApprovalDecisions.Transfer && x.ActorRef == "u-manager");
        Assert.Contains(stored.Decisions, x => x.Decision == ApprovalDecisions.AddSigner && x.ActorRef == "u-engineering-backup");
    }

    [Fact]
    public async Task Approval_action_commands_reject_invalid_states()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var template = NewTemplate();
        dbContext.ApprovalTemplates.Add(template);
        var chain = ApprovalChain.Start(template, NewDocument(), "system:eco");
        dbContext.ApprovalChains.Add(chain);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var clock = new FixedApprovalClock(DateTimeOffset.Parse("2026-06-21T08:00:00Z"));
        await Assert.ThrowsAsync<KnownException>(() => new ResubmitApprovalChainCommandHandler(dbContext, clock).Handle(
            new ResubmitApprovalChainCommand(chain.Id, "user", "u-requester", "retry"),
            CancellationToken.None));
        chain.ResolveStep(1, "user", "u-engineering", "approve", "ok");
        chain.ResolveStep(2, "user", "u-quality", "approve", "ok");
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await Assert.ThrowsAsync<KnownException>(() => new WithdrawApprovalChainCommandHandler(dbContext, clock).Handle(
            new WithdrawApprovalChainCommand(chain.Id, "user", "u-requester", "already approved"),
            CancellationToken.None));

        await Assert.ThrowsAsync<KnownException>(() => new AddApprovalStepSignerCommandHandler(dbContext, clock).Handle(
            new AddApprovalStepSignerCommand(chain.Id, 1, "user", "u-finance", "user", "u-engineering", "late"),
            CancellationToken.None));
        await Assert.ThrowsAsync<KnownException>(() => new TransferApprovalStepCommandHandler(dbContext, clock).Handle(
            new TransferApprovalStepCommand(chain.Id, 2, "user", "u-not-current", "user", "u-backup", "user", "u-manager", "invalid"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Overdue_check_marks_due_pending_steps_once()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var template = NewTemplate();
        dbContext.ApprovalTemplates.Add(template);
        var chain = ApprovalChain.Start(template, NewDocument(), "system:eco");
        dbContext.ApprovalChains.Add(chain);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var dueAt = chain.Steps.Min(x => x.DueAtUtc)!.Value;
        var handler = new CheckOverdueApprovalStepsCommandHandler(dbContext, new FixedApprovalClock(dueAt.AddSeconds(1)));

        var first = await handler.Handle(new CheckOverdueApprovalStepsCommand("org-001", "env-dev"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var second = await handler.Handle(new CheckOverdueApprovalStepsCommand("org-001", "env-dev"), CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.NotNull(chain.Steps.Single(x => x.StepNo == 1).OverdueNotifiedAtUtc);
    }

    [Fact]
    public void Template_validator_rejects_invalid_condition_expression_before_chain_start()
    {
        var command = NewTemplateCommand() with
        {
            Steps =
            [
                new ApprovalTemplateStepInput(1, "Engineering review", null, "user", "u-engineering", 24, ConditionExpression: "bad-condition"),
            ]
        };

        var result = new CreateOrUpdateApprovalTemplateCommandValidator().Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName.Contains(nameof(ApprovalTemplateStepInput.ConditionExpression), StringComparison.Ordinal));
    }

    [Fact]
    public void Template_validator_accepts_structured_amount_and_organization_condition()
    {
        var command = NewTemplateCommand() with
        {
            Steps =
            [
                new ApprovalTemplateStepInput(
                    1, "Finance review", null, "user", "u-finance", 24,
                    Condition: new ApprovalRoutingConditionInput(
                        MinimumAmount: 10_000m,
                        MaximumAmount: 50_000m,
                        DocumentTypes: ["purchase-order"],
                        OrganizationIds: ["org-001"],
                        DepartmentIds: ["dept-procurement"])),
            ],
        };

        Assert.True(new CreateOrUpdateApprovalTemplateCommandValidator().Validate(command).IsValid);
    }

    [Fact]
    public void Template_validator_rejects_malformed_structured_condition_json()
    {
        var command = NewTemplateCommand() with
        {
            Steps = [new ApprovalTemplateStepInput(1, "Finance review", null, "user", "u-finance", 24, ConditionExpression: "{bad")],
        };

        Assert.False(new CreateOrUpdateApprovalTemplateCommandValidator().Validate(command).IsValid);
    }

    [Fact]
    public async Task Start_chain_reports_condition_routing_without_active_steps_as_known_exception()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ApprovalTemplates.Add(ApprovalTemplate.Create(
            "org-001",
            "env-dev",
            "ECO-DEFAULT",
            "engineering-change-order",
            1,
            true,
            [
                new ApprovalTemplateStepDefinition(1, "Procurement review", null, "user", "u-procurement", 24, "all", "documentType=purchase-order"),
            ]));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new StartApprovalChainCommandHandler(dbContext);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new StartApprovalChainCommand(
                "org-001",
                "env-dev",
                "ECO-DEFAULT",
                "eco",
                "engineering-change-order",
                "ECO-1001",
                null,
                "system:eco"),
            CancellationToken.None));

        Assert.Contains("active step", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Start_chain_reuses_pending_chain_for_same_source_document()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ApprovalTemplates.Add(NewTemplate());
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new StartApprovalChainCommandHandler(dbContext);
        var command = new StartApprovalChainCommand("org-001", "env-dev", "ECO-DEFAULT", "eco", "engineering-change-order", "ECO-IDEMPOTENT", null, "user:requester");

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Single(dbContext.ApprovalChains);
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
        var engineeringTasks = await handler.Handle(new ListPendingApprovalTasksQuery("org-001", "env-dev", "user", "u-engineering", 0, 100), CancellationToken.None);
        var qualityTasksBefore = await handler.Handle(new ListPendingApprovalTasksQuery("org-001", "env-dev", "user", "u-quality", 0, 100), CancellationToken.None);
        chain.ResolveStep(1, "user", "u-engineering", "approve", "ok");
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var qualityTasksAfter = await handler.Handle(new ListPendingApprovalTasksQuery("org-001", "env-dev", "user", "u-quality", 0, 100), CancellationToken.None);

        Assert.Single(engineeringTasks.Items);
        Assert.Empty(qualityTasksBefore.Items);
        Assert.Single(qualityTasksAfter.Items);
        Assert.Equal(2, qualityTasksAfter.Items.Single().StepNo);
    }

    [Fact]
    public void Validators_reject_invalid_approval_codes_and_decisions()
    {
        var templateResult = new CreateOrUpdateApprovalTemplateCommandValidator().Validate(
            NewTemplateCommand() with { TemplateCode = "ECO;DROP" });
        var resolveResult = new ResolveApprovalStepCommandValidator().Validate(
            new ResolveApprovalStepCommand(new ApprovalChainId(Guid.CreateVersion7()), 1, "user", "u-engineering", "escalate", null));
        var decisionsResult = new ListApprovalDecisionsQueryValidator().Validate(
            new ListApprovalDecisionsQuery("org-001", "env-dev", "not-a-guid", null, null, null, null, null, 0, 10));

        Assert.False(templateResult.IsValid);
        Assert.Contains(templateResult.Errors, x => x.ErrorMessage.Contains("letters", StringComparison.OrdinalIgnoreCase));
        Assert.False(resolveResult.IsValid);
        Assert.Contains(resolveResult.Errors, x => x.ErrorMessage.Contains("approve", StringComparison.OrdinalIgnoreCase));
        Assert.False(decisionsResult.IsValid);
        Assert.Contains(decisionsResult.Errors, x => x.ErrorMessage.Contains("valid GUID", StringComparison.OrdinalIgnoreCase));
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
        var databaseName = $"approval-api-contract-{Guid.CreateVersion7():N}";
        var databaseRoot = new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName, databaseRoot));
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

    private sealed class FixedApprovalClock(DateTimeOffset utcNow) : IApprovalClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
