# 可配置编码规则引擎（Coding Rule Engine）设计

> 状态：草案 v1（2026-06-12）。本文件是「编码规则引擎」能力的**设计真相**。
> 引擎核心库 `Nerv.IIP.Coding` 将**完全替换并删除**现有的 `Nerv.IIP.Numbering`。
> 实现交由 codex 按配套实施计划 [`2026-06-12-coding-rule-engine.md`](../plans/2026-06-12-coding-rule-engine.md) 执行。

---

## 1. 背景与目标

### 1.1 现状

平台已有两套与「编码/编号」相关的机制，但都是**规则写死**的：

1. **`Nerv.IIP.Numbering`**（`backend/common/Numbering`，#188 生产级持久化基线）：跨 **5 个业务服务**复用的编号分配引擎，但生成格式硬编码为 `{Prefix}-{yyyyMMdd}-{NNNNNN}`（见 `NumberingServiceCore.NextNumberAsync`），段构成、位宽、重置周期、字段引用都不可配置。`csproj` 引用 `Nerv.IIP.Numbering` 的服务为 MasterData(SKU)、MES、ERP、DemandPlanning、ProductEngineering（已核实，BarcodeLabel 不引用 Numbering）。
2. **`BarcodeRule`**（BarcodeLabel 服务）：条码值规则，`prefix/length/checksum/barcodeType` 等配置字段落库，但拼接逻辑 `{Prefix}{token}{sequence:D4}` 仍写死，且仅服务于条码场景。

此外，MasterData 的多数基础资源（UOM、Site、Workshop、ProductionLine、Shift、WorkCenter、DeviceAsset、BusinessPartner、TeamMember 等）的业务 `code` 目前仍由用户**手工输入**，只有 SKU 走了 `Numbering` 自动分配。

### 1.2 目标

建设一个**可配置的编码规则引擎**，用规则模板（段化）描述编码构成，替换现有写死实现，并把平台所有需要业务编码的对象统一纳入「自动生成、不手动输入」：

1. 新建 `Nerv.IIP.Coding` 能力库，用**有序段（segment）**配置驱动编码生成。
2. **删除** `Nerv.IIP.Numbering`，5 个 Numbering 接入服务全部切换到新引擎。
3. **集中定义、service-local 计数**：编码规则作为一类主数据集中定义（配置即代码 + MasterData 可查表），但运行时的流水计数与幂等保持各服务本地，无分布式运行时依赖。
4. **全覆盖基础数据自动编码**：MasterData 全部资源类型的 `code` 改为引擎自动生成，去掉前端手填编码框。
5. 保留现有引擎已验证的并发（乐观锁+重试）与幂等（idempotency key + payload fingerprint）能力。

### 1.3 前置事实

- **开发阶段、无生产数据**：数据库可重建、全量重新 seed，无历史编号迁移包袱。已落库的开发期编号可随 seed 重生成。
- 现有架构：各业务服务为独立微服务、独立 PostgreSQL schema、**不跨 schema FK**，服务间通过集成事件 / Gateway facade 通信。新引擎必须延续 service-local 运行时，不得引入「每次创建都远程要号」的耦合。
- 命名遵循 `Nerv.IIP.<Capability>` 体系；契约遵循 `Nerv.IIP.Contracts.<X>`。

---

## 2. 范围

### 2.1 本轮交付（In Scope，后端 MVP）

- `Nerv.IIP.Coding` 引擎核心库（规则模型、段类型、分配算法、并发、幂等、EF 存储）。
- `Nerv.IIP.Contracts.Coding` 公共契约（规则定义 DTO + 标准规则常量）。
- `backend/tests/Nerv.IIP.Coding.Tests` 引擎单测（替换 `Nerv.IIP.Numbering.Tests`）。
- 删除 `Nerv.IIP.Numbering` 及各服务 `numbering_*` 运行时表 / EF 模型 / 封装服务 / 测试，**历史 migration 文件保留**，并追加 `AddCodingTables` 使最终 runtime schema 不含 `numbering_*`、改用 `code_*`（迁移策略见 §5.2 注与实施计划 Task 8 Step 6，禁止 `migrations remove`）。
- **5 个现有 Numbering 接入服务**切换：MasterData、MES、ERP、DemandPlanning、ProductEngineering。
- BarcodeLabel **不在本轮切换范围**：它自带 `BarcodeRule`、未引用 `Nerv.IIP.Numbering`；其编号位是否并入本引擎仅做评估（见 §2.2 后置）。
- **MasterData 全资源类型** `code` 改自动生成，连带 BusinessGateway facade / OpenAPI / `@nerv-iip/api-client` / Business Console 表单移除手填编码输入。
- 标准编码规则 seed（各文档类型 + 全主数据），MasterData 落 `code_rules` 主数据表。
- schema convention tests、schema catalog、authorization matrix（如需）、readiness 文档、聚合验证脚本 `scripts/verify-coding-rule-engine.ps1`。

