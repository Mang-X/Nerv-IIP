# 前端导航地图与分期

本文档是 Nerv-IIP 前端导航的长期约束，覆盖主平台 Console、Business Console 与 Business PDA。代码事实复核日期为 2026-07-02（PDA 三域一线闭环已落地：WMS 收货入库/复核发货/拣货/上架/盘点、MES 工序执行/报工/领料/完工入库、设备运维报修/点检/报警查看；Business Console 已挂 ERP sales/finance、WMS putaway/picking/counts、Maintenance work-orders/plans、BarcodeLabel rules/templates）；当前服务状态仍以 `docs/architecture/implementation-readiness.md` 为入口。任何修改“已落地/过渡/后端已落地/前端待建/规划”状态的 PR，必须同步更新本日期并在 PR 中列出校验命令。

## 状态标签

| 标签 | 含义 |
| --- | --- |
| 已落地 | 路由、Gateway facade、generated api-client 消费和页面已存在。 |
| 过渡 | 路由已存在，但页面仍是诊断、聚合壳、演示数据或非最终交互。 |
| 后端已落地/前端待建 | 领域服务或后端事实已存在，但缺 Gateway facade、api-client、页面或正式导航入口。 |
| 规划 | 服务、facade 或页面尚未落地，不能在当前可见菜单中提前暴露。 |

## 导航硬约束

1. 主平台 Console 只承载 IAM、AppHub、Ops、FileStorage、Notification、系统监控等通用控制面；不得加入 MES、WMS、ERP、PDM/PLM、CMMS 等业务 CRUD 或业务工作流。
2. Business Console 只通过 `backend/gateway/BusinessGateway` 的 `/api/business-console/v1/**` facade 消费业务服务；不得直连业务服务 URL，也不得 deep import generated client。
3. 后端 bounded context 不是前端用户操作边界。前端页面可以按角色任务聚合多个服务事实，例如仓储作业页同时显示 Inventory 可用量，MES 设备异常同时串到设备监控和维修报修。
4. 逻辑菜单最多三级：域 -> 模块 -> 页面。当前 `@nerv-iip/app-shell` 只支持两级可见侧边栏（分组 + 子项），所以三级菜单在文档中表达为能力目录边界；实现前不得用页面内卡片或临时嵌套菜单绕过壳层。
5. 长期导航清单是能力目录，不是所有用户的默认侧边栏。实际可见导航必须按 RBAC、角色画像、组织/环境上下文和 feature flag 裁剪；普通操作员应只看到工作台和少数任务入口。
6. 超级管理员、厂长、生产副总等跨域用户需要“近期使用”“星标收藏”和全局搜索，而不是暴露完整长菜单作为唯一导航。
7. Business Console 必须提供全局命令/对象搜索能力作为长期导航能力，支持按菜单名、业务对象号、物料、工单、采购单、销售单、设备和批次进入对应页面或详情。
8. PDA/mobile 明确是独立任务范式，不复用 PC 的复杂菜单树。PDA 首页应优先是“我的任务”、快捷应用墙和扫码直达，扫码结果直接唤起报工、收货、拣货、盘点、巡检、报修等操作页。
9. 页面内必须支持上下文穿透。跨域事实通过链接、Drawer、Sheet、关联侧栏或对象详情跳转串起来，避免用户回到主菜单手工切换。
10. WMS 与 Inventory 在 UI 上必须融合：仓储收货、上架、拣货、复核、发货、盘点页面应能就地查看关联物料、库位、批次、可用量、冻结和预留信息；Inventory 仍保留台账、余额、批次和库存分析等事实视角。
11. 创建、编辑、审批、关闭、确认、打印、报工、入库、排程运行等动作不作为菜单项，应在列表、详情、抽屉或对话框中承载。
12. 对象详情页不是常驻菜单项。物料详情、实例详情、工单详情、订单详情等应由列表行、搜索结果或关联对象进入。
13. 页面内 Tabs 不进入菜单树。物料采购属性、生产属性、销售属性，或工单的工序、用料、质量等，均属于页面布局。
14. 后端服务存在不等于前端可见菜单已交付。菜单项升级为“已落地”前，至少需要 Gateway facade、OpenAPI/api-client 生成、页面、权限和 focused verification。
15. 带移动端优先属性的页面可以在能力目录中保留位置，但不得按当前 PC 交付声明完成。
16. PC 端长期导航采用顶部-侧边 T 型架构：顶部承载经过权限裁剪后的一级能力区，左侧只显示当前能力区内的模块和页面。顶部导航不得换行或挤压内容，超出项必须进入“更多”或应用切换器。
17. 当前 `@nerv-iip/app-shell` 只有侧边栏 `navItems` 契约，尚不支持顶部一级域导航；实现 T 型导航前必须先演进 app-shell 公共 API 和测试，不得在业务页面局部硬拼第二套导航。

## 当前代码事实

### 端口

| 入口/服务 | 端口 |
| --- | --- |
| PlatformGateway | 5100 |
| Console | 5105 |
| BusinessMasterData | 5107 |
| BusinessProductEngineering | 5108 |
| BusinessInventory | 5109 |
| BusinessQuality | 5110 |
| BusinessMES | 5111 |
| BusinessDemandPlanning | 5112 |
| BarcodeLabel | 5113 |
| BusinessApproval | 5114 |
| WMS | 5115 |
| BusinessIndustrialTelemetry | 5116 |
| BusinessMaintenance | 5117 |
| BusinessERP | 5118 |
| BusinessGateway | 5119 |
| BusinessScheduling | 5120 |
| Business Console | 5125 |
| Business PDA | 5126 |
| Design System | 5180 |

### Gateway 覆盖

| Gateway | 已有 facade | 尚未有正式 facade/页面的重点能力与导航优先级 |
| --- | --- | --- |
| PlatformGateway | Console auth、AppHub 实例列表/详情、Ops restart 与任务详情、IAM 用户/角色/权限 catalog/会话、Notification 消息/任务、FileStorage 上传会话/元数据/download grant 管理 facade。 | P1：Ops 任务列表/审批页、服务健康聚合；P2：FileStorage 管理页、审计日志、DLQ 管理、ExternalClient；P3：SSO/OIDC/MFA、性能基线和渠道配置。 |
| BusinessGateway | MasterData SKU/资源 lifecycle、typed list、workshop/team-member、BusinessPartner 多角色/taxId；Inventory 可用量/移动/盘点、Quality 检验/NCR、ProductEngineering MBOM/工艺路线/生产版本、DemandPlanning 需求/MRP/建议、ERP Procurement/Sales/Finance 窄化 facade、Scheduling/APS lite、设备运行事实、MES PC 工作台、WMS 收货/出库/上架任务/拣货任务/盘点执行/WCS 读面 facade、BarcodeLabel 规则/模板/打印批次/扫码记录分页 facade、BusinessApproval 模板/流程/记录/委托 facade、全局对象搜索后端 facade（MES 工单、SKU、当前报警；Inventory batch/lot 与设备列表搜索当前返回 unsupported）、IndustrialTelemetry tags/alarm-rules/alarms/history/OEE/runtime-availability facade、Maintenance 工单/计划/点检/备件/availability-windows facade。 | P1：当前 route-ready 页面硬化和工作台最低可用性；P2：前端 Cmd/K 面板、BusinessApproval、IndustrialTelemetry rule/OEE 页面、Maintenance 深化页面；P3：预测、CRM-lite、CAPA 和高级分析。 |

