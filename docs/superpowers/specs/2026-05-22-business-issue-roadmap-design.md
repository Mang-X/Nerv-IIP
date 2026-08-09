# 业务 Issue 路线图设计

> 历史 Issue 清理记录。Issue 拆分仍有参考价值，但 `Source Facts`（来源事实）章节反映的是 2026-05-22 第一波实施前的代码事实。当前服务状态、端口、验证脚本和 Issue 状态以 `docs/architecture/implementation-readiness.md` 为准。

## 背景

当前 business-platform GitHub Issue 混合了旧式 epic 范围、已完成工作、部分完成的基础设施和较新的切片专用 Issue。由于 Issue 列表已不再匹配代码和文档事实，因此难以决定下一步执行什么。

本设计在修改 ADR、架构文档、规格或实施计划之前，先将当前 Issue 整理为 epic 和可执行子 Issue。

## 来源事实

截至 2026-05-22 的代码事实：

1. `backend/services/Business/MasterData` 已存在，包含 Domain、Infrastructure、Web、migration 和测试。
2. `backend/services/Business/ProductEngineering` 已存在，包含 Domain、Infrastructure、Web、migration 和测试，但当前实施范围仅有 ProductionVersion。
3. `backend/services/Business/Quality` 已存在，包含 Domain、Infrastructure、Web、migration 和测试，但当前实施范围仅有 NonconformanceReport。
4. `backend/services/Business/Mes` 仅存在 Web 和 Web 测试，并具有内存式计划与重排行为。
5. `Inventory`、`DemandPlanning`、`Wms`、`Erp`、`IndustrialTelemetry`、`Maintenance`、`BarcodeLabel` 和 `Approval` 服务目录尚不存在。
6. `infra/aspire/Nerv.IIP.AppHost` 尚未注册业务服务。
7. `scripts/verify-business-master-data-realignment.ps1` 已存在；#77 引用的其他业务验证脚本尚不存在。
8. Notification 服务、Gateway notification facade、Console notifications UI、FileStorage contract/SDK、PostgreSQL 元数据和本地 tus 上传/下载 MVP 已经存在。

文档事实：

1. ADR 0012 仍是正确的领域分层决策。
2. ADR 0013 仍是正确的 BusinessMasterData 治理决策。
3. `docs/architecture/implementation-readiness.md` 是当前状态的规范来源，并已记录 BusinessMasterData 重新对齐和 FileStorage MVP 事实。
4. `docs/architecture/business-platform-domain-architecture.md` 正确定义了关键链模型，但尚未把 GitHub Issue 映射到可执行切片。
5. `docs/superpowers/plans/2026-05-20-business-*.md` 下的现有计划是有用输入，但部分计划由于代码在其编写后落地而已经过时。

## 目标

1. 使每个未关闭的非 Gantt Issue 都映射到 epic、可执行子 Issue 或已知未来跟进项之一。
2. 保留有用的历史 Issue，不要过早关闭。
3. 仅关闭范围已被完全取代或已完成的 Issue。
4. 使 GitHub Issue 与 ADR 0012、ADR 0013、实施就绪状态和实际代码事实保持一致。
5. 为后续架构更新、规格和计划准备清晰输入。

## 非目标

1. 本步骤不实施服务代码。
2. 不编辑生成的 API client。
3. 不借 Issue 清理改变业务边界。
4. 不重新打开 #72。
5. 本路线图不包含 #78 Gantt/RFC 工作。

## Issue 处置

