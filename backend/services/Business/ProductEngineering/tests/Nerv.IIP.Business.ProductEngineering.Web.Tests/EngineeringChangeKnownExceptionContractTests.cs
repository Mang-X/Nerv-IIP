using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringDocumentAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Queries;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Scheduling;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class EngineeringChangeKnownExceptionContractTests
{
    [Fact]
    public async Task Public_release_hides_change_state_message_behind_stable_chinese_text()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            new ApprovedVerifier(),
            businessDateProvider: new FixedBusinessDateProvider(new DateOnly(2026, 6, 1)));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-RELEASE-DRAFT",
                "Reject empty affected version set",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                []),
            CancellationToken.None));

        Assert.Equal("工程变更发布失败，请检查变更状态和受影响版本。", exception.Message);
        Assert.DoesNotContain("requires at least one affected version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Public_release_hides_domain_archive_message_behind_stable_chinese_text()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var draftBom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-DRAFT", "A", "ENG-3000")
            .AddLine("ENG-3001", 1m, "EA");
        dbContext.EngineeringBoms.Add(draftBom);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            new ApprovedVerifier(),
            businessDateProvider: new FixedBusinessDateProvider(new DateOnly(2026, 6, 1)));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-DRAFT-ARCHIVE",
                "Reject draft archive as business error",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand("engineering-bom", "EBOM-DRAFT:A")]),
            CancellationToken.None));

        Assert.Equal("第 1 条受影响工程 BOM 归档失败，请检查版本状态和替代版本。", exception.Message);
        Assert.DoesNotContain("Only released", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Public_release_engineering_document_archive_wrapper_preserves_diagnostic_inner_reason()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var archivedDocument = EngineeringDocument.Register(
            "org-001", "env-dev", "DOC-ARCHIVED", "A", "file-001", "manual.pdf", "application/pdf", "manual");
        archivedDocument.Archive("seed archive");
        dbContext.EngineeringDocuments.Add(archivedDocument);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            new ApprovedVerifier(),
            businessDateProvider: new FixedBusinessDateProvider(new DateOnly(2026, 6, 1)),
            engineeringDocumentRepository: new EngineeringDocumentRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001", "env-dev", "ECO-DOC-ARCHIVE", "Reject archived document", Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand("engineering-document", "DOC-ARCHIVED:A")]),
            CancellationToken.None));

        Assert.Equal("第 1 条受影响工程文档归档失败，请检查版本状态和替代版本。", exception.Message);
        Assert.Equal(
            "Only published engineering document versions can be archived.",
            Assert.IsType<InvalidOperationException>(exception.InnerException).Message);
    }

    [Fact]
    public async Task Public_release_active_production_successor_message_is_actionable_and_within_display_limit()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var oldVersion = ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-OLD",
            "MBOM-OLD:A",
            "ROUTE-OLD:A",
            new DateOnly(2026, 1, 1),
            null,
            null,
            null,
            10,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
        var successor = ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-NEW",
            "MBOM-NEW:A",
            "ROUTE-NEW:A",
            new DateOnly(2026, 1, 1),
            null,
            null,
            null,
            20,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
        dbContext.ProductionVersions.AddRange(oldVersion, successor);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            new ApprovedVerifier(),
            businessDateProvider: new FixedBusinessDateProvider(new DateOnly(2026, 6, 1)));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-PV-SKU-MISMATCH",
                "Reject successor SKU mismatch",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand("production-version", oldVersion.Id.Id.ToString("D"), successor.Id.Id.ToString("D"))]),
            CancellationToken.None));

        Assert.Equal("第 1 条受影响生产版本的替代版本 SKU 或状态不符合要求，请检查替代版本。", exception.Message);
        Assert.Contains("SKU", exception.Message, StringComparison.Ordinal);
        Assert.Contains("状态", exception.Message, StringComparison.Ordinal);
        Assert.True(exception.Message.Length <= 60, $"消息 {exception.Message.Length} 字，超过前端 60 字透传上限");
        Assert.DoesNotContain(successor.Id.Id.ToString("D"), exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("unsupported", "engineering/bom", "<EC-001>\n", "第 1 条受影响版本类型不受支持，请检查提交内容。")]
    [InlineData("self", "engineering-bom", "<EBOM-SELF>/\u0001", "第 1 条受影响版本不能将自身设为替代版本，请修改替代版本。")]
    [InlineData("duplicate-different-successor", "engineering-bom", "<EBOM-DUP>/\u0002", "第 2 条受影响版本已指定其他替代版本，请删除重复项。")]
    [InlineData("duplicate-same-successor", "engineering-bom", "<EBOM-DUP>/\u0003", "第 2 条受影响版本重复声明，请保留一项。")]
    [InlineData("cycle", "engineering-bom", "<EBOM-CYCLE>/\u0004", "第 1 条与第 2 条受影响版本的替代关系形成循环，请修改替代版本。")]
    public async Task Public_release_batch_validation_names_the_affected_version_and_next_action(
        string caseName,
        string versionKind,
        string versionId,
        string expectedMessage)
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var handler = CreateHandler(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        IReadOnlyCollection<AffectedVersionCommand> affectedVersions = caseName switch
        {
            "unsupported" => [new AffectedVersionCommand(versionKind, versionId)],
            "self" => [new AffectedVersionCommand(versionKind, versionId, versionId)],
            "duplicate-different-successor" => [
                new AffectedVersionCommand(versionKind, versionId, "EBOM-A:A"),
                new AffectedVersionCommand(versionKind, versionId, "EBOM-B:A")],
            "duplicate-same-successor" => [
                new AffectedVersionCommand(versionKind, versionId, "EBOM-A:A"),
                new AffectedVersionCommand(versionKind, versionId, "EBOM-A:A")],
            "cycle" => [
                new AffectedVersionCommand(versionKind, versionId, "EBOM-CYCLE:B"),
                new AffectedVersionCommand(versionKind, "EBOM-CYCLE:B", versionId)],
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                $"ECO-BATCH-{caseName}",
                "Reject invalid affected version batch",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                affectedVersions),
            CancellationToken.None));

        Assert.Equal(expectedMessage, exception.Message);
        Assert.Contains("第 ", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(versionKind, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(versionId, exception.Message, StringComparison.Ordinal);
        Assert.Contains("请", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("cannot", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(exception.Message.Length <= 60, $"消息 {exception.Message.Length} 字，超过安全展示上限");
        Assert.DoesNotMatch("[\\x00-\\x1F\\x7F]", exception.Message);
        Assert.DoesNotContain("<", exception.Message);
        Assert.DoesNotContain(">", exception.Message);
        Assert.DoesNotContain("/", exception.Message);
        Assert.DoesNotContain("\\", exception.Message);
    }

    [Fact]
    public async Task Public_release_batch_validation_keeps_maximum_version_id_out_of_public_message()
    {
        const string versionKind = "engineering-bom";
        var versionId = new string('V', 150);
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var handler = CreateHandler(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-BATCH-LONG-ID",
                "Reject self supersede with maximum identifier",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand(versionKind, versionId, versionId)]),
            CancellationToken.None));

        Assert.Equal("第 1 条受影响版本不能将自身设为替代版本，请修改替代版本。", exception.Message);
        Assert.DoesNotContain(versionId, exception.Message, StringComparison.Ordinal);
        Assert.True(exception.Message.Length <= 60);
    }

    [Fact]
    public async Task Public_release_missing_production_successor_uses_safe_sequence_message()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var source = ProductionVersion.Create(
            "org-001", "env-dev", "SKU-OLD", "MBOM-OLD:A", "ROUTE-OLD:A",
            new DateOnly(2026, 1, 1), null, null, null, 10, true,
            EngineeringVersionStatus.Published, EngineeringVersionStatus.Published);
        dbContext.ProductionVersions.Add(source);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        const string missingSuccessor = "<script>/\u0001";
        var exception = await Assert.ThrowsAsync<KnownException>(() => CreateHandler(dbContext).Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001", "env-dev", "ECO-MISSING-SUCCESSOR", "Reject missing successor",
                Guid.NewGuid().ToString("D"), new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand("production-version", source.Id.Id.ToString("D"), missingSuccessor)]),
            CancellationToken.None));

        Assert.Equal("第 1 条受影响版本的替代生产版本不存在，请检查替代版本标识。", exception.Message);
        Assert.DoesNotContain(missingSuccessor, exception.Message, StringComparison.Ordinal);
        Assert.True(exception.Message.Length <= 60);
        Assert.DoesNotMatch("[\\x00-\\x1F\\x7F]", exception.Message);
        Assert.DoesNotContain("<", exception.Message);
        Assert.DoesNotContain(">", exception.Message);
        Assert.DoesNotContain("/", exception.Message);
        Assert.DoesNotContain("\\", exception.Message);
    }

    [Theory]
    [InlineData("engineering-bom", "第 1 条受影响版本的替代工程 BOM 不存在，请检查替代版本标识。", "<ebom>/\u0001")]
    [InlineData("manufacturing-bom", "第 1 条受影响版本的替代制造 BOM 不存在，请检查替代版本标识。", "<mbom>/\u0002")]
    [InlineData("routing", "第 1 条受影响版本的替代工艺路线不存在，请检查替代版本标识。", "<routing>/\u0003")]
    [InlineData("production-version", "第 1 条受影响版本的替代生产版本不存在，请检查替代版本标识。", "<production>/\u0004")]
    [InlineData("engineering-document", "第 1 条受影响版本的替代工程文档不存在，请检查替代版本标识。", "<document>/\u0005")]
    public async Task Public_release_missing_successor_uses_safe_sequence_message_for_each_supported_kind(
        string versionKind,
        string expectedMessage,
        string missingSuccessor)
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        IEngineeringDocumentRepository? engineeringDocumentRepository = null;
        string sourceVersionId;

        switch (versionKind)
        {
            case "engineering-bom":
                var engineeringBom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-SOURCE", "A", "SKU-SOURCE")
                    .AddLine("SKU-COMPONENT", 1m, "EA");
                engineeringBom.Release(new DateOnly(2026, 1, 1));
                dbContext.EngineeringBoms.Add(engineeringBom);
                sourceVersionId = "EBOM-SOURCE:A";
                break;
            case "manufacturing-bom":
                var manufacturingBom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-SOURCE", "A", "SKU-SOURCE")
                    .AddMaterialLine("SKU-COMPONENT", 1m, "EA", 0m);
                manufacturingBom.ReleaseFromEngineeringBom("EBOM-SOURCE:A", EngineeringVersionStatus.Published, new DateOnly(2026, 1, 1));
                dbContext.ManufacturingBoms.Add(manufacturingBom);
                sourceVersionId = "MBOM-SOURCE:A";
                break;
            case "routing":
                var routing = Routing.CreateDraft("org-001", "env-dev", "ROUTE-SOURCE", "A", "SKU-SOURCE")
                    .AddOperation(10, "WC-SOURCE", "operation", "Operation", 30);
                routing.Release(new DateOnly(2026, 1, 1));
                dbContext.Routings.Add(routing);
                sourceVersionId = "ROUTE-SOURCE:A";
                break;
            case "production-version":
                var productionVersion = ProductionVersion.Create(
                    "org-001", "env-dev", "SKU-SOURCE", "MBOM-SOURCE:A", "ROUTE-SOURCE:A",
                    new DateOnly(2026, 1, 1), null, null, null, 10, true,
                    EngineeringVersionStatus.Published, EngineeringVersionStatus.Published);
                dbContext.ProductionVersions.Add(productionVersion);
                sourceVersionId = productionVersion.Id.Id.ToString("D");
                break;
            case "engineering-document":
                var engineeringDocument = EngineeringDocument.Register(
                    "org-001", "env-dev", "DOC-SOURCE", "A", "file-source", "source.pdf", "application/pdf", "manual");
                dbContext.EngineeringDocuments.Add(engineeringDocument);
                engineeringDocumentRepository = new EngineeringDocumentRepository(dbContext);
                sourceVersionId = "DOC-SOURCE:A";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(versionKind), versionKind, null);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
        var exception = await Assert.ThrowsAsync<KnownException>(() => CreateHandler(dbContext, engineeringDocumentRepository).Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001", "env-dev", $"ECO-MISSING-{versionKind}", "Reject missing successor",
                Guid.NewGuid().ToString("D"), new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand(versionKind, sourceVersionId, missingSuccessor)]),
            CancellationToken.None));

        Assert.Equal(expectedMessage, exception.Message);
        Assert.DoesNotContain(missingSuccessor, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(versionKind, exception.Message, StringComparison.Ordinal);
        Assert.True(exception.Message.Length <= 60);
        Assert.DoesNotMatch("[\\x00-\\x1F\\x7F]", exception.Message);
        Assert.DoesNotContain("<", exception.Message);
        Assert.DoesNotContain(">", exception.Message);
        Assert.DoesNotContain("/", exception.Message);
        Assert.DoesNotContain("\\", exception.Message);
    }

    [Fact]
    public async Task Public_release_missing_production_successor_hides_a_valid_guid()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var source = ProductionVersion.Create(
            "org-001", "env-dev", "SKU-GUID", "MBOM-GUID:A", "ROUTE-GUID:A",
            new DateOnly(2026, 1, 1), null, null, null, 10, true,
            EngineeringVersionStatus.Published, EngineeringVersionStatus.Published);
        dbContext.ProductionVersions.Add(source);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        const string missingSuccessor = "3f4b2d1e-6c8a-4a5b-9d10-1234567890ab";
        var exception = await Assert.ThrowsAsync<KnownException>(() => CreateHandler(dbContext).Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001", "env-dev", "ECO-MISSING-PRODUCTION-GUID", "Reject missing successor",
                Guid.NewGuid().ToString("D"), new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand("production-version", source.Id.Id.ToString("D"), missingSuccessor)]),
            CancellationToken.None));

        Assert.Equal("第 1 条受影响版本的替代生产版本不存在，请检查替代版本标识。", exception.Message);
        Assert.DoesNotContain(missingSuccessor, exception.Message, StringComparison.Ordinal);
        Assert.True(exception.Message.Length <= 60);
    }

    [Fact]
    public async Task Public_release_missing_engineering_document_successor_hides_14_and_150_character_ids()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.EngineeringDocuments.Add(EngineeringDocument.Register(
            "org-001", "env-dev", "DOC-SOURCE-LENGTH", "A", "file-source", "source.pdf", "application/pdf", "manual"));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = CreateHandler(dbContext, new EngineeringDocumentRepository(dbContext));
        var missingSuccessors = new[]
        {
            new string('D', 12) + ":A",
            new string('M', 148) + ":A",
        };

        Assert.Equal(14, missingSuccessors[0].Length);
        Assert.Equal(150, missingSuccessors[1].Length);
        foreach (var (missingSuccessor, index) in missingSuccessors.Select((value, index) => (value, index)))
        {
            var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
                new ReleaseEngineeringChangeCommand(
                    "org-001", "env-dev", $"ECO-MISSING-DOCUMENT-{index}", "Reject missing successor",
                    Guid.NewGuid().ToString("D"), new DateOnly(2026, 6, 1),
                    [new AffectedVersionCommand("engineering-document", "DOC-SOURCE-LENGTH:A", missingSuccessor)]),
                CancellationToken.None));

            Assert.Equal("第 1 条受影响版本的替代工程文档不存在，请检查替代版本标识。", exception.Message);
            Assert.DoesNotContain(missingSuccessor, exception.Message, StringComparison.Ordinal);
            Assert.True(exception.Message.Length <= 60);
            Assert.DoesNotMatch("[\\x00-\\x1F\\x7F]", exception.Message);
            Assert.DoesNotContain("<", exception.Message);
            Assert.DoesNotContain(">", exception.Message);
            Assert.DoesNotContain("/", exception.Message);
            Assert.DoesNotContain("\\", exception.Message);
        }
    }

    [Fact]
    public async Task Public_release_unknown_version_kind_does_not_use_successor_not_found_message()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        const string unknownKind = "<unknown>/\u0006";
        const string sourceVersionId = "<source>/\u0007";
        const string missingSuccessor = "<successor>/\u0008";

        var exception = await Assert.ThrowsAsync<KnownException>(() => CreateHandler(dbContext).Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001", "env-dev", "ECO-MISSING-UNKNOWN-KIND", "Reject unknown kind",
                Guid.NewGuid().ToString("D"), new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand(unknownKind, sourceVersionId, missingSuccessor)]),
            CancellationToken.None));

        Assert.Equal("第 1 条受影响版本类型不受支持，请检查提交内容。", exception.Message);
        Assert.DoesNotContain("替代", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(unknownKind, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(sourceVersionId, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(missingSuccessor, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("engineering-bom", "第 2 条受影响工程 BOM 归档失败，请检查版本状态和替代版本。", "Only released engineering BOM versions can be archived by an engineering change.")]
    [InlineData("manufacturing-bom", "第 2 条受影响制造 BOM 归档失败，请检查版本状态和替代版本。", "Only released manufacturing BOM versions can be archived by an engineering change.")]
    [InlineData("routing", "第 2 条受影响工艺路线归档失败，请检查版本状态和替代版本。", "Only released routing versions can be archived by an engineering change.")]
    [InlineData("production-version-archive", "第 2 条受影响生产版本归档失败，请检查版本状态和生效日期。", "Archived production version cannot be changed or referenced by new work orders.")]
    [InlineData("production-version-supersede", "第 2 条受影响生产版本替代失败，请检查版本状态、生效日期和替代版本窗口。", "Archived production version cannot be changed or referenced by new work orders.")]
    [InlineData("engineering-document", "第 2 条受影响工程文档归档失败，请检查版本状态和替代版本。", "Only published engineering document versions can be archived.")]
    public async Task Public_release_batch_archive_failures_name_the_failing_entry_and_preserve_inner_diagnostic(
        string versionKind,
        string expectedMessage,
        string expectedInnerMessage)
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        IEngineeringDocumentRepository? engineeringDocumentRepository = null;
        string firstVersionId;
        string secondVersionId;
        string? successorVersionId = null;

        switch (versionKind)
        {
            case "engineering-bom":
                var firstEngineeringBom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-BATCH-FIRST", "A", "SKU-BATCH")
                    .AddLine("SKU-COMPONENT", 1m, "EA");
                firstEngineeringBom.Release(new DateOnly(2026, 1, 1));
                var secondEngineeringBom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-BATCH-SECOND", "A", "SKU-BATCH")
                    .AddLine("SKU-COMPONENT", 1m, "EA");
                dbContext.EngineeringBoms.AddRange(firstEngineeringBom, secondEngineeringBom);
                firstVersionId = "EBOM-BATCH-FIRST:A";
                secondVersionId = "EBOM-BATCH-SECOND:A";
                break;
            case "manufacturing-bom":
                var firstManufacturingBom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-BATCH-FIRST", "A", "SKU-BATCH")
                    .AddMaterialLine("SKU-COMPONENT", 1m, "EA", 0m);
                firstManufacturingBom.ReleaseFromEngineeringBom("EBOM-BATCH-FIRST:A", EngineeringVersionStatus.Published, new DateOnly(2026, 1, 1));
                var secondManufacturingBom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-BATCH-SECOND", "A", "SKU-BATCH")
                    .AddMaterialLine("SKU-COMPONENT", 1m, "EA", 0m);
                dbContext.ManufacturingBoms.AddRange(firstManufacturingBom, secondManufacturingBom);
                firstVersionId = "MBOM-BATCH-FIRST:A";
                secondVersionId = "MBOM-BATCH-SECOND:A";
                break;
            case "routing":
                var firstRouting = Routing.CreateDraft("org-001", "env-dev", "ROUTE-BATCH-FIRST", "A", "SKU-BATCH")
                    .AddOperation(10, "WC-BATCH", "operation", "Operation", 30);
                firstRouting.Release(new DateOnly(2026, 1, 1));
                var secondRouting = Routing.CreateDraft("org-001", "env-dev", "ROUTE-BATCH-SECOND", "A", "SKU-BATCH")
                    .AddOperation(10, "WC-BATCH", "operation", "Operation", 30);
                dbContext.Routings.AddRange(firstRouting, secondRouting);
                firstVersionId = "ROUTE-BATCH-FIRST:A";
                secondVersionId = "ROUTE-BATCH-SECOND:A";
                break;
            case "production-version-archive":
                var firstProductionVersion = ProductionVersion.Create(
                    "org-001", "env-dev", "SKU-BATCH-FIRST", "MBOM-BATCH-FIRST:A", "ROUTE-BATCH-FIRST:A",
                    new DateOnly(2026, 1, 1), null, null, null, 10, true,
                    EngineeringVersionStatus.Published, EngineeringVersionStatus.Published);
                var secondProductionVersion = ProductionVersion.Create(
                    "org-001", "env-dev", "SKU-BATCH-SECOND", "MBOM-BATCH-SECOND:A", "ROUTE-BATCH-SECOND:A",
                    new DateOnly(2026, 1, 1), null, null, null, 10, true,
                    EngineeringVersionStatus.Published, EngineeringVersionStatus.Published);
                secondProductionVersion.Archive("seed archive");
                dbContext.ProductionVersions.AddRange(firstProductionVersion, secondProductionVersion);
                firstVersionId = firstProductionVersion.Id.Id.ToString("D");
                secondVersionId = secondProductionVersion.Id.Id.ToString("D");
                break;
            case "production-version-supersede":
                var firstSupersedeVersion = ProductionVersion.Create(
                    "org-001", "env-dev", "SKU-BATCH-FIRST", "MBOM-BATCH-FIRST:A", "ROUTE-BATCH-FIRST:A",
                    new DateOnly(2026, 1, 1), null, null, null, 10, true,
                    EngineeringVersionStatus.Published, EngineeringVersionStatus.Published);
                var secondSupersedeVersion = ProductionVersion.Create(
                    "org-001", "env-dev", "SKU-BATCH-SECOND", "MBOM-BATCH-SECOND:A", "ROUTE-BATCH-SECOND:A",
                    new DateOnly(2026, 1, 1), null, null, null, 10, true,
                    EngineeringVersionStatus.Published, EngineeringVersionStatus.Published);
                var successorProductionVersion = ProductionVersion.Create(
                    "org-001", "env-dev", "SKU-BATCH-SECOND", "MBOM-BATCH-SECOND:B", "ROUTE-BATCH-SECOND:B",
                    new DateOnly(2026, 1, 1), null, null, null, 20, true,
                    EngineeringVersionStatus.Published, EngineeringVersionStatus.Published);
                secondSupersedeVersion.Archive("seed archive");
                dbContext.ProductionVersions.AddRange(firstSupersedeVersion, secondSupersedeVersion, successorProductionVersion);
                firstVersionId = firstSupersedeVersion.Id.Id.ToString("D");
                secondVersionId = secondSupersedeVersion.Id.Id.ToString("D");
                successorVersionId = successorProductionVersion.Id.Id.ToString("D");
                break;
            case "engineering-document":
                var firstEngineeringDocument = EngineeringDocument.Register(
                    "org-001", "env-dev", "DOC-BATCH-FIRST", "A", "file-first", "first.pdf", "application/pdf", "manual");
                var secondEngineeringDocument = EngineeringDocument.Register(
                    "org-001", "env-dev", "DOC-BATCH-SECOND", "A", "file-second", "second.pdf", "application/pdf", "manual");
                secondEngineeringDocument.Archive("seed archive");
                dbContext.EngineeringDocuments.AddRange(firstEngineeringDocument, secondEngineeringDocument);
                engineeringDocumentRepository = new EngineeringDocumentRepository(dbContext);
                firstVersionId = "DOC-BATCH-FIRST:A";
                secondVersionId = "DOC-BATCH-SECOND:A";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(versionKind), versionKind, null);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = CreateHandler(dbContext, engineeringDocumentRepository);
        var affectedVersions = new List<AffectedVersionCommand>
        {
            new(versionKind.Replace("-archive", string.Empty, StringComparison.Ordinal).Replace("-supersede", string.Empty, StringComparison.Ordinal), firstVersionId),
            new(versionKind.Replace("-archive", string.Empty, StringComparison.Ordinal).Replace("-supersede", string.Empty, StringComparison.Ordinal), secondVersionId, successorVersionId),
        };
        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001", "env-dev", $"ECO-BATCH-ARCHIVE-{versionKind}", "Reject second archive",
                Guid.NewGuid().ToString("D"), new DateOnly(2026, 6, 1), affectedVersions),
            CancellationToken.None));

        Assert.Equal(expectedMessage, exception.Message);
        Assert.Equal(expectedInnerMessage, Assert.IsType<InvalidOperationException>(exception.InnerException).Message);
        Assert.Contains("第 2 条", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(firstVersionId, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secondVersionId, exception.Message, StringComparison.Ordinal);
        Assert.True(exception.Message.Length <= 60);
        Assert.DoesNotMatch("[\\x00-\\x1F\\x7F]", exception.Message);
    }

    [Fact]
    public async Task Public_cancel_and_reschedule_hide_domain_state_messages_behind_stable_chinese_text()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.EngineeringChanges.AddRange(
            EngineeringChange.Open("org-001", "env-dev", "ECO-CANCEL-DRAFT", "Cancel draft"),
            EngineeringChange.Open("org-001", "env-dev", "ECO-RESCHEDULE-DRAFT", "Reschedule draft"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var cancelException = await Assert.ThrowsAsync<KnownException>(() => new CancelScheduledEngineeringChangeCommandHandler(dbContext).Handle(
            new CancelScheduledEngineeringChangeCommand("org-001", "env-dev", "ECO-CANCEL-DRAFT", "operator cancelled"),
            CancellationToken.None));
        var rescheduleException = await Assert.ThrowsAsync<KnownException>(() => new RescheduleEngineeringChangeCommandHandler(dbContext).Handle(
            new RescheduleEngineeringChangeCommand("org-001", "env-dev", "ECO-RESCHEDULE-DRAFT", new DateOnly(2026, 6, 10), "supplier delay"),
            CancellationToken.None));

        Assert.Equal("取消工程变更失败，请确认变更处于已排期状态。", cancelException.Message);
        Assert.Equal("改期工程变更失败，请确认变更处于已排期状态。", rescheduleException.Message);
        Assert.Equal("Only scheduled engineering changes can be changed by this operation.", cancelException.InnerException?.Message);
        Assert.Equal("Only scheduled engineering changes can be changed by this operation.", rescheduleException.InnerException?.Message);
    }

    [Fact]
    public async Task Public_cancel_and_reschedule_missing_change_use_safe_fixed_messages()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        const string missingChangeNumber = "<missing>/" + "XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX";

        var cancelException = await Assert.ThrowsAsync<KnownException>(() => new CancelScheduledEngineeringChangeCommandHandler(dbContext).Handle(
            new CancelScheduledEngineeringChangeCommand("org-001", "env-dev", missingChangeNumber, "operator cancelled"),
            CancellationToken.None));
        var rescheduleException = await Assert.ThrowsAsync<KnownException>(() => new RescheduleEngineeringChangeCommandHandler(dbContext).Handle(
            new RescheduleEngineeringChangeCommand("org-001", "env-dev", missingChangeNumber, new DateOnly(2026, 6, 10), "supplier delay"),
            CancellationToken.None));

        Assert.Equal("工程变更不存在，请检查变更编号。", cancelException.Message);
        Assert.Equal(cancelException.Message, rescheduleException.Message);
        Assert.DoesNotContain(missingChangeNumber, cancelException.Message, StringComparison.Ordinal);
        Assert.True(cancelException.Message.Length <= 60);
        Assert.DoesNotMatch("[\\x00-\\x1F\\x7F]", cancelException.Message);
        Assert.DoesNotContain("<", cancelException.Message);
        Assert.DoesNotContain(">", cancelException.Message);
        Assert.DoesNotContain("/", cancelException.Message);
        Assert.DoesNotContain("\\", cancelException.Message);
    }

    [Fact]
    public async Task Public_engineering_change_queries_use_chinese_not_found_messages()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        const string missingChangeNumber = "<missing-change>/\u0001" + "XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX";
        var getException = await Assert.ThrowsAsync<KnownException>(() => new GetEngineeringChangeQueryHandler(dbContext).Handle(
            new GetEngineeringChangeQuery("org-001", "env-dev", missingChangeNumber),
            CancellationToken.None));
        var previewException = await Assert.ThrowsAsync<KnownException>(() => new GetEngineeringChangeImpactPreviewQueryHandler(dbContext).Handle(
            new GetEngineeringChangeImpactPreviewQuery("org-001", "env-dev", new DateOnly(2026, 6, 1), []),
            CancellationToken.None));

        Assert.Equal("工程变更不存在，请检查变更编号。", getException.Message);
        Assert.DoesNotContain(missingChangeNumber, getException.Message, StringComparison.Ordinal);
        Assert.True(getException.Message.Length <= 60);
        Assert.DoesNotMatch("[\\x00-\\x1F\\x7F]", getException.Message);
        Assert.DoesNotContain("<", getException.Message);
        Assert.DoesNotContain(">", getException.Message);
        Assert.DoesNotContain("/", getException.Message);
        Assert.DoesNotContain("\\", getException.Message);
        Assert.Equal("影响预览至少需要一个受影响版本。", previewException.Message);
    }

    [Theory]
    [InlineData("EngineeringBom", "engineering-bom")]
    [InlineData("engineering-bom", "engineering-bom")]
    [InlineData("engineering_bom", "engineering-bom")]
    [InlineData("ManufacturingBom", "manufacturing-bom")]
    [InlineData("manufacturing-bom", "manufacturing-bom")]
    [InlineData("manufacturing_bom", "manufacturing-bom")]
    [InlineData("Routing", "routing")]
    [InlineData("routing", "routing")]
    [InlineData("ProductionVersion", "production-version")]
    [InlineData("production-version", "production-version")]
    [InlineData("production_version", "production-version")]
    public async Task Public_release_normalizes_pascal_kebab_and_snake_version_kinds(
        string submittedVersionKind,
        string canonicalVersionKind)
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        IEngineeringDocumentRepository? engineeringDocumentRepository = null;
        var sourceVersionId = SeedSupportedAffectedVersion(dbContext, canonicalVersionKind, ref engineeringDocumentRepository);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await CreateHandler(dbContext, engineeringDocumentRepository).Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001", "env-dev", $"ECO-NORMALIZE-{canonicalVersionKind}-{submittedVersionKind}", "Normalize version kind",
                Guid.NewGuid().ToString("D"), new DateOnly(2026, 6, 10),
                [new AffectedVersionCommand(submittedVersionKind, sourceVersionId)]),
            CancellationToken.None);

        Assert.Equal($"ECO-NORMALIZE-{canonicalVersionKind}-{submittedVersionKind}", result.Id);
    }

    private static string SeedSupportedAffectedVersion(
        ApplicationDbContext dbContext,
        string canonicalVersionKind,
        ref IEngineeringDocumentRepository? engineeringDocumentRepository)
    {
        return canonicalVersionKind switch
        {
            "engineering-bom" => SeedEngineeringBom(dbContext),
            "manufacturing-bom" => SeedManufacturingBom(dbContext),
            "routing" => SeedRouting(dbContext),
            "production-version" => SeedProductionVersion(dbContext),
            "engineering-document" => SeedEngineeringDocument(dbContext, ref engineeringDocumentRepository),
            _ => throw new ArgumentOutOfRangeException(nameof(canonicalVersionKind), canonicalVersionKind, null)
        };
    }

    private static string SeedEngineeringBom(ApplicationDbContext dbContext)
    {
        var bom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-NORMALIZE", "A", "SKU-NORMALIZE")
            .AddLine("SKU-COMPONENT", 1m, "EA");
        bom.Release(new DateOnly(2026, 1, 1));
        dbContext.EngineeringBoms.Add(bom);
        return "EBOM-NORMALIZE:A";
    }

    private static string SeedManufacturingBom(ApplicationDbContext dbContext)
    {
        var bom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-NORMALIZE", "A", "SKU-NORMALIZE")
            .AddMaterialLine("SKU-COMPONENT", 1m, "EA", 0m);
        bom.ReleaseFromEngineeringBom("EBOM-NORMALIZE:A", EngineeringVersionStatus.Published, new DateOnly(2026, 1, 1));
        dbContext.ManufacturingBoms.Add(bom);
        return "MBOM-NORMALIZE:A";
    }

    private static string SeedRouting(ApplicationDbContext dbContext)
    {
        var routing = Routing.CreateDraft("org-001", "env-dev", "ROUTE-NORMALIZE", "A", "SKU-NORMALIZE")
            .AddOperation(10, "WC-NORMALIZE", "operation", "Operation", 30);
        routing.Release(new DateOnly(2026, 1, 1));
        dbContext.Routings.Add(routing);
        return "ROUTE-NORMALIZE:A";
    }

    private static string SeedProductionVersion(ApplicationDbContext dbContext)
    {
        var productionVersion = ProductionVersion.Create(
            "org-001", "env-dev", "SKU-NORMALIZE", "MBOM-NORMALIZE:A", "ROUTE-NORMALIZE:A",
            new DateOnly(2026, 1, 1), null, null, null, 10, true,
            EngineeringVersionStatus.Published, EngineeringVersionStatus.Published);
        dbContext.ProductionVersions.Add(productionVersion);
        return productionVersion.Id.Id.ToString("D");
    }

    private static string SeedEngineeringDocument(
        ApplicationDbContext dbContext,
        ref IEngineeringDocumentRepository? engineeringDocumentRepository)
    {
        var document = EngineeringDocument.Register(
            "org-001", "env-dev", "DOC-NORMALIZE", "A", "file-normalize", "normalize.pdf", "application/pdf", "manual");
        dbContext.EngineeringDocuments.Add(document);
        engineeringDocumentRepository = new EngineeringDocumentRepository(dbContext);
        return "DOC-NORMALIZE:A";
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var databaseName = $"product-engineering-known-exception-contract-{Guid.NewGuid():N}";
        return new ServiceCollection()
            .AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly))
            .AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .BuildServiceProvider();
    }

    private static ReleaseEngineeringChangeCommandHandler CreateHandler(
        ApplicationDbContext dbContext,
        IEngineeringDocumentRepository? engineeringDocumentRepository = null)
    {
        return new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            new ApprovedVerifier(),
            businessDateProvider: new FixedBusinessDateProvider(new DateOnly(2026, 6, 1)),
            engineeringDocumentRepository: engineeringDocumentRepository);
    }

    private sealed class ApprovedVerifier : IEngineeringApprovalVerifier
    {
        public Task EnsureApprovedAsync(
            string organizationId,
            string environmentId,
            string approvalReferenceId,
            string changeNumber,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

}
