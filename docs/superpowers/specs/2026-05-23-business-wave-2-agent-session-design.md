# 业务第二波代理会话设计

## 背景

业务第一波完成了阻塞下一执行波次的基础工作：

1. ProductEngineering 现负责已发布的 EBOM、MBOM、Routing、ProductionVersion 和工程变更事实。
2. Inventory 现负责库位、台账、移动、可用量和盘点调整。
3. Quality 现负责检验计划、检验记录和 NCR 行为。
4. MES 现已具备工单、工序任务、报工、排程和成品收货请求的持久化 CleanDDD Domain/Infrastructure/Web 状态。
5. BusinessMasterData、ProductEngineering、Inventory、Quality 和 MES 已加入 `backend/Nerv.IIP.sln`、Aspire AppHost 和第一波验证脚本。

第二波是首个下游业务执行波次。它应当为计划、仓储执行、条码/扫描工作流和业务审批解除阻塞，同时避免过早启动 ERP、IIoT 或 Maintenance。

## 第二波范围

第二波包含以下执行 issue：

1. #128 DemandPlanning MVP。
2. #133 BarcodeLabel MVP。
3. #134 BusinessApproval MVP。
4. #136 WMS 执行 MVP。

配套的设计系统工作：

1. #143 前端组件缺口收口应由 `frontend/DESIGN` 治理，Superpowers 计划仅作为执行检查清单。

延后事项：

1. #142 FileStorage MinIO/S3 分片对象存储集成仍属于 MVP 后工作，不应阻塞业务服务开发。
2. ERP #137 至 #139 应在 DemandPlanning 建议和 WMS 执行契约稳定后启动。
3. IndustrialTelemetry #129 和 Maintenance #130 应在订单/计划/仓储核心链路具备服务级基线后启动，除非设备维修演示成为优先事项。

## 目标

1. 为每个第二波代理提供自包含的实施交接。
2. 使计划和仓储事实能够通过 ProductEngineering 与 Inventory 得到解释，而不是采用仅依赖 fixture 的捷径。
3. 将 BarcodeLabel 和 BusinessApproval 添加为独立的 Layer 1 服务，使后续 WMS、ERP、MES 和 ProductEngineering 工作能够引用标签、扫描和审批。
4. 将服务本地实施与第二波注册/就绪状态集成分离，以降低共享文件冲突压力。
5. 使前端组件工作与设计系统保持一致，避免将 #143 变成临时拼凑的计划片段。

## 非目标

1. 第二波不得实施 ERP Procurement、Sales 或 Finance。
2. 不得构建 APS、有限产能优化、Gantt 或排程可视化。
3. 不得将 MinIO/S3 分片上传作为业务附件或上传功能的前置条件。
4. 不得让 WMS 负责库存余额，也不得让 DemandPlanning 创建正式采购订单或工单。
5. 不得将 shadcn-vue 内部实现直接导入应用页面。

## 会话边界

| 会话 | Issue | 负责范围 | 不得负责 |
| --- | --- | --- | --- |
| DP-MRP | #128 | DemandSource、MPS、MrpRun、PlanningSuggestion、按日分桶的 MRP、pegging 和计划事件。 | ERP 请购、MES 工单、Inventory 余额、ProductEngineering 版本编制。 |
| BARCODE | #133 | 条码规则、标签模板、打印批次、扫描记录以及幂等的打印/扫描工作流。 | Inventory 数量、WMS 任务状态、FileStorage 对象键、业务单据状态。 |
| APPROVAL | #134 | 审批模板、审批链、审批步骤、审批记录和业务审批事件。 | Ops 操作审批、IAM 角色/权限、审计日志归属。 |
| WMS | #136 | 入库/出库执行、上架/拣货/盘点任务、WCS adapter 任务映射和仓储完成事件。 | 库存余额、采购/销售/工单状态、外部 WCS 内部实现。 |
| WAVE2-INTEG | #77 后续事项 | 服务分支就绪后的共享 solution 条目、AppHost 资源、验证脚本以及 schema/权限/就绪状态文档。 | 各服务会话负责的 Domain 行为。 |
| DS-READY | #143 | DESIGN 文档、shadcn primitive 导出、上传/图表/日期/抽屉/标签页组件契约。 | 业务领域逻辑、MinIO/S3 分片上传、页面专用样式。 |

## 依赖规则

1. #128 现在可以启动，因为 ProductEngineering 发布事实和 Inventory 可用量已经存在。它应先使用由 fixture 支撑的 adapter，再切换到稳定的服务/API 契约。
2. #133 和 #134 没有硬性后端依赖，可以立即并行运行。
3. #136 现在可以启动，因为 Inventory 移动/可用量和 MES 成品收货请求事实已经存在。WMS 应将 Inventory 过账置于内部 client/adapter 之后，使服务本地测试无需另一个服务进程。
4. WAVE2-INTEG 应在至少一个第二波服务能够编译后运行。它只应集成实际存在且聚焦测试通过的服务。
5. DS-READY 可以与后端工作并行运行，但应先更新 `frontend/DESIGN`，并仅在设计契约写成后实施组件。

