# 可配置编码规则引擎（Coding Rule Engine）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: 用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现本计划。步骤用 `- [ ]` 复选框跟踪。
> 设计真相见配套 spec：[`2026-06-12-coding-rule-engine-design.md`](../specs/2026-06-12-coding-rule-engine-design.md)。术语、段类型、标准规则种子表、治理约定以 spec 为准，本计划不重复，只给可执行步骤。

**Goal:** 新建可配置编码规则引擎 `Nerv.IIP.Coding`，用段化规则模板替换写死的 `Nerv.IIP.Numbering`，5 个 Numbering 接入服务（MasterData、MES、ERP、DemandPlanning、ProductEngineering）全部切换，MasterData 全资源 `code` 改自动生成、去手填。（BarcodeLabel 自带 `BarcodeRule`、未引用 Numbering，不在本轮。）

**Architecture:** 集中定义（`StandardCodeRules` 配置即代码 + MasterData `code_rules` 主数据表）+ service-local 计数（各服务本地 `code_counters`/`code_idempotency_keys`，乐观锁+重试+幂等）。引擎核心纯函数段求值 + EF 存储抽象，无跨服务运行时依赖。

**Tech Stack:** .NET 10、C#、EF Core 10（PostgreSQL）、xUnit、NetCorePal（`KnownException`、`IGuidStronglyTypedId`、CleanDDD）、FastEndpoints、Hey API（前端 codegen）、Vue 3 + `@nerv-iip/api-client`。

**约定（每个服务通用）：**
- 后端在仓库根用 `dotnet build backend/Nerv.IIP.sln` / `dotnet test <proj>` 验证。EF 迁移用 `dotnet tool restore` 后 `dotnet tool run dotnet-ef`（见 readiness §环境前置 10）。
- 每个任务末尾 commit，message 前缀 `feat(coding):` / `refactor(coding):` / `test(coding):` / `chore(coding):`。
- TDD：先写失败测试 → 跑红 → 最小实现 → 跑绿 → commit。

---

## 文件结构总览

**新建（纯契约规则模型 + 引擎核心 + 测试）：**

> **依赖方向（按 review 修正）：`Nerv.IIP.Coding` → `Nerv.IIP.Contracts.Coding`，单向。** 契约层是纯规则模型 DTO（无 EF/实现依赖，沿用现有 `Contracts.*` 约定）；引擎引用契约、直接消费 `CodeRuleDefinition`，**不另设平行的运行时 `CodeRule` 类型**（DRY）。

```
# 纯契约规则模型（无任何实现依赖；先建）
backend/common/Contracts/Nerv.IIP.Contracts.Coding/Nerv.IIP.Contracts.Coding.csproj
backend/common/Contracts/Nerv.IIP.Contracts.Coding/CodeRuleEnums.cs       # SegmentType, ResetPeriod, ScopeDimension, FieldTransform
backend/common/Contracts/Nerv.IIP.Contracts.Coding/CodeRuleSegment.cs     # 段 DTO record + 工厂方法
backend/common/Contracts/Nerv.IIP.Contracts.Coding/CodeRuleDefinition.cs  # 规则定义 DTO + Validate()
backend/common/Contracts/Nerv.IIP.Contracts.Coding/StandardCodeRules.cs   # 标准规则目录（返回 CodeRuleDefinition）

# 引擎核心（引用 Contracts.Coding）
backend/common/Coding/Nerv.IIP.Coding/Nerv.IIP.Coding.csproj
backend/common/Coding/Nerv.IIP.Coding/CodeAllocator.cs         # CodeAllocator + CodeAllocationRequest/Allocation + CodeConcurrencyException（消费 CodeRuleDefinition）
backend/common/Coding/Nerv.IIP.Coding/CodeEntities.cs          # CodeCounter, CodeIdempotencyKey
backend/common/Coding/Nerv.IIP.Coding/ICodeStore.cs            # ICodeStore, CodeCounterScope
backend/common/Coding/Nerv.IIP.Coding/EfCoreCodeStore.cs       # EfCoreCodeStore, CodeDbContextLease

backend/tests/Nerv.IIP.Coding.Tests/Nerv.IIP.Coding.Tests.csproj          # 引用 Coding + Contracts.Coding
backend/tests/Nerv.IIP.Coding.Tests/CodeAllocatorTests.cs
backend/tests/Nerv.IIP.Coding.Tests/StandardCodeRulesTests.cs
```

**每个业务服务（MES/ERP/DemandPlanning/ProductEngineering/MasterData）：**
```
.../Infrastructure/Coding/CodeEntityTypeConfigurations.cs      # 替换 Infrastructure/Numbering/NumberingEntityTypeConfigurations.cs
.../Web/Application/Commands/<Svc>CodingService.cs             # 替换 <Svc>NumberingService.cs
.../Infrastructure/Migrations/*_AddCodingTables.cs             # 替换 *_AddNumberingCounters
```

**MasterData 额外：**
```
.../Domain/AggregatesModel/CodeRuleAggregate/CodeRule.cs       # code_rules 聚合
.../Infrastructure/Coding/CodeRuleEntityTypeConfiguration.cs
.../Web/Application/Seed/CodeRuleSeed.cs                        # 物化 StandardCodeRules → code_rules
```

**删除：**
```
backend/common/Numbering/                          （整目录）
backend/tests/Nerv.IIP.Numbering.Tests/            （整目录）
各服务 Infrastructure/Numbering/                    （整目录）
各服务 Web/Application/Commands/**/*NumberingService.cs
backend/Nerv.IIP.sln 中 Numbering / Numbering.Tests 工程项与 solution folder
```

> ⚠️ **不要删除**历史 `*_AddNumberingCounters.cs` migration 文件：`numbering_*` 表的退场由各服务**新追加**的 `AddCodingTables` migration 通过模型 diff `DropTable` 完成（见 Task 8 Step 6），历史 migration 链保持不变。

---

## Phase 1：契约规则模型 + 引擎核心（TDD，独立通过）

> **依赖方向（按 review 修正）**：先建纯契约 `Nerv.IIP.Contracts.Coding`（无 EF/实现依赖），引擎 `Nerv.IIP.Coding` 单向引用它并直接消费 `CodeRuleDefinition`。契约层**不得**引用引擎库。

### Task 1：建两个工程骨架（契约 + 引擎，方向单向）

**Files:**
- Create: `backend/common/Contracts/Nerv.IIP.Contracts.Coding/Nerv.IIP.Contracts.Coding.csproj`
- Create: `backend/common/Coding/Nerv.IIP.Coding/Nerv.IIP.Coding.csproj`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: 创建契约 csproj（纯净，无实现依赖）**

镜像同目录其它 `Nerv.IIP.Contracts.*` 的 csproj（参考 `Nerv.IIP.Contracts.Scheduling.csproj`，**无任何 PackageReference/ProjectReference**——已核实现有 `Contracts.Scheduling` 零依赖、`Contracts.Inventory/Wms` 仅引用 `Contracts.IntegrationEvents`）。`Nerv.IIP.Contracts.Coding` **不引用 EF Core、不引用 `Nerv.IIP.Coding`**。

- [ ] **Step 2: 创建引擎 csproj（引用契约）**

镜像 `Nerv.IIP.Numbering.csproj`（保留 `Microsoft.EntityFrameworkCore`、`NetCorePal.Extensions.Primitives`），并加 `ProjectReference` 指向 `Nerv.IIP.Contracts.Coding`。**方向只能 `Coding → Contracts.Coding`。**

- [ ] **Step 3: 加入 solution**

```bash
dotnet sln backend/Nerv.IIP.sln add backend/common/Contracts/Nerv.IIP.Contracts.Coding/Nerv.IIP.Contracts.Coding.csproj --solution-folder Contracts
dotnet sln backend/Nerv.IIP.sln add backend/common/Coding/Nerv.IIP.Coding/Nerv.IIP.Coding.csproj --solution-folder Coding
```

