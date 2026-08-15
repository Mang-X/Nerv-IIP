# 业务第一波代理会话设计

## 背景

业务 issue 路线图已重新组织：epic 保持宽泛，执行工作在子 issue 中开展。第一波是这次整理后的首个并行开发波次，目的是在不增加共享文件合并压力的前提下，为下游计划、仓储、ERP 和全链路工作解除阻塞。

本设计涵盖首批五个执行会话：

1. #127 补齐 ProductEngineering 缺口。
2. #131 Inventory MVP。
3. #132 Quality 检验 MVP。
4. #135 MES CleanDDD 持久化。
5. #140 业务服务注册、验证脚本模式与就绪状态跟踪。

## 来源事实

截至 2026-05-23：

1. BusinessMasterData 是 Layer 0 参考来源，已有 Domain、Infrastructure、Web、migration、测试、重对齐 API 和验证脚本。
2. ProductEngineering 已有 Domain、Infrastructure、Web、migration 和测试，但当前范围主要是 ProductionVersion。
3. Quality 已有 Domain、Infrastructure、Web、migration 和测试，但当前范围主要是 NonconformanceReport。
4. MES 仅有 Web 项目和 Web 测试，包含内存态排程、急单和重排程行为。
5. Inventory 尚无服务目录。
6. 业务服务尚未在平台 AppHost 中注册。
7. 业务专用验证目前只有 `scripts/verify-business-master-data-realignment.ps1`。

## 目标

1. 为每个第一波代理提供一份自包含的实施计划。
2. 使 ProductEngineering 和 Inventory API 足够稳定，以支持 DemandPlanning、WMS 和 ERP 的后续会话。
3. 扩展 Quality，同时不得导致既有 NCR 行为回归。
4. 在保持当前 endpoint 行为的同时，将 MES 从内存态 Web 状态迁移到 CleanDDD Domain 和 Infrastructure。
5. 将共享集成修改集中在 #140，使各实施会话能够并行运行，并降低合并冲突风险。

## 非目标

1. 第一波不得启动 DemandPlanning #128。
2. ProductEngineering 和 Inventory 契约稳定前，不得启动 WMS #136 或 ERP #137 至 #139。
3. 首批文档不得实施 BarcodeLabel #133 或 BusinessApproval #134。
4. 不得纳入 Gantt/RFC #78。
5. 不得将业务规则放入 PlatformGateway、IAM、AppHub 或 Ops。

## 会话边界

| 会话 | Issue | 负责范围 | 不得负责 |
| --- | --- | --- | --- |
| PE-GAP | #127 | ProductEngineering 工程文档、物料项、EBOM、MBOM、工艺路线、ECO/ECN 和发布事件。 | Inventory、MES 工单、MRP 计算、FileStorage 内部实现。 |
| INV-MVP | #131 | Inventory 库位、台账、移动、可用量和盘点。 | WMS 执行、ERP 计价、MES 物料发料执行、跨 schema 外键。 |
| QI-MVP | #132 | Quality 检验计划和检验记录，以及检验结果事件。 | Inventory 变更、WMS 任务状态、ERP 采购收货状态、MES 工序状态。 |
| MES-PERSIST | #135 | MES Domain/Infrastructure 持久化，以及持久的工单、排程和报工事实。 | ProductEngineering 版本编制、Inventory 余额、WMS 入库执行。 |
| BIZ-INTEG | #140 | 共享 solution/AppHost 注册、验证脚本模式、就绪状态和文档更新。 | 各服务会话负责的 Domain 功能范围。 |

## 共享文件策略

除非计划另有明确说明，实施会话应避免修改共享文件。共享文件包括：

1. `backend/Nerv.IIP.sln`
2. `infra/aspire/Nerv.IIP.AppHost/Program.cs`
3. `docs/architecture/authorization-matrix.md`
4. `docs/architecture/database-schema-catalog.md`
5. `docs/architecture/implementation-readiness.md`
6. `README.md`
7. `scripts/verify-business-*.ps1`

服务会话需要共享修改时，应在其 PR 摘要的 `Shared Changes Needed` 下记录所请求新增内容的精确说明。服务工作已合并或已具备集成条件后，由 #140 会话负责应用这些新增内容。

## 合并门禁

每个服务会话必须提供：

1. 针对聚合不变量的聚焦 Domain 测试。
2. 针对 FastEndpoints 路由、授权预期、请求验证和稳定 operation ID 的聚焦 Web 测试。
3. 持久化服务的 PostgreSQL migration 和 schema 约定测试。
4. 服务发布事件时的集成事件转换器测试。
5. 需要在 IAM seed 和 `authorization-matrix.md` 中登记的权限清单。
6. 供 #140 使用的 AppHost 服务注册事实清单。

#140 会话必须提供：

1. 所有已合并第一波服务项目的共享 solution 条目。
2. 对 Web 项目可编译的服务进行 AppHost 注册。
3. 使用 `scripts/lib/ScriptAutomation.ps1` helper 的根级验证脚本。
4. 在验证命令明确后更新就绪状态文档。

## 依赖规则

1. #127 和 #131 是最高优先级会话，因为 #128、#136 和 #137 依赖其契约。
2. #132 可以独立运行，因为它扩展既有 Quality NCR 范围，并且只发出结果。
3. #135 可以独立运行，前提是保持当前 MES API 行为，并在真实集成可用前将 ProductEngineering/Inventory 引用作为 ID 使用。
4. #140 应在至少一个服务会话已有就绪分支后启动，但可以立即准备验证模式。

## 验收

第一波文档在满足以下条件时完成：

1. #127、#131、#132、#135 和 #140 各自都有专用会话计划。
2. Inventory 和 Quality 检验有明确规格，因为它们定义了新的领域事实。
3. ProductEngineering 和 MES 的增量计划以当前代码事实为起点，而不是沿用旧的从零开始计划。
4. 各计划明确共享文件协调规则和验证命令。
5. `implementation-readiness.md` 将未来代理指向第一波交接文档。
