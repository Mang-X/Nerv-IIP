namespace Nerv.IIP.VocabularyGovernance.Tests;

/// <summary>
/// 扫描器自身的 probe 用例（照抄 MasterData <c>KnownExceptionMessageArchitectureTests</c> 的分层）：
/// 用内存源码串逐条钉住「什么算违规 / 什么不算」，覆盖票面 (a) 同值不同义、(b) 逐字副本两类假阳与真阳。
/// 扫描器没有自测就等于鉴别力未知。
/// </summary>
public sealed class VocabularyScannerProbeTests
{
    private const string ContractsSource = """
        namespace Nerv.IIP.Contracts.Approval;

        public static class ApprovalSourceServices
        {
            public const string Quality = "quality";
            public const string BusinessErp = "business-erp";
        }
        """;

    private static IReadOnlyList<VocabularyConstant> Constants(params string[] extraSources)
    {
        var documents = new List<SourceDocument> { new("Contracts.cs", ContractsSource) };
        documents.AddRange(extraSources.Select((source, index) => new SourceDocument($"Extra{index}.cs", source)));
        var extraction = ContractsVocabularyExtractor.Extract(documents);
        Assert.Empty(extraction.Errors);
        return extraction.Constants;
    }

    private static VocabularyScanResult Scan(
        string consumerSource,
        IReadOnlyCollection<VocabularyExemption>? exemptions = null,
        IReadOnlyCollection<string>? replicaFileNames = null,
        string consumerPath = "services/Probe/Application/Probe.cs",
        IReadOnlyList<VocabularyConstant>? constants = null) =>
        VocabularyLiteralScanner.Scan(
            constants ?? Constants(),
            [new SourceDocument(consumerPath, consumerSource)],
            exemptions ?? [],
            replicaFileNames ?? []);

    // ── 词表抽取：从类型系统穷举 ────────────────────────────────────────────────

    [Fact]
    public void Extractor_enumerates_public_const_strings_of_public_static_contracts_classes()
    {
        var constants = Constants();

        Assert.Contains(constants, constant =>
            constant.Reference == "Nerv.IIP.Contracts.Approval.ApprovalSourceServices.Quality"
            && constant.Value == "quality");
        Assert.Contains(constants, constant => constant.Value == "business-erp");
    }

    [Fact]
    public void Extractor_evaluates_constant_concatenation_via_the_semantic_model()
    {
        const string source = """
            namespace Nerv.IIP.Contracts.Erp;

            public static class ErpIdempotencyKeyPrefixes
            {
                private const string Prefix = "erp";
                public const string DeliveryOrder = Prefix + "-delivery-order";
            }
            """;

        var constants = Constants(source);

        Assert.Contains(constants, constant => constant.Value == "erp-delivery-order");
    }

    [Fact]
    public void Extractor_ignores_constants_outside_the_contracts_namespace_or_non_public_surfaces()
    {
        const string source = """
            namespace Nerv.IIP.Business.Mes.Web.Application
            {
                public static class NotAContract { public const string Value = "not-a-contract"; }
            }

            namespace Nerv.IIP.Contracts.Mes
            {
                public static class MesVocabulary
                {
                    internal const string InternalValue = "internal-value";
                    public const int NotAString = 42;
                }

                public sealed class NotStatic { public const string Value = "not-static"; }

                internal static class NotPublic { public const string Value = "not-public"; }
            }
            """;

        var constants = Constants(source);

        Assert.DoesNotContain(constants, constant => constant.Value is "not-a-contract" or "internal-value" or "not-static" or "not-public");
    }

    [Fact]
    public void Extractor_reports_empty_valued_constants_instead_of_silently_guarding_nothing()
    {
        const string source = """
            namespace Nerv.IIP.Contracts.Mes;

            public static class MesVocabulary { public const string Empty = ""; }
            """;

        var extraction = ContractsVocabularyExtractor.Extract(
            [new SourceDocument("Contracts.cs", ContractsSource), new SourceDocument("Extra.cs", source)]);

        Assert.Contains(extraction.Errors, error => error.Contains("不允许取值为空串", StringComparison.Ordinal));
    }

    // ── 真阳：裸字面量的各种形态 ────────────────────────────────────────────────