- [ ] **Step 4: 编译空库 + Commit**

Run: `dotnet build backend/common/Coding/Nerv.IIP.Coding/Nerv.IIP.Coding.csproj` → succeeded
```bash
git add backend/common/Coding backend/common/Contracts/Nerv.IIP.Contracts.Coding backend/Nerv.IIP.sln
git commit -m "chore(coding): scaffold Contracts.Coding + Coding libraries (one-way dependency)"
```

### Task 2：规则模型（纯契约层 `Nerv.IIP.Contracts.Coding`）

**Files:**
- Create: `backend/common/Contracts/Nerv.IIP.Contracts.Coding/CodeRuleEnums.cs`
- Create: `backend/common/Contracts/Nerv.IIP.Contracts.Coding/CodeRuleSegment.cs`
- Create: `backend/common/Contracts/Nerv.IIP.Contracts.Coding/CodeRuleDefinition.cs`

- [ ] **Step 1: 写规则模型（纯 DTO + 定义期校验，命名空间 `Nerv.IIP.Contracts.Coding`）**

> 规则模型只有一份：契约层的 `CodeRuleDefinition`。引擎不再设平行运行时类型，直接消费它。`Validate()` 是结构校验，抛 `ArgumentException`（BCL，无需任何包依赖）。

```csharp
namespace Nerv.IIP.Contracts.Coding;

public enum SegmentType { Constant, Date, Sequence, Field, Checksum }
public enum ResetPeriod { None, Day, Month, Year }
public enum FieldTransform { None, Upper, Lower }

[Flags]
public enum ScopeDimension { Organization = 1, Environment = 2, Site = 4 }

public sealed record CodeRuleSegment
{
    public required SegmentType Type { get; init; }

    // constant
    public string? Value { get; init; }
    // date
    public string? Format { get; init; }
    // sequence
    public int Width { get; init; } = 6;
    public int Start { get; init; } = 1;
    public char PadChar { get; init; } = '0';
    public ResetPeriod Reset { get; init; } = ResetPeriod.None;
    // field
    public string? Source { get; init; }
    public FieldTransform Transform { get; init; } = FieldTransform.None;
    public int? MaxLength { get; init; }
    public bool Required { get; init; } = true;
    // checksum
    public string? Algorithm { get; init; }

    public static CodeRuleSegment ConstantOf(string value) => new() { Type = SegmentType.Constant, Value = value };
    public static CodeRuleSegment DateOf(string format) => new() { Type = SegmentType.Date, Format = format };
    public static CodeRuleSegment SequenceOf(int width, ResetPeriod reset = ResetPeriod.None, int start = 1)
        => new() { Type = SegmentType.Sequence, Width = width, Reset = reset, Start = start };
    public static CodeRuleSegment FieldOf(string source, FieldTransform transform = FieldTransform.None, int? maxLength = null, bool required = true)
        => new() { Type = SegmentType.Field, Source = source, Transform = transform, MaxLength = maxLength, Required = required };
}

public sealed record CodeRuleDefinition
{
    private static readonly HashSet<string> AllowedDateFormats =
        ["yyyyMMdd", "yyMMdd", "yyyyMM", "yyMM", "yyyy", "yy"];

    public required string RuleKey { get; init; }
    public required string DisplayName { get; init; }
    public string AppliesTo { get; init; } = string.Empty;
    public ScopeDimension Scope { get; init; } = ScopeDimension.Organization | ScopeDimension.Environment;
    public required IReadOnlyList<CodeRuleSegment> Segments { get; init; }
    public bool IsActive { get; init; } = true;
    public int Version { get; init; } = 1;

    /// <summary>规则定义结构校验。无效抛 ArgumentException（定义期错误，非运行期）。</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RuleKey)) throw new ArgumentException("RuleKey required.");
        if (Segments is null || Segments.Count == 0) throw new ArgumentException($"Rule '{RuleKey}' has no segments.");
        if (!Segments.Any(s => s.Type == SegmentType.Sequence))
            throw new ArgumentException($"Rule '{RuleKey}' must contain at least one sequence segment.");
        foreach (var s in Segments)
        {
            switch (s.Type)
            {
                case SegmentType.Constant when string.IsNullOrEmpty(s.Value):
                    throw new ArgumentException($"Rule '{RuleKey}' constant segment requires Value.");
                case SegmentType.Date when string.IsNullOrEmpty(s.Format) || !AllowedDateFormats.Contains(s.Format):
                    throw new ArgumentException($"Rule '{RuleKey}' date segment format invalid: '{s.Format}'.");
                case SegmentType.Sequence when s.Width <= 0:
                    throw new ArgumentException($"Rule '{RuleKey}' sequence width must be positive.");
                case SegmentType.Field when string.IsNullOrEmpty(s.Source):
                    throw new ArgumentException($"Rule '{RuleKey}' field segment requires Source.");
                case SegmentType.Checksum when s.Algorithm is not ("hash-mod10" or "hash-mod11"):
                    throw new ArgumentException($"Rule '{RuleKey}' checksum algorithm unsupported: '{s.Algorithm}'.");
            }
        }
    }
}
```

> 拆三个文件（enums / segment / definition）即把上面三块按类型分别落文件；也可单文件，编译等价。

- [ ] **Step 2: 编译**

Run: `dotnet build backend/common/Contracts/Nerv.IIP.Contracts.Coding/Nerv.IIP.Contracts.Coding.csproj`
Expected: Build succeeded（纯 BCL，无外部包）。

- [ ] **Step 3: Commit**

```bash
git add backend/common/Contracts/Nerv.IIP.Contracts.Coding
git commit -m "feat(coding): add CodeRuleDefinition rule model in Contracts.Coding"
```

### Task 3：EF 实体与存储抽象 `CodeEntities.cs` + `ICodeStore.cs`

**Files:**
- Create: `backend/common/Coding/Nerv.IIP.Coding/CodeEntities.cs`
- Create: `backend/common/Coding/Nerv.IIP.Coding/ICodeStore.cs`

- [ ] **Step 1: 写实体（移植自 `NumberingEntities.cs`，`DocumentType`→`RuleKey`、`DateSegment`→`ResetKey`、`Number`→`Code`）**

```csharp
#pragma warning disable S1144 // EF Core sets surrogate identifiers through materialization.
namespace Nerv.IIP.Coding;

public sealed class CodeCounter
{
    private CodeCounter() { }

    public CodeCounter(string organizationId, string environmentId, string ruleKey, string siteCode, string resetKey)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        RuleKey = ruleKey;
        SiteCode = siteCode;
        ResetKey = resetKey;
    }

    public long Id { get; private set; }
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string RuleKey { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string ResetKey { get; private set; } = string.Empty;
    public long CurrentValue { get; private set; }
    public long Version { get; private set; }

    public long AdvanceFrom(long start)
    {
        CurrentValue = CurrentValue < start - 1 ? start : CurrentValue + 1;
        Version++;
        return CurrentValue;
    }
}

public sealed class CodeIdempotencyKey
{
    private CodeIdempotencyKey() { }

    public CodeIdempotencyKey(string organizationId, string environmentId, string ruleKey,
        string idempotencyKey, string code, string payloadFingerprint, DateTimeOffset createdAtUtc)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        RuleKey = ruleKey;
        IdempotencyKey = idempotencyKey;
        Code = code;
        PayloadFingerprint = payloadFingerprint;
        CreatedAtUtc = createdAtUtc;
    }

    public long Id { get; private set; }
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string RuleKey { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string PayloadFingerprint { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
```