当前 `frontend/apps/business-console/src/pages/erp/index.vue`、`erp/sales.vue` 和 `erp/finance.vue` 已分别承载采购与供应、销售管理和财务窄化页面；不能据此把完整 ERP 菜单、月结、税务、银行或完整财务报表标为已交付。

### IAM Enforcement 口径

前端导航裁剪只是体验优化，不是授权边界。Console/Business Console 必须以 Gateway 的 per-request enforcement 为权威：

1. Gateway facade 按当前 bearer token、`organizationId`、`environmentId` 和 operation permission 做每请求授权；客户端不得因为菜单已隐藏或已显示而跳过 401/403 处理。
2. 客户端可以用 permission catalog、`me` 上下文和 feature flag 预裁剪导航，但缓存只能作为展示优化，不能作为放行依据。
3. 本文的导航规则默认只约束功能级权限；数据行级、对象级或组织范围权限必须由对应 facade 显式实现后，前端才能展示为“对象级可见/不可见”能力。
4. 如果权限校验结果被 Gateway 或客户端缓存，缓存必须绑定当前用户、组织、环境和权限版本；权限撤销后的最终保护仍是 Gateway 返回 401/403。

### AppShell 覆盖

| 包/文件 | 当前能力 | 差距 |
| --- | --- | --- |
| `frontend/packages/app-shell/src/AppShell.vue` | 旧两级侧栏壳层，仍接收 `navItems`；迁移期保留以兼容尚未迁移的消费方。 | 已被 T 型 `AppShellT` 取代为长期形态；新页面不应再用旧 `AppShell`。 |
| `frontend/packages/app-shell/src/AppShellT.vue` | FE-3 落地的 T 型壳层：顶部一级能力区（`NavTopDomains`，超出进入“更多”溢出）、左侧域内菜单（`NavSide`）、命令搜索入口（⌘/Ctrl+K + 按钮，占位待 FE-13）、顶部用户菜单、近期/星标入口；基于 FE-2 `AppShellInset`（dashboard-01 inset）。类型 `NavDomain`/`NavLink`/`NavGroup`/`SideNav`/`OverflowStrategy`/`ShellUser` 位于 `@nerv-iip/app-shell` 稳定导出。 | 命令搜索面板实装、应用切换器（九宫格）strategy 仍待后续；Console 已随 FE-10 迁入 `AppShellT`。 |
| `frontend/apps/console/src/layouts/DefaultLayout.vue` | 已随 FE-10 迁移到 `AppShellT`：顶部一级域（实例/通知/业务/IAM）+ IAM 域内菜单（用户/角色/会话）+ route→域解析 + 用户菜单/退出。IAM 用户/角色/会话三页已随 FE-10 batch 2 迁到 FE-2 块（`PageHeader`/`Toolbar`/`DataTable`/`DataTablePagination` + 行常用操作显式按钮，文案统一中文，服务端分页/搜索沿用 composable）；运维任务详情/`OperationTimeline` 与通知页已随 batch 3 迁（`PageHeader` + `SectionCards` + `StatusBadge`，删除 `NotificationToolbar`，文案统一中文）；实例看板 `pages/index` + `InstanceTable` + `InstanceDetailPanel` 已随 batch 4 迁（`PageHeader` + `DataTable` + `StatusBadge`，文案中文，状态走 i18n）。至此 FE-10 命名页（IAM/运维/通知/实例）全部 block 化。`pages/business/index`（业务平台状态）已随 batch 5 迁到 `PageHeader` + `SectionCards` + `DataTable` + `StatusBadge` 并中文化，**移除 UI 中的工程 issue 号**（#-引用），以 business-facing 状态/范围语言呈现；`pages/login` + `LoginForm` 经核实早已是 i18n + shadcn 原语（默认 zh-CN 渲染中文），无需迁移。 | 命令搜索入口占位；主题切换待后续补；console 页面 block 化已覆盖全部可见页。 |
| `frontend/apps/business-console/src/layouts/BusinessLayout.vue` | 已迁移到 `AppShellT`，导航模型由 `frontend/apps/business-console/src/navigation.ts` 驱动（顶部能力域 + 域内菜单 + route→域解析 + `permittedBy` RBAC/feature-flag 裁剪钩子），顶部集成命令搜索占位、主题色/亮暗切换和用户菜单。MAN-330/#621 后，顶层域、域内菜单和 route meta 已挂接 BusinessGateway/IAM catalog permission code。 | Gateway enforcement 仍是权威；前端裁剪只做体验收敛。命令搜索面板实装后也必须复用同一 permission 口径。 |

### AppShell T 型导航解锁路径

约束 #17 不是永久禁止 T 型导航，而是要求先把公共壳层能力做成稳定契约，再迁移 Console 和 Business Console。AppShell 演进由 #236 承接；新增 ERP/WMS/BarcodeLabel/Approval/Telemetry/Maintenance 等大域到可见导航前，必须先完成该 issue 的公共 API 和迁移门禁。

> **状态（2026-07-01 校验，FE-3 / #278 / #236 / MAN-330 #621）：最小 API 契约与 Business Console 权限挂接已落地。** `@nerv-iip/app-shell` 已导出 `AppShellT` 及稳定类型，覆盖 `topDomains`、`currentDomainId`、`sideNavItems`（`SideNav`）、`overflowStrategy='more'`、用户菜单、命令搜索入口和近期/星标入口；旧 `navItems` 经 `AppShell` 保留兼容。Business Console 已迁移到 `AppShellT`，并按 `navigation.ts` 的 `requiredPermissions` 对顶部域和域内菜单做权限裁剪；核心 route-ready 页面也在 `definePage` meta 上声明同一 permission 语义。Console 迁移与命令搜索面板实装仍待后续。门禁已过：`@nerv-iip/app-shell` typecheck + test、Business Console typecheck/build。**因此“新增大域到可见导航前必须先完成 #236”的解锁前置条件已满足。**

| 项 | 要求 |
| --- | --- |
| 入口 issue | #236，主题限定为 `@nerv-iip/app-shell` T 型导航公共 API、测试和双控制台迁移；不得混入具体业务页面开发。 |
| Owner | 前端平台/AppShell owner 牵头，Console 与 Business Console owner 作为迁移方评审。 |
| 截止条件 | 第一个新增 ERP/WMS/BarcodeLabel/Approval/Telemetry/Maintenance 等大域到可见 Business Console 导航的 PR 之前完成；否则该业务导航 PR 不应合并。 |
| 最小 API 契约 | 支持 `topDomains`、`currentDomainId`/`currentDomain`、`sideNavItems`、`overflowStrategy`、用户菜单、命令搜索入口、近期/星标入口；类型应位于 `@nerv-iip/app-shell` 稳定导出边界。 |
| 溢出策略 | 至少支持“更多”下拉；如果要做九宫格应用切换器，应作为同一 API 的可替换 strategy，而不是业务 app 自己拼 DOM。 |
| 迁移顺序 | 先让 AppShell 同时兼容旧 `navItems` 和新 T 型模型；再让 Business Console 用权限裁剪后的模型接入；最后考虑 Console 是否接入。 |
| BusinessLayout 迁移触发 | 当前 `BusinessLayout.vue` 的静态 route-ready 侧边栏只允许服务现有已落地/过渡路由。任何新增大域、引入角色裁剪、引入顶部一级域或把 P2/P3 能力转为默认可见导航的 PR，都必须先基于 #236 切换到权限/角色/feature flag 驱动的导航模型。 |
| 验证 | AppShell 变更至少跑 `pnpm -C frontend --filter @nerv-iip/app-shell typecheck`、`pnpm -C frontend --filter @nerv-iip/app-shell test`，并跑受影响应用的 typecheck/build。 |

