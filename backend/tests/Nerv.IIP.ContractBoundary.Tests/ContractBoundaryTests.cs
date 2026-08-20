using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Wms.Web.Endpoints.Wms;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Ops.Domain;

namespace Nerv.IIP.ContractBoundary.Tests;

public sealed class ContractBoundaryTests
{
    private static readonly DomainForbiddenContractPolicy[] ForbiddenContractPolicies =
    [
        new(typeof(IAppHubStateStore).Assembly, ["Nerv.IIP.Contracts.AppHubQueries"]),
        new(typeof(OperationTaskFact).Assembly, ["Nerv.IIP.Contracts.Ops"]),
        new(typeof(SchedulePlan).Assembly, ["Nerv.IIP.Contracts.Scheduling"]),
        // #1857 / #1890：为 Domain 开放词表契约时，查询 / 读模型 / 算法契约禁引面必须同时登记。
        new(typeof(ApprovalChain).Assembly,
            ["Nerv.IIP.Contracts.AppHubQueries", "Nerv.IIP.Contracts.Ops", "Nerv.IIP.Contracts.Scheduling"]),
        new(typeof(StockMovement).Assembly,
            ["Nerv.IIP.Contracts.AppHubQueries", "Nerv.IIP.Contracts.Ops", "Nerv.IIP.Contracts.Scheduling"]),
        new(typeof(InspectionRecord).Assembly,
            ["Nerv.IIP.Contracts.AppHubQueries", "Nerv.IIP.Contracts.Ops", "Nerv.IIP.Contracts.Scheduling"]),
    ];

    /// <summary>
    /// Domain 契约开门登记：一条 = Domain 程序集 × 允许的契约程序集 × 允许使用的词表类型。
    ///
    /// 词表成员大多是 <c>const string</c>，消费方编译后会内联值，不留下 TypeRef / MemberRef；
    /// 因而「零元数据引用」是合法预期，不代表 Domain 没有使用词表，也不代表这道门可以删除。
    /// 非 <c>const</c> 成员（例如 <c>ApprovalDecisions.StepResolutions</c>）会留下元数据引用，
    /// 必须命中这里的精确类型白名单。同一契约程序集里的集成事件 DTO 等其他类型一律禁止。
    /// </summary>
    private static readonly DomainContractOpeningPolicy[] ContractOpeningPolicies =
    [
        new(
            typeof(ApprovalChain).Assembly,
            typeof(ApprovalChainStatuses).Assembly,
            [typeof(ApprovalChainStatuses).FullName!, typeof(ApprovalDecisions).FullName!]),
        // #1891 / #1892 将在对应 Domain 引用化时使用；先登记开门边界，禁止放宽到整个契约程序集。
        new(
            typeof(StockMovement).Assembly,
            typeof(InventoryQualityStatuses).Assembly,
            [typeof(InventoryQualityStatuses).FullName!, typeof(InventoryMovementTypes).FullName!]),
        new(
            typeof(InspectionRecord).Assembly,
            typeof(QualityInspectionDispositionStatuses).Assembly,
            [typeof(QualityInspectionDispositionStatuses).FullName!]),
    ];

