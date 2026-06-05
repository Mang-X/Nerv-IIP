# 实施状态清单

本文档记录 Nerv-IIP 从“文档冻结完成”到“第一、第二、第三阶段纵切已落地，第四阶段真实基础设施门禁已通过，第五阶段迁移发布底座已通过，第六阶段 schema governance hardening 已完成，第七阶段 IAM Persistent Auth Foundation 已落地，Phase 8 IAM Admin Console 与蓝色 Design System 基线已实现，脚本自动化治理开始收敛”的状态，给出首批实施的环境前置、目录落点、引用规则、已完成范围和后续边界。

## 当前结论

1. 平台 HTTP 服务命名已经冻结为 .Web、.Domain、.Infrastructure。
2. Connector Host 与平台的 v1 协议边界已经冻结到公开接口和最小对象级别。
3. 后端 CleanDDD 与 netcorepal 的模板参数、目录、事件、事务、仓储和测试约定已经冻结。
4. 核心术语、Platform SDK 模块边界、IAM 对外授权边界、文件存储基线、通知能力基线、知识源生命周期和首批纵切验收口径已经补齐。
5. backend、connector-hosts 两个工作面已经完成第一迭代纵切骨架，可通过 `scripts/verify-first-slice.ps1` 做本地验证。
6. 第二阶段低风险动作闭环已经落地，可通过 `scripts/verify-second-slice-ops.ps1` 验证 Gateway、Ops、Connector Host 和 Docker Connector 的 restart 闭环。
7. 平台 HTTP 接口统一使用 FastEndpoints；新增接口必须放在 Web 项目的 `Endpoints/` 目录，不在启动文件中写 Minimal API 路由映射。
8. 部署策略已经冻结为“多部署目标，单一部署模型”：平台级 Aspire AppHost 作为统一拓扑入口，Docker Compose、安装包和整合安装脚本作为不同环境的交付目标。
9. 仓库根必须保留 `NuGet.config` 固定 NuGet restore 包源，避免本机全局多包源配置与 Central Package Management、`TreatWarningsAsErrors` 组合后触发 `NU1507`。
10. 第三阶段控制台纵切已经落地，可通过 `scripts/verify-third-slice-console.ps1` 验证 Gateway OpenAPI、Hey API 生成、Vue 控制台 typecheck/test/build。
11. 前端工作区使用 Vite+ 作为统一工具入口；`pnpm -C frontend check`、`lint`、`fmt` 需要 Node.js `>=22.18.0`，仓库根 `.node-version` 保留 22.22.3 作为保守复现基线；本地开发可以使用更新的 Current 版本（如 Node.js 26），只要前端质量门禁可复现通过。
12. 技术栈官方文档与源码仓库链接统一维护在 docs/architecture/technology-stack-references.md；新增长期技术选型时必须同步更新，避免同名项目或社区分叉造成歧义。
13. 第四阶段已经补齐 AppHub/Ops 的 netcorepal/CleanDDD PostgreSQL profile、code-analysis endpoint、`scripts/verify-fourth-slice-real-infra.ps1` 和平台级 `infra/aspire/Nerv.IIP.AppHost`；当前 AppHost build 与完整真实基础设施验证均已通过。
14. 第四阶段统一验收口径见 docs/architecture/fourth-vertical-slice-real-infra.md；后续生产级迁移、初始化、seed 和回滚策略按 docs/adr/0009-database-migration-release-and-seed-strategy.md 执行。
15. 第五阶段 Release-grade Persistence Foundation 已落地：AppHub/Ops 已有初始 migrations，PostgreSQL profile tests 已切换到 `MigrateAsync()`，第五阶段验证脚本已经通过。
16. Phase 8 已冻结 Console Calm Control Plane 蓝色 Design System 基线；后续控制台页面、视觉组件、组件库迁移或样式体系决策必须沿用 shadcn semantic tokens、`@nerv-iip/ui` 稳定导出和 docs/architecture/frontend-design-system-planning.md 的治理口径。
17. 数据库 schema、建表注释、schema catalog 和发布 runbook 已补齐第一版，后续持久化服务必须先满足 docs/architecture/database-schema-conventions.md、docs/architecture/database-schema-catalog.md 和 docs/architecture/database-release-runbook.md。
18. 第六阶段 Schema Governance & Migration Hardening 用 AppHub/Ops 作为已迁移服务样本，把业务表注释、业务列注释、JSON/text 兼容注释、string ID 约束和 service-schema migrations history 配置固化为测试门禁；IAM 已沿用该门禁，FileStorage 也已沿用到 `filestorage` schema、初始 migration 和 schema convention tests。
19. 第七阶段 IAM Persistent Auth Foundation 已落地：IAM 保留 InMemory profile，同时具备 PostgreSQL `iam` schema、初始 admin seed、JWT access token、refresh token rotation、session revoke、`/me`、Connector Host credential validation 和 internal authorization check 的后端持久化基线。
20. 现有 PlatformGateway Console API 已接入 IAM-backed permission enforcement；Gateway 只转发 bearer token 与 permission/context，不直接引用 IAM Domain 或 Infrastructure。
21. Console Auth + shadcn-vue Baseline 已提供最小登录 UI、会话恢复、Gateway bearer 注入、路由守卫、退出登录和 shadcn-vue UI 基线。
22. 脚本自动化治理已冻结到 ADR 0010 和 docs/architecture/script-automation-governance.md；IAM、第五阶段和第四阶段核心 verify 脚本已迁移到 helper 门禁，Ubuntu WSL 兼容门禁已通过，新增或修改脚本必须声明分类、副作用、日志、清理和 helper 使用方式。
23. Phase 8 IAM Admin Console 已交付用户、角色、权限 catalog 和会话管理闭环：IAM 管理写入口已产品化，PlatformGateway 暴露 Console IAM Admin facade 并执行 IAM-backed permission enforcement，frontend 通过 generated `@nerv-iip/api-client` 和 `useIamAdmin.ts` 消费 `/iam/users`、`/iam/roles`、`/iam/sessions` 页面。
24. BusinessMasterData 作为业务平台 Layer 0 基石已经完成审查重裁决：原 `2026-05-20-business-master-data-foundation.md` 只作为最小骨架，继续 Task 4/5 或下游业务域前必须先执行 `docs/superpowers/plans/2026-05-21-business-master-data-realignment.md`，并遵守 `docs/adr/0013-business-master-data-governance.md`、`docs/architecture/business-master-data-field-matrix.md` 和 `docs/architecture/business-master-data-process-manufacturing-supplement.md`。
25. BusinessMasterData realignment 已开始落地：MasterData Domain 增加 UOM、UOM conversion、Site、ProductionLine、Shift、ReferenceDataCode 和扩展 SKU/WorkCenter/DeviceAsset 属性；Infrastructure 已生成 `RealignBusinessMasterData` migration；Web 层已提供 MasterData 变更 IntegrationEvent payload、批量 resolve/validate query、统一 list query，并补齐 SKU、UOM、UOM conversion、伙伴、部门、团队、人员技能、工厂、产线、班次、日历、工作中心、设备和参考数据的 create endpoints；API 合同测试已覆盖稳定 operationId、路由、权限码、创建成功和重复业务键；IAM seed 已加入 `business.masterdata.*` 六个权限。可通过 `scripts/verify-business-master-data-realignment.ps1` 做本地验证。
26. FileStorage MVP 已交付公开 contracts、`Sdk.FileStorage` HTTP client、server-proxy metadata API 子集、PostgreSQL-backed API service、`filestorage` PostgreSQL schema baseline、初始 migration、schema convention tests、本地 tus `HEAD`/`PATCH` 上传 endpoint，以及 PlatformGateway Console 管理 facade（上传会话创建/complete、文件元数据、download grant 创建）；本地 tus endpoint 已补齐 size/checksum 校验、过期未完成上传清理和 complete 最终一致性校验；当前不包含 MinIO/S3 multipart 或 Console 页面。
27. Messaging provider 已与 persistence provider 解耦：AppHub、Ops、Notification、BusinessMasterData、BusinessProductEngineering、BusinessInventory、BusinessQuality、BusinessMES、BusinessDemandPlanning、BarcodeLabel、BusinessApproval、WMS、BusinessIndustrialTelemetry、BusinessMaintenance 和 BusinessScheduling 的 Development PostgreSQL profile 默认使用 `Messaging:Provider=InMemory` + CAP InMemory message queue；非 Development 环境禁止使用 CAP InMemory transport，必须显式设置 `Messaging:Provider=RabbitMQ` 以避免进程重启后集成事件静默丢失。平台级 AppHost 默认不创建 RabbitMQ resource，`scripts/verify-second-slice-ops.ps1 -UsePostgres` 默认也不再依赖 RabbitMQ。
28. 业务平台 GitHub issue roadmap 已完成阶段性重整：#70/#71、#127 到 #141、#143、#166 到 #175 均已关闭；截至 2026-05-26，#77 P0 full-chain acceptance 已完成收口。2026-05-27 起，MES PC 交付复盘发现页面依赖的上游源事实、排程和设备运行事实还不足以支撑真实一线使用，因此 MES operational foundation reset 以 #188 到 #207 作为新的 P0 执行入口；#142 仍是 post-MVP MinIO/S3 multipart，#78 仍是甘特/RFC 前端参考，#206 负责 APS 调度内核与排程数据契约，#207 负责设备 IIoT/IndustrialTelemetry 运行事实与 APS/MES 联动。
29. 业务平台 Wave 1 agent handoff 已补齐：#127、#131、#132、#135 和 #140 分别有 session 级 plan，Inventory 与 Quality inspection 另有独立 spec；并行开发前先读 `docs/superpowers/specs/2026-05-23-business-wave-1-agent-session-design.md`，再进入对应 issue 的 plan。
30. ProductEngineering MVP 已补齐 EngineeringDocument、EngineeringItem、EBOM、MBOM、Routing、ECO/ECN 和 ProductionVersion 的 Domain/Infrastructure/Web/API contract/schema convention 测试；服务已加入 `backend/Nerv.IIP.sln` 和 Aspire AppHost，IAM seed、权限矩阵、schema catalog 与 `scripts/verify-business-product-engineering-mvp.ps1` 已同步。
31. Business Wave 1 closure 已完成代码事实对齐：BusinessMasterData、BusinessProductEngineering、BusinessInventory、BusinessQuality 和 BusinessMES 均纳入 `backend/Nerv.IIP.sln`、平台级 Aspire AppHost、schema catalog、readiness 映射和 `scripts/verify-business-wave1-foundation.ps1` 聚合验证；本地端口固定为 5107-5111。
32. 业务平台 Wave 2 agent handoff 已补齐：#128、#133、#134 和 #136 分别有 session 级 spec/plan，统一入口为 `docs/superpowers/specs/2026-05-23-business-wave-2-agent-session-design.md`，共享注册/验证由 `docs/superpowers/plans/2026-05-23-business-wave-2-registration-verify-readiness.md` 收口。
33. FileStorage MinIO/S3 multipart #142 明确后置：当前业务 MVP 只依赖 FileStorage `fileId`/`FileReference`、metadata API、SDK 和本地 tus/server-proxy 能力；对象存储直传作为 Upload Provider adapter 后续接入，不阻塞 Wave 2 业务服务。
34. 前端组件缺口 #143 归入 Design System 范畴，设计事实来源为 `frontend/DESIGN/roadmaps/business-console-readiness.md`；Superpowers plan 只作为执行清单，不替代 DESIGN 组件契约。
35. Business Wave 2 execution closure 已完成共享接入：BusinessDemandPlanning、BarcodeLabel、BusinessApproval 和 WMS 均纳入 `backend/Nerv.IIP.sln`、平台级 Aspire AppHost、IAM seed/catalog、schema catalog、readiness 映射和 `scripts/verify-business-wave2-execution.ps1` 聚合验证；本地端口固定为 5112-5115。
36. Equipment Reliability / Wave 2.5 closure 已完成共享接入：BusinessIndustrialTelemetry (#129) 和 BusinessMaintenance (#130) 均纳入 `backend/Nerv.IIP.sln`、平台级 Aspire AppHost、IAM seed/catalog、schema catalog、readiness 映射和 `scripts/verify-business-equipment-reliability.ps1` 聚合验证；本地端口固定为 5116-5117。IndustrialTelemetry 报警事件已抽入 `Nerv.IIP.Contracts.IndustrialTelemetry`，Maintenance 通过该公共契约消费 `industrialTelemetry.AlarmRaised`，不引用 IndustrialTelemetry 的 Domain/Infrastructure/Web 项目。
37. Business Wave 3/Wave 4 规划入口已补齐：ERP #137/#138/#139 使用 `docs/superpowers/specs/2026-05-23-business-wave-3-agent-session-design.md`、`docs/superpowers/specs/2026-05-23-erp-procurement-sales-finance-mvp-design.md` 和三份 ERP 子计划推进；#77 Full-chain acceptance 使用 `docs/superpowers/specs/2026-05-23-business-full-chain-acceptance-design.md` 和 `docs/superpowers/plans/2026-05-23-business-full-chain-acceptance.md`。
38. Business Wave 3 ERP 已落地：BusinessERP 已纳入 `backend/Nerv.IIP.sln`、平台级 Aspire AppHost、IAM seed/catalog、schema catalog、readiness 映射和 `scripts/verify-business-erp-procurement-sales-finance-mvp.ps1` 聚合验证；本地端口固定为 5118。ERP Sales 后端已包含商机事实，但 BusinessGateway facade、Business Console 菜单和商机页面仍按前端菜单分期推进，不因后端聚合根已存在而提前暴露。WMS 已抽出 `Nerv.IIP.Contracts.Wms` 公共事件契约，并默认通过 HTTP client 向 Inventory posting 库存移动。
39. #77 Full-chain acceptance P0 已完成收口：WMS completion 使用 HTTP Inventory movement client，并通过 service-local live HTTP TestServer 验证 WCS dispatch/fail/retry/complete 后可由 public query 看到完成状态和失败诊断；MES 暴露生产报工、完工入库请求和维护产能影响查询；ERP Finance 暴露 AP/AR/Cost candidate source-document drill-down。`backend/tests/Nerv.IIP.Business.Acceptance.Tests` 保留统一 fixture/correlation/event recorder/HTTP envelope helper和七条链路 acceptance evidence，`scripts/verify-business-full-chain-acceptance.ps1` 默认覆盖 WMS live HTTP TestServer proof、MES/ERP public-surface 支撑测试与七链路验收。当前默认 profile 不启动 Docker/PostgreSQL/RabbitMQ 或外部 WCS 设备，真实基础设施联调作为后续 hardening 扩展。
40. 业务服务 API 的权限粒度边界已明确：各业务服务端点保留 `PermissionCode` 作为公开契约、IAM catalog 和 Gateway facade 映射元数据，运行时只接受 `InternalServiceAuthorizationPolicy` 保护的内部调用；面向 Console/BusinessConsole 的 per-operation permission enforcement 在 BusinessGateway/PlatformGateway facade 层执行，转发前通过 IAM internal authorization check 校验用户 bearer、组织/环境上下文和具体 permission code。业务服务本身不得直接接受终端用户 bearer 作为权限判定来源。
41. Business Console MVP 已落地：`backend/gateway/BusinessGateway` 纳入平台级 Aspire AppHost，本地端口固定为 5119，并通过 IAM、MasterData、ProductEngineering、Inventory、Quality、DemandPlanning、ERP Procurement、BusinessApproval、Notification、Scheduling、IndustrialTelemetry、Maintenance、WMS 和 MES 的 HTTP 边界聚合 `/api/business-console/v1/**`；`frontend/apps/business-console` 已作为独立 Vite Plus app 纳入前端工作区和 AppHost，本地端口固定为 5125。OpenAPI 导出脚本会同时写入 PlatformGateway 和 BusinessGateway 快照，`@nerv-iip/api-client` 生成 business-console 稳定导出。#166 到 #169 已提供首批 MVP 页面：SKU 列表/创建、库存可用量/移动/盘点、检验计划/检验记录/NCR 处置关闭、MES 工单/急单/报工，以及 MES 规则排程触发/结果状态页；后续又补入 ProductEngineering 窄化工程资料页、DemandPlanning 计划工作台、ERP 采购与供应窄化页、MES PC 标准执行工作台、APS lite 页面级 facade 和设备运行事实 route-ready 页面。当前 `/erp` 已通过 BusinessGateway ERP Procurement facade 展示采购订单、供应商编码、预计到货和部分收货状态；这不代表 ERP 销售、财务或完整 ERP 前端菜单已完成。BusinessGateway 已提供 WMS 收货入库列表、出库列表和 WCS 任务读面 facade，其中收货列表可融合 Inventory availability context/sourceStatus；WMS 前端页面、OpenAPI 快照和 api-client 生成仍由后续/父线程统一接入。BusinessGateway 已提供 `/api/business-console/v1/workbench/summary` 数字化工作台聚合 facade，按当前 principal 分别过滤 BusinessApproval 待办、Notification 消息/任务、IndustrialTelemetry 预警以及 MES/Quality KPI 来源；Inventory 汇总当前明确返回 unsupported source status，不伪造跨域库存 KPI。BusinessGateway 已提供 `/api/business-console/v1/search` 全局对象搜索后端 facade，首批按读时权限过滤聚合 MES 工单、MasterData SKU 和 IndustrialTelemetry 当前报警；Inventory batch/lot 与 equipment device 当前明确返回 unsupported source/type status，前端 Cmd/K 面板和 api-client 生成由后续/父线程统一接入。2026-05-26 MES PC 工作台已按 `2026-05-26-business-console-mes-pc-completion` 补齐标准执行主线：生产驾驶舱、基础准备、生产计划、计划与工单、工单详情、齐套与物料、派工看板、工序执行、在制状态、报工与完工、质量与不良、完工入库、规则排程、设备与停机、班次交接、追溯查询和产能影响；页面文案先直接中文化，不把首轮业务页面重载到 i18n。MES `foundation-readiness` 是系统管理/数据就绪诊断，不作为一线日常执行菜单的优先入口；`/mes/production-plans` 的目标来源是 DemandPlanning 的 demand source/source plan 或等价来源计划，MES 后端已在计划转工单路径把来源计划/需求引用持久化到 `work_orders` 并通过生产计划列表、工单详情和追溯查询回显；MES 不读取 DemandPlanning schema，也不建立跨 schema FK。2026-05-27 的 `2026-05-27-mes-operational-foundation-reset` 将 MES 页面交付重新绑定到主数据、工程资料、MRP、采购供应、库存/WMS 齐套、质量设备 readiness、APS lite 和设备 IIoT 运行事实。MES 工作台本身不内嵌甘特或调度算法；当前 `runBusinessConsoleMesSchedule` 只是 MES 规则排程过渡入口，不是长期 APS 权威，#206 落地 BusinessScheduling 后应消费其排程输出；#78 做甘特/排产展示，#206 做 APS 后端内核，#207 做设备 IIoT 运行事实联动。#207 设备页面仅展示业务运行事实，不显示 organization/environment/debug/source metadata；PDA/mobile 明确后置。Business Console 能力目录、角色导航、当前可见范围、上下文穿透和升级门禁以 `docs/architecture/frontend-navigation-map.md` 为准。
42. #170 事件可靠性基线已落地契约、消费侧守门和首批持久 DLQ：`backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests` 验证 Ops、Maintenance、WMS 和 IndustrialTelemetry 公共集成事件暴露 ADR 0011 envelope 字段、结构化 payload，并实现 `Nerv.IIP.Contracts.IntegrationEvents.IIntegrationEventEnvelope`，消费侧 guard 不再通过反射读取 envelope 关键字段；WMS 和 IndustrialTelemetry 事件契约已补齐 correlation、causation、actor、version、source、idempotency 和 payload 形态。`Nerv.IIP.Messaging.CAP` 已提供 `IntegrationEventEnvelopeValidator`、`IntegrationEventConsumerGuard` 和 in-memory DLQ/replay 标记 store；EF-backed `PersistentIntegrationEventDeadLetterStore<TDbContext>` 位于 `Nerv.IIP.Messaging.CAP.EntityFrameworkCore` 扩展包，PostgreSQL-backed 服务显式引用该扩展以避免基础 CAP 包携带 EF Core 传递依赖。当前已有真实消费者 BusinessMaintenance `AlarmRaised`、Notification `OperationTaskFailed`、BusinessMES `AssetUnavailable`/`AssetRestored`、AppHub `OperationTaskCompleted`/`OperationTaskFailed` 接入版本校验，unsupported version 等消费前拒绝会进入 DLQ，避免执行业务副作用；handler 执行业务逻辑后抛出的异常仍交由 CAP retry，不在当前 DLQ 范围内。Notification、AppHub、BusinessMaintenance 和 BusinessMES 的真实消费者均保留 NetCorePal `IntegrationEventConsumer` 标记，并补充 CAP 原生 `ICapSubscribe` 接法复用同一个 handler/guard；BusinessMaintenance、Notification、BusinessMES 和 AppHub 均已在各自 PostgreSQL schema 落地 `integration_event_dead_letters` 表，PostgreSQL profile 下使用持久 DLQ store，非 PostgreSQL 测试/本地 profile 仍可使用 in-memory store。当前 Phase 2 只覆盖拒绝捕获、列表查询和 replay 后标记 `Replayed`，不包含自动 replay executor、handler 异常兜底 DLQ、replay 失败 `Failed` 状态、AppHub/MES 业务 inbox 持久表或 DLQ 管理入口；这些仍按后续小 issue 深化。
43. #171 业务服务 CAP/outbox 发布订阅验收已完成真实基础设施门禁：CAP 官方 PostgreSQL storage 已替代旧 `UseNetCorePalStorage`，避免 EF Core 10 下 NetCorePal CAP PostgreSQL storage 兼容问题；`NotificationCapOutboxAcceptanceTests` 使用 Notification 真实 `OperationTaskFailed` 消费者，通过 CAP topic 发布 Ops 公共契约事件，验证 PostgreSQL CAP outbox、CAP inbox、Notification 业务副作用和 Notification 业务 inbox。`scripts/verify-infra-cap-outbox-acceptance.ps1` 提供 InMemory messaging 与 RabbitMQ messaging profile 的 opt-in 入口，要求真实 PostgreSQL；`all` profile 会分进程顺序运行 InMemory 与 RabbitMQ，避免 CAP in-memory transport 静态状态互相污染。当前验收覆盖单服务真实 CAP pipeline、真实 PostgreSQL 和真实 RabbitMQ broker；跨进程 Ops→Notification/AppHub 多服务联调仍作为后续扩展。
44. #172 ExternalClient 与 AuthorizationGrant 已落地首批 `client_credentials` 闭环：IAM 新增 `external_clients` 和 `authorization_grants` 持久化事实、seed 驱动 demo external client、`/api/iam/v1/auth/client-token` token 入口、`external-client` JWT claims、`/api/iam/v1/me` 主体识别和 internal authorization check grant 校验。P2 企业身份深化已补配置驱动的 OIDC callback、MFA challenge hook、SSO session binding 字段和 ExternalClient resource-scope ABAC grant enforcement。当前仍不包含完整 OAuth/OIDC 授权码服务器、动态客户端注册 UI、consent 页面或第三方应用市场。
45. #173 生产安全硬化已形成可验证闭环：AppHub connector ingestion 不再固定接受仓库内 `local-connector-secret`；配置 `ConnectorHostCredential:Secret` 后只接受配置值，非 Development 缺失配置会拒绝请求。平台级 AppHost 已把 connector secret 和 `InternalService__BearerToken` 传给子服务，Development fallback 仅保留用于本地无配置测试。IAM 生产环境不论 persistence profile 都要求显式 `Iam:Jwt:SigningKey`，且签名密钥至少 32 bytes、`Iam:Jwt:AccessTokenMinutes` 必须在 1-60 分钟内；PlatformGateway 与 BusinessGateway 在非 Development 下要求 `Security:Cors:AllowedOrigins`、启用 HSTS/HTTPS redirect、CORS 白名单和全局 rate limit；业务 JWT metadata 在非 Development 不再关闭 HTTPS metadata；Ops `audit_records` 新增 `IntegrityHash`，新审计事实带 tamper-evident SHA-256 摘要。
46. #174 生产部署产物已形成可验证闭环：平台级 AppHost 继续作为服务拓扑真相源，并已接入 Aspire Docker Compose deployment target；`.\nerv.ps1 publish-compose` 从 AppHost 生成 Compose 产物，`.\nerv.ps1 prepare-compose` 和 `.\nerv.ps1 deploy-compose` 通过 Aspire 执行环境化准备和部署。`infra/compose/nerv-iip.dependencies.yml` 与 `infra/compose/nerv-iip.platform.yml` 仍保留为 legacy 发布基线、发布演练和迁移期 overlay 参考，不再作为新增完整平台拓扑的首选来源；`infra/docker/dotnet-service.Dockerfile`、`infra/docker/vite-spa.Dockerfile` 和 `infra/compose/nerv-iip.production.env.example` 提供统一镜像构建与配置样例；`scripts/install/start-nerv-iip-apphost.ps1` 是受治理 release-install AppHost 启动入口，`scripts/package/create-nerv-iip-package.ps1` 是 zip 包生成入口，`scripts/verify-production-deployment-artifacts.ps1` 验证部署产物和 Compose config。
47. #175/P2 性能基线已从 opt-in 骨架升级为可阈值化 release gate：`backend/tests/Nerv.IIP.Business.Performance.Tests` 会写出 machine-readable JSONL 指标，`scripts/verify-business-performance-baseline.ps1` 可在显式 PostgreSQL 连接串下输出 summary JSON，并通过全局或 inventory/mes/erp 分场景 elapsed-time 阈值失败门禁；未配置 `NERV_IIP_PERF_POSTGRES` 时测试 skip，脚本失败提示，避免用 InMemory 结果冒充性能事实。
48. P2 部署演练已新增 opt-in 入口：`scripts/verify-production-release-rehearsal.ps1 -Profile dependencies|platform-smoke` 使用生产 Compose artifacts 创建 disposable project、启动依赖、执行 PostgreSQL/Redis/MinIO smoke，并在 `platform-smoke` profile 下构建核心平台服务、执行 Development-only auto-migration smoke 和 `/health` 检查；脚本默认清理自有 Compose project 和 volumes，Docker 不可用时报环境不可用，不进入默认本地 verify。
49. P2 Ops 高风险动作审批与通知联动已落地首个 approval gate：`operation_templates.RequiresApproval=true` 的 Ops 任务创建后进入 `approval-pending`，通过 `/api/ops/v1/operation-tasks/{operationTaskId}/approval/approve|reject` 决策；审批通过后才可被 Connector Host claim，拒绝进入 `rejected` 终态。Ops 新增审批字段 migration 和 `OperationApprovalRequested/Approved/Rejected` 事件，Notification 真实消费者会把审批待办、审批结果和执行完成/失败结果转为 Notification intent。
50. #193 MES 线边齐套与领料闭环已补齐 MES 执行侧持久事实：`material_requirements` 保存 released MBOM/Inventory/WMS readiness 的工单/工序物料快照，齐套计算只采用同物料/批次最新快照；`material_issue_requests` 保存领料申请、部分/全量线边接收和实际批次；`production_report_material_consumptions` 要求报工引用已接收的线边领料批次并进入追溯图，且通过报工/物料/批次唯一索引兜底重复写入；release/start 都会在持久齐套短缺时以业务异常阻断。当前仍不让 MES 拥有库存余额，Inventory/WMS/Quality/ERP 的正式 adapter/event 刷新按后续 hardening 深化。
51. ADR 0014 已冻结 APS 与设备 IIoT 排程边界：APS lite / BusinessScheduling 进入 P0，拥有排程问题、排程方案、资源负载、冲突项、不可排原因和排程版本事实；DemandPlanning 仍拥有 MRP 和计划建议，MES 仍拥有执行事实，IndustrialTelemetry/Maintenance 提供设备状态、报警、停机和维护窗口。高级优化器、仿真、自动重排、高频 historian 和现场控制闭环仍后置。
52. #188 Numbering and Idempotent Creation 已完成生产级持久化基线：MasterData SKU、MES rush/plan work order、MES 报工/物料请求/不良/停机/交接/完工入库请求、ProductEngineering release documents/items/BOM/routing/ECO、DemandPlanning demand source，以及 ERP procurement/sales/finance P0 create commands 均允许普通创建请求省略系统编号并携带可选 idempotency key；各 owning service 已落地 `numbering_counters` 与 `numbering_idempotency_keys` 表、EF migrations、schema convention tests 和 schema catalog 条目，编号按组织/环境/文档类型/date segment 持久递增并通过 idempotency key 绑定首次分配结果。BusinessGateway/OpenAPI/api-client 与 Business Console SKU、急单、计划转工单、报工入口已同步移除普通用户手工系统编号输入。
53. #206 APS lite / BusinessScheduling 已落地后端 P0 内核、公开 Scheduling contracts、PostgreSQL `scheduling` schema、服务 API、IAM seed 权限、BusinessGateway facade、平台级 Aspire AppHost 注册和 `scripts/verify-business-scheduling-aps-lite.ps1` 验证入口；本地端口固定为 `5120`。当前算法是 deterministic finite-capacity heuristic，只提供可复现、可解释的有限产能排程，不包含全局 solver/optimizer、仿真或自动重排。
54. #207 设备 IIoT/IndustrialTelemetry 运行事实与 APS/MES 联动已进入当前 forked workspace：`Nerv.IIP.Contracts.EquipmentRuntime` 提供设备运行事实 DTO，IndustrialTelemetry 增加 source_system/source_connector 幂等 scope 和 runtime availability 查询，Maintenance 增加维护窗口字段，Scheduling/MES 消费设备 availability/readiness，BusinessGateway 暴露 `/api/business-console/v1/equipment/**` facade，Business Console 已有 `/equipment`、`/equipment/alarms` 和设备详情 route-ready 页面。当前限制：Gateway equipment facade 只强制 IIoT read permissions，Maintenance read permissions 仍是下游/domain catalog 相关权限；页面不暴露 org/env/debug/source metadata；脚本验证聚焦 #207 相关 backend tests、api-client 生成和 Business Console focused gates，不替代 Task 9 full verification。

### 业务平台代码事实与 issue 映射

| 服务/能力 | 当前代码事实 | GitHub 跟踪 |
| --- | --- | --- |
| BusinessMasterData | 已有 Domain/Infrastructure/Web、PostgreSQL migration、测试与 `scripts/verify-business-master-data-realignment.ps1`；realignment 已补齐 UOM、资源、设备、resolve/list/create endpoint 和变更事件 payload。 | #72 已关闭；下游接线由 #127、#131 到 #143 承接 |
| Quality | 已有 Domain/Infrastructure/Web、PostgreSQL migration 和测试；当前已完成 NonconformanceReport，并在 #132 补齐 InspectionPlan、InspectionRecord、收货/工序/终检等检验事实、API、事件和 schema 门禁；服务已纳入 solution/AppHost/Wave 1 verify。Quality list `keyword` 当前只用于 NCR/检验方案定位字段，仍使用 `LOWER(...) LIKE '%keyword%'`，PostgreSQL 表达式索引或 `pg_trgm` 优化作为后续 hardening。 | #73、#132 |
| ProductEngineering | 已有 Domain/Infrastructure/Web、PostgreSQL migration 和测试；已覆盖 EngineeringDocument、EngineeringItem、EBOM、MBOM、Routing、ECO/ECN、ProductionVersion，服务已纳入 solution/AppHost/IAM seed/schema catalog，并提供 `scripts/verify-business-product-engineering-mvp.ps1`。 | #127 |
| MES | 已有 Domain/Infrastructure/Web、PostgreSQL migration 和测试；已覆盖工单、工序任务、报工、制程不良记录、班次交接、完工入库请求、规则排产结果、工作中心不可用窗口、设备资产映射，以及计划转工单时持久化 DemandPlanning/source plan durable reference；#77 所需生产报工、完工入库请求和产能影响 public query surface 已补齐；#320 补齐关联质量项与班次交接真实分页和 total；服务已纳入 solution/AppHost/Wave 1 verify，并提供 `scripts/verify-business-mes-execution-mvp.ps1`。Business Console 的 foundation-readiness 只作为系统管理/数据就绪诊断；规则排程接口只是 #206 前过渡入口。#194 继续补工单释放快照与执行生命周期。 | #74、#135、#163、#194、#272、#320 |
| Inventory | 已有 Domain/Infrastructure/Web、PostgreSQL migration 和测试；已覆盖库存地点、库存台账、库存移动、盘点任务和盘点调整；服务已纳入 solution/AppHost/Wave 1 verify，并提供 `scripts/verify-business-inventory-mvp.ps1`。 | #73、#131 |
| BarcodeLabel | 已有 Domain/Infrastructure/Web、PostgreSQL migration 和测试；已覆盖条码规则、标签模板、打印批次、打印项和扫码记录，并接入 solution/AppHost/IAM seed/schema catalog，提供 `scripts/verify-business-barcode-label-mvp.ps1`。 | #73、#133 |
| BusinessApproval | 已有 Domain/Infrastructure/Web、PostgreSQL migration 和测试；已覆盖审批模板、审批链、审批步骤和审批决定，并接入 solution/AppHost/IAM seed/schema catalog，提供 `scripts/verify-business-approval-mvp.ps1`。 | #73、#134 |
| DemandPlanning | 已有 Domain/Infrastructure/Web、PostgreSQL migration 和测试；已覆盖需求来源、MPS、MRP run、pegging 和计划建议，并接入 solution/AppHost/IAM seed/schema catalog，提供 `scripts/verify-business-demand-planning-mrp-mvp.ps1`。 | #128 |
| WMS | 已有 Domain/Infrastructure/Web、PostgreSQL migration 和测试；已覆盖入库、出库、仓库任务、盘点执行、WCS 任务和 Inventory movement request 元数据；#77 所需 Inventory HTTP posting、WCS failure/retry/complete public query 和 `wms.WcsTaskCompleted` 公共事件类型已补齐，并接入 solution/AppHost/IAM seed/schema catalog，提供 `scripts/verify-business-wms-execution-mvp.ps1`。 | #75、#136、#162 |
| ERP | 已有 Domain/Infrastructure/Web、PostgreSQL migration 和测试；已覆盖 Procurement（采购申请、RFQ、供应商报价、采购订单、采购收货）、Sales（商机、报价、销售订单、发货请求）和 Finance（AP、AR、凭证、成本候选）；#77 所需 AP/AR/Cost source-document drill-down query 已补齐，并接入 solution/AppHost/IAM seed/schema catalog，提供 `scripts/verify-business-erp-procurement-sales-finance-mvp.ps1`。商机后端事实不自动扩大 BusinessGateway/Business Console 当前菜单范围，商机页面仍按前端分期另行进入。 | #76、#137、#138、#139、#164 |
| Scheduling / APS lite | 已有 `Nerv.IIP.Contracts.Scheduling`、BusinessScheduling Domain/Infrastructure/Web、PostgreSQL `scheduling` schema、有限产能 deterministic heuristic、计划 preview/create/list/detail/gantt/release API、BusinessGateway facade、IAM seed 权限、AppHost 注册和 `scripts/verify-business-scheduling-aps-lite.ps1`；本地端口固定为 5120。#207 已补设备 availability adapter，用于把设备运行事实纳入 APS/MES readiness。当前不包含全局优化器或自动重排。 | #206、#207 |
| IndustrialTelemetry | 已有 Domain/Infrastructure/Web、PostgreSQL migration 和测试；已覆盖 TelemetryTag、AlarmRule、DeviceStateSnapshot、AlarmEvent 和 TelemetrySummary，并接入 solution/AppHost/IAM seed/schema catalog，提供 `scripts/verify-business-industrial-telemetry-mvp.ps1`；#207 已补 source_system/source_connector 幂等 scope、current-state/runtime availability 查询、alarm rule/OEE 服务读面和 BusinessGateway equipment/telemetry facade 消费边界。保持 PLC/DCS/SCADA 控制命令和凭据在外部边界。 | #129、#207 |
| Maintenance | 已有 Domain/Infrastructure/Web、PostgreSQL migration 和测试；已覆盖维修工单、保养计划、点检、停机原因和备件行，并接入 solution/AppHost/IAM seed/schema catalog，提供 `scripts/verify-business-maintenance-mvp.ps1`；报警开单通过 `Nerv.IIP.Contracts.IndustrialTelemetry` 消费 IndustrialTelemetry 公共事件。#207 已补 maintenance plan runtime availability 窗口字段并由 equipment facade 合并读取。 | #130、#207 |
| 业务服务注册与验收 | BusinessMasterData、BusinessProductEngineering、BusinessInventory、BusinessQuality 和 BusinessMES 已纳入平台级 AppHost、`backend/Nerv.IIP.sln` 和聚合 `scripts/verify-business-wave1-foundation.ps1`；BusinessDemandPlanning、BarcodeLabel、BusinessApproval 和 WMS 已纳入平台级 AppHost、`backend/Nerv.IIP.sln` 和聚合 `scripts/verify-business-wave2-execution.ps1`；BusinessIndustrialTelemetry 和 BusinessMaintenance 已纳入平台级 AppHost、`backend/Nerv.IIP.sln` 和聚合 `scripts/verify-business-equipment-reliability.ps1`；BusinessERP 已纳入 Wave 3 验证；BusinessScheduling 已纳入平台级 AppHost 和 `scripts/verify-business-scheduling-aps-lite.ps1`；#207 focused gate 为 `scripts/verify-business-iiot-runtime-facts-aps-mes.ps1`；端口矩阵与 readiness 已同步到 5107-5120，BusinessConsole Vite Plus app 固定为 5125。 | #77、#140、#206、#207 |
| BusinessGateway / BusinessConsole MVP | BusinessGateway 已纳入 `backend/Nerv.IIP.sln`、Aspire AppHost、OpenAPI 导出和 api-client 生成链路；BusinessConsole 位于 `frontend/apps/business-console`，通过 `@nerv-iip/api-client` 稳定 business-console exports 消费 MasterData、Inventory、Quality、Scheduling、Equipment runtime facts 和 MES facade，已交付 #166-#169 首批页面、APS lite facade 和 #207 设备运行事实 route-ready 页面。WMS 已有 BusinessGateway 收货/出库/WCS 读面 facade，但前端页面、OpenAPI 快照和 api-client 生成仍未由本次 worker 接入；后端服务已存在的 ERP 商机、BarcodeLabel/Approval 后端事实或 MES 诊断接口，不自动成为当前 Business Console 菜单入口；新增页面仍跟随菜单分期、BusinessGateway facade 和 OpenAPI/codegen 流程。 | #166、#167、#168、#169、#206、#207、#264 |

### 业务平台 Wave 1 agent handoff

| Issue | Handoff docs | 说明 |
| --- | --- | --- |
| #127 ProductEngineering | `docs/superpowers/plans/2026-05-23-product-engineering-gap-completion.md` | 已完成工程文档、工程物料、EBOM、MBOM、Routing、ECO/ECN 和 ProductionVersion 的 Domain/Infrastructure/Web/API contract/schema convention 测试，并接入 solution/AppHost/verify/readiness。 |
| #131 Inventory | `docs/superpowers/specs/2026-05-23-inventory-mvp-design.md`、`docs/superpowers/plans/2026-05-23-inventory-mvp.md` | 已完成库存事实源服务，覆盖库存地点、台账、移动和盘点，并接入 solution/AppHost/verify/readiness。 |
| #132 Quality inspection | `docs/superpowers/specs/2026-05-23-quality-inspection-mvp-design.md`、`docs/superpowers/plans/2026-05-23-quality-inspection-mvp.md` | 已在现有 Quality NCR 上增量落地 InspectionPlan、InspectionRecord、NCR-from-inspection、集成事件和验证脚本基线。 |
| #135 MES persistence | `docs/superpowers/plans/2026-05-23-mes-cleanddd-persistence.md` | 已保留 Web/API contract 并迁移到 Domain、Infrastructure 和 PostgreSQL，补齐持久化执行模型与 verify script。 |
| #140 Registration/readiness | `docs/superpowers/plans/2026-05-23-business-service-registration-verify-readiness.md` | 本收口 PR 统一补齐 Wave 1 solution、AppHost、verify scripts、端口矩阵、schema catalog 和 readiness。 |

### 业务平台 Wave 2 agent handoff

| Issue | Handoff docs | 说明 |
| --- | --- | --- |
| #128 DemandPlanning | `docs/superpowers/specs/2026-05-23-demand-planning-mrp-mvp-design.md`、`docs/superpowers/plans/2026-05-23-demand-planning-mrp-mvp.md` | 已完成需求来源、MPS/MRP、计划采购建议、计划工单建议和 pegging；不创建正式 ERP/MES 单据，已接入 solution/AppHost/verify/readiness。 |
| #133 BarcodeLabel | `docs/superpowers/specs/2026-05-23-barcode-label-mvp-design.md`、`docs/superpowers/plans/2026-05-23-barcode-label-mvp.md` | 已完成条码规则、标签模板、打印批次和扫码记录；不拥有库存数量或业务单据状态，已接入 solution/AppHost/verify/readiness。 |
| #134 BusinessApproval | `docs/superpowers/specs/2026-05-23-business-approval-mvp-design.md`、`docs/superpowers/plans/2026-05-23-business-approval-mvp.md` | 已完成业务审批模板、审批链、审批步骤和审批结果事件；不替代 Ops 运维审批，已接入 solution/AppHost/verify/readiness。 |
| #136 WMS | `docs/superpowers/specs/2026-05-23-wms-execution-mvp-design.md`、`docs/superpowers/plans/2026-05-23-wms-execution-mvp.md` | 已完成入库、出库、上架、拣货、盘点和 WCS adapter 边界；当前 Inventory posting 使用可替换 client，占位实现不阻塞共享接入。 |
| Wave 2 registration | `docs/superpowers/plans/2026-05-23-business-wave-2-registration-verify-readiness.md` | 已统一补齐 solution、AppHost、verify scripts、schema catalog、authorization matrix 和 readiness；聚合验证入口为 `scripts/verify-business-wave2-execution.ps1`。 |
| #143 Design System | `frontend/DESIGN/roadmaps/business-console-readiness.md`、`docs/superpowers/plans/2026-05-23-frontend-business-console-component-readiness.md` | Tabs、Sheet、Date、Chart、FileUpload 等组件先入 DESIGN 契约，再进入 `@nerv-iip/ui` 导出。 |

### Equipment Reliability / Wave 2.5 handoff

| Issue | Handoff docs | 说明 |
| --- | --- | --- |
| #129 IndustrialTelemetry | issue worker implementation + `scripts/verify-business-industrial-telemetry-mvp.ps1` | 已完成 tag 映射、报警规则、设备状态快照、报警 raise/clear、P0 OEE 聚合和采集汇总；P0 OEE 的 availability 按状态持续时间计算，performance/quality 为估算占位，响应标志在 P0 期间保持 true（无状态数据窗口下数值为 0 但仍非真实测量值）；公开报警事件契约位于 `backend/common/Contracts/Nerv.IIP.Contracts.IndustrialTelemetry`。 |
| #130 Maintenance | issue worker implementation + `scripts/verify-business-maintenance-mvp.ps1` | 已完成维修工单、保养计划、点检、停机原因、备件行和 `maintenance.AssetUnavailable`/`maintenance.AssetRestored` 事件；报警触发开单消费 #129 公共事件契约。 |
| Equipment Reliability registration | `scripts/verify-business-equipment-reliability.ps1` | 已统一补齐 solution、AppHost、verify scripts、schema catalog、authorization matrix 和 readiness；聚合验证入口为 `scripts/verify-business-equipment-reliability.ps1`。 |

### 业务平台 Wave 3 / Wave 4 planning

| Issue | Handoff docs | 说明 |
| --- | --- | --- |
| #137 ERP Procurement | `docs/superpowers/specs/2026-05-23-erp-procurement-sales-finance-mvp-design.md`、`docs/superpowers/plans/2026-05-23-erp-procurement-mvp.md` | 已创建 ERP 服务骨架并落地采购申请、RFQ、供应商报价、采购订单和采购收货；ERP 不拥有库存余额或 WMS 执行事实。 |
| #138 ERP Sales | `docs/superpowers/specs/2026-05-23-erp-procurement-sales-finance-mvp-design.md`、`docs/superpowers/plans/2026-05-23-erp-sales-mvp.md` | 已在 ERP 服务中落地商机、报价、销售订单和发货请求；WMS 仍拥有出库执行。 |
| #139 ERP Finance | `docs/superpowers/specs/2026-05-23-erp-procurement-sales-finance-mvp-design.md`、`docs/superpowers/plans/2026-05-23-erp-finance-mvp.md` | 已在 ERP 服务中落地 AP、AR、凭证和成本候选；凭证必须借贷平衡，完整总账月结后置。 |
| ERP registration | `docs/superpowers/plans/2026-05-23-business-wave-3-erp-registration-verify-readiness.md` | 已统一补齐 ERP solution、AppHost、IAM seed、schema catalog、readiness、README 和 verify scripts；本地端口为 5118。 |
| #77 Full-chain acceptance | `docs/superpowers/specs/2026-05-23-business-full-chain-acceptance-design.md`、`docs/superpowers/plans/2026-05-23-business-full-chain-acceptance.md` | P0 已完成收口：WMS/Inventory/WCS 已有 service-local live HTTP TestServer proof，MES production/capacity 和 ERP finance drill-down 支撑 surface 已有 focused tests，七条链路 acceptance evidence 纳入统一 verify 脚本。 |

## 环境前置

根据 netcorepal-cloud-framework 官方入门文档，首批后端实施需要先满足以下条件：

1. 安装 .NET 10 SDK，作为 Nerv-IIP 当前目标框架。
2. 安装 Docker 环境，用于本地调试、自动化测试和依赖服务联调。
3. 创建服务时显式生成 `net10.0`；后续等 netcorepal-cloud-framework 明确适配 .NET 11 后，再统一升级到 .NET 11。
4. 安装 NetCorePal.Template：`dotnet new install NetCorePal.Template`。
5. 创建服务前运行 `dotnet new netcorepal-web --help` 核对本机模板参数。
6. 平台领域服务优先使用 netcorepal 的 web 模板作为初始骨架，但命令必须显式指定 `--Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false`，详见 docs/architecture/backend-cleanddd-netcorepal-guidelines.md；运行时消息传输由 `Messaging:Provider` 选择，默认 `InMemory`。
7. 2026-05-17 已确认 NetCorePal.Template 3.2.0 支持 `PostgreSQL`、`GaussDB`、`DMDB` 等数据库参数；Nerv-IIP 默认落地 PostgreSQL profile，信创环境按 database profile 验证替换，不承诺无验证的完全无感切换。
8. 后续落地平台级 AppHost、Compose 生成和 Aspire Dashboard 时，需要安装 Aspire CLI；服务模板仍保持 `--UseAspire false`，避免生成服务级局部编排入口。
9. 第三阶段前端工具链需要 Node.js `>=22.18.0`、pnpm 11.1.2 和 Vite+ 0.1.21。仓库根 `.node-version` 保留 22.22.3，原因是 Node `22.17.x` 会在 Vite+ lint/fmt 路径读取 `vite.config.ts` 时触发 TS config 加载错误；Node.js 26 这类更新 Current 版本可以作为本地开发版本，不视为环境漂移阻塞。
10. 第五阶段起仓库包含本地 `dotnet-tools.json`，用于固定 `dotnet-ef` 版本。首次生成或检查迁移前运行 `dotnet tool restore`，再使用 `dotnet tool run dotnet-ef ...`，避免依赖开发者全局工具。
11. AppHub/Ops 生成 EF migration 时优先显式进入 PostgreSQL profile：设置 `Persistence__Provider=PostgreSQL` 和对应 `ConnectionStrings__AppHubDb` 或 `ConnectionStrings__OpsDb`；AppHub 也提供 design-time DbContext factory 作为迁移生成兜底，Ops 仍依赖 PostgreSQL profile 的 Web startup 解析。

## 包源恢复基线

1. backend 启用 Central Package Management，且 `backend/Directory.Build.props` 将 warning 视为 error。
2. 如果开发者全局 NuGet 配置同时启用多个 HTTP 包源，NuGet 会产生 `NU1507`；在本仓库中该 warning 会被提升为 restore error。
3. 仓库根 `NuGet.config` 使用 `<clear />` 后只保留 `nuget.org`，用于确保本地与自动化 restore 结果一致。
4. 如客户或离线环境需要镜像源，应通过受控环境配置或后续交付脚本生成，不修改仓库默认 `NuGet.config` 为私有镜像。
5. 验证脚本默认使用仓库根 `NuGet.config`，不要求开发者修改个人全局 NuGet 源。

## 共享契约落点

1. 平台与 Connector Host 公共协议契约的源码事实来源固定放在 backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol。
2. Ops 公共协议契约的源码事实来源固定放在 backend/common/Contracts/Nerv.IIP.Contracts.Ops。
3. 首批单仓实施时，backend/Nerv.IIP.sln 与 connector-hosts/Nerv.IIP.ConnectorHost.sln 可以共同引用这些公开契约项目，确保注册、心跳、状态同步、运维任务和动作结果 DTO 只有一份代码实现。
4. Platform SDK 的首批源码落点固定放在 backend/common/Sdk，当前已经按 Core、Auth、ConnectorProtocol、FileStorage、Ops 五个最小模块拆分。
5. 发布边界上，Connector Host 只依赖 Platform SDK、版本化公开契约包、OpenAPI 契约或等价契约，不依赖主平台源码或服务实现项目。
6. Connector Host 与主平台主版本必须对齐；同一主版本内，Connector Host 小版本可以低于主平台小版本。
7. connector-hosts/src/Nerv.IIP.ConnectorHost.Contracts 只保留 Connector Host 内部抽象，不复制公共协议 DTO。

## 首批工程创建顺序

### Wave 1. 底座

1. backend/Nerv.IIP.sln
2. backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol
3. backend/common/Contracts/Nerv.IIP.Contracts.Ops
4. backend/common/Sdk/Nerv.IIP.Sdk.Core
5. backend/common/Sdk/Nerv.IIP.Sdk.Auth
6. backend/common/Sdk/Nerv.IIP.Sdk.ConnectorProtocol
7. backend/common/Sdk/Nerv.IIP.Sdk.FileStorage
8. backend/common/Sdk/Nerv.IIP.Sdk.Ops
9. backend/common/Caching/Nerv.IIP.Caching
10. backend/common/Observability/Nerv.IIP.Observability
11. backend/common/Testing/Nerv.IIP.Testing

### Wave 2. 平台服务骨架

1. backend/services/AppHub/src/Nerv.IIP.AppHub.Web
2. backend/services/AppHub/src/Nerv.IIP.AppHub.Domain
3. backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure
4. backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web
5. backend/services/Iam/src/Nerv.IIP.Iam.Web
6. backend/services/Iam/src/Nerv.IIP.Iam.Domain
7. backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure
8. backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web
9. backend/services/FileStorage/src/Nerv.IIP.FileStorage.Domain
10. backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure
11. backend/services/Ops/src/Nerv.IIP.Ops.Web
12. backend/services/Ops/src/Nerv.IIP.Ops.Domain
13. backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure

### Wave 3. Connector Host 工程骨架

1. connector-hosts/Nerv.IIP.ConnectorHost.sln
2. connector-hosts/src/Nerv.IIP.ConnectorHost.Host
3. connector-hosts/src/Nerv.IIP.ConnectorHost.Application
4. connector-hosts/src/Nerv.IIP.ConnectorHost.Contracts
5. connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Abstractions
6. connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Docker

### Wave 4. 前端骨架

1. frontend/package.json、pnpm-workspace.yaml、vite.config.ts、tsconfig.base.json
2. frontend/apps/console
3. frontend/packages/api-client
4. frontend/packages/ui
5. frontend/packages/app-shell
6. frontend/packages/layer-base、layer-platform、auth、shared-types 作为长期边界保留，不在第三阶段空建；Console Auth 当前为 app 内实现，`packages/auth` 仅在出现跨应用登录复用时按 frontend-structure 留档边界抽取。

## 引用规则

1. *.Web 可以引用同服务的 *.Domain、*.Infrastructure 和 backend/common 下的窄共享库。
2. *.Domain 不引用 *.Infrastructure，也不引用其它服务的 Domain。
3. *.Infrastructure 可以引用同服务的 *.Domain 和 backend/common 下的窄共享库。
4. PlatformGateway.Web 不引用任何服务的 Domain 或 Infrastructure，只通过稳定契约、OpenAPI 客户端或明确的查询接口聚合数据。
5. ConnectorHost.Application 可以引用 Nerv.IIP.Sdk.Core、Nerv.IIP.Sdk.Auth、Nerv.IIP.Sdk.ConnectorProtocol、Nerv.IIP.Sdk.Ops、Nerv.IIP.Contracts.ConnectorProtocol、Nerv.IIP.Contracts.Ops 和 Connector Host 内部抽象，但不直接引用平台服务实现项目；发布后以 Platform SDK、版本化契约包或等价契约作为依赖边界。
6. SDK 项目只能引用公开契约、Sdk.Core、Sdk.Auth 或其它明确允许的 SDK 模块，不能引用服务 Web、Domain、Infrastructure 或数据库模型。

## 开工边界

### 第一迭代已落地范围

1. backend 与 connector-hosts 两套 solution 创建。
2. Platform SDK 的 Core、Auth、ConnectorProtocol、FileStorage、Ops 最小项目骨架。
3. IAM 用户、角色、权限、会话、外部客户端、Connector Host 凭证和授权授予的最小事实骨架。
4. FileStorage 文件元数据、上传会话、上传指令、下载授权、Upload Provider 抽象、FilePurposePolicy、scanStatus 和 object storage provider 边界的最小服务骨架；MinIO/S3 multipart 不进入当前 MVP。
5. AppHub registrations、heartbeats、state-snapshots 三个接口。
6. PlatformGateway 实例列表与实例详情查询接口。
7. Connector Host 通过 Nerv.IIP.Sdk.ConnectorProtocol 到 AppHub 的客户端与 Docker CLI-backed Docker Connector。
8. 统一 OpenTelemetry 接线、health、build info、基础 structured logging。
9. 统一 FusionCache 接线、Redis L2/backplane、缓存键命名和首批读侧缓存策略。
10. 以 docs/architecture/first-vertical-slice.md 作为首批纵切验收口径。
11. 以 docs/architecture/backend-cleanddd-netcorepal-guidelines.md 作为后端代码放置、事件转换、事务和测试验收口径。

### 第二迭代已落地范围

1. `Nerv.IIP.Contracts.Ops` 与 `Nerv.IIP.Sdk.Ops` 已落地，用于运维任务、claim/lease、任务详情和结果回传。
2. Ops.Web 已提供 operation task 创建、详情查询、claim/heartbeat/abandon 和 operation result 回传接口。
3. PlatformGateway 已提供实例 restart facade 与 operation task detail facade。
4. Connector Host 已提供 operation loop，可领取低风险任务、调用 Connector 执行，并回传结果。
5. Docker Connector 已支持通过 Docker CLI 执行真实 `lifecycle.restart`，并对 not found、timeout、daemon unavailable、permission denied 和 runtime failure 做失败分类。
6. Ops 当前会记录 OperationTask、OperationAttempt 和 AuditRecord 的内存态事实。
7. 以 docs/architecture/second-vertical-slice-ops.md 和 docs/superpowers/plans/2026-05-15-second-vertical-slice-low-risk-ops.md 作为第二阶段验收口径。

### 第三迭代已落地范围

1. Gateway 已提供控制台 OpenAPI 文档与稳定 operationId。
2. frontend 工作区已创建 pnpm workspace、Vite+ 根配置、console 应用、api-client、ui 和 app-shell 初版。
3. api-client 已通过 Hey API 从 Gateway OpenAPI 生成 types、fetch SDK 和 Pinia Colada query/mutation options。
4. console 首屏已展示实例列表、实例详情、restart 动作入口和 OperationTask 状态页。
5. Vite+ 质量门禁已覆盖 `check`、`lint`、`fmt`、`typecheck`、`test`、`build`。
6. 以 docs/architecture/third-vertical-slice-console.md、docs/superpowers/plans/2026-05-16-third-vertical-slice-console.md 和 scripts/verify-third-slice-console.ps1 作为第三阶段验收口径。

### 第四迭代已完成范围

1. AppHub 和 Ops 已作为 netcorepal/CleanDDD 迁移试点，落 Domain aggregate、Application command/query、Infrastructure repository/ApplicationDbContext 和 mediator-driven endpoint。
2. PostgreSQL 使用服务级 database 与 schema：AppHub 默认连接 `nerv_iip_apphub` 并使用 `apphub` schema，Ops 默认连接 `nerv_iip_ops` 并使用 `ops` schema；provider 选择只留在 Infrastructure/profile/test/deployment 层。
3. AppHub/Ops 已暴露 `/code-analysis`，用于查看 netcorepal 识别的命令、查询、聚合、事件和处理器流向。
4. `scripts/verify-fourth-slice-real-infra.ps1` 已作为第四阶段验收入口，默认通过 `infra/docker-compose.dev.yml` 拉起 PostgreSQL、Redis、RabbitMQ、MinIO 和 OpenTelemetry Collector；本机 PostgreSQL 默认端口为 `15432`，避免撞到本机已有 `5432`。RabbitMQ 是第四阶段历史验证资源，当前常规 PostgreSQL + 单机 messaging profile 可不依赖 RabbitMQ。
5. 平台级 AppHost 已落到 `infra/aspire/Nerv.IIP.AppHost`，覆盖 PlatformGateway、AppHub、IAM、Ops、FileStorage、Notification、BusinessMasterData、BusinessProductEngineering、BusinessInventory、BusinessQuality、BusinessMES、BusinessDemandPlanning、BarcodeLabel、BusinessApproval、WMS、BusinessIndustrialTelemetry、BusinessMaintenance、Connector Host、Console、PostgreSQL、Redis、MinIO、可选 OpenTelemetry Collector 和 Aspire Dashboard；RabbitMQ 仅在 `Messaging:Provider=RabbitMQ` 时加入拓扑。本地开发默认由 Aspire 注入 Dashboard OTLP endpoint，只有显式 `Observability:UseCollector=true` 时才启用 AppHost Collector 转发路径。AppHost 当前 build 通过，并为 AppHub/IAM/Ops/Notification 与 Business Wave 1/Wave 2/Equipment Reliability 服务使用独立 database resource。
6. PlatformGateway、Connector Host、Contracts/SDK 和 frontend console 不强行套完整 netcorepal 三项目模型；IAM 完整授权、FileStorage 上传下载、CAP outbox、通知和审批不进入本阶段实现范围。
7. `pwsh scripts/verify-fourth-slice-real-infra.ps1` 已在 Docker Desktop 环境下通过，最终输出 `Fourth vertical slice real infrastructure verified.`。

### 第五迭代已完成范围

1. AppHub 和 Ops 已增加 EF Core 初始迁移，并把 PostgreSQL profile 测试切换到 migration-based schema creation。
2. AppHub.Web 和 Ops.Web 启动期迁移受 `Persistence:AutoMigrate=true` 显式控制，且只允许在 Development 环境执行；非 Development 环境必须使用显式 migrator、发布脚本或 migration bundle。
3. 已增加 service-owned migration runner，供测试、脚本和后续安装流程复用。
4. 已增加 `scripts/verify-fifth-slice-persistence-foundation.ps1`，覆盖迁移、后端 solution、connector-host solution 和 SDK/契约回归。
5. 前端只在后端 OpenAPI 变化时机械生成 api-client 并跑质量门禁；不新增页面、路由、组件皮肤或 Design System 决策。
6. 前端 Design System 规划记录在 docs/architecture/frontend-design-system-planning.md，后续需要独立 Superpowers spec 后才能实施。
7. `pwsh scripts/verify-fifth-slice-persistence-foundation.ps1` 已通过，最终输出 `Fifth slice release-grade persistence foundation verified.`。

### 第六迭代已完成范围

1. AppHub/Ops 已补齐业务表 table comment，并通过 EF Core migration 固化为 schema metadata。
2. AppHub `application_instances.Metadata`、`application_instances.Capabilities` 和 Ops `operation_tasks.ParametersJson`、`operation_attempts.FailureJson` 已补齐 JSON 格式、生产方、消费方和兼容策略注释。
3. AppHub/Ops PostgreSQL profile 已显式把 `__EFMigrationsHistory` 配置到各自 service schema。
4. `Nerv.IIP.Testing` 已提供 schema convention helper，覆盖 business table comment、business column comment、JSON/text compatibility、string ID 和 migrations history schema 规则。
5. AppHub/Ops Web tests 已增加 schema convention tests；IAM、FileStorage、Notification 和 BusinessMES 已复用同一门禁，后续 Knowledge、AI Integration 和 Observability 索引建表前必须继续复用。
6. 客户发布包、安装脚本、备份恢复演练、seed 清单和诊断输出契约仍是后续交付工作，不属于本阶段完成范围。

### 第七迭代已完成范围

1. IAM 保留 InMemory profile，并新增 PostgreSQL profile，默认 schema 为 `iam`。
2. IAM 已有 `users`、`roles`、`role_permissions`、`memberships`、`user_sessions`、`connector_host_credentials` 和 seed manifest 等首批持久化表。
3. IAM 登录、refresh token rotation、logout/session revoke、`/me` 和 Connector Host credential validation 已可在 PostgreSQL profile 下运行。
4. IAM 初始 admin、platform admin role、seed permissions、membership 和 local Connector Host credential seed 具备幂等执行语义。
5. IAM schema convention tests 与 PostgreSQL profile tests 已作为后续 IAM 持久化变更门禁。
6. Gateway-wide permission enforcement 已覆盖现有 Console endpoints：实例列表、实例详情、restart 运维任务创建和 operation task detail 查询都会先通过 IAM internal authorization check。
7. Console Auth + shadcn-vue Baseline 已提供最小登录 UI、会话恢复、Gateway bearer 注入、路由守卫、退出登录和 shadcn-vue UI 基线；完整 IAM Admin Console 已在 Phase 8 交付。P2 已补 IAM 后端 OIDC callback/MFA hook/SSO session binding/resource-scope ABAC grant；完整 OAuth/OIDC 授权码服务器、WebAuthn、复杂策略语言和客户发布 bundle 仍属于后续阶段。

### Phase 8 已完成范围

1. Console Design System 已采用 Calm Control Plane 蓝色基线：`--primary`、`--ring`、`--accent`、`--sidebar-primary` 和 `--chart-1` 使用蓝色语义 token；旧 legacy tokens 只保留兼容用途。
2. shadcn-vue 新增 Table、Dialog、AlertDialog、Checkbox、Select、Pagination、Empty 等 IAM admin 所需 primitives，并通过 `@nerv-iip/ui` barrel 稳定导出。
3. IAM 后端已实现角色创建、角色权限 patch、权限 catalog、用户重置密码和会话撤销等管理能力。
4. PlatformGateway 已实现 Console IAM Admin facade：用户、角色、权限 catalog 和会话 endpoints 均先执行 IAM-backed permission enforcement，再把原 bearer token 转发给 IAM。
5. Gateway OpenAPI 已固定 11 个 Console IAM operation IDs，并已再生成 `frontend/packages/api-client` 的 types、fetch SDK 和 Pinia Colada options。
6. Console 已提供 `/iam/users`、`/iam/roles`、`/iam/sessions` 三个受保护页面，页面逻辑集中在 `frontend/apps/console/src/composables/useIamAdmin.ts`，只消费 generated Gateway api-client 稳定导出。
7. IAM admin 单元测试、Gateway facade 测试、frontend page/composable 测试和 Playwright E2E 覆盖了登录后进入 Users、Roles、Sessions 的基础路径，并补齐用户创建、编辑、禁用、重置密码、角色创建、角色权限更新、会话 revoke 和 403 permission-denied safe state。
8. 2026-05-20 浏览器验证补充：`frontend/apps/console/e2e/iam-admin.spec.ts` 使用 mock `/api/console/v1/auth/*` 与 `/api/console/v1/iam/*` 响应，在 desktop `1366x900/1366x1200` 与 mobile `390x844` 下访问 `/iam/users`、`/iam/roles`、`/iam/sessions`，检查无横向溢出、可见文本无明显重叠、当前导航蓝色、Create user/Create role primary actions 的真实 computed background/border/text 体现蓝色主动作、Users search input focus ring/border/outline 体现蓝色 token、Enabled/Active success badge 不使用 blue primary、Create user/Create role/Revoke session 对话框具备可访问标题。

### Phase 8 Task 9 验证记录

1. `pnpm -C frontend --filter @nerv-iip/console e2e -- iam-admin.spec.ts` 在未设置 Playwright 浏览器路径时失败，原因为 Playwright managed browser executable 缺失；这是本机 Playwright browser install 环境阻塞。
2. 设置 `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` 指向本机可用 Chromium 后运行 `pnpm -C frontend --filter @nerv-iip/console e2e -- iam-admin.spec.ts` 通过，3/3 Playwright tests passed。
3. `dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj` 通过，34 passed；一次并行验证曾遇到 `Nerv.IIP.Observability.dll` 被其它 dotnet 进程占用，单独重跑通过。
4. `dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj` 通过，30 passed。
5. `pnpm -C frontend test` 通过，19 files passed、2 skipped，76 tests passed、2 skipped；IAM users/sessions/roles focused test 通过，17/17 passed。
6. `pnpm -C frontend typecheck` 通过；`pnpm -C frontend build` 通过。
7. `dotnet test backend/Nerv.IIP.sln --no-restore` 通过；`dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln --no-restore` 通过，Connector Host Docker integration test 按环境条件 skip。
8. `pnpm -C frontend lint` 通过，剩余 1 个范围外 warning：`apps/console/src/composables/useConsoleOperations.ts` unused `InstanceListResponse` import。
9. `pnpm -C frontend check` 与 `pnpm -C frontend fmt` 仍被既有范围外格式问题阻塞；本次触碰的 7 个前端文件已单独运行 `pnpm -C frontend exec vp check ...` 并通过。Playwright 失败产物 `frontend/apps/console/test-results/` 仅为测试 artifact，提交前清理。
10. `pwsh scripts/verify-iam-persistent-auth-foundation.ps1` 因本机 Docker Desktop Linux engine 不可用阻塞：无法连接 `npipe:////./pipe/dockerDesktopLinuxEngine`，无法拉取 `postgres:17`。
11. `pwsh scripts/verify-third-slice-console.ps1` 因其调用 `verify-second-slice-ops.ps1`，同样被 Docker daemon 不可达阻塞；脚本明确要求 Docker CLI 和可达 Docker daemon 用于真实容器发现与 restart。

### FileStorage MVP 已完成范围

1. FileStorage Web 已提供公开 API 子集：`POST /api/files/v1/upload-sessions`、`POST /api/files/v1/upload-sessions/{uploadSessionId}/complete`、`GET /api/files/v1/files/{fileId}`、`POST /api/files/v1/files/{fileId}/download-grants`。
2. 默认上传/下载策略仍为 `server-proxy` metadata stub，返回平台控制 placeholder 路径；设置 `FileStorage:UploadProvider=tus` 后创建上传会话会返回 tus 上传指令 `/api/files/v1/tus/{uploadSessionId}`，并支持 `HEAD` 查询 `Upload-Offset`、`Upload-Length`、`Upload-Expires`，`PATCH` 按 offset 追加字节、暂停后续传、size 上限校验、`Upload-Checksum` sha256 chunk 校验、过期未完成本地字节清理，以及 download grant content endpoint 下载本地 tus 字节。MinIO/S3 multipart 仍不进入 MVP。
3. 公开响应和测试覆盖 `objectKey`/`object_key` 不泄漏；内部 object key 只保留在 FileStorage Domain/Infrastructure 实现与持久化模型中。
4. `backend/common/Contracts/Nerv.IIP.Contracts.FileStorage` 已提供 Web/SDK 共享公开 DTO；`Nerv.IIP.Sdk.FileStorage` 已提供 `IFileStorageClient` 与 `HttpFileStorageClient`，覆盖创建上传会话、完成上传会话、读取文件元数据和创建下载授权。
5. FileStorage Infrastructure 已新增 `ApplicationDbContext`、`filestorage` schema、`stored_files`、`upload_sessions`、`download_grants`、`20260521061426_InitialFileStorageSchema` migration 和 `FileStorageSchemaConventionTests`。
6. 默认 WebApplicationFactory 和本地启动仍使用 in-memory API store；显式 `Persistence:Provider=PostgreSQL` 时启用 PostgreSQL-backed API service，且 `Persistence:AutoMigrate=true` 时执行 FileStorage migration runner。tus 字节当前落本地 `FileStorage:Tus:RootPath`。
7. 2026-05-21 验证通过：`dotnet test backend/tests/Nerv.IIP.Contracts.FileStorage.Tests/Nerv.IIP.Contracts.FileStorage.Tests.csproj --no-restore`、`dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore`、`dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore`。
8. 2026-06-03 PlatformGateway 已新增 `/api/console/v1/files/**` 管理 facade，覆盖上传会话创建、上传会话 complete、文件元数据详情和 download grant 创建；Gateway 对外仍返回 `ResponseData<T>`，下游通过 internal service token 调用 FileStorage，并将 tus/download grant 相对传输 URL 重写为 Console facade 相对路径，不暴露内部 object key 或直连对象存储 URL。Console 页面和 generated api-client 刷新仍按后续前端接线流程处理。
9. 2026-05-24 本地 tus hardening 已补齐：超出声明大小的 `PATCH` 返回 `413` 且不推进 offset，tus `Upload-Checksum` mismatch 返回 `460` 且不写入，过期未完成上传在后续 `HEAD`/`PATCH` 被拒绝并清理本地字节，complete 会校验实际本地大小和可选 checksum；MinIO/S3 multipart 放到 post-MVP 部署联调。

### 当前初步使用方式

1. 根目录 `.\nerv.ps1 bootstrap` 已成为有网空白机器的预检/restore/本地 secrets 初始化入口；Windows 有网机器可用 `.\nerv.ps1 bootstrap -InstallMissing` 补齐缺失工具链，Docker Desktop 首次安装后仍需人工启动 daemon。根目录 `.\nerv.ps1 dev` 已成为主平台本地联调入口；`.\nerv.ps1 ports` 输出标准本地端口矩阵。
2. 平台 HTTP 服务端口收敛到 `5100-5125`，其中 PlatformGateway 使用 `5100`，Console 使用 `5105` 而不是 Vite 默认 `5173`；Business Wave 1 服务使用 `5107-5111`，Business Wave 2 服务使用 `5112-5115`：DemandPlanning `5112`、BarcodeLabel `5113`、BusinessApproval `5114`、WMS `5115`；Equipment Reliability 服务使用 `5116-5117`：IndustrialTelemetry `5116`、Maintenance `5117`；BusinessERP 使用 `5118`；BusinessGateway 使用 `5119`；BusinessScheduling 使用 `5120`；BusinessConsole 使用 `5125`。
3. 本地 MinIO 运行镜像使用 `pgsty/minio:RELEASE.2026-04-17T00-00-00Z`。
4. 运行 `pwsh scripts/verify-first-slice.ps1` 可验证 backend 与 connector-hosts 的 restore、build、test，以及 AppHub 到 PlatformGateway 的第一条本地纵切。
5. 运行 `pwsh scripts/verify-second-slice-ops.ps1` 可验证 Gateway、Ops、Connector Host 和 Docker Connector 的低风险 restart 闭环。
6. 运行 `pwsh scripts/verify-third-slice-console.ps1` 可验证 Gateway OpenAPI 导出、前端 api-client 生成、Vue 控制台 typecheck/test/build。
7. 运行 `pwsh scripts/verify-third-slice-console.ps1 -UsePostgres` 可在 Development PostgreSQL profile 下复跑第三阶段链路，前提是本地 PostgreSQL 可用；默认 messaging profile 为 InMemory。非 Development 环境必须显式使用 `Messaging:Provider=RabbitMQ`，并准备本地或部署环境 RabbitMQ。可通过 `NERV_IIP_APPHUB_POSTGRES`、`NERV_IIP_IAM_POSTGRES` 与 `NERV_IIP_OPS_POSTGRES` 分别覆盖服务连接串。
8. 运行 `pwsh scripts/verify-fourth-slice-real-infra.ps1` 可拉起本地依赖并执行第四阶段真实基础设施门禁；脚本会重建 `nerv_iip_apphub_verify`、`nerv_iip_iam_verify` 和 `nerv_iip_ops_verify` 验证库，避免共享库或旧数据影响结果。
9. 运行 `pwsh scripts/verify-fifth-slice-persistence-foundation.ps1` 可验证 AppHub/Ops 迁移发布底座和后端 SDK/契约回归。
10. 运行 `pwsh scripts/verify-iam-persistent-auth-foundation.ps1` 可验证 IAM PostgreSQL profile、迁移、seed、登录/刷新/退出、`/me`、Connector Host credential validation 和后端回归。
11. 运行 `pwsh scripts/check-script-governance.ps1` 可验证脚本解析、分类声明、高风险命令 wrapper 和 legacy exemption 是否仍受控。
12. 运行 `pwsh scripts/check-script-compatibility.ps1` 可在 macOS/Linux 上记录脚本兼容门禁证据；Windows 本地只能使用 `-AllowWindows -FastOnly` 做 smoke，不作为兼容性声明依据。
13. 运行 AppHub/Ops/IAM/FileStorage/Notification/BusinessMES schema convention tests 可验证当前已迁移服务的 schema metadata 门禁。
14. 运行 `pwsh scripts/verify-business-master-data-realignment.ps1` 可验证 BusinessMasterData realignment 的 Domain、Web、schema convention 和 IAM seed 基线。
15. 运行 `pwsh scripts/verify-business-product-engineering-mvp.ps1` 可验证 BusinessProductEngineering release facts 的 Domain、Web、schema convention、API contract 和 IAM seed 权限基线。
16. 运行 `pwsh scripts/verify-business-inventory-mvp.ps1` 可验证 BusinessInventory MVP 的 Domain、Web、schema convention 和内部服务 API contract 基线。
17. 运行 `pwsh scripts/verify-business-quality-inspection-mvp.ps1` 可验证 BusinessQuality inspection MVP 的 Domain、Web、contracts 和 IAM seed 权限基线。
18. 运行 `pwsh scripts/verify-business-mes-execution-mvp.ps1` 可验证 BusinessMES persistence MVP 的 Domain、Web、schema convention 和 API contract 基线。
19. 运行 `pwsh scripts/verify-business-wave1-foundation.ps1` 可一次性验证 Business Wave 1 五个业务服务专用脚本和平台级 AppHost 编译。
20. 运行 `pwsh scripts/verify-business-demand-planning-mrp-mvp.ps1` 可验证 BusinessDemandPlanning MRP MVP 的 Domain、Web、schema convention 和 API contract 基线。
21. 运行 `pwsh scripts/verify-business-barcode-label-mvp.ps1` 可验证 BarcodeLabel MVP 的 Domain、Web、schema convention 和 API contract 基线。
22. 运行 `pwsh scripts/verify-business-approval-mvp.ps1` 可验证 BusinessApproval MVP 的 Domain、Web、schema convention 和 API contract 基线。
23. 运行 `pwsh scripts/verify-business-wms-execution-mvp.ps1` 可验证 WMS execution MVP 的 Domain、Web、schema convention 和 API contract 基线。
24. 运行 `pwsh scripts/verify-business-wave2-execution.ps1` 可一次性验证 Business Wave 2 四个业务服务专用脚本和平台级 AppHost 编译。
25. 运行 `pwsh scripts/verify-business-industrial-telemetry-mvp.ps1` 可验证 BusinessIndustrialTelemetry MVP 的 Domain、Web、schema convention 和 API contract 基线。
26. 运行 `pwsh scripts/verify-business-maintenance-mvp.ps1` 可验证 BusinessMaintenance MVP 的 Domain、Web、schema convention 和 API contract 基线。
27. 运行 `pwsh scripts/verify-business-equipment-reliability.ps1` 可一次性验证 Equipment Reliability 两个业务服务专用脚本和平台级 AppHost 编译。
28. 运行 `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore` 可验证平台级 AppHost 编译。
29. 运行 `pwsh scripts/verify-business-full-chain-acceptance.ps1` 可验证 #77 P0 full-chain acceptance：WMS service-local live HTTP TestServer proof、WMS/MES/ERP 支撑 public-surface focused tests、七条链路公开 API contract surface、统一 fixture/correlation、event recorder 和 HTTP response envelope helper；该脚本使用 governed `ScriptAutomation.ps1` helper，当前不启动 Docker/PostgreSQL/RabbitMQ、外部 WCS 设备或长驻服务进程。
30. 运行 `pwsh scripts/verify-business-scheduling-aps-lite.ps1` 可验证 #206 APS lite / BusinessScheduling：Scheduling contracts、Domain lifecycle、Web/API/schema convention tests 和平台级 AppHost build。该脚本默认不启动 Docker/PostgreSQL/RabbitMQ 或 BusinessGateway facade；`-SkipRestore` 可用于已 restore 的本地工作区。运行 `pwsh scripts/verify-business-iiot-runtime-facts-aps-mes.ps1` 可验证 #207 设备 IIoT runtime facts 与 APS/MES 联动的 EquipmentRuntime contracts、IndustrialTelemetry/Maintenance/Scheduling/MES Web tests、BusinessGateway tests、api-client 生成和 Business Console typecheck/test/build；`-SkipRestore` 可用于已 restore 的本地工作区，`-SkipFrontend` 可只跑 backend focused tests。
31. 运行 `dotnet test backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/Nerv.IIP.Contracts.IntegrationEvents.Tests.csproj --no-restore` 可验证 #170 当前已纳入的 ADR 0011 集成事件 envelope 契约门禁。
32. 运行 `dotnet test backend/tests/Nerv.IIP.Messaging.CAP.Tests/Nerv.IIP.Messaging.CAP.Tests.csproj --no-restore` 可验证 #170 当前已纳入的 consumer guard、envelope 版本校验、in-memory DLQ/replay store 和共享 EF-backed persistent DLQ store 行为。
33. 运行 `pwsh scripts/verify-infra-cap-outbox-acceptance.ps1 -PostgresConnectionString "<postgres>" -Profile inmemory|rabbitmq|all` 可 opt-in 验证 #171 Notification 真实消费者的 CAP outbox/inbox 发布订阅；`rabbitmq`/`all` profile 需要 RabbitMQ 可达，默认 `inmemory` profile 不要求 broker。
34. 运行 `pwsh scripts/verify-business-performance-baseline.ps1 -ConnectionString "<postgres>" -Scenario inventory|mes|erp|all -MaxElapsedMilliseconds <ms>` 可 opt-in 运行业务性能基线 release gate；脚本写出 metrics JSONL 与 summary JSON，也可用 `-InventoryMaxElapsedMilliseconds`、`-MesMaxElapsedMilliseconds`、`-ErpMaxElapsedMilliseconds` 覆盖分场景阈值。测试只接受真实 PostgreSQL 连接串，未设置 `NERV_IIP_PERF_POSTGRES` 时直接 skip，脚本未提供连接串时失败提示，不使用 InMemory 生成性能结论。
35. 运行 `pwsh scripts/verify-production-release-rehearsal.ps1 -Profile dependencies` 可用 disposable Compose project 验证生产依赖层启动和 PostgreSQL/Redis/MinIO smoke；运行 `pwsh scripts/verify-production-release-rehearsal.ps1 -Profile platform-smoke` 会额外构建核心平台镜像、启动 AppHub/IAM/Ops/FileStorage/Notification/Gateway/BusinessGateway，执行显式 Development auto-migration smoke 和 `/health` 检查。该脚本默认使用独立 project name 和高位端口，结束后清理自有 project/volumes；`-KeepRunning` 仅用于人工诊断。
36. 运行 `pnpm -C frontend check`、`lint`、`fmt`、`typecheck`、`test`、`build` 可单独验证前端工作区质量门禁；第五阶段只有发生 OpenAPI/api-client 变化时才需要触发。
37. Business Console 可用以下 focused gate 验证：`pnpm -C frontend --filter @nerv-iip/business-console typecheck`、`pnpm -C frontend --filter @nerv-iip/business-console test`、`pnpm -C frontend --filter @nerv-iip/business-console build`；本机已安装 Chrome/Chromium 时，可设置 `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` 后运行 `pnpm -C frontend --filter @nerv-iip/business-console e2e -- business-console.spec.ts`。MES PC 工作台可运行 `scripts/verify-business-console-mes-pc-workbench.ps1` 覆盖 MES 服务测试、BusinessGateway 测试、api-client codegen/typecheck/test 和 Business Console typecheck/test/build；需要浏览器 smoke 时追加 `-E2E -ChromiumExecutablePath "<chrome.exe>"`。
38. AppHub 当前提供 registration、heartbeat、state-snapshot 和内部实例查询接口。
39. PlatformGateway 当前提供实例列表、实例详情、实例 restart、operation task detail、Console Notification 和 Console IAM Admin facade；这些 Console API 需要 bearer token，并由 Gateway 转发到 IAM 做权限校验。BusinessGateway 当前提供 Business Console MasterData、ProductEngineering、Inventory、Quality、DemandPlanning、Scheduling、Equipment runtime facts、WMS 收货/出库/WCS 读面、IndustrialTelemetry tags/alarm-rules/alarms/history/OEE/runtime-availability、Maintenance 工单/计划/availability-windows 和 MES facade，使用用户 bearer token 到 IAM 做权限校验，并使用 internal service token 调用业务服务。
40. Connector Host 当前可通过 Platform SDK 将 Docker Connector 的发现结果上报到 AppHub，并通过 Ops SDK 拉取和回传低风险动作。
41. 当前实现用于本地开发、接口联调和 PoC/私有化部署基线，已包含 IAM 用户/角色/权限 catalog/会话管理控制台、ExternalClient client_credentials、OIDC callback/MFA hook/SSO session binding/resource-scope ABAC grant、Ops 高风险动作 approval gate、Notification 站内消息/任务纵切与 Console facade、Notification/AppHub/BusinessMES/BusinessMaintenance PostgreSQL persistent DLQ、BusinessMasterData Layer 0 realignment、BusinessProductEngineering release facts、BusinessInventory MVP、BusinessQuality inspection MVP、BusinessMES persistence MVP（含制程不良记录、班次交接和对应真实分页查询）、BusinessDemandPlanning MRP MVP、BarcodeLabel MVP、BusinessApproval MVP、WMS execution MVP、BusinessIndustrialTelemetry MVP、BusinessMaintenance MVP、BusinessERP MVP、BusinessScheduling/APS lite 独立服务、BusinessGateway 控制台 facade、BusinessConsole 独立 Vite Plus app、#166-#169 首批业务页面、ProductEngineering/DemandPlanning 窄化业务页面、MES PC 标准执行工作台、设备运行事实 route-ready 页面、IndustrialTelemetry tags/alarm-rules/alarms/history/OEE/runtime-availability facade 和 Maintenance 工单/计划/availability-windows facade，以及 FileStorage contracts/SDK、metadata API、PostgreSQL-backed service、本地 tus `HEAD`/`PATCH` 上传与 download content endpoint；不包含完整 OAuth/OIDC 授权码服务器、WebAuthn、复杂 ABAC 策略语言、Ops 审批 Console 管理入口、Notification 外部通道 provider、BusinessGateway 的 ERP/WMS/BarcodeLabel/BusinessApproval 正式 facade、IndustrialTelemetry rule/OEE 正式页面、Maintenance 正式页面、高级 APS 优化器、BusinessConsole 的高级报表/甘特/跨域工作流、PDA/mobile 或 MinIO/S3 multipart。
42. 当前部署交付已经有平台级 AppHost 编译入口、完整平台 Compose overlay、统一 Docker build flow、生产 env 样例、AppHost release-install 启动脚本、zip 包生成脚本、部署产物静态验证、disposable Compose 发布演练入口和性能阈值化 release gate；Windows Service/systemd 注册器、客户现场备份恢复演练和生产日志长期查询后端仍按 deployment-baseline 后续深化。

### 可以并行但不阻塞开工的事项

1. Ops 持久化 outbox、复杂失败重试、审批 Console 管理入口和生产级调度策略。
2. 高风险动作审批的人工确认 UI、细粒度权限 scope 和批量/恢复类动作策略。
3. Sdk.Observability 的完整实现和诊断附件链路。
4. AI Integration 与 Knowledge 的具体代码骨架。
5. Notification 的偏好/订阅、外部通道 provider、限流、模板映射和 DLQ replay 管理入口；边界口径应遵守 docs/architecture/notification-baseline.md。
6. KnowledgeSource 的完整管理后台，但生命周期口径应遵守 docs/architecture/knowledge-source-lifecycle.md。
7. 复杂 IAM 授权能力，包括跨组织委派、临时授权、完整 OAuth/OIDC 协议矩阵、MFA、SSO、细粒度 ABAC 与第三方应用市场。
8. 超出 Console Auth + shadcn-vue Baseline 的前端视觉系统、组件皮肤、主题和导航策略；需要先按 docs/architecture/frontend-design-system-planning.md 的 Future Spec Triggers 创建独立设计规格。
9. Compose 发布产物、安装包和整合安装脚本，口径见 docs/architecture/deployment-baseline.md 与 docs/architecture/database-release-runbook.md。
10. 剩余 legacy 脚本继续迁移到 docs/architecture/script-automation-governance.md 的 helper 和门禁；剩余顺序是 OpenAPI 导出、第三阶段 console、第二阶段 Ops、第一阶段 slice。

## 开工验收标准

满足以下条件时，说明仓库已经从“规划阶段”进入“可持续实施阶段”：

1. dotnet restore backend/Nerv.IIP.sln 通过。
2. dotnet build backend/Nerv.IIP.sln 通过。
3. dotnet restore connector-hosts/Nerv.IIP.ConnectorHost.sln 通过。
4. dotnet build connector-hosts/Nerv.IIP.ConnectorHost.sln 通过。
5. AppHub.Web 可接收 registration、heartbeat、state snapshot。
6. Connector Host 可向本地 AppHub 成功发送至少一组注册、心跳、状态同步请求。
7. PlatformGateway 能查询到至少一个被注册的实例事实。
8. Ops.Web 可创建 restart OperationTask，并接收 Connector Host 回传的 OperationResult。
9. Connector Host 可领取 pending task，执行 `lifecycle.restart`，并在结果回传失败时先做本轮内存重试。

## 结论

Nerv-IIP 已经完成第一迭代接入查询纵切、第二迭代低风险动作闭环、第三迭代控制台纵切、第四迭代真实基础设施门禁、第五迭代迁移发布底座、第六迭代 schema governance hardening、第七迭代 IAM Persistent Auth Foundation，并已完成 Phase 8 IAM Admin Console、Calm Control Plane 蓝色 Design System 基线、FileStorage MVP 当前子集、Business Console MVP、MES PC 标准执行工作台、#77 P0 full-chain acceptance、事件可靠性基线、ExternalClient client_credentials、生产安全硬化、生产部署产物、opt-in 发布演练和性能阈值化 release gate：backend/common、Iam、FileStorage、AppHub、PlatformGateway、BusinessGateway、Ops、Connector Host、Docker Connector、frontend console、frontend business-console、api-client、ui、app-shell、infra/aspire 与 infra/compose 的工程结构与验证链路已经存在。Console Auth + shadcn-vue Baseline 已提供最小登录 UI、会话恢复、Gateway bearer 注入、路由守卫、退出登录和 shadcn-vue UI 基线；Phase 8 在此之上补齐用户/角色/权限 catalog/会话管理页面、Gateway Console IAM Admin facade、11 个稳定 Console IAM operation IDs 和 generated api-client 消费边界；Business Console MVP 在独立 `frontend/apps/business-console` 中交付 #166-#169 的 MasterData、Inventory、Quality 和 MES 首批页面，并已补入 ProductEngineering 工程资料、DemandPlanning 计划工作台、ERP 采购与供应窄化页、MES PC 标准执行页面、APS lite facade 和设备运行事实 route-ready 页面，通过 BusinessGateway `/api/business-console/v1/**` facade 与 generated business-console api-client 消费业务服务；当前 `/erp` 已通过 BusinessGateway ERP Procurement facade 消费采购订单供应明细，不代表 ERP 销售、财务或完整 ERP 前端已交付；MES PC 当前已扩展生产驾驶舱、基础准备、生产计划、计划与工单、工单详情、齐套与物料、派工、工序执行、在制、报工与完工、质量与不良、完工入库、规则排程、设备与停机、班次交接、追溯和产能影响视图；FileStorage 已补齐公开 contracts/SDK、server-proxy metadata API、PostgreSQL-backed API service、`filestorage` schema、初始 migration、schema convention tests、本地 tus `HEAD`/`PATCH` 上传与 download content endpoint，并已补齐本地 tus size/checksum/expiration hardening。脚本自动化治理已补入 ADR 和架构说明，后续新增或修改脚本必须进入分类、副作用、helper、日志、进程清理和门禁口径。#188 到 #207 的 MES operational foundation reset 已持续收口，APS 与设备 IIoT 排程边界以 ADR 0014 为准；Task 9 full verification 另行执行。#142 MinIO/S3 multipart、PDA/mobile、事件可靠性 hardening 与部署发布深化继续并行；#78 作为甘特/RFC 技术评估入口并消费 APS 输出。真实持久化先主推 PostgreSQL，同时用 database profile 约束后续 GaussDB/DMDB 等信创替换成本。数据库建表和注释规范、schema catalog、Observability baseline 与数据库发布 runbook 已有第一版，AppHub/Ops/IAM/FileStorage table comment、JSON/text 注释、migrations history schema 配置和 convention tests 已作为后续持久化服务的门禁样本。后续前端功能必须沿用 Console Auth + shadcn-vue Baseline 已选 registry、preset、token、Calm Control Plane 蓝色语义、`@nerv-iip/ui` 导出边界和 `docs/architecture/frontend-navigation-map.md` 的导航升级门禁。后续任务继续参考 docs/architecture/third-vertical-slice-console.md、docs/architecture/fourth-vertical-slice-real-infra.md、docs/architecture/frontend-design-system-planning.md、docs/architecture/frontend-navigation-map.md、docs/architecture/database-schema-conventions.md、docs/architecture/database-schema-catalog.md、docs/architecture/database-release-runbook.md、docs/architecture/script-automation-governance.md、docs/architecture/observability-baseline.md、docs/adr/0009-database-migration-release-and-seed-strategy.md、docs/adr/0010-automation-script-trusted-execution-governance.md、docs/adr/0014-aps-and-iiot-scheduling-boundary.md、docs/superpowers/specs/2026-05-23-business-full-chain-acceptance-design.md、docs/superpowers/plans/2026-05-23-business-full-chain-acceptance.md 与 docs/architecture/deployment-baseline.md。