## UX 方案决策（2026-05-29）

本节记录 2026-05-29 对 15 域菜单方案、WMS/Inventory 融合、PDA/mobile 范式、RBAC 裁剪、全局搜索和 T 型导航的决策。结论已按当前代码事实校验；外部 AI 讨论只作为输入，不作为权威来源。15 个业务域列表必须被解释为能力目录，不是可直接写入 `frontend/apps/business-console/src/layouts/BusinessLayout.vue` 的静态侧边栏。

| 建议/能力 | 裁决 | 调整口径 |
| --- | --- | --- |
| WMS 与 Inventory 融合 | 采纳 | 后端边界继续分离；仓储收货、上架、拣货、复核、发货、盘点等作业页必须就地嵌入库存可用量、批次、冻结、预留和来源单据上下文。 |
| PDA/mobile 不复用 PC 菜单 | 采纳 | PDA/mobile 以我的任务、快捷应用墙和扫码直达为主；Business PDA v1 的完成状态以 `frontend/apps/business-pda` 和 PDA 文档为准，PC 能力目录中的移动端页面只表达能力关系，不表示 Business Console 菜单完成。 |
| 菜单按 RBAC 裁剪 | 采纳 | 当前 `BusinessLayout.vue` 仍是静态导航，后续不得把后端服务清单继续追加成长侧边栏；新增大域前应先引入 permission/role/feature flag 驱动的导航模型。 |
| 全局搜索与近期/星标 | 采纳 | `Cmd/Ctrl+K`、对象号直达、近期访问和星标应作为长期导航基础能力；结果仍必须经过权限过滤。 |
| 业务术语调整 | 采纳 | `IndustrialTelemetry` 可见标签用“设备监控（IoT）”，`ProductEngineering` 可见标签按页面范围用“产品工程（PLM）”或“工艺工程”。 |
| 上下文穿透 | 采纳 | 跨域链路通过链接、Drawer、Sheet、关联侧栏和详情跳转完成，不要求用户回主菜单切换服务。 |
| 15 个一级域 | 调整 | 作为能力目录可保留；真实侧边栏必须按角色裁剪，且当前 app shell 只支持两级可见导航。 |
| 一级域置顶 | 采纳为 PC 目标架构 | 顶部显示权限裁剪后的一级能力区，左侧显示当前能力区的二级/三级菜单；超出顶部宽度的域进入“更多”或应用切换器。当前代码需先改 AppShell 契约。 |
| 外协管理 | 前置 | 当前没有独立外协服务或 BusinessGateway facade，不应作为默认一级域。P2 先作为 ERP Procurement + MES/WMS 上下文流程候选，只有事实复杂度足够时再独立。 |
| WCS/SCADA/PLC 控制 | 边界约束 | WCS 是 WMS adapter 边界；SCADA/PLC/DCS 控制系统和凭据留在 Connector/外部系统边界，Business Console 只展示受控事实和状态。 |
| 编号规则 | 不补菜单 | 当前 numbering 是服务内持久编号和幂等基线，不是可配置业务菜单；只有出现可配置编号规则 facade 后才考虑管理页。 |
| AI/Knowledge | 不进 Business Console | AI Integration 与 Knowledge 是平台规划能力，不属于 Business Console 业务菜单；没有服务和 facade 前不暴露可见菜单。 |

## PC 目标导航架构

Business Console 的 PC 端目标形态是“顶部一级能力区 + 左侧域内菜单 + 全局对象直达”。

| 区域 | 承载内容 | 规则 |
| --- | --- | --- |
| 顶部一级导航 | 数字化工作台、基础数据、产品工程、经营管理、需求与计划、APS、MES、质量、仓储、库存、条码、设备监控、设备运维、审批等能力区。 | 只显示当前用户有权限且 feature flag 开启的能力区；常用项可按角色排序；空间不足时进入“更多”。顶部项不得换行。 |
| 更多/应用切换器 | 低频能力区、管理员才可见的能力区、P2/P3 开启后的新增能力区。 | 超级管理员不应被迫看到 15 个平铺 Tab；可用下拉或九宫格面板承载完整能力目录。 |
| 左侧域内菜单 | 当前顶部能力区下的模块和页面。 | 只展示当前域内导航，例如选中 MES 后左侧显示生产计划与工单、派工与执行、物料与齐套、报工与完工等；不混入其它域。 |
| 面包屑 | 当前对象路径和跨域来源。 | 显示“能力区 > 模块 > 页面/对象”，跨域进入时保留来源上下文，支持返回来源对象。 |
| 全局搜索 | 菜单、命令和业务对象直达。 | `Cmd/Ctrl+K` 入口全局存在，按权限过滤结果；搜索是长菜单的替代路径，不是额外菜单树。 |
| 近期/星标 | 高频页面和对象。 | 固定在顶部或工作台入口附近，不能绕过权限。 |

首个实现步骤应是扩展 `@nerv-iip/app-shell`：把一级能力区、当前能力区、域内侧边菜单、溢出策略、用户菜单和命令搜索入口做成稳定 API，再让 Console/Business Console 各自传入权限裁剪后的导航模型。

## 用户导航形态

Business Console 同时需要能力目录、角色导航和对象直达，不能只依赖一棵长菜单。

| 形态 | 适用用户 | 规则 |
| --- | --- | --- |
| 能力目录 | 架构、权限、规划和超级管理员视角。 | 本文的长期导航清单属于能力目录，用于约束能力归属和分期，不等于默认展开侧边栏。 |
| 角色导航 | 生产计划员、班组长、仓库管理员、质量工程师、设备工程师、采购/销售/财务人员。 | 登录后按 RBAC 和角色画像裁剪，只展示与当前职责相关的域和页面；跨域协作通过工作台、待办、上下文链接和 Drawer 承载。 |
| 全局搜索 | 横跨多部门的重度用户和管理员。 | 支持 `Cmd/Ctrl+K` 打开命令/对象搜索，按菜单、单号、物料、批次、设备、客户、供应商直达页面或详情。 |
| 近期/星标 | 高频重复操作用户。 | 最近访问和星标页面固定在导航顶部，但不得绕过权限。 |
| PDA/mobile | 一线报工、收货、拣货、盘点、巡检、报修、报警处理。 | 不复用 PC 菜单树；首页优先是我的任务、快捷应用墙和扫码直达。 |