### 2.2 明确后置（Out of Scope，各自单独 issue）

- 前端「编码规则模板」CRUD 管理页（工厂管理员自助配置）。
- 规则变更的运行时热更新与跨服务事件分发（`coding.CodeRuleChanged`）。
- 跨服务规则集中查看 facade（统一只读聚合）。
- 高级段类型：自定义校验位算法库、外部序列源、按客户/项目维度的复杂派生段。
- `BarcodeRule` 条码值规则是否合并进本引擎（本轮仅做接口评估，不强制合并）。

---

## 3. 术语与核心概念

| 术语 | 含义 |
|---|---|
| **CodeRuleDefinition（编码规则定义）** | 一个对象/文档类型的编码模板，由 `ruleKey` 标识，含有序 `segments`、作用域维度、状态、版本。纯契约 DTO（`Nerv.IIP.Contracts.Coding`），是规则模型的唯一形状；集中定义的一类主数据。MasterData 侧另有同名领域聚合 `CodeRule`（`code_rules` 表）承载它的持久化与未来编辑。 |
| **CodeRuleSegment（段）** | 编码的一个组成部分（契约 DTO）。类型见 §6：`constant` / `date` / `sequence` / `field` / `checksum`。 |
| **ruleKey** | 规则的稳定业务键（kebab-case），如 `sku`、`material`、`work-order`、`uom`、`purchase-order`。各服务用它向引擎请求编码。 |
| **CodeCounter** | service-local 流水计数器，scope = org/env/ruleKey/site/resetKey。 |
| **resetKey** | 由 `sequence` 段的重置周期派生的计数器分桶键（如 `20260612`、`202606`、`2026`、空串=永不重置）。 |
| **CodeAllocator** | 引擎核心：给定规则与上下文，产出最终编码。 |
| **CodeIdempotencyKey** | service-local 幂等记录：把创建请求的 idempotency key 绑定到首次分配结果。 |
| **分配上下文（AllocationContext）** | 调用方传入的字段值字典，供 `field` 段取值（如 `materialType=raw-material`）。 |

---

## 4. 架构

### 4.1 库结构与落点

> **分层约束（依赖方向，按 review 修正）**：`Nerv.IIP.Contracts.Coding` 是**纯规则模型契约层**（枚举 + 段 DTO + 规则定义 DTO + 标准规则目录 + 定义期校验），**不依赖任何实现库**（沿用现有 `Contracts.*` 只承载可序列化公共契约的约定——见 readiness §共享契约落点；已核实 `Contracts.Inventory/Scheduling/Wms` 均无 EF/实现依赖）。引擎库 `Nerv.IIP.Coding` **单向引用** `Nerv.IIP.Contracts.Coding`，分配核心**直接消费 `CodeRuleDefinition`**——不再设平行的运行时 `CodeRule` 类型（避免与契约 DTO 形状重复，DRY）。**依赖方向严格单向：`Coding` → `Contracts.Coding`**，契约层绝不反向依赖引擎，避免下游/未来 SDK 只想消费规则定义却被迫引入 EF Core 等实现依赖。