    /// <summary>
    /// 领域层禁引面登记表：一条 = 一个领域程序集 × 它不得引用的查询 / 读模型 / 算法契约。
    ///
    /// 口径是「Domain 不得引**查询 / 读模型 / 算法**契约」，不是「Domain 不得引任何契约」——
    /// 领域引词表类契约（<c>Contracts.Approval</c> / <c>Contracts.Wms</c> /
    /// <c>Contracts.ConnectorProtocol</c>）是既有且允许的形态。
    ///
    /// **凡是给某个领域程序集开了「可以引契约」这道门，必须同时在本表登记它的禁引面**
    /// （#1857 走查：Approval.Domain 当时开了门却没登记，禁引面上零条断言——
    /// 文档强度强于实现强度）。
    /// </summary>
    public static TheoryData<Assembly, string[]> DomainAssembliesWithForbiddenContracts
    {
        get
        {
            var data = new TheoryData<Assembly, string[]>();
            foreach (var policy in ForbiddenContractPolicies)
            {
                data.Add(policy.DomainAssembly, policy.ForbiddenContractNames);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(DomainAssembliesWithForbiddenContracts))]
    public void Domain_projects_do_not_reference_query_or_algorithm_contracts(
        Assembly domainAssembly,
        string[] forbiddenContractNames)
    {
        var referencedAssemblyNames = CollectReferencedAssemblyNames(domainAssembly);
        var offenders = forbiddenContractNames
            .Where(referencedAssemblyNames.Contains)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Domain assembly {domainAssembly.GetName().Name} must not reference query/read-model/algorithm contracts: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Known_domain_contract_openings_are_registered_exactly()
    {
        var registrations = ContractOpeningPolicies
            .Select(policy =>
                $"{policy.DomainAssembly.GetName().Name} -> {policy.ContractAssembly.GetName().Name}: "
                + string.Join(",", policy.AllowedVocabularyTypeNames.OrderBy(name => name, StringComparer.Ordinal)))
            .OrderBy(registration => registration, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "Nerv.IIP.Business.Approval.Domain -> Nerv.IIP.Contracts.Approval: "
                + "Nerv.IIP.Contracts.Approval.ApprovalChainStatuses,Nerv.IIP.Contracts.Approval.ApprovalDecisions",
                "Nerv.IIP.Business.Inventory.Domain -> Nerv.IIP.Contracts.Inventory: "
                + "Nerv.IIP.Contracts.Inventory.InventoryMovementTypes,Nerv.IIP.Contracts.Inventory.InventoryQualityStatuses",
                "Nerv.IIP.Business.Quality.Domain -> Nerv.IIP.Contracts.Quality: "
                + "Nerv.IIP.Contracts.Quality.QualityInspectionDispositionStatuses",
            ],
            registrations);
    }

    [Fact]
    public void Domain_contract_openings_have_forbidden_contract_coverage()
    {
        var coveredDomains = ForbiddenContractPolicies
            .Select(policy => policy.DomainAssembly.GetName().Name)
            .ToHashSet(StringComparer.Ordinal);
        var missing = ContractOpeningPolicies
            .Select(policy => policy.DomainAssembly.GetName().Name!)
            .Where(domainName => !coveredDomains.Contains(domainName))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(domainName => domainName, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "已开放词表契约的 Domain 必须同时登记查询/读模型/算法契约禁引面：" + string.Join(", ", missing));
    }

    [Fact]
    public void Domain_contract_references_are_limited_to_registered_vocabulary_types()
    {
        var violations = ContractOpeningPolicies
            .SelectMany(policy =>
                CollectDisallowedContractTypeReferences(policy)
                    .Select(reference =>
                        $"{policy.DomainAssembly.GetName().Name} -> {policy.ContractAssembly.GetName().Name}: "
                        + $"{reference.TypeName} ({reference.MetadataKinds})"))
            .OrderBy(violation => violation, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Domain 只能使用开门登记中的词表类型，不得借门引用同程序集的 DTO/事件类型："
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Metadata_scanner_detects_non_vocabulary_contract_types_inside_type_specs()
    {
        var violations = CollectDisallowedContractTypeReferences(new DomainContractOpeningPolicy(
            typeof(ContractBoundaryTests).Assembly,
            typeof(InventoryQualityStatuses).Assembly,
            [typeof(InventoryQualityStatuses).FullName!]));

        Assert.Contains(
            violations,
            reference => reference.TypeName == "Nerv.IIP.Contracts.Inventory.InventoryMovementRequestedIntegrationEvent");
    }

    [Fact]
    public void Approval_non_const_vocabulary_reference_is_visible_in_metadata()
    {
        var references = CollectContractTypeReferences(
            typeof(ApprovalChain).Assembly,
            typeof(ApprovalDecisions).Assembly);

        Assert.Contains(
            references,
            reference => reference.TypeName == typeof(ApprovalDecisions).FullName
                && reference.MetadataKinds.Contains("MemberRef", StringComparison.Ordinal));
    }

    [Fact]
    public void Wms_uses_public_inventory_contract_instead_of_local_inventory_dto_copy()
    {
        var wmsAssembly = typeof(WmsEndpoint<,>).Assembly;
        var referencedAssemblyNames = CollectReferencedAssemblyNames(wmsAssembly);
        var localDtoTypeNames = wmsAssembly
            .GetTypes()
            .Select(x => x.Name)
            .Where(typeName =>
                typeName is "IInventoryMovementClient"
                    or "PostStockMovementRequest"
                    or "PostStockMovementResponse")
            .ToArray();

        Assert.Contains("Nerv.IIP.Contracts.Inventory", referencedAssemblyNames);
        Assert.Empty(localDtoTypeNames);
    }

    private static HashSet<string> CollectReferencedAssemblyNames(Assembly rootAssembly)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Collect(rootAssembly);
        return visited;

        void Collect(Assembly assembly)
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (string.IsNullOrWhiteSpace(reference.Name) || !visited.Add(reference.Name))
                {
                    continue;
                }

                var localAssemblyPath = Path.Combine(AppContext.BaseDirectory, $"{reference.Name}.dll");
                if (File.Exists(localAssemblyPath))
                {
                    Collect(Assembly.LoadFrom(localAssemblyPath));
                }
            }
        }
    }