> PDA v1 任务地图与组件/UX 标准见 `docs/architecture/mobile-pda-module-product-design.md` 与 `docs/superpowers/specs/2026-06-09-mobile-pda-design.md`。实现轨为独立 app `frontend/apps/business-pda`，不复用 PC 菜单树。
>
> PDA 仓储作业状态（Plan 2 + #374，已建 5 页）：**收货入库 `/wms/inbound`、复核发货 `/wms/review`（写闭环 + 幂等）+ 拣货 `/wms/pick`、上架 `/wms/putaway`（只读任务清单）、盘点 `/wms/count`（写闭环 + 幂等）五页全量落地**；#374 拣货/上架/盘点 list facade 已交付并接出 curated barrel（`@nerv-iip/api-client`），首页应用墙五个 WMS 入口已点亮。拣货/上架无逐任务 complete 端点，做只读清单（写闭环经父单 complete）；盘点写经 count-executions complete（幂等键注入）。扫码 resolve 与真实个人任务过滤仍缺（#374 未含），按库位/状态过滤、不传非空 `operatorUserId`。
>
> PDA MES 状态（Plan 3，已建）：MES 工序执行/报工/领料/完工入库 已建——`@nerv-iip/business-core` 已点亮 `mes.operation`/`mes.report`/`mes.issue`/`mes.receipt` 四个应用墙入口（`routeReady=true`），并落地报工/完工入库的 `productionReportFlow`/`finishedGoodsReceiptFlow` StepFlow；MES facade 全就绪、无后端阻塞。
>
> **设备运维 PDA（Plan 4，已建）：** 设备运维 报修/点检/报警查看 已建 (Plan 4)（facade 就绪、无后端阻塞）。`@nerv-iip/business-core` 已点亮设备字典（`equipment.repair`/`equipment.inspect`/`equipment.alarms` routeReady=true，应用墙入口可跳）、落地设备 StepFlow（`repairOrderFlow`/`inspectionFlow`）与设备标签（severity/state/priority/工单状态/点检结果中文，镜像 PC `useBusinessEquipment`）；PDA 作业页 报修(故障报修)/点检/报警查看 三页 + 数据 composable（`useBusinessMaintenance`/`useBusinessEquipmentAlarms`）+ StepFlow/标签接线 + e2e 均已建 (Plan 4)。报警→报修保持上下文穿透（带 `deviceAssetId` + `sourceAlarmId`）。合并后 WMS/MES/设备运维三域 PDA 入口全部点亮，应用墙无 disabled 入口。
>

### 角色导航样例

下表是第一版 RBAC 裁剪参考，不替代 IAM permission catalog、Gateway enforcement 或业务行级授权；与 IAM permission catalog 有差异时，以 IAM permission catalog 为准。导航隐藏只是 UX 优化，Gateway 仍必须按接口和动作返回 401/403。

| 角色画像 | 默认可见能力区 | 不应默认可见 | 说明 |
| --- | --- | --- | --- |
| 生产计划员 | 数字化工作台、需求与计划、制造执行（生产计划/工单/派工）、库存台账只读、产品工程只读。 | 经营管理财务、设备运维、系统管理。 | 重点是从需求/MRP/MPS 到工单和齐套状态的闭环；采购/库存联动通过上下文链接和只读 Drawer 进入。 |
| 仓库管理员 | 数字化工作台、仓储作业（WMS）、库存台账/库存移动、条码标签、质量收货检验入口。 | 产品工程、高级排程、财务管理、审批模板配置。 | 入库、上架、拣货、复核、发货、盘点是主入口；库存事实在作业页就地展示，不要求切换到库存菜单。 |
| 质量工程师 | 数字化工作台、质量管理、MES 质量与不良、库存冻结/批次视图、供应商/物料只读。 | 销售订单、财务凭证、APS 排程设置。 | 检验计划、检验记录、NCR、冻结和返工穿透是主链路。 |
| 设备工程师 | 数字化工作台、设备监控（IoT）、设备运维（CMMS）、MES 设备与停机、备件库存只读。 | 经营管理销售/财务、产品工程变更、审批模板配置。 | 设备异常应能从监控进入报修、维修工单、停机和 OEE，不把服务名当操作边界。 |
| 一线操作员/PDA | 我的任务、扫码直达、快捷应用墙。 | PC 能力目录和完整菜单树。 | 报工、收货、拣货、盘点、巡检、报修、报警处理从任务或扫码进入。 |

### 工作台最低可用性

`/` 当前作为 Business Console 的数字化工作台入口。BusinessGateway 已提供跨域 KPI、BusinessApproval 待办、Notification 消息和 Telemetry 预警聚合 facade；前端已通过 generated `@nerv-iip/api-client` stable export 消费该 facade，最低可用版本必须满足：

1. 按角色和权限显示 route-ready 页面快捷入口，不能展示用户无权限或 feature flag 未开启的能力区。
2. 使用真实 facade 可得的数据展示待关注事项；来源返回 `forbidden`、`unavailable` 或 `unsupported` 时展示清晰空态/降级状态，不得使用 demo/seed 文案伪装成真实业务事实。
3. 近期/星标和全局搜索结果必须经过当前登录主体的权限过滤。
4. 工作台入口应服务跨域跳转，不替代各域页面内的上下文 Drawer、Sheet 和对象详情链接。

### 权限过滤时机

近期访问、星标、全局搜索和应用切换器不得因为客户端缓存而泄露已撤权能力：

1. 写入近期/星标时只保存 route/object reference，不保存未授权后仍可见的业务名称、金额、客户、供应商或详情 payload。
2. 每次读取和渲染时按当前 principal 的 permission catalog、feature flag 和组织/环境上下文过滤；写入时校验可以作为补充，但读时过滤是硬要求。
3. 权限移除或 feature flag 关闭后，入口应隐藏或显示为不可访问状态；点击仍必须走 Gateway 401/403，不允许只靠前端判断。
4. 对象级搜索结果的详情字段以 facade 返回结果为准；没有权限的对象不能仅因历史访问记录而显示名称或状态。

## 业务术语

后端服务名可以保留在文档和代码边界中，但可见 UI 文案应贴近业务角色。

| 后端/架构名 | 推荐 UI 标签 | 说明 |
| --- | --- | --- |
| ProductEngineering | 产品工程（PLM）或工艺工程 | 面向研发、工艺和生产准备人员；如果页面只处理 MBOM/路线/生产版本，可用“工艺工程”。 |
| IndustrialTelemetry | 设备监控（IoT） | “工业遥测”适合作为架构名，不适合作为一线菜单主标签。 |
| Maintenance | 设备运维（CMMS） | 保留 CMMS 作为专业括注，不要求普通用户理解后端服务名。 |
| BusinessApproval | 审批中心 | 待办入口仍放工作台；审批中心只维护模板、流程、记录和委托。 |
| Inventory | 库存台账/库存管理 | 在仓储作业页面中作为嵌入事实视图出现；独立菜单承载余额、台账、批次、序列号和分析。 |
| WMS | 仓储作业（WMS） | 面向仓库角色时应以收货、上架、拣货、复核、发货、盘点等任务命名。 |

## 上下文穿透

跨域跳转是 Business Console 的核心可用性要求：