```
backend/common/Contracts/Nerv.IIP.Contracts.Coding/       # 纯规则模型契约层（无 EF/实现依赖）
  CodeRuleEnums.cs          # SegmentType, ResetPeriod, ScopeDimension, FieldTransform 枚举
  CodeRuleSegment.cs        # 段 DTO（可序列化 record + 工厂方法）
  CodeRuleDefinition.cs     # 规则定义 DTO（可序列化、可跨服务共享）+ 定义期 Validate()（结构校验，抛 ArgumentException）
  StandardCodeRules.cs      # 标准规则目录（配置即代码，唯一事实源；All/Get 返回 CodeRuleDefinition）

backend/common/Coding/Nerv.IIP.Coding/                    # 引擎核心（替换 Numbering，引用 Contracts.Coding）
  CodeAllocator.cs          # 分配核心（消费 CodeRuleDefinition；替换 NumberingServiceCore；运行期缺值等抛 KnownException）
  CodeEntities.cs           # CodeCounter, CodeIdempotencyKey（EF 实体）
  ICodeStore.cs             # 存储抽象（替换 INumberingStore）
  EfCoreCodeStore.cs        # EF 实现（替换 EfCoreNumberingStore）
  CodeAllocatorOptions.cs   # 并发重试选项

backend/tests/Nerv.IIP.Coding.Tests/                      # 引擎单测（引用 Coding + Contracts.Coding）
```

各服务侧（与现有 Numbering 接入对称）：

```
services/Business/<Svc>/.../Infrastructure/Coding/CodeEntityTypeConfigurations.cs   # code_counters / code_idempotency_keys EF 配置
services/Business/<Svc>/.../Web/Application/Commands/<Svc>CodingService.cs           # 薄封装（替换 XxxNumberingService）
```

MasterData 额外拥有规则定义主数据表：

```
services/Business/MasterData/.../Domain/AggregatesModel/CodeRuleAggregate/CodeRule.cs   # code_rules 聚合
services/Business/MasterData/.../Web/Application/Seed/  # 标准规则 seed（与 StandardCodeRules 对齐）
```

### 4.2 归属与运行时模型（集中定义 + service-local 计数）

- **唯一事实源 = `Nerv.IIP.Contracts.Coding.StandardCodeRules`**（配置即代码）。所有标准规则定义在此声明，各服务引用该契约获取本服务需要的规则定义。
- **MasterData 集中可查**：MasterData seed 时把全部标准规则物化到 `code_rules` 主数据表，作为集中查看 + 未来可编辑入口（编辑能力后置）。MasterData 自身需要的规则（SKU/资源等）直接读这张表或读 `StandardCodeRules`。
- **运行时不远程要号**：每个服务用 `StandardCodeRules` 中本服务相关规则 + 本服务 `code_counters` / `code_idempotency_keys` 表本地完成分配。服务间不因编码产生运行时调用或跨 schema 依赖。
- **一致性约束**：`StandardCodeRules`（代码真相）↔ MasterData `code_rules` seed（运行真相）↔ 本文件（设计真相）三处必须一致；改任一处需同步另两处。沿用数据字典规则同款治理约定。

### 4.3 数据流（一次编码分配）

```
Command Handler（如 CreateSkuCommandHandler）
  └─ 构造 AllocationRequest{ ruleKey, scope(org/env/site), fields{...}, requestedCode?, idempotencyKey?, fingerprint }
       └─ <Svc>CodingService.AllocateAsync
            └─ CodeAllocator.AllocateAsync
                 ├─ 若 idempotencyKey 命中 → 校验 fingerprint 一致 → 返回首次编码（IsIdempotentReplay=true）
                 ├─ 若 requestedCode 提供 → 直接采用（受治理：是否允许见 §8）
                 └─ 否则按 ruleKey 取规则 → 逐段求值：
                      constant → 字面量
                      date     → 当前 UTC 日期按 format 格式化
                      sequence → 由 resetKey 定位 CodeCounter，ICodeStore.ReserveNext（乐观锁+重试）
                      field    → 从 fields 取值 + transform（大写/CodeSet code/截断）
                      checksum → 对已拼接前缀计算校验位
                    拼接 → 最终编码
                 └─ 写 CodeIdempotencyKey（若提供 key）
```

---

## 5. 数据模型

### 5.1 CodeRuleDefinition / CodeRuleSegment（规则模型，纯契约 DTO）

> 规则模型只有一份：契约层的 `CodeRuleDefinition`（纯数据 record）。它落 `code_rules` JSON 列、跨服务共享、`StandardCodeRules` 返回此类型，引擎分配核心也直接消费它（不再有平行的运行时类型）。结构校验 `Validate()` 在契约层（抛 `ArgumentException`，定义期错误）；运行期错误（缺 `field` 值、规则 inactive 等）由引擎分配核心抛 `KnownException`。下表字段即 `CodeRuleDefinition` 形状。