| Issue | 操作 | 原因 |
| --- | --- | --- |
| #70 基础设施收尾（一期） | 保持打开，重写为基础设施收尾 epic | Notification/FileStorage/UI 范围已部分完成；正文已经过时。 |
| #71 基础设施收尾（二期） | 保持打开，重写为生产就绪 epic | 范围仍然有效，但需要子 Issue 和当前事实。 |
| #72 共享基础域（Layer 0） | 保持关闭 | BusinessMasterData 重新对齐已足以完成 Layer 0 跟踪；后续工作属于下游 Issue。 |
| #73 通用能力域（Layer 1） | 保持打开，重写为 epic | 它应跟踪 Inventory、Quality 检验、BarcodeLabel 和 BusinessApproval 子 Issue。 |
| #74 MES | 保持打开，重写为 epic | MES 已有部分内存式 Web 实现；CleanDDD 持久化和执行需要子 Issue。 |
| #75 WMS | 保持打开，重写为 epic | 尚无代码；应由一个或多个 WMS 执行子 Issue 使其完成关闭。 |
| #76 ERP | 保持打开，重写为 epic | 范围对单个执行 Issue 而言过大；拆分 Procurement、Sales 和 Finance。 |
| #77 全链路验收 | 保持打开，重写为验收 epic | 在所有业务 MVP 验证脚本通过之前，必须保持阻塞。 |
| #78 Gantt RFC | 排除 | 用户明确要求忽略 Gantt 相关 Issue。 |
| #127 ProductEngineering MVP | 作为执行 Issue 保持打开 | 这是当前用于补全 ProductEngineering 的子 Issue。 |
| #128 DemandPlanning MVP | 作为执行 Issue 保持打开 | 这是当前用于 MPS/MRP 的子 Issue。 |
| #129 IndustrialTelemetry MVP | 作为执行 Issue 保持打开 | 这是当前用于 IIoT/Telemetry 的子 Issue。 |
| #130 Maintenance MVP | 作为执行 Issue 保持打开 | 这是当前用于 CMMS-lite 的子 Issue。 |

## 要创建的新子 Issue

在大规模代码工作开始前创建以下子 Issue：

1. `feat: Inventory MVP - 库存台账、移动、可用量与盘点`
   - 父 Issue：#73
   - 标签：`enhancement`、`business-platform`
   - 依赖：#72 已完成、MasterData resolve/validate API
   - 计划输入：`docs/superpowers/plans/2026-05-20-business-common-capability-foundation.md`

2. `feat: Quality 检验 MVP - 检验计划、检验记录与来料/工序检验`
   - 父 Issue：#73
   - 标签：`enhancement`、`business-platform`、`quality`
   - 依赖：当前 Quality NCR 实现
   - 范围：添加 InspectionPlan 和 InspectionRecord，且不使 NCR 倒退。

3. `feat: BarcodeLabel MVP - 规则、模板、打印批次与扫描`
   - 父 Issue：#73
   - 标签：`enhancement`、`business-platform`
   - 依赖：MasterData SKU/barcode 策略；如需模板引用，还依赖 FileStorage。

4. `feat: BusinessApproval MVP - 模板、审批链与审批记录`
   - 父 Issue：#73
   - 标签：`enhancement`、`business-platform`
   - 依赖：仅依赖 IAM 用户/上下文；不得替代 Ops 审批。

5. `feat: MES CleanDDD 持久化与执行 MVP`
   - 父 Issue：#74
   - 标签：`enhancement`、`business-platform`
   - 依赖：当前 MES Web 测试、ProductEngineering ProductionVersion 契约。
   - 范围：引入 Domain/Infrastructure 和 PostgreSQL schema，再迁移当前内存式 scheduler 行为。

6. `feat: WMS 执行 MVP - 入库、出库、盘点与 WCS adapter 边界`
   - 父 Issue：#75
   - 标签：`enhancement`、`business-platform`
   - 依赖：Inventory movement API。

7. `feat: ERP Procurement MVP - 请购、RFQ、采购订单与收货`
   - 父 Issue：#76
   - 标签：`enhancement`、`business-platform`
   - 依赖：DemandPlanning 计划采购建议、WMS 入库边界。

8. `feat: ERP Sales MVP - 商机、报价、销售订单与交付请求`
   - 父 Issue：#76
   - 标签：`enhancement`、`business-platform`
   - 依赖：WMS 出库边界、Inventory availability query。

9. `feat: ERP Finance MVP - 应收、应付、凭证与成本候选项`
   - 父 Issue：#76
   - 标签：`enhancement`、`business-platform`
   - 依赖：Procurement/Sales/WMS/Inventory 事实。

10. `chore: 业务服务注册、验证脚本模式与就绪状态跟踪`
    - 父 Issue：#77
    - 标签：`enhancement`、`business-platform`
    - 范围：AppHost 注册策略、solution 成员资格检查、验证脚本模板和各业务服务的 readiness 状态更新。

11. `feat: FileStorage tus 强化 - 大小、checksum、过期与协议兼容性`
    - 父 Issue：#70
    - 标签：`enhancement`
    - 依赖：当前 FileStorage 本地 tus MVP。

12. `feat: FileStorage 对象存储集成 - MVP 后的 MinIO/S3 multipart`
    - 父 Issue：#70
    - 标签：`enhancement`
    - 依赖：FileStorage 强化和部署 profile。

