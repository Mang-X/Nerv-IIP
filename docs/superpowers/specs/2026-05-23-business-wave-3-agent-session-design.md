# 业务第 3 波次代理会话设计

## 背景

第 1 波次、第 2 波次和设备可靠性支线波次已经完成非 ERP 业务服务基线：

1. #127 ProductEngineering 已关闭，该服务拥有 EBOM、MBOM、Routing、ProductionVersion 和 ECO/ECN 事实。
2. #128 DemandPlanning 已关闭，该服务拥有 MPS/MRP 运行、追溯关系和计划建议。
3. #129 IndustrialTelemetry 和 #130 Maintenance 已关闭，并已通过公开集成契约连接。
4. #131、#132、#133、#134、#135 和 #136 已关闭。代码中已经具备 Inventory、Quality 检验、BarcodeLabel、BusinessApproval、MES 持久化和 WMS 执行能力。
5. `docs/architecture/implementation-readiness.md` 记录了端口 5107 至 5117，以及所有已完成业务服务的验证脚本。

此后仅剩的开放业务执行子项是 ERP #137、#138 和 #139。完整链路验收 #77 在 ERP 完成之前仍处于阻塞状态。

## 第 3 波次范围

第 3 波次包括以下执行 Issue：

1. #137 ERP Procurement MVP——请购、RFQ、采购订单和收货。
2. #138 ERP Sales MVP——商机、报价、销售订单和发货请求。
3. #139 ERP Finance MVP——应收、应付、凭证和成本候选项。

第 3 波次的共享集成包括：

1. 将 ERP 服务及测试纳入 `backend/Nerv.IIP.sln`。
2. 在 Aspire AppHost 中为 `business-erp` 注册下一个本地端口；除非端口矩阵在实施前发生变化，否则为 5118。
3. 更新 ERP 的 IAM seed（种子数据）、授权矩阵、数据库 schema 目录和实施就绪状态。
4. 提供 ERP 专用验证脚本，以及最终的 `verify-business-erp-procurement-sales-finance-mvp.ps1`。

第 4 波次在第 3 波次通过后开始，覆盖完整链路验收 #77。

## 目标

1. 在 `backend/services/Business/Erp` 下将 ERP 构建为单个 CleanDDD 业务服务。
2. 使 Procurement、Sales 和 Finance 能够按 Issue 规模的纵切片执行，同时不得假装它们是互不冲突的并行代码流。
3. ERP 仅通过公开 API/事件消费 DemandPlanning 建议、WMS 完成事实、Inventory 移动事实和 MES 生产事实。
4. 保持服务所有权规则：ERP 拥有商务和财务单据，但不拥有 WMS 执行状态或 Inventory 余额。
5. 提供从 ERP 完成到完整链路验收的明确交接。

## 非目标

1. 不得重新打开已经关闭的第 1 波次、第 2 波次或设备可靠性服务 MVP Issue。
2. 本波次不得创建独立的 SRM、CRM、CPQ 或 OMS 服务。
3. 不得实施完整的总账月末结账、税务引擎或法定报告。
4. 不得让 ERP 直接写入 Inventory 库存余额、WMS 仓库任务或 MES 工单。
5. 不得针对仅有 fixture 的 ERP 行为开始完整链路验收。

## 会话边界

| 会话 | Issue | 负责范围 | 不得负责 |
| --- | --- | --- | --- |
| ERP-PROC | #137 | ERP 脚手架、共享 ERP 基础类型、Procurement/SRM-lite 聚合、采购 endpoint、初始 `erp` schema。 | 销售订单生命周期、Finance 记账；服务分支未就绪时不得负责 AppHost 范围的集成。 |
| ERP-SALES | #138 | 商机、报价、销售订单、发货订单、销售 endpoint 和发货释放事件。 | Inventory 分配所有权、WMS 拣货/打包执行、Finance 应收记账内部实现。 |
| ERP-FIN | #139 | 应收账款、应付账款、凭证和成本候选项聚合；凭证平衡护栏；财务事件消费者/转换器。 | 完整总账结账、WMS/Inventory/MES 内部表、税务或银行结算。 |
| ERP-INTEG | #76/#77 后续项 | Solution（解决方案）、AppHost、端口 5118、IAM seed（种子数据）、授权矩阵、schema 目录、就绪状态、README 和 ERP 验证脚本。 | #137 至 #139 负责的领域行为。 |
| FULLCHAIN | #77 | ERP 通过后的验收工具和七项关键链路测试。 | 服务本地领域修复；除非发现阻塞缺陷并将其重新分派给负责人。 |

