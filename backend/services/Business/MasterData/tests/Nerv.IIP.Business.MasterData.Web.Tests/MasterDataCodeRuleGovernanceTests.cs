using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.CodeRuleAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;
using Nerv.IIP.Business.MasterData.Web.Endpoints.MasterData;
using Nerv.IIP.Contracts.Coding;
using NetCorePal.Extensions.Primitives;
using System.Text.Json;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class MasterDataCodeRuleGovernanceTests
{
    public static TheoryData<string?> InvalidChangeReasons => new()
    {
        null,
        "",
        " ",
        "\t",
        "　",
        new string('变', 501),
        $" {new string('变', 500)}",
        $"{new string('变', 500)} ",
    };

    [Fact]
    public async Task List_code_rules_returns_current_definitions_with_segments()
    {
        await using var dbContext = CreateDbContext();
        dbContext.CodeRules.Add(CodeRule.Create(
            "org-001",
            "env-dev",
            "master-data.sku",
            "SKU code",
            "sku",
            (int)(ScopeDimension.Organization | ScopeDimension.Environment),
            SegmentsJson(CodeRuleSegment.ConstantOf("SKU"), CodeRuleSegment.SequenceOf(5)),
            true,
            1));
        await dbContext.SaveChangesAsync();

        var response = await new ListCodeRulesQueryHandler(dbContext).Handle(
            new ListCodeRulesQuery("org-001", "env-dev"),
            CancellationToken.None);

        var rule = Assert.Single(response.Rules);
        Assert.Equal("master-data.sku", rule.RuleKey);
        Assert.Equal("sku", rule.AppliesTo);
        Assert.Equal(1, rule.Version);
        Assert.True(rule.IsActive);
        Assert.Equal([SegmentType.Constant, SegmentType.Sequence], rule.Segments.Select(segment => segment.Type).ToArray());
    }

    [Fact]
    public async Task Get_code_rule_detail_includes_current_definition_and_version_audit()
    {
        await using var dbContext = CreateDbContext();
        dbContext.CodeRules.Add(CodeRule.Create(
            "org-001",
            "env-dev",
            "master-data.sku",
            "SKU code",
            "sku",
            (int)ScopeDimension.Organization,
            SegmentsJson(CodeRuleSegment.ConstantOf("SKU"), CodeRuleSegment.SequenceOf(5)),
            true,
            1));
        dbContext.CodeRuleVersions.Add(CodeRuleVersion.Record(
            "org-001",
            "env-dev",
            "master-data.sku",
            "SKU code",
            "sku",
            (int)ScopeDimension.Organization,
            SegmentsJson(CodeRuleSegment.ConstantOf("SKU"), CodeRuleSegment.SequenceOf(5)),
            true,
            1,
            CodeRuleVersionStatus.Active,
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            "seed",
            "standard seed",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        await dbContext.SaveChangesAsync();

        var response = await new GetCodeRuleDetailQueryHandler(dbContext).Handle(
            new GetCodeRuleDetailQuery("org-001", "env-dev", "master-data.sku"),
            CancellationToken.None);

        Assert.Equal("master-data.sku", response.Rule.RuleKey);
        Assert.Equal(1, response.Rule.Version);
        var version = Assert.Single(response.Versions);
        Assert.Equal(CodeRuleVersionStatus.Active, version.Status);
        Assert.Equal("seed", version.CreatedBy);
    }

    [Fact]
    public async Task Create_code_rule_version_records_audit_and_promotes_immediate_effective_definition()
    {
        await using var dbContext = CreateDbContext();
        dbContext.CodeRules.Add(CodeRule.Create(
            "org-001",
            "env-dev",
            "master-data.sku",
            "SKU code",
            "sku",
            (int)ScopeDimension.Organization,
            SegmentsJson(CodeRuleSegment.ConstantOf("SKU"), CodeRuleSegment.SequenceOf(5)),
            true,
            1));
        await dbContext.SaveChangesAsync();

        var effectiveFromUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var response = await new CreateCodeRuleVersionCommandHandler(dbContext).Handle(
            new CreateCodeRuleVersionCommand(
                "org-001",
                "env-dev",
                "master-data.sku",
                "SKU code v2",
                "sku",
                ScopeDimension.Organization,
                [CodeRuleSegment.ConstantOf("SKU2"), CodeRuleSegment.SequenceOf(4)],
                true,
                effectiveFromUtc,
                "admin-001",
                "  align plant convention　"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(2, response.Version);
        Assert.Equal(CodeRuleVersionStatus.Active, response.Status);
        var current = await dbContext.CodeRules.SingleAsync();
        Assert.Equal(2, current.Version);
        Assert.Equal("SKU code v2", current.DisplayName);
        Assert.Contains("SKU2", current.SegmentsJson, StringComparison.Ordinal);
        var audit = await dbContext.CodeRuleVersions.SingleAsync();
        Assert.Equal("admin-001", audit.CreatedBy);
        Assert.Equal("align plant convention", audit.ChangeReason);
        Assert.Equal("align plant convention", response.ChangeReason);
        Assert.Equal(effectiveFromUtc, audit.EffectiveFromUtc);
    }

    [Theory]
    [MemberData(nameof(InvalidChangeReasons))]
    public void Code_rule_version_request_rejects_invalid_change_reason_differentially(string? reason)
    {
        var validator = new CreateCodeRuleVersionRequestValidator();

        Assert.True(validator.Validate(Request("调整编码规范")).IsValid);
        Assert.False(validator.Validate(Request(reason!)).IsValid);
    }

    [Fact]
    public void Code_rule_version_request_accepts_the_500_character_boundary()
    {
        var result = new CreateCodeRuleVersionRequestValidator()
            .Validate(Request(new string('变', 500)));

        Assert.True(
            result.IsValid,
            string.Join("; ", result.Errors.Select(failure => failure.ErrorMessage)));
    }

    [Theory]
    [MemberData(nameof(InvalidChangeReasons))]
    public void Code_rule_version_domain_rejects_invalid_change_reason_differentially(string? reason)
    {
        Assert.Equal("调整编码规范", Version("  调整编码规范　").ChangeReason);

        Assert.ThrowsAny<ArgumentException>(() => Version(reason!));
    }

    [Fact]
    public void Code_rule_version_domain_accepts_the_500_character_boundary()
    {
        var reason = new string('变', 500);
        var paddedReason = $" {new string('变', 498)} ";

        Assert.Equal(reason, Version(reason).ChangeReason);
        Assert.Equal(new string('变', 498), Version(paddedReason).ChangeReason);
    }

    [Theory]
    [MemberData(nameof(InvalidChangeReasons))]
    public async Task Create_code_rule_version_rejects_invalid_reason_before_state_or_audit_changes(string? reason)
    {
        await using var dbContext = CreateDbContext();
        var current = CodeRule.Create(
            "org-001",
            "env-dev",
            "master-data.sku",
            "SKU code",
            "sku",
            (int)ScopeDimension.Organization,
            SegmentsJson(CodeRuleSegment.ConstantOf("SKU"), CodeRuleSegment.SequenceOf(5)),
            true,
            1);
        var activeVersion = Version("initial governance reason");
        dbContext.CodeRules.Add(current);
        dbContext.CodeRuleVersions.Add(activeVersion);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<KnownException>(() => new CreateCodeRuleVersionCommandHandler(dbContext).Handle(
            Command(reason!),
            CancellationToken.None));

        Assert.Equal(1, current.Version);
        Assert.Equal(CodeRuleVersionStatus.Active, activeVersion.Status);
        Assert.Single(dbContext.CodeRuleVersions.Local);
        Assert.DoesNotContain(
            dbContext.ChangeTracker.Entries<CodeRuleVersion>(),
            entry => entry.State == EntityState.Added);
    }

    [Fact]
    public async Task Create_code_rule_version_accepts_500_characters_and_persists_the_real_audit_reason()
    {
        await using var dbContext = CreateDbContext();
        var reason = new string('变', 500);

        var response = await new CreateCodeRuleVersionCommandHandler(dbContext).Handle(
            Command(reason),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(reason, response.ChangeReason);
        Assert.Equal(reason, (await dbContext.CodeRuleVersions.SingleAsync()).ChangeReason);
    }

    [Fact]
    public async Task Create_code_rule_version_trims_the_raw_500_character_boundary_before_audit()
    {
        await using var dbContext = CreateDbContext();
        var normalizedReason = new string('变', 498);
        var rawReason = $" {normalizedReason} ";

        var response = await new CreateCodeRuleVersionCommandHandler(dbContext).Handle(
            Command(rawReason),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(500, rawReason.Length);
        Assert.Equal(normalizedReason, response.ChangeReason);
        Assert.Equal(normalizedReason, (await dbContext.CodeRuleVersions.SingleAsync()).ChangeReason);
    }

    [Fact]
    public async Task Create_immediate_code_rule_version_marks_previous_active_version_superseded()
    {
        await using var dbContext = CreateDbContext();
        dbContext.CodeRules.Add(CodeRule.Create(
            "org-001",
            "env-dev",
            "master-data.sku",
            "SKU code",
            "sku",
            (int)ScopeDimension.Organization,
            SegmentsJson(CodeRuleSegment.ConstantOf("SKU"), CodeRuleSegment.SequenceOf(5)),
            true,
            1));
        dbContext.CodeRuleVersions.Add(CodeRuleVersion.Record(
            "org-001",
            "env-dev",
            "master-data.sku",
            "SKU code",
            "sku",
            (int)ScopeDimension.Organization,
            SegmentsJson(CodeRuleSegment.ConstantOf("SKU"), CodeRuleSegment.SequenceOf(5)),
            true,
            1,
            CodeRuleVersionStatus.Active,
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            "seed",
            "standard seed",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        await dbContext.SaveChangesAsync();

        var response = await new CreateCodeRuleVersionCommandHandler(dbContext).Handle(
            new CreateCodeRuleVersionCommand(
                "org-001",
                "env-dev",
                "master-data.sku",
                "SKU code v2",
                "sku",
                ScopeDimension.Organization,
                [CodeRuleSegment.ConstantOf("SKU2"), CodeRuleSegment.SequenceOf(4)],
                true,
                new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero),
                "admin-001",
                "align plant convention"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(2, response.Version);
        var statuses = await dbContext.CodeRuleVersions.OrderBy(x => x.Version).Select(x => x.Status).ToArrayAsync();
        Assert.Equal(new[] { CodeRuleVersionStatus.Superseded, CodeRuleVersionStatus.Active }, statuses);
    }

    [Fact]
    public async Task Create_code_rule_version_advances_from_scheduled_audit_when_current_definition_missing()
    {
        await using var dbContext = CreateDbContext();
        var handler = new CreateCodeRuleVersionCommandHandler(dbContext);

        var firstResponse = await handler.Handle(
            new CreateCodeRuleVersionCommand(
                "org-001",
                "env-dev",
                "master-data.new-rule",
                "New rule",
                "new-resource",
                ScopeDimension.Organization,
                [CodeRuleSegment.ConstantOf("NEW-"), CodeRuleSegment.SequenceOf(4)],
                true,
                DateTimeOffset.UtcNow.AddDays(30),
                "admin-001",
                "schedule first version"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var secondResponse = await handler.Handle(
            new CreateCodeRuleVersionCommand(
                "org-001",
                "env-dev",
                "master-data.new-rule",
                "New rule revised",
                "new-resource",
                ScopeDimension.Organization,
                [CodeRuleSegment.ConstantOf("NR-"), CodeRuleSegment.SequenceOf(4)],
                true,
                DateTimeOffset.UtcNow.AddDays(31),
                "admin-001",
                "schedule second version"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, firstResponse.Version);
        Assert.Equal(CodeRuleVersionStatus.Scheduled, firstResponse.Status);
        Assert.Equal(2, secondResponse.Version);
        Assert.Equal(CodeRuleVersionStatus.Scheduled, secondResponse.Status);
        Assert.Empty(await dbContext.CodeRules.ToArrayAsync());
        var versions = await dbContext.CodeRuleVersions.OrderBy(x => x.Version).Select(x => x.Version).ToArrayAsync();
        Assert.Equal(new[] { 1, 2 }, versions);
    }

    [Fact]
    public async Task Promote_due_scheduled_versions_advances_current_definition_to_latest_due_version()
    {
        await using var dbContext = CreateDbContext();
        dbContext.CodeRules.Add(CodeRule.Create(
            "org-001",
            "env-dev",
            "master-data.sku",
            "SKU code",
            "sku",
            (int)ScopeDimension.Organization,
            SegmentsJson(CodeRuleSegment.ConstantOf("SKU"), CodeRuleSegment.SequenceOf(5)),
            true,
            1));
        dbContext.CodeRuleVersions.Add(CodeRuleVersion.Record(
            "org-001",
            "env-dev",
            "master-data.sku",
            "SKU code v2",
            "sku",
            (int)ScopeDimension.Organization,
            SegmentsJson(CodeRuleSegment.ConstantOf("SKU2"), CodeRuleSegment.SequenceOf(4)),
            true,
            2,
            CodeRuleVersionStatus.Scheduled,
            new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero),
            "admin-001",
            "scheduled v2",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        dbContext.CodeRuleVersions.Add(CodeRuleVersion.Record(
            "org-001",
            "env-dev",
            "master-data.sku",
            "SKU code v3",
            "sku",
            (int)ScopeDimension.Organization,
            SegmentsJson(CodeRuleSegment.ConstantOf("SKU3"), CodeRuleSegment.SequenceOf(3)),
            true,
            3,
            CodeRuleVersionStatus.Scheduled,
            new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero),
            "admin-001",
            "scheduled v3",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        await dbContext.SaveChangesAsync();

        var promoted = await new CodeRuleVersionActivationService(dbContext).PromoteDueVersionsAsync(
            new DateTimeOffset(2026, 6, 12, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(1, promoted);
        var current = await dbContext.CodeRules.SingleAsync();
        Assert.Equal(3, current.Version);
        Assert.Equal("SKU code v3", current.DisplayName);
        Assert.Contains("SKU3", current.SegmentsJson, StringComparison.Ordinal);
        var statuses = await dbContext.CodeRuleVersions.OrderBy(x => x.Version).Select(x => x.Status).ToArrayAsync();
        Assert.Equal(new[] { CodeRuleVersionStatus.Superseded, CodeRuleVersionStatus.Active }, statuses);
    }

    [Fact]
    public async Task Promote_due_scheduled_version_marks_it_superseded_when_newer_current_definition_exists()
    {
        await using var dbContext = CreateDbContext();
        dbContext.CodeRules.Add(CodeRule.Create(
            "org-001",
            "env-dev",
            "master-data.sku",
            "SKU code",
            "sku",
            (int)ScopeDimension.Organization,
            SegmentsJson(CodeRuleSegment.ConstantOf("SKU"), CodeRuleSegment.SequenceOf(5)),
            true,
            1));
        dbContext.CodeRuleVersions.Add(CodeRuleVersion.Record(
            "org-001",
            "env-dev",
            "master-data.sku",
            "SKU code",
            "sku",
            (int)ScopeDimension.Organization,
            SegmentsJson(CodeRuleSegment.ConstantOf("SKU"), CodeRuleSegment.SequenceOf(5)),
            true,
            1,
            CodeRuleVersionStatus.Active,
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            "seed",
            "standard seed",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        dbContext.CodeRuleVersions.Add(CodeRuleVersion.Record(
            "org-001",
            "env-dev",
            "master-data.sku",
            "SKU code v2",
            "sku",
            (int)ScopeDimension.Organization,
            SegmentsJson(CodeRuleSegment.ConstantOf("SKU2"), CodeRuleSegment.SequenceOf(4)),
            true,
            2,
            CodeRuleVersionStatus.Scheduled,
            new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero),
            "admin-001",
            "scheduled v2",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        await dbContext.SaveChangesAsync();

        var immediateResponse = await new CreateCodeRuleVersionCommandHandler(dbContext).Handle(
            new CreateCodeRuleVersionCommand(
                "org-001",
                "env-dev",
                "master-data.sku",
                "SKU code v3",
                "sku",
                ScopeDimension.Organization,
                [CodeRuleSegment.ConstantOf("SKU3"), CodeRuleSegment.SequenceOf(3)],
                true,
                new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero),
                "admin-001",
                "immediate v3"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var promoted = await new CodeRuleVersionActivationService(dbContext).PromoteDueVersionsAsync(
            new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(3, immediateResponse.Version);
        Assert.Equal(0, promoted);
        var current = await dbContext.CodeRules.SingleAsync();
        Assert.Equal(3, current.Version);
        var statuses = await dbContext.CodeRuleVersions.OrderBy(x => x.Version).Select(x => x.Status).ToArrayAsync();
        Assert.Equal(
            new[] { CodeRuleVersionStatus.Superseded, CodeRuleVersionStatus.Superseded, CodeRuleVersionStatus.Active },
            statuses);
    }

    [Fact]
    public async Task Preview_code_rule_uses_candidate_segments_without_persisting_counters()
    {
        await using var dbContext = CreateDbContext();

        var response = await new PreviewCodeRuleCommandHandler(dbContext).Handle(
            new PreviewCodeRuleCommand(
                "org-001",
                "env-dev",
                "master-data.sku",
                [CodeRuleSegment.ConstantOf("SKU-"), CodeRuleSegment.SequenceOf(width: 4, start: 42)],
                null,
                "SITE-01"),
            CancellationToken.None);

        Assert.Equal("SKU-0042", response.SampleCode);
        Assert.Empty(await dbContext.CodeCounters.ToArrayAsync());
    }

    [Fact]
    public void Code_rule_endpoints_are_registered_in_contract_registry()
    {
        var contracts = MasterDataEndpointContracts.All;

        Assert.Contains(contracts, contract =>
            contract.EndpointType == typeof(ListCodeRulesEndpoint) &&
            contract.Route == "/api/business/v1/master-data/code-rules" &&
            contract.HttpMethod == "GET" &&
            contract.OperationId == "listBusinessMasterDataCodeRules");
        Assert.Contains(contracts, contract =>
            contract.EndpointType == typeof(GetCodeRuleDetailEndpoint) &&
            contract.Route == "/api/business/v1/master-data/code-rules/{ruleKey}" &&
            contract.HttpMethod == "GET" &&
            contract.OperationId == "getBusinessMasterDataCodeRule");
        Assert.Contains(contracts, contract =>
            contract.EndpointType == typeof(CreateCodeRuleVersionEndpoint) &&
            contract.Route == "/api/business/v1/master-data/code-rules/{ruleKey}/versions" &&
            contract.HttpMethod == "POST" &&
            contract.OperationId == "createBusinessMasterDataCodeRuleVersion");
        Assert.Contains(contracts, contract =>
            contract.EndpointType == typeof(PreviewCodeRuleEndpoint) &&
            contract.Route == "/api/business/v1/master-data/code-rules/{ruleKey}/preview" &&
            contract.HttpMethod == "POST" &&
            contract.OperationId == "previewBusinessMasterDataCodeRule");
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options, mediator);
    }

    private static string SegmentsJson(params CodeRuleSegment[] segments) =>
        JsonSerializer.Serialize(segments, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static CreateCodeRuleVersionRequest Request(string reason) =>
        new(
            "org-001",
            "env-dev",
            "master-data.sku",
            "SKU 编码规则",
            "sku",
            ScopeDimension.Organization,
            [CodeRuleSegment.ConstantOf("SKU-"), CodeRuleSegment.SequenceOf(4)],
            true,
            new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero),
            "admin-001",
            reason);

    private static CreateCodeRuleVersionCommand Command(string reason) =>
        new(
            "org-001",
            "env-dev",
            "master-data.sku",
            "SKU 编码规则 v2",
            "sku",
            ScopeDimension.Organization,
            [CodeRuleSegment.ConstantOf("SKU-"), CodeRuleSegment.SequenceOf(4)],
            true,
            new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero),
            "admin-001",
            reason);

    private static CodeRuleVersion Version(string reason) =>
        CodeRuleVersion.Record(
            "org-001",
            "env-dev",
            "master-data.sku",
            "SKU 编码规则",
            "sku",
            (int)ScopeDimension.Organization,
            SegmentsJson(CodeRuleSegment.ConstantOf("SKU-"), CodeRuleSegment.SequenceOf(4)),
            true,
            1,
            CodeRuleVersionStatus.Active,
            new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero),
            "admin-001",
            reason,
            new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero));
}