13. `feat: 补齐 business console 就绪所需的前端组件缺口`
    - 父 Issue：#70
    - 标签：`enhancement`、`area:frontend`
    - 范围：缺失的 Sheet、Tabs、日期选择器、文件上传和图表 primitive；Table/Dialog/Select/Pagination 已经存在。

## Epic 重写模板

### #70 替换正文

```markdown
## 当前事实

本 Issue 现在是基础设施收尾 epic，而不是从零开始的实施任务。

已经存在：
- Notification 服务的 Domain/Infrastructure/Web、contract、SDK、Gateway facade 和 Console notifications UI。
- FileStorage contract/SDK、PostgreSQL 元数据服务、schema 约定测试、本地 tus HEAD/PATCH 上传和下载内容 endpoint。
- 核心 shadcn-vue primitive，包括 Table、Dialog、AlertDialog、Select、Pagination 和 Empty。

## 剩余范围

1. FileStorage tus 强化：大小验证、checksum 验证、过期清理和更广泛的 tus 兼容性。
2. FileStorage 对象存储部署集成：MinIO/S3 multipart 仍属于 MVP 之后的工作。
3. business-console 就绪所需的前端组件缺口：Sheet、Tabs、日期/日期范围、文件上传和图表 primitive。
4. 仅在尚未实施处跟进 Notification：偏好设置、外部 provider 或更多事件 consumer。

## 子 Issue

- `feat: FileStorage tus 强化 - 大小、checksum、过期与协议兼容性`
- `feat: FileStorage 对象存储集成 - MVP 后的 MinIO/S3 multipart`
- `feat: 补齐 business console 就绪所需的前端组件缺口`

## 范围外

- Gantt 和排程可视化（#78）。
- 重建已经交付的 Notification/FileStorage MVP 能力。
```

### #71 替换正文

```markdown
## 当前事实

本 Issue 跟踪跨平台服务和业务服务的生产就绪工作。

已经存在：
- AppHub、Ops、IAM、FileStorage、Notification 和选定业务服务的 PostgreSQL migration 基线。
- 已迁移服务的 schema 约定测试。
- 消息 provider 可以默认为 InMemory，并仅在配置时使用 RabbitMQ。

## 剩余范围

1. 跨业务服务的 CAP/outbox 发布-订阅验收。
2. IntegrationEvent consumer 幂等性、版本检查、DLQ/replay 指引和测试。
3. 补全 IAM ExternalClient 和 AuthorizationGrant。
4. 安全强化：TLS/CORS/secret/token 生命周期/审计完整性。
5. 生产部署产物：Compose、安装/启动脚本和 AppHost 覆盖。
6. 高频写入库存移动和高频读取工作/订单列表的性能基线。

## 子 Issue

实施前应按工作流创建子 Issue。只有所有子 Issue 和 readiness 文档都完成后，才应关闭此 epic。
```

### #73 替换正文

```markdown
## 当前事实

这是 Layer 1 通用能力 epic。

已经存在：
- BusinessQuality 服务已存在，并包含 NonconformanceReport 聚合和 API。

尚未存在：
- Inventory 服务。
- BarcodeLabel 服务。
- BusinessApproval 服务。
- Quality InspectionPlan 和 InspectionRecord。

## 子 Issue

- `feat: Inventory MVP - 库存台账、移动、可用量与盘点`
- `feat: Quality 检验 MVP - 检验计划、检验记录与来料/工序检验`
- `feat: BarcodeLabel MVP - 规则、模板、打印批次与扫描`
- `feat: BusinessApproval MVP - 模板、审批链与审批记录`

## 验收

1. Inventory 是唯一的库存余额和库存移动事实来源。
2. Quality 检验和 NCR 不直接变更 Inventory、WMS、ERP 或 MES。
3. Barcode 命令对于打印/扫描工作流具有幂等性。
4. BusinessApproval 处理业务单据审批，不替代 Ops。
5. 按服务更新 IAM seed、授权矩阵、schema 目录、migration 和验证脚本。
```

### #74 替换正文

```markdown
## 当前事实

这是 MES 执行 epic。

已经存在：
- `backend/services/Business/Mes` Web 项目和 Web 测试。
- 内存式排程、加急工单和 maintenance asset 事件 handler 行为。

尚未存在：
- MES Domain 项目。
- MES Infrastructure 项目。
- PostgreSQL schema 和 migration。
- 持久化的 WorkOrder、OperationTask、ProductionReport 和 FinishedGoodsReceiptRequest 事实。

## 子 Issue

- `feat: MES CleanDDD 持久化与执行 MVP`
- 后续可为 Planning、WMS、Quality、Telemetry 和 Maintenance 接线创建 MES 集成 Issue。

## 验收

1. 保留当前 scheduler 行为，或通过测试有意调整该行为。
2. MES 存储持久的工单、工序任务、报工和排程事实。
3. 工单引用 ProductEngineering ProductionVersion，而不是复制工程事实。
4. 完工入库请求通过 API/事件边界与 WMS 集成，而不是使用共享表。
```

