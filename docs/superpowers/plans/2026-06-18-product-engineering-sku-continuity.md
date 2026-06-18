# ProductEngineering SKU Continuity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close issue #405 by making ProductEngineering treat EngineeringItem and EBOM codes as SKU codes and by requiring EBOM lines to match MBOM material lines.

**Architecture:** Keep existing public field names (`ItemCode`, `ParentItemCode`, `ChildItemCode`) as compatibility names, but freeze their meaning as MasterData SKU codes. Do not add a ProductEngineering item-to-SKU mapping table. Add command-handler validation so MBOM release cannot publish a manufacturing BOM whose material lines drift from the referenced EBOM.

**Tech Stack:** .NET 10, CleanDDD, FastEndpoints, MediatR, EF Core, xUnit.

## Global Constraints

- Do not introduce cross-schema foreign keys or direct references from ProductEngineering Domain/Application to MasterData Infrastructure.
- Keep ProductEngineering as the owner of EBOM, MBOM, Routing, ProductionVersion and revision facts.
- Keep MasterData as the owner of durable SKU/material identity.
- Use TDD: add failing tests before implementation.

---

### Task 1: ProductEngineering SKU Continuity Tests

**Files:**
- Modify: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringReleaseApiContractTests.cs`
- Modify: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/ProductEngineeringReleaseAggregateTests.cs`

**Interfaces:**
- Consumes: existing `ReleaseManufacturingBomCommandHandler`
- Produces: tests that require MBOM material lines to match referenced EBOM child SKU codes.

- [ ] Add failing tests for missing EBOM material line and orphan MBOM material line.
- [ ] Update existing EBOM/MBOM fixtures so compatibility field names use SKU-like codes.
- [ ] Run ProductEngineering Web tests and confirm the new tests fail for missing continuity validation.

### Task 2: ProductEngineering Continuity Validation

**Files:**
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductEngineeringReleaseCommands.cs`

**Interfaces:**
- Consumes: `EngineeringBom.Lines` and `ReleaseManufacturingBomCommand.MaterialLines`
- Produces: deterministic `KnownException` failures when EBOM child SKU codes and MBOM material SKU codes diverge.

- [ ] Add minimal release-handler validation for MBOM release against the EBOM parent SKU and child SKU code set.
- [ ] Run ProductEngineering Domain and Web tests.

### Task 3: Documentation

**Files:**
- Modify: `docs/architecture/business-platform-domain-architecture.md`
- Modify: `docs/architecture/api-contract-and-codegen.md`
- Modify: `docs/architecture/implementation-readiness.md`

**Interfaces:**
- Consumes: code diff from Tasks 1-2.
- Produces: docs that state existing `itemCode` compatibility names now represent SKU codes.

- [ ] Document the compatibility semantic: EngineeringItem itemCode and EBOM parent/child codes are SKU codes.
- [ ] Document MBOM release line continuity validation.
- [ ] Run focused tests again after doc updates.