## 共享文件策略

服务会话应主要在各自目录下写入：

1. `backend/services/Business/DemandPlanning`
2. `backend/services/Business/BarcodeLabel`
3. `backend/services/Business/Approval`
4. `backend/services/Business/Wms`
5. `backend/common/Contracts/Nerv.IIP.Contracts.{Context}` 下的可选公开契约

共享文件应由 WAVE2-INTEG 协调：

1. `backend/Nerv.IIP.sln`
2. `infra/aspire/Nerv.IIP.AppHost/Program.cs`
3. `docs/architecture/authorization-matrix.md`
4. `docs/architecture/database-schema-catalog.md`
5. `docs/architecture/implementation-readiness.md`
6. `scripts/verify-business-*.ps1`

如果服务会话为了在本地运行而必须修改共享文件，应将修改保持在最小范围，并在交接中包含 `Shared Changes Needed` 章节。

## 合并门禁

每个服务会话必须提供：

1. 针对聚合不变量和不可变规则的 Domain 测试。
2. 针对路由形状、授权预期、验证和 operation ID 的 Web/API 契约测试。
3. PostgreSQL 支撑服务的 schema 约定测试。
4. 针对已发布事件的集成事件转换器测试或契约序列化测试。
5. 供 WAVE2-INTEG 使用的就绪权限代码和 schema catalog 条目。
6. 专用验证脚本请求，即使该脚本由 WAVE2-INTEG 创建也必须提供。

WAVE2-INTEG 必须提供：

1. 所有就绪第二波项目的 solution 成员关系。
2. 所有就绪 Web 项目的 AppHost 数据库和服务注册。
3. 各服务验证脚本和 `scripts/verify-business-wave2-execution.ps1`。
4. 显示哪些服务已就绪、已跳过或受阻的就绪状态文档。
5. 确认注册后第一波聚合验证仍通过。

## #142 决策

MinIO/S3 分片上传不会阻塞下一业务波次。当前 FileStorage 基线已提供元数据契约、SDK、PostgreSQL 支撑的元数据和本地 tus endpoint。业务服务应仅存储 `fileId` 或 `FileReference` 值，不应关心字节当前由本地 tus、服务器代理还是 S3 分片上传承载。

只要以下规则持续成立，延后 #142 的风险就较低：

1. 公开契约绝不暴露对象存储键或长期有效的对象 URL。
2. 上传会话将 provider/upload-mode 字段保持为 FileStorage 内部决策。
3. FileUpload UI 只与 FileStorage 上传会话和 tus/download-grant endpoint 交互，绝不直接与 MinIO 交互。
4. 后续 S3 分片上传工作仍作为 Upload Provider adapter，置于 FileStorage 控制的授权之后。

只有当多节点部署、大文件直传、生产对象存储保留或外部 client 直连对象存储成为近期需求时，才启动 #142。

## #143 决策

#143 是设计系统就绪性 issue。规范规格应位于 `frontend/DESIGN`，而不能只放在 `docs/superpowers/plans` 中。

当前立场：

1. 在可行情况下，对 Tabs、Sheet、Popover、Calendar/RangeCalendar 和 Chart 使用 shadcn-vue registry primitive。
2. 将 FileUpload 构建为 Nerv-IIP wrapper，采用 shadcn 视觉结构和 FileStorage 语义。
3. 当需要真实上传进度、重试和暂停/恢复时，可恢复 tus 上传应优先使用 Uppy core/headless 加 `@uppy/tus`。不得将 Uppy Dashboard 的视觉皮肤作为设计基线。
4. 手写 tus client 仅适用于范围狭窄的本地单文件上传路径；如果可恢复性和协议兼容性很重要，它不应成为默认方案。
5. 组件必须通过 `@nerv-iip/ui` 导出，并在应用消费前更新 DESIGN 组件文档。

## 建议的代理顺序

并行启动以下会话：

1. DP-MRP (#128)
2. BARCODE (#133)
3. APPROVAL (#134)
4. WMS (#136)

将以下会话作为配套或后续会话运行：

1. 在前端业务控制台工作即将开始时运行 DS-READY (#143)。
2. 前两个后端服务可编译后运行 WAVE2-INTEG，并在所有就绪服务可用后再次运行。

在 DP-MRP 具备稳定的建议 API/事件且 WMS 具备稳定的入库/出库完成契约之前，不得启动 ERP #137 至 #139。