1. 仓储作业页发现缺料时，应就地显示可用库存、替代批次、冻结/预留原因和相关入库/调拨入口，避免用户回主菜单打开库存查询。
2. 生产驾驶舱或工单页发现设备异常时，应能带设备 ID 跳到设备监控、OEE、停机记录，或打开故障报修/维修工单抽屉。
3. 质量检验、NCR、库存冻结和返工之间应互相带入物料、批次、工单和检验记录上下文。
4. ERP 采购收货、WMS 入库、Quality 收货检验、Inventory 入账应通过来源单据和行号互通，页面不要求用户手输跨域 ID。
5. MES 报工、完工入库、WMS 入库和 ERP 成本候选应能从工单/报工记录一路下钻，不以服务名作为导航断点。

### 上下文穿透验证口径

上下文穿透不是静态菜单要求，只有触及对应页面或跨域链路的 PR 才需要验证。验证入口按变更范围就近落在页面测试或 focused gate 中：

1. 新增或修改跨域链接、Drawer、Sheet 或详情跳转时，页面测试至少覆盖“从来源对象打开目标上下文”的 smoke path，并断言目标对象 ID/单号/设备/批次已被带入。
2. MES 设备异常到设备监控/维修报修、WMS 作业到库存可用量、质量 NCR 到库存冻结/返工这类链路上线前，应补 Playwright 或组件测试；暂未具备目标 facade 时，页面必须保持不可点击、disabled 或 feature-flag hidden，而不是提供空跳转。
3. `scripts/verify-business-console-mes-pc-workbench.ps1` 覆盖 MES PC 范围内的上下文 smoke；其它域在建立 focused gate 前，按受影响 app 的 typecheck/build 加页面测试执行。

## 平台 Console 菜单

| 域 | 页面 | 状态 | 路由/说明 |
| --- | --- | --- | --- |
| 平台概览 | 平台仪表盘 | 已落地/窄化 | `/` 当前以实例列表、实例详情面板和 restart 入口为主；服务健康/KPI 聚合属于 P2。 |
| 身份与访问 | 用户管理 | 已落地 | `/iam/users` |
| 身份与访问 | 角色管理 | 已落地 | `/iam/roles` |
| 身份与访问 | 权限目录 | 已落地/嵌入 | 通过角色权限编辑器消费 `/api/console/v1/iam/permissions`；暂无独立菜单页。 |
| 身份与访问 | 会话管理 | 已落地 | `/iam/sessions` |
| 身份与访问 | 外部客户端 | 后端已落地/前端待建 | ExternalClient 与 `client_credentials` 已有后端闭环，Console facade/page 后置。 |
| 身份与访问 | SSO/OIDC/MFA 配置 | 规划 | 当前是 callback/hook/session binding 后端基础，不是完整管理 UI。 |
| 应用管理 | 实例列表 | 已落地 | `/` |
| 应用管理 | 实例详情 | 已落地/非菜单 | 当前是实例列表内详情面板，不作为常驻菜单项。 |
| 应用管理 | Connector Host 管理 | 规划 | 只能在 AppHub/Connector Host 事实和 Gateway facade 成熟后暴露。 |
| 运维中心 | 运维任务详情 | 已落地/窄化 | `/operations/:operationTaskId`；任务列表、模板配置和批量操作未交付。 |
| 运维中心 | 审批待办 | 后端已落地/前端待建 | Ops high-risk approval gate 存在，但 Console 审批页面未交付；不得用 BusinessApproval 替代。 |
| 运维中心 | 审计日志 | 规划 | 后续独立查询页。 |
| 通知管理 | 通知记录/待办 | 已落地/窄化 | `/notifications`，覆盖站内消息和任务列表；模板、偏好、外部渠道后置。 |
| 文件存储 | 文件浏览、存储配置 | 后端已落地/前端待建 | FileStorage MVP 有 API/SDK/tus，PlatformGateway 已有上传会话、complete、元数据和 download grant 管理 facade；Console 页面仍待建。 |
| AI 与知识治理 | 模型提供方、MCP/Skill、工具授权、知识源 | 规划 | Context map 已冻结 AI Integration 与 Knowledge 边界，但当前无服务骨架、Gateway facade 或 Console 页面；不得放入 Business Console。 |
| 系统监控 | 服务健康、DLQ、性能基线 | 规划 | 当前只有后端 health、验证脚本和部分持久 DLQ 基础；Console 聚合页后置。 |
| 业务平台状态 | 业务服务状态页 | 过渡 | `/business` 只作为平台管理员查看业务服务 MVP 状态，不承载业务 CRUD。 |

## Business Console 当前可见范围