    private static IReadOnlyList<ContractTypeReference> CollectContractTypeReferences(
        Assembly consumerAssembly,
        Assembly contractAssembly)
    {
        if (string.IsNullOrWhiteSpace(consumerAssembly.Location))
        {
            throw new InvalidOperationException($"程序集 {consumerAssembly.GetName().Name} 没有可读取的物理路径。");
        }

        using var stream = File.OpenRead(consumerAssembly.Location);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            throw new InvalidOperationException($"程序集 {consumerAssembly.GetName().Name} 不包含 CLI 元数据。");
        }

        var reader = peReader.GetMetadataReader();
        var contractAssemblyName = contractAssembly.GetName().Name
            ?? throw new InvalidOperationException("契约程序集缺少名称。");
        var references = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var handle in reader.TypeReferences)
        {
            AddIfTarget(handle, "TypeRef");
        }

        foreach (var handle in reader.MemberReferences)
        {
            var member = reader.GetMemberReference(handle);
            if (member.Parent.Kind == HandleKind.TypeReference)
            {
                AddIfTarget((TypeReferenceHandle)member.Parent, "MemberRef");
            }
            // TypeSpec 的签名仍会引用 TypeRef 表中的底层类型；上面的全表扫描负责闭合该路径。
        }

        return references
            .Select(pair => new ContractTypeReference(
                pair.Key,
                string.Join("+", pair.Value.OrderBy(kind => kind, StringComparer.Ordinal))))
            .OrderBy(reference => reference.TypeName, StringComparer.Ordinal)
            .ToArray();

        void AddIfTarget(TypeReferenceHandle handle, string metadataKind)
        {
            if (!string.Equals(ResolveAssemblyName(reader, handle), contractAssemblyName, StringComparison.Ordinal))
            {
                return;
            }

            var typeName = ResolveTypeName(reader, handle);
            if (!references.TryGetValue(typeName, out var kinds))
            {
                kinds = new HashSet<string>(StringComparer.Ordinal);
                references.Add(typeName, kinds);
            }

            kinds.Add(metadataKind);
        }
    }

    private static IReadOnlyList<ContractTypeReference> CollectDisallowedContractTypeReferences(
        DomainContractOpeningPolicy policy)
    {
        var allowedTypes = policy.AllowedVocabularyTypeNames.ToHashSet(StringComparer.Ordinal);
        return CollectContractTypeReferences(policy.DomainAssembly, policy.ContractAssembly)
            .Where(reference => !allowedTypes.Contains(reference.TypeName))
            .ToArray();
    }

    private static string? ResolveAssemblyName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var typeReference = reader.GetTypeReference(handle);
        return typeReference.ResolutionScope.Kind switch
        {
            HandleKind.AssemblyReference => reader.GetString(
                reader.GetAssemblyReference((AssemblyReferenceHandle)typeReference.ResolutionScope).Name),
            HandleKind.TypeReference => ResolveAssemblyName(reader, (TypeReferenceHandle)typeReference.ResolutionScope),
            _ => null,
        };
    }

    private static string ResolveTypeName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var typeReference = reader.GetTypeReference(handle);
        var name = reader.GetString(typeReference.Name);
        if (typeReference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return $"{ResolveTypeName(reader, (TypeReferenceHandle)typeReference.ResolutionScope)}+{name}";
        }

        var @namespace = reader.GetString(typeReference.Namespace);
        return string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
    }

    // 泛型方法签名通过 TypeSpec 引用 DTO；底层 DTO TypeRef 必须被全表扫描捕获，不能使用会内联的 const 作探针。
    private static IReadOnlyList<InventoryMovementRequestedIntegrationEvent>? NonVocabularyContractTypeProbe() => null;

    private sealed record DomainForbiddenContractPolicy(Assembly DomainAssembly, string[] ForbiddenContractNames);

    private sealed record DomainContractOpeningPolicy(
        Assembly DomainAssembly,
        Assembly ContractAssembly,
        string[] AllowedVocabularyTypeNames);

    private sealed record ContractTypeReference(string TypeName, string MetadataKinds);
}