```
CodeRuleDefinition
  ruleKey      : string             # 稳定业务键，kebab-case，唯一
  displayName  : string             # 中文展示名（如「物料编码」）
  appliesTo    : string             # 适用对象/文档类型描述
  scope        : ScopeDimension     # [Flags] 计数作用域：Organization|Environment|Site（默认 org+env；site 可选）
  segments     : CodeRuleSegment[]  # 有序
  isActive     : bool               # 启用状态（inactive 规则运行期请求抛 KnownException）
  version      : int                # 规则版本（用于未来变更追踪）

CodeRuleSegment
  type     : SegmentType   # constant | date | sequence | field | checksum
  # 各类型专有参数见 §6（用可空字段承载；序列化为 code_rules 的 JSON 列）
```

### 5.2 持久化表（service-local，替换 numbering_*）

**`code_counters`**（替换 `numbering_counters`）：

| 列 | 说明 |
|---|---|
| id | 代理主键 |
| organization_id / environment_id | 作用域 |
| rule_key | 规则键（替换原 document_type） |
| site_code | 站点作用域，空串=组织内全局 |
| reset_key | 重置分桶键（替换原 date_segment，永不重置时为空串） |
| current_value | 当前已分配最大流水 |
| version | 乐观并发令牌 |
| 唯一索引 | `(organization_id, environment_id, rule_key, site_code, reset_key)` |

**`code_idempotency_keys`**（替换 `numbering_idempotency_keys`）：列与现有 `numbering_idempotency_keys` 同构，`document_type` 改名 `rule_key`，`number` 改名 `code`。

**`code_rules`**（仅 MasterData，新增主数据表）：承载 §5.1 规则定义 + 多租户 `(organization_id, environment_id)` + 审计字段；段以 JSON 列存储（带列注释，遵守 schema convention）。

> 注（迁移策略，与实施计划一致）：各服务**只追加一条替换 migration**（`AddCodingTables`），由 EF 模型 diff 自动 `DropTable numbering_*` + `CreateTable code_*`。**历史 `AddNumberingCounters` 等 migration 文件一律保留、不删除**——因为 `AddNumberingCounters` 在各服务并非最新 migration，`dotnet ef migrations remove` 只会删最新一条而删错目标。最终 runtime schema 不含 `numbering_*`。详见实施计划 Task 8 Step 6。

---

## 6. 段类型规范

所有段按 `segments` 顺序拼接，**段之间无隐式分隔符**——分隔符用 `constant` 段显式表达，避免歧义。

| 类型 | 参数 | 行为 | 示例 |
|---|---|---|---|
| `constant` | `value` | 固定字面量（前缀、分隔符、固定码段） | `SKU`、`-` |
| `date` | `format`（`yyyyMMdd`/`yyMMdd`/`yyyyMM`/`yyMM`/`yyyy` 等白名单） | 取分配时刻 UTC 日期格式化 | `20260612` |
| `sequence` | `width`、`start`(默认1)、`padChar`(默认`0`)、`reset`(`none`/`day`/`month`/`year`) | 由 `reset` 派生 resetKey 定位计数器并保留下一个值，按 width 左补零 | `000001` |
| `field` | `source`（上下文字段名）、`transform`(`none`/`upper`/`lower`)、`maxLength`(可选)、`required`(默认true) | 从 AllocationContext.fields 取值（值本身已是 kebab-case code，如 material-type 的 code），按 transform/截断处理 | `raw-material`→`RAW`（配合 maxLength/upper） |
| `checksum` | `algorithm`(`hash-mod10`/`hash-mod11`) | 对当前已拼接字符串计算 hash 取模占位校验位追加（**本轮预留实现，默认规则不启用；不是 Luhn/ISBN 等行业标准校验位**） | `7` |

**重置周期与计数器作用域**：`sequence.reset` 决定 `code_counters.reset_key`：
- `none` → `""`（跨期累计，永不重置）
- `day` → `yyyyMMdd`
- `month` → `yyyyMM`
- `year` → `yyyy`

