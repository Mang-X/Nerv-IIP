# ProductEngineering Issue 408 实施计划

> **供代理执行者使用：**必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**闭合 ProductEngineering issue #408 中有关 ECO 传播、BOM 行语义、Routing 标准工序快照和 ProductionVersion 校验的后端业务缺口。

**架构：**所有规则都保留在 BusinessProductEngineering 内。添加范围收敛的聚合方法和仓储查询；命令处理器编排跨聚合校验，但不产生跨服务数据库耦合。通过 EF migration 持久化新的自有行快照字段，并更新 schema 文档。

**技术栈：**.NET 10、CleanDDD、FastEndpoints、EF Core、xUnit、PostgreSQL migration。

---

### Task 1：ProductEngineering 引用校验测试

**文件：**
- 修改： `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductionVersionApiContractTests.cs`
- 修改： `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Repositories/ProductEngineeringReleaseRepositories.cs`
- 修改： `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductionVersions/CreateProductionVersionCommand.cs`
- 修改： `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductionVersions/UpdateProductionVersionCommand.cs`

- [ ] 为缺失、草稿、SKU 不匹配和尚未生效的 MBOM/Routing 引用添加失败测试。
- [ ] 添加用于解析 `Code:Revision` MBOM/Routing 引用的仓储方法。
- [ ] 更新创建/更新处理器以传递真实状态，并使用 `KnownException` 拒绝无效引用。
- [ ] 运行 ProductEngineering Web 测试。

### Task 2：BOM 与 Routing 快照测试

**文件：**
- 修改： `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/ProductEngineeringReleaseAggregateTests.cs`
- 修改： `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringReleaseApiContractTests.cs`
- 修改： `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringBomAggregate/EngineeringBom.cs`
- 修改： `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/ManufacturingBomAggregate/ManufacturingBom.cs`
- 修改： `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/RoutingAggregate/Routing.cs`
- 修改： `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductEngineeringReleaseCommands.cs`

- [ ] 为 BOM 替代件/虚拟件/引用件/成品率/反冲字段以及 routing 准备/运行/收尾/控制标志添加失败的聚合测试。
- [ ] 添加失败的命令处理器测试，证明 routing 发布会加载已启用的 StandardOperation 默认值。
- [ ] 扩展聚合构造函数和命令记录，同时保留现有请求兼容性。
- [ ] 更新 routing 发布处理器，要求每个工序代码都存在已启用的 StandardOperation。

### Task 3：ECO 传播测试

**文件：**
- 修改： `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringReleaseApiContractTests.cs`
- 修改： `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringBomAggregate/EngineeringBom.cs`
- 修改： `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/ManufacturingBomAggregate/ManufacturingBom.cs`
- 修改： `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/RoutingAggregate/Routing.cs`
- 修改： `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductEngineeringReleaseCommands.cs`

- [ ] 添加失败测试：发布一个影响 EBOM、MBOM、Routing 和 ProductionVersion 的 ECO，然后观察这些版本被归档。
- [ ] 为已发布的 EBOM、MBOM 和 Routing 聚合添加归档/取代方法。
- [ ] 在 ECO 命令处理器中添加受影响版本解析。
- [ ] 校验成功后保留 EngineeringChangeReleased 事件。

### Task 4：持久化、Migration 与文档

**文件：**
- 修改：ProductEngineering EF 实体配置。
- 创建：`Infrastructure/Migrations` 下的 ProductEngineering migration。
- 修改： `docs/architecture/database-schema-catalog.md`
- 修改： `docs/architecture/implementation-readiness.md`

- [ ] 映射所有新的自有行列，包括最大长度、精度和注释。
- [ ] 生成 PostgreSQL migration。
- [ ] 更新 #408 闭环行为的 schema 目录/就绪说明。
- [ ] 运行 schema 约定测试。

### Task 5：验证与交付

**文件：**
- 所有已修改的 ProductEngineering 文件。

- [ ] 为 ProductEngineering Domain 测试运行 `dotnet test`。
- [ ] 为 ProductEngineering Web 测试运行 `dotnet test`。
- [ ] 如果本地前置条件允许，运行 `scripts/verify-business-product-engineering-mvp.ps1`。
- [ ] 提交并推送 `codex/issue-408-product-engineering-gap-v2`，然后创建包含 `Closes #408` 的 PR。
