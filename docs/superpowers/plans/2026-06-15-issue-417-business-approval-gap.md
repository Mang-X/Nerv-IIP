# Issue 417 BusinessApproval Gap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the first real BusinessApproval backend loop by making workflow behavior executable and requiring ProductEngineering ECO release to verify an approved BusinessApproval chain.

**Architecture:** Keep BusinessApproval as the business approval fact source, not an Ops replacement. Latest `main` already provides the public `Nerv.IIP.Contracts.Approval` contract and ERP purchase-order approval loop from #411. This plan now uses that contract, keeps ERP PO behavior from main, and closes the remaining #417 gap by making workflow behavior executable and requiring ProductEngineering ECO release to verify an approved BusinessApproval chain before archiving affected versions.

**Tech Stack:** .NET 10, CleanDDD, FastEndpoints, EF Core PostgreSQL migrations, CAP integration events, xUnit, `Nerv.IIP.Messaging.CAP`.

---

### Task 1: Public Approval Event Contract

**Files:**
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Approval/Nerv.IIP.Contracts.Approval.csproj`
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Approval/ApprovalIntegrationEvents.cs`
- Modify: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Nerv.IIP.Business.Approval.Web.csproj`
- Modify: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/IntegrationEventConverters/ApprovalIntegrationEventConverters.cs`
- Modify: `backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/ApprovalIntegrationEventTests.cs`

- [x] Add a failing test proving BusinessApproval converters emit public `Nerv.IIP.Contracts.Approval` envelope events.
- [x] Add the contracts project with ADR 0011 envelope-compatible started/step/completed/overdue event records.
- [x] Move event type/source constants to the contracts project and update converters/tests.

### Task 2: Workflow Policies And Conditions

**Files:**
- Modify: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalTemplateAggregate/ApprovalTemplate.cs`
- Modify: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalChainAggregate/ApprovalChain.cs`
- Modify: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/EntityConfigurations/ApprovalTemplateStepEntityTypeConfiguration.cs`
- Modify: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/EntityConfigurations/ApprovalStepEntityTypeConfiguration.cs`
- Modify: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Endpoints/Approvals/ApprovalEndpoints.cs`
- Modify: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/Commands/Templates/CreateOrUpdateApprovalTemplateCommand.cs`
- Modify: `backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Domain.Tests/ApprovalAggregateTests.cs`

- [x] Add failing domain tests for same-step `any` approval, same-step `all` approval, and simple condition routing.
- [x] Add `CompletionPolicy` (`all`/`any`) and `ConditionExpression` to template/runtime steps.
- [x] Evaluate supported MVP conditions against document reference metadata: empty condition always applies; `documentType=<value>` and `sourceService=<value>` route matching steps.
- [x] Ensure pending task sequencing treats a previous step number as complete when every active group at that number meets its policy.

### Task 3: Delegation Enforcement

**Files:**
- Modify: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalChainAggregate/ApprovalChain.cs`
- Modify: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/Commands/Chains/ResolveApprovalStepCommand.cs`
- Modify: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/EntityConfigurations/ApprovalDecisionEntityTypeConfiguration.cs`
- Modify: `backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/ApprovalEndpointContractTests.cs`

- [x] Add a failing handler test showing an active delegate can approve on behalf of the original approver and revoked/expired delegations cannot.
- [x] Load active matching delegations in the command handler.
- [x] Record `OnBehalfOfActorType` and `OnBehalfOfActorRef` on decisions when a delegate resolves a delegator step.

### Task 4: Timeout Escalation

**Files:**
- Modify: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/DomainEvents/ApprovalDomainEvents.cs`
- Modify: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalChainAggregate/ApprovalChain.cs`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/Commands/Chains/CheckOverdueApprovalStepsCommand.cs`
- Modify: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Endpoints/Approvals/ApprovalEndpoints.cs`
- Modify: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/IntegrationEventConverters/ApprovalIntegrationEventConverters.cs`
- Modify: `backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/ApprovalEndpointContractTests.cs`
- Modify: `backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/ApprovalIntegrationEventTests.cs`

- [x] Add a failing command test for marking due pending steps as overdue exactly once.
- [x] Add an internal-service authorized endpoint so overdue detection has a real trigger surface and uses the service clock instead of caller-supplied time.
- [x] Add step overdue state and an `ApprovalStepOverdue` integration event for Notification/workbench consumers.
- [x] Keep escalation light: emit an event and preserve assignment; automatic reassignment is a later policy extension.

### Task 5: ProductEngineering ECO Approval Gate

**Files:**
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Nerv.IIP.Business.ProductEngineering.Web.csproj`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductEngineeringReleaseCommands.cs`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Program.cs`
- Modify: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringReleaseApiContractTests.cs`
- Modify: `docs/architecture/implementation-readiness.md`

- [x] Add a failing ProductEngineering command handler test showing ECO release requires a matching approved BusinessApproval chain.
- [x] Add a failing command validator/handler test preventing direct arbitrary approval references except idempotent already-released records.
- [x] Add a guarded HTTP verifier that reads BusinessApproval chain detail with an internal service token; rejected/returned/non-matching chains block release.
- [x] Update readiness docs to state the ECO gate and retain latest-main ERP PO approval closure from #411.

### Task 6: Migration And Verification

**Files:**
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/Migrations/*Issue417ApprovalWorkflowGaps*`
- Modify: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Modify: `docs/architecture/database-schema-catalog.md`

- [x] Generate the BusinessApproval migration for new workflow/delegation/overdue columns.
- [x] Update schema catalog comments.
- [x] Run focused BusinessApproval and ProductEngineering tests, then the BusinessApproval verify script.