一条规则**至少含一个 `sequence` 段**以保证唯一性（校验规则定义时强制）。`field` 段缺值且 `required=true` 时分配失败并抛业务异常（`KnownException`）。

---

## 7. 标准编码规则种子清单

> 下表是**初版标准规则**，作为 `StandardCodeRules` 与 MasterData seed 的依据。`code` 段命名、位宽、重置周期在实现评审时可微调，但需三处同步。`field` 段引用的 CodeSet 见 [`master-data-dictionary-rules.md`](../../architecture/master-data-dictionary-rules.md)。

### 7.1 现有文档类型（保持兼容形态，迁移到新引擎）

| ruleKey | 段构成 | 示例 |
|---|---|---|
| `sku` | `const(SKU)` `const(-)` `date(yyyyMMdd)` `const(-)` `seq(6,day)` | `SKU-20260612-000001` |
| `work-order` | `const(WO)` `const(-)` `date(yyyyMMdd)` `const(-)` `seq(6,day)` | `WO-20260612-000001` |
| ProductEngineering release / DemandPlanning demand-source / ERP procurement·sales·finance 各单据 | 沿用各自现有 prefix，段构成同上式（`const(prefix)-date-seq`） | 如 `PO-20260612-000001` |

### 7.2 主数据全覆盖（新增，改自动生成）

> 这些此前手填的资源 `code` 全部改引擎生成。多数采用「分类码段 + 流水」，少数纯流水。

| ruleKey | 对象 | 段构成（初版） | 示例 |
|---|---|---|---|
| `material` | 物料/SKU（如与 `sku` 合并见实现评审） | `field(materialType→upper,maxLen=3)` `const(-)` `seq(5,none)` | `RAW-00001` |
| `unit-of-measure` | 计量单位 | `const(UOM)` `const(-)` `seq(4,none)` | `UOM-0001` |
| `site` | 站点/工厂 | `const(ST)` `seq(3,none)` | `ST001` |
| `workshop` | 车间 | `const(WS)` `seq(3,none)` | `WS001` |
| `production-line` | 产线 | `const(PL)` `seq(3,none)` | `PL001` |
| `shift` | 班次 | `const(SH)` `seq(2,none)` | `SH01` |
| `work-center` | 工作中心 | `const(WC)` `seq(4,none)` | `WC0001` |
| `device-asset` | 设备资产 | `const(EQ)` `seq(5,none)` | `EQ00001` |
| `business-partner` | 业务伙伴 | `field(partnerType→upper,maxLen=4)` `const(-)` `seq(5,none)` | `CUST-00001` |
| `team-member` | 人员 | `const(EMP)` `seq(5,none)` | `EMP00001` |
| `reference-data-code` | 字典码（仅工厂自定义 CodeSet，系统枚举不生成） | 评审确定（多数字典 code 是受控语义，**默认不自动生成**） | — |

> 主数据具体类型清单以 MasterData 现有资源枚举为准（实施计划会逐一列全并标注「自动生成 / 保持受控不生成」）。系统枚举类 CodeSet 的 code 是语义键，**不纳入**自动生成。

---

## 8. 治理：requestedCode 与手填编码

- **默认全自动**：所有纳管对象创建时不接受用户手填系统编码；前端表单移除编码输入框，由后端引擎生成。
- **requestedCode 通道保留但收紧**：引擎仍支持 `requestedCode`（用于数据导入/迁移/系统间对接等受控场景），但常规 Console 创建路径不暴露该入参。是否允许某 ruleKey 走 requestedCode 由实施计划按对象界定。
- **唯一性**：引擎只保证同一计数器作用域内流水唯一；最终 `code` 的业务唯一性仍由各聚合既有唯一约束兜底（如 SKU code 唯一索引），二者叠加。

---

## 9. 错误处理、并发与幂等