> 注：`AdvanceFrom(start)` 让 `sequence.start` 生效——首次分配返回 `start`，之后逐 +1（`CurrentValue` 初值 0，首次 `0 < start-1` 时取 `start`）。

- [ ] **Step 2: 写存储抽象（移植 `INumberingStore`，scope 用 RuleKey/ResetKey）**

```csharp
namespace Nerv.IIP.Coding;

public sealed record CodeCounterScope(
    string OrganizationId, string EnvironmentId, string RuleKey, string SiteCode, string ResetKey, long Start);

public interface ICodeStore
{
    Task<CodeIdempotencyKey?> FindIdempotencyRecordAsync(
        string organizationId, string environmentId, string ruleKey, string idempotencyKey, CancellationToken cancellationToken);

    void AddIdempotencyRecord(CodeIdempotencyKey idempotencyKey);

    Task<long> ReserveNextCounterValueAsync(CodeCounterScope scope, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: 编译 + Commit**

Run: `dotnet build backend/common/Coding/Nerv.IIP.Coding/Nerv.IIP.Coding.csproj` → succeeded
```bash
git add backend/common/Coding/Nerv.IIP.Coding/CodeEntities.cs backend/common/Coding/Nerv.IIP.Coding/ICodeStore.cs
git commit -m "feat(coding): add code counter/idempotency entities and store abstraction"
```

### Task 4：分配核心 `CodeAllocator.cs`（先写测试）

**Files:**
- Create: `backend/tests/Nerv.IIP.Coding.Tests/Nerv.IIP.Coding.Tests.csproj`（镜像 `Nerv.IIP.Numbering.Tests.csproj`，引用 `Nerv.IIP.Coding` 与 `Nerv.IIP.Contracts.Coding`）
- Create: `backend/tests/Nerv.IIP.Coding.Tests/CodeAllocatorTests.cs`
- Create: `backend/common/Coding/Nerv.IIP.Coding/CodeAllocator.cs`

- [ ] **Step 1: 建测试工程并入 sln**

复制 `backend/tests/Nerv.IIP.Numbering.Tests/Nerv.IIP.Numbering.Tests.csproj` → 新路径，`ProjectReference` 指向 `Nerv.IIP.Coding` 与 `Nerv.IIP.Contracts.Coding`。
Run: `dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Coding.Tests/Nerv.IIP.Coding.Tests.csproj --solution-folder tests`

- [ ] **Step 2: 写失败测试（覆盖段求值、拼接、重置桶、start、field 大写/截断、in-memory 流水、幂等回放/冲突、规则校验）**

```csharp
using Nerv.IIP.Coding;
using Nerv.IIP.Contracts.Coding;
using Xunit;

namespace Nerv.IIP.Coding.Tests;

public class CodeAllocatorTests
{
    private static CodeRuleDefinition SkuRule() => new()
    {
        RuleKey = "sku", DisplayName = "SKU",
        Segments =
        [
            CodeRuleSegment.ConstantOf("SKU"),
            CodeRuleSegment.ConstantOf("-"),
            CodeRuleSegment.DateOf("yyyyMMdd"),
            CodeRuleSegment.ConstantOf("-"),
            CodeRuleSegment.SequenceOf(6, ResetPeriod.Day),
        ],
    };

    private static CodeAllocator InMemoryAllocator(DateTimeOffset now)
        => new(store: null, timeProvider: new FixedTimeProvider(now));

    [Fact]
    public async Task Allocates_sku_with_date_and_zero_padded_sequence()
    {
        var allocator = InMemoryAllocator(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero));
        var req = new CodeAllocationRequest("org", "env", SkuRule(), Fields: null, RequestedCode: null,
            IdempotencyKey: null, PayloadFingerprint: "fp", ConflictResourceLabel: "sku");

        var first = await allocator.AllocateAsync(req, CancellationToken.None);
        var second = await allocator.AllocateAsync(req, CancellationToken.None);

