# ProductEngineering SKU 连续性实施计划

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**让 ProductEngineering 将 EngineeringItem 和 EBOM 编码视为 SKU 编码，并要求非 phantom EBOM 行必须由 MBOM 物料行覆盖，从而补齐 issue #405。

**架构：**保留现有公共字段名（`ItemCode`、`ParentItemCode`、`ChildItemCode`）作为兼容名称，但将其含义冻结为 MasterData SKU 编码。不得添加 ProductEngineering EngineeringItem 到 SKU 的映射表。添加命令处理器校验，使 MBOM 发布不能发布输出 SKU 与所引用 EBOM 父 SKU 不同，或遗漏必需的非 phantom（虚拟）EBOM 子 SKU 的制造 BOM。Phantom EBOM 子项可在 MBOM 中省略或展开，MBOM 也可包含仅用于制造的物料行。

**技术栈：**.NET 10、CleanDDD、FastEndpoints、MediatR、EF Core、xUnit。

## 全局约束

- 不得引入跨 schema 外键，也不得让 ProductEngineering Domain/Application 直接引用 MasterData Infrastructure。
- ProductEngineering 继续拥有 EBOM、MBOM、Routing、ProductionVersion 和修订事实。
- MasterData 继续拥有持久的 SKU/物料身份。
- 使用 TDD：实施前先添加失败测试。

---

### Task 1：ProductEngineering SKU 连续性测试

**文件：**
- 修改：`backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringReleaseApiContractTests.cs`
- 修改：`backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/ProductEngineeringReleaseAggregateTests.cs`

**接口：**
- 消费：现有 `ReleaseManufacturingBomCommandHandler`
- 产出：要求 MBOM 物料行覆盖所引用的非 phantom EBOM 子 SKU 编码，同时允许省略 phantom 项和添加仅用于制造的物料行的测试。

- [x] 为缺失 EBOM 物料行、父 SKU 去除首尾空白、phantom 省略、仅用于制造的 MBOM 新增项和无效物料行输入添加失败测试。
- [x] 更新现有 EBOM/MBOM fixture，使兼容字段名使用 SKU 形式的编码。
- [x] 运行 ProductEngineering Web 测试，并确认新测试因缺少连续性校验而失败。

### Task 2：ProductEngineering 连续性校验

**文件：**
- 修改：`backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductEngineeringReleaseCommands.cs`

**接口：**
- 消费：`EngineeringBom.Lines` 和 `ReleaseManufacturingBomCommand.MaterialLines`
- 产出：当 MBOM 输出 SKU 与 EBOM 父 SKU 不同，或 MBOM 物料行缺少必需的非 phantom EBOM 子 SKU 编码时，产生确定性的 `KnownException` 失败。

- [x] 为 MBOM 发布添加最小发布处理器校验，核对去除首尾空白后的 EBOM 父 SKU，并确保覆盖必需的非 phantom 子 SKU。
- [x] 运行 ProductEngineering Domain 和 Web 测试。

### Task 3：文档

**文件：**
- 修改：`docs/architecture/business-platform-domain-architecture.md`
- 修改：`docs/architecture/api-contract-and-codegen.md`
- 修改：`docs/architecture/implementation-readiness.md`

**接口：**
- 消费：Task 1–2 的代码差异。
- 产出：说明现有 `itemCode` 兼容名称现在表示 SKU 编码的文档。

- [x] 记录兼容语义：EngineeringItem itemCode 与 EBOM 父/子编码均为 SKU 编码。
- [x] 记录 MBOM 发布行连续性校验。
- [x] 更新文档后再次运行聚焦测试。
