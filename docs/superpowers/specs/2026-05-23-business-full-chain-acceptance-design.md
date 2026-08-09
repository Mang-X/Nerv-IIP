# 业务全链验收设计

## 背景

业务平台当前已经具备 MasterData、ProductEngineering、Inventory、Quality、MES、DemandPlanning、BarcodeLabel、BusinessApproval、WMS、IndustrialTelemetry 和 Maintenance 的代码。ERP 是尚未完成的业务服务。只有在 ERP #137、#138 和 #139 通过其最终验证脚本后，才应启动全链验收 #77。

本设计根据当前代码事实和 issue 状态更新此前的 2026-05-20 全链计划。

## 目标

1. 在各个业务服务之外创建一个验收测试项目。
2. 通过公开 HTTP API 和集成事件可见事实验证七条关键业务链。
3. 主要断言使用已授权客户端和服务级契约，而不是读取服务数据库。
4. 提供统一入口 `scripts/verify-business-full-chain-acceptance.ps1`。
5. 在失败信息中记录足够的单据 ID 和事件名，使跨服务缺陷可诊断。

## 非目标

1. 不在验收项目中实现缺失的服务领域行为。
2. 主要断言不得直接读取服务表。
3. 不新增可视化 Gantt 或排程 UI。
4. 不使用生产对象存储、外部 PLC/DCS/SCADA 或真实 WCS 硬件。
5. 除非验证运行显式选择 `Messaging:Provider=RabbitMQ`，否则不要求使用 RabbitMQ。

## 前置条件

1. `scripts/verify-business-wave1-foundation.ps1`
2. `scripts/verify-business-wave2-execution.ps1`
3. `scripts/verify-business-equipment-reliability.ps1`
4. `scripts/verify-business-erp-procurement-sales-finance-mvp.ps1`
5. `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore`

## 验收前审查

在编写七条业务链测试之前，检查以下审计发现，并修复或记录它们：

1. WMS Inventory 过账必须达到足以验收的真实性。空操作的库存移动客户端可用于服务本地测试，但不能用于证明采购到付款或订单到收款流程。
2. WMS 公开事件契约应当可在 WMS 外部消费。如果事件仍然仅限 Web 层，验收工具必须使用公开 HTTP 结果，而不得假装存在共享契约。
3. MES 可能需要为工单、工序任务、生产报工、排程和成品入库申请事实提供公开查询 endpoint。
4. MasterData、ProductEngineering 和 Quality endpoint 的授权应当符合仓库对内部服务 API 的规则。
5. 默认本地 profile 的 CAP/outbox 投递可以继续使用 InMemory，但事件断言的编写方式必须支持日后添加 RabbitMQ profile，而无需改变领域预期。

## 验收工具

测试项目位于：

```text
backend/tests/Nerv.IIP.Business.Acceptance.Tests/
```

验收工具应当：

1. 在现有 WebApplicationFactory 模式可用时，通过该模式启动服务测试宿主。
2. 使用与各服务现有测试一致的隔离测试数据库或内存运行配置。
3. 通过现有测试辅助工具预置 IAM/内部服务授权。
4. 为每个服务公开强类型或最小化的 `HttpClient` 包装器。
5. 通过公开事件转换器、测试总线钩子或可见的服务 API 结果捕获集成事件。
6. 在测试之间重置状态，但不得访问另一个服务的生产数据库。

## 业务链

| 业务链 | 必需断言 |
| --- | --- |
| 工程到制造 | 已发布的 ProductionVersion 引用 MBOM 和 Routing；MES 工单引用 ProductionVersion 和已发布路线事实。 |
| 计划到采购/生产 | MRP 创建计划采购和计划工单建议；ERP 和 MES 接受这些建议并返回下游单据 ID；DemandPlanning 以幂等方式将建议标记为已接受。 |
| 采购到库存到应付 | ERP 收货、Quality 检验、WMS 入库完成和 Inventory 移动产生数量与金额匹配的 AP 候选。 |
| 订单到交付到应收 | ERP 销售订单和交付单、WMS 出库完成及 Inventory 移动产生已发货数量与金额匹配的 AR 候选。 |
| 生产执行到成本 | MES 工序报工和成品入库申请流经 WMS/Inventory，并产生 ERP 成本候选。 |
| 设备到维护到产能 | IndustrialTelemetry 告警创建 Maintenance 工单；Maintenance 资产不可用/已恢复事件对 MES 排程约束可见。 |
| WMS 到 WCS 适配器 | WMS 下发 WCS 任务、记录失败诊断、重试并完成任务；仓库作业完成前不得执行 Inventory 移动过账。 |

## 测试规则

1. 测试使用公开 API 和已记录的集成事件契约。
2. 断言应当优先使用稳定 ID、状态、数量、事件名和下游引用。
3. 仅允许在服务自身测试已经使用的服务本地测试夹具（fixture）中读取数据库，并且不得将这种读取作为跨服务验收证明。
4. 测试失败信息应当包含来源单据 ID、下游单据 ID、事件名和业务链名称，以便直接采取行动。
5. 测试应当分组，使局部验收可按业务链运行，而完整验证脚本运行全部测试。

## Issue 映射

| Issue | 作用 |
| --- | --- |
| #77 | 全链验收 epic 和最终门禁。 |
| #76, #137, #138, #139 | 财务与商业业务链的 ERP 前置条件。 |
| #75, #136 | 仓储业务链的 WMS 前置条件。 |
| #74, #135 | 制造及生产到成本业务链的 MES 前置条件。 |
| #129, #130 | 设备到维护业务链的设备可靠性前置条件。 |

## 验收

1. `backend/tests/Nerv.IIP.Business.Acceptance.Tests` 位于 `backend/Nerv.IIP.sln` 中。
2. 七条业务链中的每一条都至少有一个聚焦测试。
3. `scripts/verify-business-full-chain-acceptance.ps1` 运行所有前置验证和验收测试项目。
4. 实施就绪文档和 README 指向新的验证脚本。
5. 只有在验证脚本于目标本地 profile 中通过后，才能关闭 #77。