        Assert.Equal("SKU-20260612-000001", first.Code);
        Assert.False(first.IsIdempotentReplay);
        Assert.Equal("SKU-20260612-000002", second.Code);
    }

    [Fact]
    public async Task Field_segment_uppercases_and_truncates()
    {
        var rule = new CodeRuleDefinition
        {
            RuleKey = "material", DisplayName = "物料",
            Segments =
            [
                CodeRuleSegment.FieldOf("materialType", FieldTransform.Upper, maxLength: 3),
                CodeRuleSegment.ConstantOf("-"),
                CodeRuleSegment.SequenceOf(5),
            ],
        };
        var allocator = InMemoryAllocator(new DateTimeOffset(2026, 6, 12, 0, 0, 0, TimeSpan.Zero));
        var req = new CodeAllocationRequest("org", "env", rule,
            Fields: new Dictionary<string, string> { ["materialType"] = "raw-material" },
            RequestedCode: null, IdempotencyKey: null, PayloadFingerprint: "fp", ConflictResourceLabel: "material");

        var result = await allocator.AllocateAsync(req, CancellationToken.None);

        Assert.Equal("RAW-00001", result.Code);
    }

    [Fact]
    public async Task Missing_required_field_throws()
    {
        var rule = new CodeRuleDefinition
        {
            RuleKey = "material", DisplayName = "物料",
            Segments = [CodeRuleSegment.FieldOf("materialType"), CodeRuleSegment.SequenceOf(3)],
        };
        var allocator = InMemoryAllocator(new DateTimeOffset(2026, 6, 12, 0, 0, 0, TimeSpan.Zero));
        var req = new CodeAllocationRequest("org", "env", rule, Fields: null, RequestedCode: null,
            IdempotencyKey: null, PayloadFingerprint: "fp", ConflictResourceLabel: "material");

        await Assert.ThrowsAsync<NetCorePal.Extensions.Primitives.KnownException>(
            () => allocator.AllocateAsync(req, CancellationToken.None));
    }

    [Fact]
    public async Task Idempotency_replays_first_code_and_rejects_conflicting_fingerprint()
    {
        var allocator = InMemoryAllocator(new DateTimeOffset(2026, 6, 12, 0, 0, 0, TimeSpan.Zero));
        CodeAllocationRequest Req(string fp) => new("org", "env", SkuRule(), Fields: null, RequestedCode: null,
            IdempotencyKey: "idem-1", PayloadFingerprint: fp, ConflictResourceLabel: "sku");

        var first = await allocator.AllocateAsync(Req("fp-A"), CancellationToken.None);
        var replay = await allocator.AllocateAsync(Req("fp-A"), CancellationToken.None);

        Assert.Equal(first.Code, replay.Code);
        Assert.True(replay.IsIdempotentReplay);
        await Assert.ThrowsAsync<NetCorePal.Extensions.Primitives.KnownException>(
            () => allocator.AllocateAsync(Req("fp-B"), CancellationToken.None));
    }

    [Fact]
    public async Task RequestedCode_is_used_verbatim()
    {
        var allocator = InMemoryAllocator(new DateTimeOffset(2026, 6, 12, 0, 0, 0, TimeSpan.Zero));
        var req = new CodeAllocationRequest("org", "env", SkuRule(), Fields: null, RequestedCode: "SKU-LEGACY-1",
            IdempotencyKey: null, PayloadFingerprint: "fp", ConflictResourceLabel: "sku");

        var result = await allocator.AllocateAsync(req, CancellationToken.None);

        Assert.Equal("SKU-LEGACY-1", result.Code);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
```

- [ ] **Step 3: 跑红**

Run: `dotnet test backend/tests/Nerv.IIP.Coding.Tests/Nerv.IIP.Coding.Tests.csproj`
Expected: 编译失败/红（`CodeAllocator`、`CodeAllocationRequest` 未定义）。

- [ ] **Step 4: 实现 `CodeAllocator.cs`（移植 `NumberingServiceCore` 结构 + 段求值引擎）**

```csharp
using System.Globalization;
using System.Text;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Contracts.Coding;

namespace Nerv.IIP.Coding;

public sealed record CodeAllocation(string Code, bool IsIdempotentReplay);

public sealed record CodeAllocationRequest(
    string OrganizationId,
    string EnvironmentId,
    CodeRuleDefinition Rule,
    IReadOnlyDictionary<string, string>? Fields,
    string? RequestedCode,
    string? IdempotencyKey,
    string PayloadFingerprint,
    string ConflictResourceLabel,
    string SiteCode = "");

public sealed class CodeConcurrencyException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed record CodeAllocatorOptions(int MaxConcurrencyAttempts, Func<int, TimeSpan> RetryBackoff)
{
    public static CodeAllocatorOptions Default { get; } = new(5, attempt => TimeSpan.FromMilliseconds(attempt * 10));
}

public sealed class CodeAllocator(
    ICodeStore? store = null,
    TimeProvider? timeProvider = null,
    CodeAllocatorOptions? options = null)
{
    private readonly ICodeStore? _store = store;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly CodeAllocatorOptions _options = options ?? CodeAllocatorOptions.Default;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CodeIdempotencyKey> _idempotency = new(StringComparer.Ordinal);

    public async Task<CodeAllocation> AllocateAsync(CodeAllocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Rule.Validate();
        if (!request.Rule.IsActive)
            throw new KnownException($"Code rule '{request.Rule.RuleKey}' is inactive.");

        var idemKey = Normalize(request.IdempotencyKey);
        var requested = Normalize(request.RequestedCode);

        if (_store is null)
            return AllocateInMemory(request, requested, idemKey);

        var record = idemKey is null ? null
            : await _store.FindIdempotencyRecordAsync(request.OrganizationId, request.EnvironmentId,
                request.Rule.RuleKey, idemKey, cancellationToken);
        if (record is not null)
        {
            if (!string.Equals(record.PayloadFingerprint, request.PayloadFingerprint, StringComparison.Ordinal))
                throw new KnownException($"Idempotency key '{idemKey}' conflicts with a different {request.ConflictResourceLabel} create payload.");
            return new CodeAllocation(record.Code, true);
        }

        var code = requested ?? await BuildCodeAsync(request, cancellationToken);
        if (idemKey is not null)
            _store.AddIdempotencyRecord(new CodeIdempotencyKey(request.OrganizationId, request.EnvironmentId,
                request.Rule.RuleKey, idemKey, code, request.PayloadFingerprint, _timeProvider.GetUtcNow()));
        return new CodeAllocation(code, false);
    }

    public static string Fingerprint(params object?[] parts) => string.Join('|', parts.Select(p => p switch
    {
        null => string.Empty,
        IEnumerable<string> values => string.Join(',', values.Order(StringComparer.Ordinal)),
        _ => Convert.ToString(p, CultureInfo.InvariantCulture) ?? string.Empty,
    }));

    private async Task<string> BuildCodeAsync(CodeAllocationRequest request, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var sb = new StringBuilder();
        foreach (var seg in request.Rule.Segments)
        {
            switch (seg.Type)
            {
                case SegmentType.Constant:
                    sb.Append(seg.Value);
                    break;
                case SegmentType.Date:
                    sb.Append(now.ToString(seg.Format!, CultureInfo.InvariantCulture));
                    break;
                case SegmentType.Field:
                    sb.Append(ResolveField(request, seg));
                    break;
                case SegmentType.Sequence:
                    var next = await ReserveAsync(request, seg, now, cancellationToken);
                    sb.Append(next.ToString(CultureInfo.InvariantCulture).PadLeft(seg.Width, seg.PadChar));
                    break;
                case SegmentType.Checksum:
                    sb.Append(Checksum(sb.ToString(), seg.Algorithm!));
                    break;
            }
        }
        return sb.ToString();
    }

    private static string ResolveField(CodeAllocationRequest request, CodeRuleSegment seg)
    {
        var value = request.Fields is not null && request.Fields.TryGetValue(seg.Source!, out var v) ? v : null;
        if (string.IsNullOrWhiteSpace(value))
        {
            if (seg.Required) throw new KnownException($"Code rule '{request.Rule.RuleKey}' requires field '{seg.Source}'.");
            value = string.Empty;
        }
        value = seg.Transform switch
        {
            FieldTransform.Upper => value!.ToUpperInvariant(),
            FieldTransform.Lower => value!.ToLowerInvariant(),
            _ => value!,
        };
        if (seg.MaxLength is { } max && value.Length > max) value = value[..max];
        return value;
    }

    private static string Checksum(string prefix, string algorithm)
    {
        var sum = prefix.Where(char.IsDigit).Select(c => c - '0').Sum();
        var mod = algorithm == "hash-mod11" ? 11 : 10;
        return (sum % mod % 10).ToString(CultureInfo.InvariantCulture);
    }

    private static string ResetKey(ResetPeriod reset, DateTimeOffset now) => reset switch
    {
        ResetPeriod.Day => now.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
        ResetPeriod.Month => now.ToString("yyyyMM", CultureInfo.InvariantCulture),
        ResetPeriod.Year => now.ToString("yyyy", CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    private async Task<long> ReserveAsync(CodeAllocationRequest request, CodeRuleSegment seg, DateTimeOffset now, CancellationToken ct)
    {
        var scope = new CodeCounterScope(request.OrganizationId, request.EnvironmentId, request.Rule.RuleKey,
            request.SiteCode, ResetKey(seg.Reset, now), seg.Start);
        if (_store is null) return ReserveInMemory(scope);
        for (var attempt = 1; ; attempt++)
        {
            try { return await _store!.ReserveNextCounterValueAsync(scope, ct); }
            catch (CodeConcurrencyException) when (attempt < _options.MaxConcurrencyAttempts)
            { await Task.Delay(_options.RetryBackoff(attempt), ct); }
        }
    }

    private long ReserveInMemory(CodeCounterScope scope)
    {
        var key = Key(scope.OrganizationId, scope.EnvironmentId, scope.RuleKey, scope.SiteCode, scope.ResetKey);
        lock (_lock)
        {
            _counters.TryGetValue(key, out var current);
            var next = current < scope.Start - 1 ? scope.Start : current + 1;
            _counters[key] = next;
            return next;
        }
    }

    private CodeAllocation AllocateInMemory(CodeAllocationRequest request, string? requested, string? idemKey)
    {
        lock (_lock)
        {
            if (idemKey is not null && _idempotency.TryGetValue(
                Key(request.OrganizationId, request.EnvironmentId, request.Rule.RuleKey, idemKey), out var rec))
            {
                if (!string.Equals(rec.PayloadFingerprint, request.PayloadFingerprint, StringComparison.Ordinal))
                    throw new KnownException($"Idempotency key '{idemKey}' conflicts with a different {request.ConflictResourceLabel} create payload.");
                return new CodeAllocation(rec.Code, true);
            }

            var code = requested ?? BuildCodeAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            if (idemKey is not null)
                _idempotency[Key(request.OrganizationId, request.EnvironmentId, request.Rule.RuleKey, idemKey)] =
                    new CodeIdempotencyKey(request.OrganizationId, request.EnvironmentId, request.Rule.RuleKey,
                        idemKey, code, request.PayloadFingerprint, _timeProvider.GetUtcNow());
            return new CodeAllocation(code, false);
        }
    }

    private static string Key(params string[] parts) => string.Join('|', parts.Select(p => p.Trim().ToLowerInvariant()));
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
```

> 注：`ResolveField` 中第一行多余的 `request.Fields is not null && ...;` 语句不要写——只保留 `var value = ... ? v : null;` 起的逻辑。（已在下方实现版去除；若复制到此处请删掉那一行无效语句。）

- [ ] **Step 5: 跑绿**

Run: `dotnet test backend/tests/Nerv.IIP.Coding.Tests/Nerv.IIP.Coding.Tests.csproj`
Expected: All passed（6+ tests）。

- [ ] **Step 6: Commit**

```bash
git add backend/common/Coding backend/tests/Nerv.IIP.Coding.Tests backend/Nerv.IIP.sln
git commit -m "feat(coding): add CodeAllocator with segment evaluation, concurrency retry and idempotency"
```

### Task 5：`EfCoreCodeStore.cs`（移植 `EfCoreNumberingStore`）

**Files:**
- Create: `backend/common/Coding/Nerv.IIP.Coding/EfCoreCodeStore.cs`

- [ ] **Step 1: 移植实现（`NumberingCounter`→`CodeCounter`、scope 字段改名、`Advance()`→`AdvanceFrom(scope.Start)`、异常类型→`CodeConcurrencyException`）**

读 `backend/common/Numbering/Nerv.IIP.Numbering/EfCoreNumberingStore.cs` 作为母本，逐一替换：
- 类型：`NumberingDbContextLease`→`CodeDbContextLease`、`EfCoreNumberingStore`→`EfCoreCodeStore`、`INumberingStore`→`ICodeStore`、`NumberingCounter`→`CodeCounter`、`NumberingIdempotencyKey`→`CodeIdempotencyKey`、`NumberingConcurrencyException`→`CodeConcurrencyException`、`NumberingCounterScope`→`CodeCounterScope`。
- scope 比较字段：`DocumentType`→`RuleKey`、`DateSegment`→`ResetKey`。
- 计数推进：`counter.Advance()` → `counter.AdvanceFrom(scope.Start)`。
- 新建 counter 构造：`new CodeCounter(scope.OrganizationId, scope.EnvironmentId, scope.RuleKey, scope.SiteCode, scope.ResetKey)`。
- 幂等查询字段同改名。

- [ ] **Step 2: 编译 + Commit**

Run: `dotnet build backend/common/Coding/Nerv.IIP.Coding/Nerv.IIP.Coding.csproj` → succeeded
```bash
git add backend/common/Coding/Nerv.IIP.Coding/EfCoreCodeStore.cs
git commit -m "feat(coding): add EfCoreCodeStore"
```

---

## Phase 2：标准规则目录 + 分层守卫

> 契约工程与 `CodeRuleDefinition` 已在 Task 1/2 落地（纯契约层）。本阶段补 `StandardCodeRules` 目录，并用测试钉死「契约层不依赖引擎」的分层约束。

### Task 6：分层守卫测试 + 序列化往返

**Files:**
- Create: `backend/tests/Nerv.IIP.Coding.Tests/ContractsLayeringTests.cs`

- [ ] **Step 1: 写守卫测试（断言 Contracts.Coding 不引用引擎 + 定义可 JSON 往返）**

```csharp
using System.Reflection;
using System.Text.Json;
using Nerv.IIP.Contracts.Coding;
using Xunit;

namespace Nerv.IIP.Coding.Tests;

public class ContractsLayeringTests
{
    [Fact]
    public void Contracts_assembly_does_not_reference_engine()
    {
        var contracts = typeof(CodeRuleDefinition).Assembly;
        Assert.DoesNotContain(
            contracts.GetReferencedAssemblies(),
            a => a.Name == "Nerv.IIP.Coding" || a.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void CodeRuleDefinition_round_trips_through_json()
    {
        var rule = new CodeRuleDefinition
        {
            RuleKey = "sku", DisplayName = "SKU",
            Segments = [CodeRuleSegment.ConstantOf("SKU"), CodeRuleSegment.SequenceOf(6, ResetPeriod.Day)],
        };
        var json = JsonSerializer.Serialize(rule);
        var back = JsonSerializer.Deserialize<CodeRuleDefinition>(json)!;
        back.Validate();
        Assert.Equal(rule.RuleKey, back.RuleKey);
        Assert.Equal(rule.Segments.Count, back.Segments.Count);
    }
}
```

- [ ] **Step 2: 跑测试**

Run: `dotnet test backend/tests/Nerv.IIP.Coding.Tests/Nerv.IIP.Coding.Tests.csproj --filter ContractsLayeringTests`
Expected: passed（若守卫红，说明误把 EF/引擎引用进了契约层，需移除）。

- [ ] **Step 3: Commit**

```bash
git add backend/tests/Nerv.IIP.Coding.Tests/ContractsLayeringTests.cs
git commit -m "test(coding): guard Contracts.Coding has no engine/EF dependency"
```

### Task 7：`StandardCodeRules`（spec §7 全表，配置即代码）

**Files:**
- Create: `backend/common/Contracts/Nerv.IIP.Contracts.Coding/StandardCodeRules.cs`
- Create/extend: `backend/tests/Nerv.IIP.Coding.Tests/StandardCodeRulesTests.cs`

- [ ] **Step 1: 写失败测试（每条规则 Validate 通过、RuleKey 唯一、能产出示例编码）**

```csharp
using Nerv.IIP.Contracts.Coding;
using Xunit;

namespace Nerv.IIP.Coding.Tests;

public class StandardCodeRulesTests
{
    [Fact]
    public void All_rules_are_valid_and_unique()
    {
        var rules = StandardCodeRules.All;
        Assert.NotEmpty(rules);
        foreach (var r in rules) r.Validate();
        Assert.Equal(rules.Count, rules.Select(r => r.RuleKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("sku")]
    [InlineData("work-order")]
    [InlineData("material")]
    [InlineData("unit-of-measure")]
    [InlineData("business-partner")]
    public void Known_rule_keys_present(string ruleKey)
        => Assert.Contains(StandardCodeRules.All, r => r.RuleKey == ruleKey);
}
```

- [ ] **Step 2: 跑红** → `StandardCodeRules` 未定义。

- [ ] **Step 3: 实现 `StandardCodeRules`（按 spec §7.1 + §7.2 逐条；下示范，补全全表）**

```csharp
using static Nerv.IIP.Contracts.Coding.CodeRuleSegment;

namespace Nerv.IIP.Contracts.Coding;

public static class StandardCodeRules
{
    public static IReadOnlyList<CodeRuleDefinition> All { get; } =
    [
        // §7.1 文档类
        Rule("sku", "SKU 编码", ConstantOf("SKU"), ConstantOf("-"), DateOf("yyyyMMdd"), ConstantOf("-"), SequenceOf(6, ResetPeriod.Day)),
        Rule("work-order", "工单编号", ConstantOf("WO"), ConstantOf("-"), DateOf("yyyyMMdd"), ConstantOf("-"), SequenceOf(6, ResetPeriod.Day)),
        // ProductEngineering / DemandPlanning / ERP 文档：沿用各自现有 prefix（见各服务现状），形如 prefix-date-seq
        // §7.2 主数据类
        Rule("material", "物料编码", FieldOf("materialType", FieldTransform.Upper, maxLength: 3), ConstantOf("-"), SequenceOf(5)),
        Rule("unit-of-measure", "计量单位编码", ConstantOf("UOM"), ConstantOf("-"), SequenceOf(4)),
        Rule("site", "站点编码", ConstantOf("ST"), SequenceOf(3)),
        Rule("workshop", "车间编码", ConstantOf("WS"), SequenceOf(3)),
        Rule("production-line", "产线编码", ConstantOf("PL"), SequenceOf(3)),
        Rule("shift", "班次编码", ConstantOf("SH"), SequenceOf(2)),
        Rule("work-center", "工作中心编码", ConstantOf("WC"), SequenceOf(4)),
        Rule("device-asset", "设备资产编码", ConstantOf("EQ"), SequenceOf(5)),
        Rule("business-partner", "业务伙伴编码", FieldOf("partnerType", FieldTransform.Upper, maxLength: 4), ConstantOf("-"), SequenceOf(5)),
        Rule("team-member", "人员编码", ConstantOf("EMP"), SequenceOf(5)),
        // ... 补齐 MasterData 全部其它资源类型（见 Task 12 资源清单），每个一行
    ];

    public static CodeRuleDefinition Get(string ruleKey) =>
        All.FirstOrDefault(r => r.RuleKey == ruleKey)
        ?? throw new KeyNotFoundException($"Unknown code ruleKey '{ruleKey}'.");

    private static CodeRuleDefinition Rule(string key, string name, params CodeRuleSegment[] segments) =>
        new() { RuleKey = key, DisplayName = name, AppliesTo = name, Segments = segments };
}
```

> 文档类 prefix 对齐：实现时先 grep 各服务现有 `NumberingService` 调用点的 `prefix` 实参（如 MES `"WO"`、SKU `"SKU"`），ERP/PE/DemandPlanning 的各 documentType→prefix 逐一登记为对应规则，确保切换后编号前缀不变。

- [ ] **Step 4: 跑绿 + Commit**

Run: `dotnet test backend/tests/Nerv.IIP.Coding.Tests/Nerv.IIP.Coding.Tests.csproj` → passed
```bash
git add backend/common/Contracts/Nerv.IIP.Contracts.Coding backend/tests/Nerv.IIP.Coding.Tests
git commit -m "feat(coding): add StandardCodeRules catalog"
```

---

## Phase 3：逐服务切换（MES → ERP → DemandPlanning → ProductEngineering → MasterData/SKU）

> 每个服务一组任务，模式相同。下用 **MES 作为完整范本（Task 8）**；其余服务（Task 9–11）给出精确替换参数表与文件清单，逐项照范本执行。每个服务切换后单独 build + focused test + commit。

### Task 8：MES 切换（完整范本）

**Files:**
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Coding/CodeEntityTypeConfigurations.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/WorkOrders/MesCodingService.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Program.cs:45`
- Modify: 调用点 `.../Commands/WorkOrders/CreateRushWorkOrderCommand.cs`、`.../Commands/Production/MesProductionCommands.cs`、`.../Commands/Workbench/MesWorkbenchCommands.cs`（把 `MesNumberingService`→`MesCodingService`，`AllocateWorkOrderIdAsync` 等→新签名）
- Delete: `.../Infrastructure/Numbering/NumberingEntityTypeConfigurations.cs`、`.../Commands/WorkOrders/MesNumberingService.cs`
- Modify: `.../Nerv.IIP.Business.Mes.Infrastructure.csproj`（移除 `Nerv.IIP.Numbering` ProjectReference，新增 `Nerv.IIP.Coding`）；`.../Nerv.IIP.Business.Mes.Web.csproj` 同步（若直接引用过 Numbering）
- Migration: **追加一条**新 migration `AddCodingTables`，由模型 diff 自动 drop `numbering_*` / create `code_*`。**绝不**对历史 migration 执行 `migrations remove`（见 Step 6 说明）

- [ ] **Step 1: 写 EF 配置（移植 `NumberingEntityTypeConfigurations.cs`，表/列/索引改名）**

母本：`backend/services/Business/MasterData/.../Infrastructure/Numbering/NumberingEntityTypeConfigurations.cs`（结构一致）。改名映射：表 `numbering_counters`→`code_counters`、`numbering_idempotency_keys`→`code_idempotency_keys`；列 `document_type`→`rule_key`、`date_segment`→`reset_key`、`number`→`code`；唯一索引名 `ux_numbering_counters_scope`→`ux_code_counters_scope`、`ux_numbering_idempotency_keys_scope`→`ux_code_idempotency_keys_scope`；实体类型 `NumberingCounter`→`CodeCounter`、`NumberingIdempotencyKey`→`CodeIdempotencyKey`。保留所有 `HasComment`（按 schema convention 必须有表/列注释），措辞改为 code 语义。

- [ ] **Step 2: 写 `MesCodingService`（替换 `MesNumberingService`，改用 `CodeAllocator` + `StandardCodeRules`）**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Coding;
using Nerv.IIP.Contracts.Coding;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;

public sealed record MesCodeAllocation(string Code, bool IsIdempotentReplay);

public sealed class MesCodingService
{
    private readonly CodeAllocator _allocator;

    public MesCodingService() => _allocator = new CodeAllocator();

    public MesCodingService(ApplicationDbContext dbContext, IServiceScopeFactory serviceScopeFactory)
        => _allocator = new CodeAllocator(new EfCoreCodeStore(
            dbContext, EfCoreCodeStore.CreateDbContextLeaseFactory<ApplicationDbContext>(serviceScopeFactory)));

    public Task<MesCodeAllocation> AllocateWorkOrderIdAsync(
        string organizationId, string environmentId, string? requestedWorkOrderId,
        string? idempotencyKey, string payloadFingerprint, CancellationToken cancellationToken)
        => AllocateAsync("work-order", organizationId, environmentId, fields: null,
            requestedWorkOrderId, idempotencyKey, payloadFingerprint, cancellationToken);

    public async Task<MesCodeAllocation> AllocateAsync(
        string ruleKey, string organizationId, string environmentId, IReadOnlyDictionary<string, string>? fields,
        string? requestedCode, string? idempotencyKey, string payloadFingerprint, CancellationToken cancellationToken)
    {
        var allocation = await _allocator.AllocateAsync(new CodeAllocationRequest(
            organizationId, environmentId, StandardCodeRules.Get(ruleKey), fields,
            requestedCode, idempotencyKey, payloadFingerprint, "MES"), cancellationToken);
        return new MesCodeAllocation(allocation.Code, allocation.IsIdempotentReplay);
    }

    public static string Fingerprint(params object?[] parts) => CodeAllocator.Fingerprint(parts);
}
```

> `EfCoreCodeStore.CreateDbContextLeaseFactory<TDbContext>` 需在 `EfCoreCodeStore` 中保留与现 `EfCoreNumberingStore` 同名静态工厂（Task 5 移植时一并保留）。

- [ ] **Step 3: 改 DI 注册**

`Program.cs:45` `builder.Services.AddScoped<MesNumberingService>();` → `builder.Services.AddScoped<MesCodingService>();`

- [ ] **Step 4: 改全部调用点**

把 3 个 command 文件中 `MesNumberingService`→`MesCodingService`、`MesNumberAllocation`→`MesCodeAllocation`、`.AllocateWorkOrderIdAsync(...)` 签名不变可直接换；其它 `AllocateAsync(org, env, documentType, prefix, ...)` 旧签名 → 新签名 `AllocateAsync(ruleKey, org, env, fields, requestedCode, idem, fp, ct)`（prefix 不再传，ruleKey 决定规则）。

- [ ] **Step 5: 删旧文件**

```bash
git rm backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Numbering/NumberingEntityTypeConfigurations.cs
git rm backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/WorkOrders/MesNumberingService.cs
```

- [ ] **Step 6: 追加迁移（不要用 `migrations remove`）**

> ⚠️ **代码事实（已核实）**：`AddNumberingCounters` 不是 MES 迁移链的最新一条——其后还有 `AddMesMaterialSupplyFacts`、`AddMesDispatchAssignmentFacts`、`AddMesDemandPlanningSourcePlanReference`、`AddMesQualityAndShiftHandoverFacts`、`AddMesConsumerInboxIdempotency`。`dotnet ef migrations remove` 只会删**最新一条**（`AddMesConsumerInboxIdempotency`），**不会**删中间的 `AddNumberingCounters`，因此严禁用 `migrations remove` 去"撤回"编号表。
>
> 正确做法：Step 1/5 已把 `NumberingEntityTypeConfigurations` 删除、`Nerv.IIP.Numbering` ProjectReference 移除、`CodeEntityTypeConfigurations` 加入。此时模型里不再有 `NumberingCounter/NumberingIdempotencyKey`、新增了 `CodeCounter/CodeIdempotencyKey`。直接**追加一条新 migration**，EF 会按模型 diff 自动生成 `DropTable numbering_*` + `CreateTable code_*`，历史 migration 全部保持不变。

```bash
dotnet tool restore
# 设 PostgreSQL profile 环境变量（见 readiness 环境前置 11），然后只 add，不 remove：
dotnet tool run dotnet-ef migrations add AddCodingTables --project backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure --startup-project backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web
```
Expected: 新生成的 `*_AddCodingTables` 的 `Up()` 含 `DropTable("numbering_counters")`、`DropTable("numbering_idempotency_keys")` 与 `CreateTable("code_counters")`、`CreateTable("code_idempotency_keys")`；`Down()` 反向；**已存在的历史 migration 文件无任何改动**（`git status` 仅新增 1 个 migration + 更新 1 个 ModelSnapshot）。

- [ ] **Step 7: build + focused test**

Run: `dotnet build backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web` 然后 `pwsh scripts/verify-business-mes-execution-mvp.ps1`
Expected: 通过（schema convention test 现校验 `code_*`）。

- [ ] **Step 8: Commit**

```bash
git add -A backend/services/Business/Mes
git commit -m "refactor(coding): switch MES to Nerv.IIP.Coding engine"
```

### Task 9：ERP 切换

照 Task 8 范本。**精确参数：**
- 封装文件：`.../Erp.Web/Application/Commands/ErpNumberingService.cs` → `ErpCodingService.cs`（`"ERP"` 作 ConflictResourceLabel）。
- 调用点：`.../Commands/Procurement/ErpProcurementCommands.cs`、`.../Commands/Sales/ErpSalesCommands.cs`、`.../Commands/Finance/ErpFinanceCommands.cs`。
- 旧 `AllocateAsync(org, env, documentType, prefix, ...)`：每个 documentType 在 `StandardCodeRules` 注册对应 ruleKey（procurement/sales/finance 各单据），prefix 保持现值。
- EF 配置：`.../Erp.Infrastructure/Numbering/` → `Coding/`，表改名同范本。
- Migration：同范本，只 `migrations add AddCodingTables`（Erp.Infrastructure / Erp.Web），由模型 diff 自动 drop `numbering_*`/create `code_*`；**不要** `migrations remove`（ERP 同样在 `AddNumberingCounters` 之后另有迁移）。
- 验证：`scripts/verify-business-erp-procurement-sales-finance-mvp.ps1`。
- Commit: `refactor(coding): switch ERP to Nerv.IIP.Coding engine`。

### Task 10：DemandPlanning 切换

- 封装：`.../DemandPlanning.Web/Application/Commands/DemandPlanningNumberingService.cs` → `DemandPlanningCodingService.cs`。
- 调用点：`.../Commands/CreateOrUpdateDemandSourceCommand.cs`。ruleKey `demand-source`，prefix 保持现值。
- EF 配置/Migration/验证脚本（`scripts/verify-business-demand-planning-mrp-mvp.ps1`）同范本。
- Commit: `refactor(coding): switch DemandPlanning to Nerv.IIP.Coding engine`。

### Task 11：ProductEngineering 切换

- 封装：`.../ProductEngineering.Web/Application/Commands/ProductEngineeringNumberingService.cs` → `ProductEngineeringCodingService.cs`。
- 调用点：`.../Commands/ProductEngineeringReleaseCommands.cs`（release documents/items/BOM/routing/ECO 各 documentType → 各 ruleKey，prefix 保持现值）。
- EF 配置/Migration/验证脚本（`scripts/verify-business-product-engineering-mvp.ps1`）同范本。
- Commit: `refactor(coding): switch ProductEngineering to Nerv.IIP.Coding engine`。

---

## Phase 4：MasterData 全资源去手填

### Task 12：MasterData SKU 切换 + 全资源自动编码

**Files:**
- Create: `.../MasterData.Web/Application/Commands/MasterData/MasterDataCodingService.cs`（替换 `MasterDataNumberingService.cs`）
- Modify: `.../Commands/MasterData/CreateMasterDataCommands.cs`（SKU 用 `MasterDataCodingService.AllocateAsync("sku", ...)`；**其余资源 create handler 从「读用户 Code」改「引擎生成」**）
- Modify: `.../MasterData.Web/Program.cs`（DI：`MasterDataNumberingService`→`MasterDataCodingService`）
- Create: `.../MasterData.Infrastructure/Coding/CodeEntityTypeConfigurations.cs`；Delete `Numbering/` 目录
- Migration: `AddCodingTables`（含 SKU 已有 counter 表的改名重建）

- [ ] **Step 1: 列全 MasterData 资源类型清单**

grep `CreateMasterDataCommands.cs` 与同目录 create commands，列出全部资源 create handler（UOM、UOM conversion、Site、Workshop、ProductionLine、Shift、WorkCenter、DeviceAsset、BusinessPartner、TeamMember、Calendar 等）。对每个标注：`自动生成`（默认）或 `保持受控不生成`（系统枚举字典码、UoM conversion 这类无独立业务 code 的关系对象）。把要自动生成的逐一确认其 ruleKey 已在 `StandardCodeRules`（Task 7）登记；缺的补登记 + 在 `StandardCodeRulesTests` 增断言。

- [ ] **Step 2: 写 `MasterDataCodingService`**（同 Task 8 范本，ConflictResourceLabel 按资源传，如 `"sku"`/`"unit-of-measure"`）。SKU 入口保留 `AllocateSkuCodeAsync` 等价方法（内部 `AllocateAsync("sku", ...)`）。

- [ ] **Step 3: 改各资源 create handler**

每个纳管资源：command 去掉用户 `Code` 入参（或忽略并改为可选 requestedCode 受控通道），handler 在创建聚合前调用 `MasterDataCodingService.AllocateAsync("<ruleKey>", org, env, fields, requestedCode: null, idempotencyKey, fingerprint, ct)`，用返回 code 建聚合。`fields` 仅在 `field` 段规则（material/business-partner）时传相应字段（materialType/partnerType）。

- [ ] **Step 4: EF 配置 + 删 Numbering + 迁移 + 验证**（同范本，脚本 `scripts/verify-business-master-data-realignment.ps1`）。

- [ ] **Step 5: Commit**

```bash
git add -A backend/services/Business/MasterData
git commit -m "feat(coding): auto-generate codes for all MasterData resources via coding engine"
```

### Task 13：MasterData `code_rules` 主数据表 + seed

**Files:**
- Create: `.../MasterData.Domain/AggregatesModel/CodeRuleAggregate/CodeRule.cs`（聚合，`CodeRuleId : IGuidStronglyTypedId`，承载 spec §5.1 + 多租户 + 段 JSON）
- Create: `.../MasterData.Infrastructure/Coding/CodeRuleEntityTypeConfiguration.cs`（表 `code_rules`，段存 JSON 列，全列 `HasComment`）
- Create: `.../MasterData.Web/Application/Seed/CodeRuleSeed.cs`（把 `StandardCodeRules.All` 物化为 `code_rules` 行，幂等 upsert）
- Modify: MasterData seed 入口注册 `CodeRuleSeed`；migration `AddCodeRules`

- [ ] **Step 1: 聚合 + 配置 + seed**（段用 `CodeRuleDefinition` 序列化为 JSON 存储/读取；seed 与 `StandardCodeRules` 对齐，重复运行不产生重复行）。
- [ ] **Step 2: schema convention test** 覆盖 `code_rules`（表/列注释、string ID、migrations history）。
- [ ] **Step 3: 迁移 + 验证 + Commit**

```bash
git commit -m "feat(coding): add code_rules master-data table and seed from StandardCodeRules"
```

---

## Phase 5：前端联动（去手填编码框）

### Task 14：Gateway facade / OpenAPI / api-client / Console 表单

**Files:**
- Modify: `backend/gateway/BusinessGateway/.../Endpoints/MasterData/BusinessConsoleMasterDataEndpoints.cs` 与 `Application/BusinessServices/BusinessConsoleModels.cs`（各资源创建模型移除必填 `code`，或保留可选受控）
- Regenerate: OpenAPI 快照 + `@nerv-iip/api-client`
- Modify: `frontend/apps/business-console/src/pages/master-data/*.vue` 各资源创建表单移除手填编码输入；提交后展示后端回填的 code

- [ ] **Step 1: 后端 facade 模型移除手填 code**（参考 SKU 现有「系统生成」已落地模式 #188；对齐 §52 readiness「移除普通用户手工系统编号输入」）。
- [ ] **Step 2: 重新导出 OpenAPI + 生成 api-client**

Run（按项目现有脚本）：导出 PlatformGateway/BusinessGateway OpenAPI 快照 → `pnpm -C frontend ...` 生成 business-console exports。
- [ ] **Step 3: 改各 Vue 创建表单**：删除编码输入字段与校验；创建成功后用响应 code 回显。逐资源页一个 commit 或合并一个 commit。
- [ ] **Step 4: 前端门禁**

Run: `pnpm -C frontend ...`（typecheck + test + build，或 `/frontend-gate`）。
- [ ] **Step 5: Commit**

```bash
git commit -m "feat(coding): remove manual code inputs from MasterData console forms"
```

---

## Phase 6：删除 `Nerv.IIP.Numbering` 与收口

### Task 15：删除旧引擎，零残留

- [ ] **Step 1: 删目录与 sln 项**

```bash
git rm -r backend/common/Numbering backend/tests/Nerv.IIP.Numbering.Tests
dotnet sln backend/Nerv.IIP.sln remove backend/common/Numbering/Nerv.IIP.Numbering/Nerv.IIP.Numbering.csproj
dotnet sln backend/Nerv.IIP.sln remove backend/tests/Nerv.IIP.Numbering.Tests/Nerv.IIP.Numbering.Tests.csproj
```
并手动删除 sln 中残留的 `Numbering` solution folder 条目（line ~410）与孤立 GUID 映射。

- [ ] **Step 2: 全局零残留检查**

Run: `git grep -n "Numbering" -- backend ':!*.md'`
Expected: 无任何 `Nerv.IIP.Numbering` / `NumberingService` / `numbering_` 命中（仅文档/历史 plan 可保留）。逐条清除残留 `using`/`ProjectReference`。

- [ ] **Step 3: 全量 build**

Run: `dotnet build backend/Nerv.IIP.sln`
Expected: Build succeeded, 0 warning（`TreatWarningsAsErrors`）。

- [ ] **Step 4: Commit**

```bash
git commit -m "chore(coding): remove legacy Nerv.IIP.Numbering"
```

### Task 16：验证脚本 + 文档收口

**Files:**
- Create: `scripts/verify-coding-rule-engine.ps1`
- Modify: `docs/architecture/implementation-readiness.md`（新增 Coding 引擎条目，更新 #188 Numbering 描述为「已被编码规则引擎替换」）
- Modify: schema catalog（各服务 `code_*` 表、MasterData `code_rules`）；如涉权限改动则 authorization matrix

- [ ] **Step 1: 写聚合验证脚本**（遵守 ADR 0010：声明分类/副作用/日志/清理/helper 使用；聚合 `dotnet build` + `Nerv.IIP.Coding.Tests` + 受影响服务 focused verify + api-client 生成 + Business Console focused gate）。
- [ ] **Step 2: 跑脚本**

Run: `pwsh scripts/verify-coding-rule-engine.ps1`
Expected: 全绿。
- [ ] **Step 3: 更新 readiness / schema catalog**（按现有文体追加条目，引用本 spec/plan 与新表）。
- [ ] **Step 4: Commit**

```bash
git add scripts/verify-coding-rule-engine.ps1 docs
git commit -m "chore(coding): add verify script and update readiness/schema catalog"
```

---

## Self-Review（计划对 spec 覆盖核对）

- spec §4 架构（分层/归属/数据流，含 `Coding → Contracts.Coding` 单向依赖）→ Task 1（双工程方向）、2（契约模型）、3–5（引擎）、6（分层守卫）、7、13 ✓
- spec §5 数据模型（CodeRuleDefinition/段/表）→ Task 2（契约模型）、3（EF 实体）、8(EF 配置)、13(code_rules) ✓
- spec §6 段类型（含 checksum 预留、无隐式分隔符、强制 sequence）→ Task 2(Validate)、4(求值) ✓
- spec §7 标准规则种子（§7.1 文档 + §7.2 全主数据）→ Task 7、12(补全) ✓
- spec §8 治理（requestedCode 保留、去手填）→ Task 4(requested)、12、14 ✓
- spec §9 错误/并发/幂等 → Task 4、5 ✓
- spec §10 测试与验证 → Task 4、6（分层守卫）、7、8(schema test)、16(脚本) ✓
- spec §2.1 删除 Numbering、5 服务切换、MasterData 全资源、前端联动 → Task 8–12、14、15 ✓
- spec §2.2 后置项 → 未排任务（正确，明确后置）✓

**一致性核对：** 类型/方法签名跨任务一致——规则模型唯一类型 `CodeRuleDefinition`（契约层，Task 2）；`CodeAllocator.AllocateAsync(CodeAllocationRequest, ct)`、`CodeAllocationRequest(org, env, CodeRuleDefinition Rule, Fields, RequestedCode, IdempotencyKey, PayloadFingerprint, ConflictResourceLabel, SiteCode)`（Task 4）、`ICodeStore.ReserveNextCounterValueAsync(CodeCounterScope, ct)`、`CodeCounterScope(...Start)`、`CodeCounter.AdvanceFrom(start)`、`EfCoreCodeStore.CreateDbContextLeaseFactory<T>`、`StandardCodeRules.Get/All`（返回 `CodeRuleDefinition`，Task 7）、`<Svc>CodingService.AllocateAsync(ruleKey, org, env, fields, requestedCode, idem, fp, ct)` 在 Task 3/4/5/7/8 一致使用。依赖方向 `Coding → Contracts.Coding` 单向（Task 1 建、Task 6 守卫）。

**已按 PR #385 review 修正（代码事实已核实）：**
1. 契约分层：`Contracts.Coding` 为纯模型层（无 EF/引擎依赖），引擎单向引用并消费 `CodeRuleDefinition`，不设平行运行时类型；Task 6 加分层守卫测试钉死。
2. 迁移策略：各服务**只追加** `AddCodingTables`（模型 diff drop `numbering_*`/create `code_*`），**禁止** `migrations remove`（`AddNumberingCounters` 非各服务最新迁移）；历史 migration 文件不删。
3. 范围口径：是 **5 个 Numbering 接入服务**（MasterData/MES/ERP/DemandPlanning/ProductEngineering）；BarcodeLabel 未引用 Numbering、不在本轮。

**已知需实现期补全（非占位漏洞，均有明确指引）：**
- Task 7/9/10/11 文档类规则的 prefix/ruleKey 需 grep 各服务现状逐一登记（已给方法）。
- Task 12 Step 1 需据 MasterData 实际资源枚举补全 `StandardCodeRules` 与「不生成」清单（已给判定准则）。