### #75 替换正文

```markdown
## 当前事实

这是 WMS 执行 epic。尚无 WMS 服务代码。

## 子 Issue

- `feat: WMS 执行 MVP - 入库、出库、盘点与 WCS adapter 边界`

## 验收

1. WMS 拥有入库/出库执行事实和 WCS adapter 任务映射。
2. WMS 不存储库存余额。
3. WMS 在入库/出库完成后请求 Inventory movement。
4. WCS adapter 失败可诊断、可补偿。
```

### #76 替换正文

```markdown
## 当前事实

这是 ERP epic。尚无 ERP 服务代码。该范围对单个执行 Issue 而言过大。

## 子 Issue

- `feat: ERP Procurement MVP - 请购、RFQ、采购订单与收货`
- `feat: ERP Sales MVP - 商机、报价、销售订单与交付请求`
- `feat: ERP Finance MVP - 应收、应付、凭证与成本候选项`

## 验收

1. Procurement 可以接受计划采购建议并记录采购收货。
2. Sales 可以创建供 WMS 履约的交付请求。
3. Finance 从业务事实创建应收/应付/voucher/成本候选项，并强制 voucher 分录平衡。
4. ERP 不拥有 WMS 执行或 Inventory 余额。
```

### #77 替换正文

```markdown
## 当前事实

本 Issue 是最终业务全链路验收 epic。在所有业务 MVP 验证脚本存在并通过之前，不得开始此项工作。

## 阻断性验证脚本

- `scripts/verify-business-master-data-realignment.ps1` 已存在。
- 其余业务验证脚本必须由各自所属切片 Issue 创建。

## 验收链路

1. 工程到制造。
2. 计划到采购/生产。
3. 采购到库存再到应付账款。
4. 订单到交付再到应收账款。
5. 生产执行到成本。
6. 设备到维护再到产能。
7. WMS 到 WCS adapter。

## 测试规则

验收测试位于 `backend/tests/Nerv.IIP.Business.Acceptance.Tests/` 下，仅验证公开 HTTP API 和 IntegrationEvent 可见结果。它们不得直接读取服务数据库。
```

## 执行顺序

1. 将 #70、#71 和 #73-#77 重写为 epic。
2. 创建上面列出的缺失子 Issue。
3. 向 #127-#130 添加评论，将它们关联到各自的业务切片、依赖事实和当前计划。
4. 使用 Issue 到切片的映射更新 `docs/architecture/business-platform-domain-architecture.md`。
5. 使用业务服务代码事实表更新 `docs/architecture/implementation-readiness.md`。
6. 按以下顺序更新或创建聚焦的规格/计划：
   - 为 #127 补全 ProductEngineering。
   - 为 #73 子 Issue 编写 Common Capability v2。
   - 在 ProductEngineering 和 Inventory 最小契约就绪后，为 #128 编写 DemandPlanning。
   - 为 #74 编写 MES 持久化。
   - 按依赖顺序处理 WMS、ERP、IndustrialTelemetry 和 Maintenance。

## 验证

Issue 清理后：

1. `gh issue list --state open --label business-platform --limit 200` 应显示 epic 和可执行子 Issue。
2. 每个未关闭的 business-platform Issue 应至少包含以下一项：父 epic、阻断依赖、计划路径或明确的未来状态。
3. 除 #78 外，任何未关闭 Issue 都不应为已经实施的工作描述过时的从零开始范围。
4. `implementation-readiness.md` 应继续作为规范的代码事实摘要。

## 待定决策

1. 在 FileStorage/UI 后续子 Issue 完成之前，将 #70 作为 epic 保持打开。只有子 Issue 已关闭且 readiness 文档明确剩余的 MVP 后排除项之后，才关闭它。
2. 分别创建 BarcodeLabel 和 BusinessApproval 子 Issue，因为它们拥有不同的事实归属和下游 consumer。
3. 在专门的跨领域 Issue 中跟踪业务服务 AppHost 注册，避免在每个服务切片中反复产生合并冲突。