    [Fact]
    public void Bare_literal_matching_a_vocabulary_value_is_reported_with_the_candidate_constant()
    {
        var result = Scan("""class Probe { string Source() => "business-erp"; }""");

        var violation = Assert.Single(result.Violations);
        Assert.Contains("business-erp", violation, StringComparison.Ordinal);
        Assert.Contains("Nerv.IIP.Contracts.Approval.ApprovalSourceServices.BusinessErp", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Local_const_redeclaration_of_a_vocabulary_value_is_reported()
    {
        // 「在服务里手工引入新的裸字面量词表」的最小形态：本地 const 重抄契约取值。
        var result = Scan("""class Probe { private const string SourceService = "business-erp"; }""");

        Assert.Single(result.Violations);
    }

    [Fact]
    public void Raw_string_literal_matching_a_vocabulary_value_is_reported()
    {
        var result = Scan("class Probe { string Source() => \"\"\"business-erp\"\"\"; }");

        Assert.Single(result.Violations);
    }

    [Fact]
    public void Interpolated_string_without_holes_matching_a_vocabulary_value_is_reported()
    {
        var result = Scan("""class Probe { string Source() => $"business-erp"; }""");

        Assert.Single(result.Violations);
    }

    // ── 真阴：常量引用与非字面量出现形态 ────────────────────────────────────────

    [Fact]
    public void Constant_reference_is_the_compliant_form_and_is_not_reported()
    {
        var result = Scan(
            """
            using Nerv.IIP.Contracts.Approval;

            class Probe { string Source() => ApprovalSourceServices.BusinessErp; }
            """);

        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Values_inside_comments_and_xml_docs_are_not_reported()
    {
        // 样板 1（ApprovalChainSeededIdentityContractTests）的已知弱点是 string.Contains
        // 分不清注释与代码；本扫描器按语法树字面量节点判定，注释/XML doc 天然不命中。
        var result = Scan(
            """
            /// <summary>文档里提到 "business-erp" 与 quality 不算违例。</summary>
            class Probe
            {
                // 行注释里的 "business-erp" 也不算。
                string Source() => "unrelated";
            }
            """);

        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Identifiers_and_nameof_matching_a_vocabulary_value_are_not_reported()
    {
        var result = Scan(
            """
            class quality { }

            class Probe { string Name() => nameof(quality); }
            """);

        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Interpolated_fragments_containing_a_vocabulary_value_are_not_reported()
    {
        // 覆盖率声明的边界：词表值作为插值/拼接片段没有静态完整取值，不在守护范围。
        var result = Scan("""class Probe { string Key(string id) => $"business-erp:{id}"; }""");

        Assert.Empty(result.Violations);
    }

    // ── (a) 类假阳：同值不同义 ──────────────────────────────────────────────────

    [Fact]
    public void Same_value_defined_by_multiple_constants_lists_every_candidate_for_human_adjudication()
    {
        // 票面 (a) 类：`"quality"` 同时是审批来源与库存流水来源等多义值。
        // 扫描器不猜语义——违例消息必须列出全部候选常量，由白名单裁决归属。
        const string inventoryContracts = """
            namespace Nerv.IIP.Contracts.Inventory;

            public static class InventoryMovementSourceServices { public const string Quality = "quality"; }
            """;

        var result = Scan(
            """class Probe { string Source() => "quality"; }""",
            constants: Constants(inventoryContracts));

        var violation = Assert.Single(result.Violations);
        Assert.Contains("Nerv.IIP.Contracts.Approval.ApprovalSourceServices.Quality", violation, StringComparison.Ordinal);
        Assert.Contains("Nerv.IIP.Contracts.Inventory.InventoryMovementSourceServices.Quality", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Exempted_value_and_path_pair_is_suppressed_and_marked_used()
    {
        var exemption = new VocabularyExemption(
            "quality",
            "services/Probe/Application/Probe.cs",
            "同值不同义：probe 里的 quality 是数据库 schema 名。");

        var result = Scan("""class Probe { string Schema() => "quality"; }""", exemptions: [exemption]);

        Assert.Empty(result.Violations);
        Assert.Empty(result.StaleExemptions);
    }

    [Fact]
    public void Exemption_is_scoped_to_the_exact_value_and_path_pair()
    {
        var exemption = new VocabularyExemption(
            "quality",
            "services/Probe/Application/Probe.cs",
            "同值不同义：probe 裁决。");

        // 同值不同文件：仍红。
        var otherFile = Scan(
            """class Probe { string Source() => "quality"; }""",
            exemptions: [exemption],
            consumerPath: "services/Probe/Application/Other.cs");
        Assert.Single(otherFile.Violations);

        // 同文件不同值：仍红（该值的豁免同时判 stale，防止裁决对象漂移后残留）。
        var otherValue = Scan(
            """class Probe { string Source() => "business-erp"; }""",
            exemptions: [exemption]);
        Assert.Single(otherValue.Violations);
        Assert.Single(otherValue.StaleExemptions);
    }

    [Fact]
    public void Stale_exemptions_are_reported_so_the_whitelist_only_shrinks()
    {
        var exemption = new VocabularyExemption(
            "business-erp",
            "services/Probe/Application/Probe.cs",
            "待 #1370 ③ 销账：probe 示例。");

        var result = Scan("""class Probe { string Source() => "unrelated"; }""", exemptions: [exemption]);

        var stale = Assert.Single(result.StaleExemptions);
        Assert.Contains("待 #1370 ③ 销账：probe 示例。", stale, StringComparison.Ordinal);
    }

    // ── (b) 类假阳：跨服务逐字副本 ──────────────────────────────────────────────

    [Fact]
    public void Literals_inside_registered_replica_files_are_not_reported_by_the_literal_gate()
    {
        var result = Scan(
            """class WorldHistorySpec { public const string Source = "business-erp"; }""",
            replicaFileNames: ["WorldHistorySpec.cs"],
            consumerPath: "services/Probe/Application/Seed/WorldHistorySpec.cs");

        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Replica_copies_with_identical_shared_members_pass_despite_namespace_and_extra_members()
    {
        // 副本圈的合法差异：namespace/using 不同、单侧存在服务专属成员、XML doc 措辞不同。
        var drifts = ReplicaConsistencyChecker.Check(
        [
            new SourceDocument(
                "services/A/Application/Seed/WorldHistorySpec.cs",
                """
                using System;

                namespace A.Seed;

                /// <summary>A 侧文档。</summary>
                public static class WorldHistorySpec
                {
                    public const string UomCode = "EA";

                    /// <summary>A 专属成员。</summary>
                    public static string OnlyInA() => "a";
                }
                """),
            new SourceDocument(
                "services/B/Application/Seed/WorldHistorySpec.cs",
                """
                namespace B.Seed;

                /// <summary>B 侧文档（措辞不同不算分裂）。</summary>
                public static class WorldHistorySpec
                {
                    public const string UomCode = "EA";
                }
                """),
        ]);

        Assert.Empty(drifts);
    }

    [Fact]
    public void Replica_copies_with_diverged_shared_members_are_reported_member_by_member()
    {
        // 「一侧改了另一侧没跟上」= Digest 分叉思路（WorldHistoryShortageComponentGoldenVector 先例）。
        var drifts = ReplicaConsistencyChecker.Check(
        [
            new SourceDocument(
                "services/A/Application/Seed/WorldHistorySpec.cs",
                """namespace A.Seed; public static class WorldHistorySpec { public const string UomCode = "EA"; }"""),
            new SourceDocument(
                "services/B/Application/Seed/WorldHistorySpec.cs",
                """namespace B.Seed; public static class WorldHistorySpec { public const string UomCode = "PCS"; }"""),
        ]);

        var drift = Assert.Single(drifts);
        Assert.Equal("WorldHistorySpec.UomCode", drift.MemberKey);
        Assert.Contains("逐字相同", drift.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Replica_positional_record_parameter_divergence_is_reported_via_the_type_header()
    {
        // 位置记录没有花括号成员，参数表分叉只能通过类型头伪成员被看见（真实先例：
        // Scheduling 侧 WorldHistoryOperation 缺 Workshop 参数）。
        var drifts = ReplicaConsistencyChecker.Check(
        [
            new SourceDocument(
                "services/A/Application/Seed/WorldHistoryMesSpec.cs",
                "namespace A.Seed; public sealed record Operation(int Sequence, string Code);"),
            new SourceDocument(
                "services/B/Application/Seed/WorldHistoryMesSpec.cs",
                "namespace B.Seed; public sealed record Operation(int Sequence, string Code, string Workshop);"),
        ]);

        var drift = Assert.Single(drifts);
        Assert.Equal("Operation.<类型头>", drift.MemberKey);
    }

    [Fact]
    public void Replica_method_overloads_are_compared_by_name_and_parameter_list()
    {
        var drifts = ReplicaConsistencyChecker.Check(
        [
            new SourceDocument(
                "services/A/Application/Seed/WorldHistorySpec.cs",
                """
                namespace A.Seed;

                public static class WorldHistorySpec
                {
                    public static string No(int index) => $"SO-{index:D4}";
                    public static string No(string prefix, int index) => $"{prefix}-{index:D4}";
                }
                """),
            new SourceDocument(
                "services/B/Application/Seed/WorldHistorySpec.cs",
                """
                namespace B.Seed;

                public static class WorldHistorySpec
                {
                    public static string No(int index) => $"SO-{index:D4}";
                    public static string No(string prefix, int index) => $"{prefix}-{index:D5}";
                }
                """),
        ]);

        var drift = Assert.Single(drifts);
        Assert.Equal("WorldHistorySpec.No(string prefix, int index)", drift.MemberKey);
    }
}