- **并发**：`sequence` 段经 `EfCoreCodeStore.ReserveNextCounterValueAsync`，乐观并发令牌 `version` + `DbUpdateConcurrencyException` → `CodeConcurrencyException` → 有限次退避重试（沿用 `NumberingServiceCore` 现有策略与默认参数）。
- **幂等**：提供 idempotency key 时，命中既有记录则校验 payload fingerprint 一致后回放首次 `code`（`IsIdempotentReplay=true`）；fingerprint 不一致抛 `KnownException`（冲突）。
- **规则错误**：未知 `ruleKey`、规则 `inactive`、缺 `sequence` 段、`field` required 缺值、`date.format` 不在白名单 → 抛 `KnownException`（明确文案）。
- **In-memory 模式**：保留无 store 的纯内存分配路径（供领域单测，等价现有 `NumberingServiceCore` 内存模式）。

---

## 10. 测试与验证

- `Nerv.IIP.Coding.Tests`：段求值（各类型）、拼接、重置周期分桶、并发保留、幂等回放/冲突、规则校验失败、in-memory 与 EF 两种 store。覆盖并替换原 `NumberingServiceCoreTests`。
- 各服务 **schema convention tests** 更新到 `code_counters` / `code_idempotency_keys`（+ MasterData `code_rules`），含表/列注释、string ID 约束、migrations history。
- MasterData：全资源 create 端点「自动生成 code」契约测试；移除手填后 API contract / operationId 稳定性测试同步。
- `scripts/verify-coding-rule-engine.ps1`：聚合 build + 引擎单测 + 受影响服务 focused tests + api-client 生成 + Business Console focused gate。声明分类/副作用/日志/清理，遵守脚本治理 ADR 0010。
- 文档：`docs/architecture/implementation-readiness.md`、schema catalog、（如涉及权限）authorization matrix 同步。

---

## 11. 风险与权衡

| 风险 | 应对 |
|---|---|
| 删除 `Nerv.IIP.Numbering` 牵动 5 服务，回归面大 | 开发阶段无数据包袱；实施计划按服务分批切换 + 每批 focused gate；引擎核心先独立测试通过再接入。 |
| 全主数据「去手填」连带前端表单/Gateway/codegen 改动多 | 后端引擎与接入先行；前端按 MasterData 资源逐类移除手填框，复用现有 SKU 已落地的「系统生成」模式。 |
| 配置即代码 vs 运行时可配置的张力 | 本轮锁定 seed/配置即代码；管理界面与事件热更新明确后置，规则模型预留 `version` 与 DTO 形状以便平滑演进。 |
| `field` 段引用 CodeSet 值，跨主数据耦合 | `field` 仅消费调用方上下文传入的字段值（已是 code），引擎不反查别的 schema；映射/截断在引擎内纯函数完成。 |
| 段拼接歧义（无分隔符易撞码） | 分隔符强制用 `constant` 段显式表达；规则定义校验强制至少一个 `sequence` 段。 |

---

## 12. 实施阶段划分（供 codex，细节见实施计划）

1. **引擎核心**：`Nerv.IIP.Coding` + `Nerv.IIP.Contracts.Coding` + `Nerv.IIP.Coding.Tests`，独立通过。
2. **契约与标准规则**：`StandardCodeRules` 落 §7 全表 + 校验。
3. **逐服务切换**：MES → ERP → DemandPlanning → ProductEngineering → MasterData(SKU)，每个替换封装 + `code_*` 表 + schema tests，删除对应 `numbering_*`。
4. **MasterData 全资源去手填**：各资源 create 接引擎 + `code_rules` 主数据表 + seed；移除手填，更新契约测试。
5. **前端联动**：BusinessGateway facade / OpenAPI / api-client / Business Console 表单移除手填编码框。
6. **删除 `Nerv.IIP.Numbering`** 及残留引用，全局搜索零残留。
7. **验证收口**：`scripts/verify-coding-rule-engine.ps1` + readiness / schema catalog / solution 引用更新。

---

## 附：相关文件

- 被替换引擎：`backend/common/Numbering/Nerv.IIP.Numbering`、`backend/tests/Nerv.IIP.Numbering.Tests`
- 现有接入封装：`services/Business/*/Web/Application/Commands/*NumberingService.cs`
- 数据字典规则（`field` 段 CodeSet 来源）：[`master-data-dictionary-rules.md`](../../architecture/master-data-dictionary-rules.md)
- 实施就绪清单：[`implementation-readiness.md`](../../architecture/implementation-readiness.md)（#188 Numbering 基线）
- 脚本治理：ADR 0010、`docs/architecture/script-automation-governance.md`