| 域 | 路由 | 状态 | 说明 |
| --- | --- | --- | --- |
| 数字化工作台 | `/` | 已落地（workbench summary facade） | 当前是 PC 业务入口和待处理入口；Business Console 通过 generated `@nerv-iip/api-client` stable export 消费 `/api/business-console/v1/workbench/summary`，展示跨域 KPI、BusinessApproval 待办、Notification 消息/任务、IndustrialTelemetry 预警和 source status，并按当前 principal 权限裁剪 route-ready 快捷入口。 |
| 基础数据 | `/master-data/skus` | 已落地（FE-5 金标准） | SKU 列表 + 创建 Dialog，消费 BusinessGateway MasterData；已按 FE-4 原型重做并去除演示数据，纳入金标准执法。 |
| 基础数据 | `/master-data/partners` | 已落地（FE-5 金标准） | 客户/供应商列表（business-partner resource facade + 角色推断），已去除演示数据；正式 partner role 字段就绪前角色为推断值。 |
| 基础数据 | `/master-data/resources` | 已落地（FE-5 金标准） | 工厂/产线/工作中心/设备/班次/日历/班组/人员技能资源列表（按类型筛选），已去除演示场景层级数据。 |
| 基础数据 | `/master-data/reference-data` | 已落地（FE-5 金标准） | 字典/参考数据列表，消费 MasterData `reference-data` resource facade（只读；可配置维护 facade 出现前不做创建/编辑）。 |
| 基础数据 | `/master-data/process` | 已收敛/退出导航（FE-6） | 旧"工艺与版本"本地演示页；工程版本已收敛到产品工程 `/engineering`，该路由已从导航移除，不再扩展（路由保留待后续清理）。 |
| 基础数据（门禁） | 编码规则 / 标签条码 | 后端 facade 已落地/前端待建 | 编码规则后端已通过 BusinessGateway 暴露 list/detail/version/preview facade，并由 MasterData 版本审计表保留配置生效边界；前端配置页仍需后续 issue 建设。标签条码依赖 BarcodeLabel facade 和前端页面分期。 |
| 产品工程 | `/engineering` | 已落地（FE-6 金标准） | 按 FE-4 原型重做（PageHeader + SectionCards + 生产版本解析卡 + Toolbar + Tabs[MBOM/工艺路线/生产版本] DataTable），消费 ProductEngineering MBOM/工艺路线/生产版本/resolve facade；已去除 BusinessContextBar 的 org/env 暴露。工程文档、工程物料、ECO/ECN 维护页（后端 facade 未覆盖前）待建。 |
| 需求与计划 | `/planning`、`/scheduling` | 已落地（FE-7 金标准 + APS 第一版） | `/planning` 按 FE-4 原型重做（PageHeader + 新建需求/运行 MRP Dialog + SectionCards + Tabs[需求池/MRP 运行+追溯/计划建议]）；消费 DemandPlanning 需求/MRP run/pegging/建议 facade，接受建议下达 MES/ERP；已去除 BusinessContextBar 的 org/env 暴露。`/scheduling` 是 BusinessScheduling/APS lite 正式排产工作台第一版，展示真实方案列表、明细、冲突/不可排原因和发布动作；甘特图仅保留明确占位，不伪造排程块。MPS 与计划执行分析待建。 |
| 经营管理 | `/erp` | 已落地/窄化 | 当前是采购与供应页，消费 BusinessGateway ERP Procurement 采购订单 facade，展示供应商编码、预计到货、未到数量和部分收货状态；ERP 销售、财务和完整采购申请/RFQ/报价操作页仍按后续分期推进。 |
| 库存管理 | `/inventory/availability` | 已落地（FE-9 金标准） | 库存可用量按 FE-4 原型重做（PageHeader + SectionCards[现存/可用/预留/冻结] + Toolbar[SKU/工厂/库位/批次/质量/货主] + DataTable 明细 + RowActions[发起移动/创建盘点]）；上下文穿透：从 MES 齐套/领料/入库带入 SKU/批次/库位查询，行动作把上下文带去移动/盘点，含返回工单链接。 |
| 库存管理 | `/inventory/movements` | 已落地（FE-9 金标准） | 库存移动过账按 FE-4 原型重做（PageHeader + 受理队列 DataTable + 新建移动 Dialog）；上下文穿透：从来源单据带入 SKU/库位/批次预填。 |
| 库存管理 | `/inventory/counts` | 已落地（FE-9 金标准） | 库存盘点按 FE-4 原型重做（PageHeader + 任务队列 DataTable + RowActions[确认差异] + 创建任务/确认差异双 Dialog）；上下文穿透：从可用量行带入 SKU/库位/批次预填。 |
| 质量管理 | `/quality/inspections` | 已落地（FE-9 金标准） | 检验方案列表（PageHeader + SectionCards + Toolbar + DataTable + 服务端分页）；创建检验记录改 Dialog（动态检验特性）；上下文穿透：从工单/工序/收货带入来源单据/批次/序列号并自动开抽屉，含返回工单链接。 |
| 质量管理 | `/quality/ncrs` | 已落地（FE-9 金标准） | NCR 列表按 FE-4 原型重做（DataTable + 服务端分页 + RowActions）；处置/关闭走 Sheet + AlertDialog；上下文穿透：从工单带入时关闭动作默认填返工工单，含返回工单链接。 |
| 制造执行 | `/mes` | 已落地（FE-8 金标准） | 生产驾驶舱：按 FE-4 原型重做（PageHeader + 指挥导航卡 + SectionCards + 现场阻塞 DataTable + 角色工作台/下一步建议）；token 色替换 raw palette。 |
| 制造执行 | `/mes/plans` | 已落地（FE-7 金标准） | 按 FE-4 原型重做（PageHeader + SectionCards + Toolbar[来源/就绪筛选] + DataTable + 转工单 Dialog）；展示来源计划（sourceSystem/sourceDocumentId，#272 durable link 已随 #290 落地）并打通计划→工单转换（含阻塞原因提示）。前端已消费 source 字段，不再受限。 |
| 制造执行 | `/mes/work-orders`、`/mes/work-orders/:workOrderId` | 已落地（FE-8 金标准） | 列表按 FE-4 原型重做（PageHeader + 来源条 + 派工分组卡 + SectionCards + Toolbar[状态/工作中心] + DataTable + **服务端分页**(#317 提供 total/skip)）；急单与生产报工改 Dialog（报工对象只读、上下文带入）；详情页改 PageHeader + SectionCards + 工序/用料 DataTable。详情不是常驻菜单项。 |
| 制造执行 | `/mes/materials` | 已落地（FE-8 金标准） | 齐套与物料按 FE-4 原型重做（PageHeader + SectionCards + Toolbar + DataTable + 服务端分页(#318 提供 total/skip)）；领料从工单详情发起。Inventory/WMS 真实联动仍按 operational foundation reset 深化。 |
| 制造执行 | `/mes/dispatch` | 已落地（FE-8 金标准） | 派工看板按 FE-4 原型重做（PageHeader + SectionCards + Toolbar + DataTable + 服务端分页 + 阻塞原因可读化）；长期应消费 APS/设备 readiness 结果。 |
| 制造执行 | `/mes/operation-tasks` | 已落地 | 工序执行任务列表与动作入口。 |
| 制造执行 | `/mes/wip` | 已落地（FE-8 金标准） | 在制跟踪按 FE-4 原型重做（PageHeader + SectionCards + Toolbar + DataTable + 服务端分页）。 |
| 制造执行 | `/mes/production-reports`、`/mes/reports` | 均已落地（FE-8 金标准） | 报工记录与完工汇总均按 FE-4 原型重做（PageHeader + SectionCards + Toolbar + DataTable + 服务端分页）；新增报工从工单或工序上下文进入。 |
| 制造执行 | `/mes/quality` | 已落地（FE-9 金标准） | MES 关联质量项按 FE-4 原型重做（PageHeader + SectionCards + Toolbar + DataTable + 服务端分页）；来源单据/NCR 交叉链接到工单与不合格品处理，含来源工单返回链接。 |
| 制造执行 | `/mes/receipts` | 已落地（FE-8 金标准） | 完工入库按 FE-4 原型重做（PageHeader + SectionCards + Toolbar + DataTable + 服务端分页）；新增入库改 Dialog，工单/物料只读由工单详情或报工完成带出。 |
| 制造执行 | `/mes/downtime` | 已落地（FE-8 金标准） | 设备与停机按 FE-4 原型重做（PageHeader + SectionCards + Toolbar + DataTable + 服务端分页）；IndustrialTelemetry/Maintenance 联动继续深化。 |
| 制造执行 | `/mes/handovers` | 已落地（FE-8 金标准） | 班次交接按 FE-4 原型重做（PageHeader + SectionCards + Toolbar + DataTable + 服务端分页）。 |
| 制造执行 | `/mes/traceability` | 已落地（FE-8 金标准） | 追溯查询：按 FE-4 原型重做（PageHeader + SectionCards + Toolbar[查询类型/工单/批次] + DataTable）。 |
| 制造执行 | `/mes/capacity` | 已落地（FE-8 金标准） | 产能影响按 FE-4 原型重做（PageHeader + SectionCards + Toolbar + DataTable + 服务端分页）。 |
| 制造执行 | `/mes/schedules` | 已落地（FE-8 金标准，过渡定位） | 规则排程按 FE-4 原型重做（PageHeader + SectionCards + 结果 DataTable + 分页 + 运行 Dialog）；不是 APS 权威，也不包含甘特。 |
| 设备异常 | `/equipment` | 已落地（FE-9 金标准） | 设备运行看板按 FE-4 原型重做（PageHeader + SectionCards + Toolbar[设备范围] + 设备 DataTable + 当前阻塞面板）；行/阻塞「记录停机」带 deviceAssetId 跳 `/mes/downtime`（MES 设备联动）；不显示 organization/environment/debug/source metadata。 |
| 设备异常 | `/equipment/alarms` | 已落地（FE-9 金标准） | 设备报警按 FE-4 原型重做（PageHeader + SectionCards + 报警 DataTable + RowActions[设备详情/记录停机]）；只展示业务可读状态，互链设备详情与 MES 停机。 |
| 设备异常 | `/equipment/:deviceAssetId` | 已落地（FE-9 金标准，非菜单） | 设备详情按 FE-4 原型重做（PageHeader + SectionCards + 状态/报警卡 + 可用性窗口 DataTable）；由看板/报警进入，「记录停机」联动 MES。报警规则维护/OEE 后端已随 #266/#325 就绪，对应维护页作为后续（FE-11 设备监控）增量，本期未建。 |
| 系统管理 | `/mes/foundation` | 过渡/诊断 | 数据就绪检查只作为系统诊断，不是 MES 一线主菜单优先入口。 |

## Business Console 能力目录

下表是能力目录和分期归属，不是所有用户默认可见的左侧菜单。实际侧边栏必须按角色裁剪，并通过工作台、全局搜索、近期/星标和上下文穿透减少跨域跳转。

| 能力区（不是默认一级菜单） | 目标页面 | 当前处理口径 |
| --- | --- | --- |
| 数字化工作台 | 工作台首页、待办中心、消息中心、预警看板 | `/` 已消费 BusinessGateway workbench summary facade；BusinessApproval、Notification、IndustrialTelemetry、Quality 和 MES 已进入摘要，Inventory 汇总仍明确标记为 unsupported。待办/消息/预警独立子页后续按真实高频工作流拆分。 |
| 基础数据 | 物料列表、物料分类、UOM、单位换算、供应商、客户、承运商、工厂/产线、工作中心、设备资产、班次与日历、部门与团队、参考数据 | MasterData 后端和部分 facade 已有；前端先补齐真实维护页，再扩展二级菜单。物料详情不作为菜单项。 |
| 产品工程（PLM） | 工程文档、工程物料、EBOM、MBOM、BOM 对比/有效性、工艺路线、工程变更、生产版本 | ProductEngineering 后端和 `/engineering` 读视图已有；细分维护页和详情页待建。仅面向工艺路线/MBOM 的页面可使用“工艺工程”标签。 |
| 经营管理（ERP） | 采购申请、询价、采购订单、采购收货、采购退货、报价、销售订单、发货、RMA、应付、应收、财务凭证、成本核算、财务报表 | ERP 后端已落地；采购与供应 `/erp`、销售管理 `/erp/sales`、财务 `/erp/finance` 已通过 BusinessGateway 窄化 facade 和页面落地。完整 ERP 菜单、月结、税务、银行和完整报表仍按后续分期暴露。 |
| 需求与计划 | 需求管理、MPS、MRP 运行、计划建议、需求溯源、计划执行跟踪、正式排产工作台、需求预测 | DemandPlanning facade 和 `/planning` 已有窄化工作台；`/scheduling` 消费 BusinessScheduling/APS lite facade 作为正式排产入口；预测和高级分析后置。 |
| 高级排程（APS） | 排程设置、排程执行、排程甘特图、资源负载、冲突管理、排程版本、排程发布 | BusinessScheduling / APS lite 后端契约、内核和 BusinessGateway facade 已落地；Business Console 第一版入口为 `/scheduling`，甘特暂为明确占位。不得把 APS 算法写入 MES 页面或前端甘特。 |
| 制造执行（MES） | 生产驾驶舱、生产计划、工单与派工、工序执行、在制跟踪、齐套与物料、报工与完工、质量与不良、设备与停机、班次交接、追溯、产能影响、规则排程过渡页 | 当前 PC 工作台已覆盖主线；PDA v1 已独立落地工序执行、报工、领料和完工入库，扫码解析、个人任务与离线 outbox/sync 仍后置。工单详情等对象页通过列表进入。 |
| 质量管理 | 检验计划、检验记录、NCR、质量分析、CAPA | 检验/NCR 已有；质量分析 P2，CAPA P3。 |
| 仓储作业（WMS） | 仓库结构、收货、入库、出库、拣货、复核与发货、退货入库、盘点执行、库内调拨、WCS 任务监控、仓储分析 | WMS 后端已落地，BusinessGateway 已提供收货入库、出库、上架任务、拣货任务、盘点执行和 WCS 任务读面 facade，并支持服务端分页与状态过滤；#374 后上架/拣货/盘点 list 还支持库位过滤，操作员过滤参数存在但因 WMS 暂无 assigned operator 字段时返回空集。Business Console 已接入 `/wms/inbound`、`/wms/outbound`、`/wms/putaway`、`/wms/picking`、`/wms/counts` 和 `/wms/wcs`；后续仓储作业深化应继续内嵌 Inventory 可用量、批次、冻结和预留视图。 |
| 库存台账/库存管理 | 库存可用量、库存台账、库存移动记录、批次、序列号、库存预留、库存冻结、库存调拨、盘点调整、库存分析 | 可用量、移动、盘点已落地；批次/序列号/预留/冻结/分析后置。库存事实仍归 Inventory，但用户作业入口可在 WMS/MES/ERP 页面内嵌使用。 |
| 条码标签 | 条码规则、标签模板、打印管理、扫码记录 | 条码规则 `/barcode/rules` 与标签模板 `/barcode/templates` 已接入 BusinessGateway BarcodeLabel facade 和 `@nerv-iip/api-client` 稳定导出；打印管理和扫码记录仍只保留后端 facade，业务扫码动作嵌入 MES/WMS/盘点流程，不在 PC 页伪造扫码闭环。 |
| 设备监控（IoT） | 标签管理、报警规则、报警列表、报警处理、设备状态、实时监控、历史数据、OEE 分析 | IndustrialTelemetry 后端已有 tag、报警规则、报警、设备时间线、P0 OEE 聚合和 runtime availability 服务读面；P0 OEE 的 availability 按状态持续时间计算，performance/quality 为估算占位，响应标志在 P0 期间保持 true（无状态数据窗口下数值为 0 但仍非真实测量值）。#207 已提供设备运行看板、设备详情和报警 route-ready 页面。BusinessGateway 已提供 tags、alarm-rules、alarms、device history、OEE、runtime availability 和 equipment alarms 分页 facade；正式 rule/OEE 页面仍待接入，设备接入配置、凭据和控制命令仍在外部/Connector 边界。 |
| 设备运维（CMMS） | 设备台账、备件管理、故障报修、维修工单、保养计划、保养任务、点检管理、停机管理、维修费用 | Maintenance 后端已有维修工单、保养计划、点检、备件需求和事件消费；BusinessGateway 已提供工单列表/详情、保养计划、点检列表、备件列表/创建和 availability-windows facade。Business Console 已接入 `/maintenance/work-orders` 和 `/maintenance/plans`；完整 CMMS 工作台、设备资产主数据维护和更深运维费用视图仍待建，设备资产主数据仍归 MasterData。 |
| 审批中心 | 审批模板、审批流配置、审批记录、委托设置 | BusinessApproval 后端与 BusinessGateway facade 已落地，页面待建；业务待办入口放数字化工作台，不在审批中心重复。Ops 运维审批仍归平台 Ops。 |
| 外协加工（P2 候选） | 外协订单、外协发料、外协收货、外协结算 | 不作为当前默认一级域。首选挂在 ERP Procurement + MES/WMS 流程下；只有出现独立事实源、BusinessGateway facade 和高频角色工作台需求时，才升级为独立能力区或服务。 |

## Business Console T 型菜单划分（FE-3 落地）

代码事实来源：`frontend/apps/business-console/src/navigation.ts`（顶部域 `BUSINESS_DOMAINS`、域内菜单 `DOMAIN_SIDE_NAV`、route→域解析 `resolveDomainId`、权限裁剪 `permittedBy`）。本表是当前 route-ready 的菜单划分；只列已落地/过渡页面，新增大域须先过“菜单项升级门禁”。顶部默认 `maxVisibleDomains=7`，其余进入“更多”。

| 顶部能力区 | 域内菜单（左侧）→ 页面 |
| --- | --- |
| 数字化工作台 | 工作台首页 `/` |
| 基础数据 | 物料与产品 `/master-data/skus`、客户与供应商 `/master-data/partners`、工厂资源 `/master-data/resources`、字典 `/master-data/reference-data` |
| 产品工程 | 工程版本 `/engineering`（MBOM / 工艺路线 / 生产版本 / 解析） |
| 需求与计划 | 需求与物料计划 `/planning`、排产工作台 `/scheduling` |
| 制造执行 | 计划与工单（生产驾驶舱 `/mes`、生产计划 `/mes/plans`、工单与派工 `/mes/work-orders`、派工看板 `/mes/dispatch`）；执行与齐套（齐套与物料 `/mes/materials`、工序执行 `/mes/operation-tasks`、在制跟踪 `/mes/wip`）；报工与完工（报工记录 `/mes/production-reports`、报工与完工汇总 `/mes/reports`、完工入库 `/mes/receipts`）；异常与协同（质量与不良 `/mes/quality`、设备与停机 `/mes/downtime`、异常与产能 `/mes/capacity`、规则排程 `/mes/schedules`、班次交接 `/mes/handovers`）；追溯与诊断（追溯查询 `/mes/traceability`、生产准备检查 `/mes/foundation`） |
| 质量管理 | 检验任务与记录 `/quality/inspections`、不合格品处理 `/quality/ncrs` |
| 库存管理 | 库存可用量 `/inventory/availability`、库存移动 `/inventory/movements`、库存盘点 `/inventory/counts` |
| 仓储作业（“更多”内） | 收货入库 `/wms/inbound`（融合库存可用量上下文）、上架任务 `/wms/putaway`、出库发货 `/wms/outbound`、拣货任务 `/wms/picking`、WCS 任务 `/wms/wcs`、盘点执行 `/wms/counts` |
| 经营管理（“更多”内） | 采购与供应 `/erp`、销售管理 `/erp/sales`、财务 `/erp/finance` |
| 条码标签（“更多”内） | 条码规则 `/barcode/rules`、标签模板 `/barcode/templates` |
| 设备监控（“更多”内） | 设备运行看板 `/equipment`、设备报警 `/equipment/alarms`、维护工单 `/maintenance/work-orders`、保养计划 `/maintenance/plans` |

> **仓储作业（FE-11 #286，2026-07-01 复核）：** 后端 WMS facade（#264/#374）已接入 `@nerv-iip/api-client` 稳定导出。入库/出库/WCS 列表已随 #329/#331 落地服务端分页 `skip/take/total` + 状态/关键字过滤，前端用 `usePagedList` + `DataTablePagination`，无假分页；#374 的上架任务、拣货任务和盘点执行 list facade 已接入 Business Console 页面与 PDA WMS 页面。写操作已接入：完成入库（幂等键）、出库复核（packReviewNo/passed）、WCS 派发/标记失败/完成（行内操作 + 确认/表单）、新建入库单 / 新建出库单（动态行明细表单）。

裁剪规则：`BUSINESS_DOMAINS` 顶层域、`DOMAIN_SIDE_NAV` 侧栏项和核心 route-ready 页面 `definePage.meta.requiredPermissions` 使用同一组 BusinessGateway/IAM permission code，均为 OR 语义；用户拥有任一声明权限即可看到对应入口或进入页面。`workbench` 是角色入口，绑定当前 Business Console 已接入业务域和待办来源的权限并随任一业务权限可见。导航隐藏只是 UX，Gateway per-request enforcement 仍是权威；直达无权页面时前端跳转到无权限空态，后端 401/403 仍作为最终保护。命令搜索（⌘/Ctrl+K）入口已在顶部占位，面板实装在 FE-13，搜索结果也必须复用同一裁剪规则。

## 菜单项升级门禁

新增或拆分 Business Console 菜单项前，必须同时满足：

1. 领域事实源已经明确，且不违反 `docs/architecture/business-platform-domain-architecture.md` 的服务边界。
2. BusinessGateway 或 PlatformGateway 已有页面级 facade，并执行 IAM permission enforcement。
3. OpenAPI snapshot 与 `@nerv-iip/api-client` 已重新生成；页面只消费稳定导出。
4. 页面文案是中文业务文案，不出现接口、样例、seed、demo、operationId、sourceSystem 等开发语义。
5. 对象详情、动作表单和页面 Tabs 没有被提升成常驻菜单。
6. touched route 的 typecheck/test/build 或对应 focused gate 已通过。

### Focused Gate 映射

当前仓库没有名为 `frontend-focused-check` 的统一 CI job；在该 job 建立前，PR 作者按变更范围执行下列命令并在 PR 中说明结果。

| 变更范围 | 最低门禁 |
| --- | --- |
| 普通文档 | `git diff --check`，并用 `rg` 检查是否残留错误状态、旧术语或 AI 来源措辞。 |
| 架构导航文档、状态标签、路由状态表或菜单升级门禁 | 普通文档门禁，加至少一名架构 owner 明确 approval；同时用 `rg` 交叉检查 `docs/architecture/implementation-readiness.md`、`docs/architecture/business-platform-domain-architecture.md` 和本文档的状态口径是否冲突。 |
| Business Console 路由、页面或布局 | `pnpm -C frontend --filter @nerv-iip/business-console typecheck`、`pnpm -C frontend --filter @nerv-iip/business-console test`、`pnpm -C frontend --filter @nerv-iip/business-console build`。 |
| AppShell 公共 API 或导航模型 | `pnpm -C frontend --filter @nerv-iip/app-shell typecheck`、`pnpm -C frontend --filter @nerv-iip/app-shell test`，并跑受影响 consuming app 的 typecheck/build。 |
| MES PC 工作台 | `scripts/verify-business-console-mes-pc-workbench.ps1`，以及对应 app 的 typecheck/build。 |
| Gateway facade 或 api-client 契约 | 后端测试、OpenAPI 导出、`pnpm -C frontend generate:api`，再跑消费页面的 typecheck/build。 |

### 状态升级和退出条件

“过渡”状态没有固定时间上限，但必须有明确退出条件。任何过渡页面进入主导航或升级为“已落地”前，必须满足上文“菜单项升级门禁”六条；不得维护第二套措辞不同的退出标准。未满足门禁时，应保留为诊断/过渡入口、挂 feature flag，或从默认导航中移除。新增大域不得靠静态菜单先占位，必须先满足 AppShell T 型导航解锁路径和 RBAC 裁剪要求。