## 依赖规则

1. ERP-PROC 必须首先运行，因为它会创建服务目录、共享项目结构、DbContext 和基线 migration。
2. ERP-SALES 在 ERP-PROC 编译通过后，或在稳定的脚手架分支可用后开始。
3. ERP-FIN 可以并行设计领域测试，但最终实施应等待采购收货和销售发货事件结构稳定。
4. ERP-INTEG 应在至少一个 ERP 切片编译通过后运行，并且只能在 #137、#138 和 #139 就绪后完成。
5. FULLCHAIN 在 `verify-business-erp-procurement-sales-finance-mvp.ps1` 和所有已完成业务服务的验证脚本通过后开始。

## 共享文件策略

ERP 服务会话主要写入：

1. `backend/services/Business/Erp`
2. 可选的公开契约 `backend/common/Contracts/Nerv.IIP.Contracts.Erp`

共享文件由 ERP-INTEG 协调：

1. `backend/Nerv.IIP.sln`
2. `infra/aspire/Nerv.IIP.AppHost/Program.cs`
3. `docs/architecture/authorization-matrix.md`
4. `docs/architecture/database-schema-catalog.md`
5. `docs/architecture/implementation-readiness.md`
6. `README.md`
7. `scripts/verify-business-erp-*.ps1`

如果某个服务切片为了本地运行而修改共享文件，其最终交接中必须包含 `Shared Changes Needed`（需要的共享变更）章节。

## 合并门禁

每个 ERP 切片都必须提供：

1. 覆盖聚合不变量和生命周期转换的领域测试。
2. 覆盖路由、操作 ID、授权策略和验证规则的 Web/API 契约测试。
3. 覆盖所有已映射 `erp` 表的 schema 约定测试。
4. 已发布事件的集成事件转换器测试或契约序列化测试。
5. 证明任何 ERP 表都不拥有库存余额、仓库执行步骤或 MES 生产任务状态的证据。

## 验收前强化检查

工作代理审计在已完成服务中发现了以下非阻塞风险。ERP 可以开始，但在这些风险得到审查或被明确延后之前，完整链路验收不应宣称最终关闭：

1. WMS 当前将集成事件保存在 Web 本地契约中，并且默认使用可替换的/no-op Inventory 移动客户端。在完整链路验收前，决定是否将 WMS 公开事件提升至 `backend/common/Contracts`，并为验收 profile 接入真实 Inventory adapter。
2. MES 拥有持久化的 Domain/Infrastructure 状态，但其公开查询面比其他服务更薄。完整链路测试可能需要用于读取工单、工序任务、报工和收货请求状态的 API。
3. 一些较早的业务 endpoint 依赖权限检查，却没有显式的内部服务策略。在将 MasterData、ProductEngineering 和 Quality endpoint 用作验收入口之前，验证其授权契约。
4. 在 ERP migration 批次将 ProductEngineering 的设计时 DbContext factory 用作参考模式之前，应根据当前 migration history table 约定检查该 factory。

ERP-INTEG 必须提供：

1. 将就绪的 ERP 项目及测试纳入 solution。
2. 注册 AppHost 数据库和服务。
3. 为 Procurement、Sales 和 Finance 权限添加 IAM seed（种子数据）和授权矩阵条目。
4. 提供 ERP 验证脚本并更新实施就绪状态。
5. 确认第 1 波次、第 2 波次和设备可靠性聚合验证脚本仍然是完整链路验收的前置条件。

## 推荐顺序

1. 启动 ERP-PROC（#137）。
2. 在 ERP 脚手架和通用领域约定具备后启动 ERP-SALES（#138）。
3. 在收货、发货和生产成本输入事件稳定后启动 ERP-FIN（#139）。
4. 服务准备好注册到 AppHost 后立即运行 ERP-INTEG，并在所有 ERP 切片通过后再次运行。
5. 只有在 ERP-INTEG 产出最终 ERP 验证脚本后，才能启动 FULLCHAIN（#77）。
