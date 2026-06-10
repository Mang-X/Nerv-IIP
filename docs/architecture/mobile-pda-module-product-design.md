# 移动端（PDA）模块产品业务文档

> 事实源：`docs/superpowers/specs/2026-06-09-mobile-pda-design.md`。后端能力以 `backend/gateway/BusinessGateway` facade 代码事实为准。
> 关联文档：`docs/architecture/frontend-navigation-map.md`（导航范式与角色入口）、`docs/architecture/frontend-structure.md`（apps/packages 边界）、`docs/architecture/implementation-readiness.md`（实施轨与端口）。
> 定位：PDA 域的产品/信息架构/角色权限/分期/验收口径文档，遵循 `frontend/apps/business-console/AGENTS.md`「新域开工先立此文档」与「先文档后代码」铁律。

## 1. 用户与场景

目标用户是车间一线操作员，手持工业 PDA（键盘楔入扫码，非相机扫码），在仓储与制造现场完成高频、窄流程的作业任务，而不是坐在 PC 前做管理与配置。核心场景覆盖两大业务域加一项轻量补充：

- **WMS 仓储作业**：收货入库、上架、拣货、复核发货、盘点。作业页内就地展示库存可用量、批次、库位等上下文（与 PC 端 WMS/Inventory 融合口径一致）。
- **MES 制造执行**：生产报工、领料/齐套、完工入库、工序执行（开工/暂停/恢复/完工）。
- **设备（轻量）**：故障报修（创建维修工单）、点检/巡检（创建点检记录）、设备报警查看。

一线操作员的诉求是「扫一下就进入正确的活、扫一步确认一步、做完有明确成功反馈」，因此 PDA 不复用 PC 的复杂菜单树，而以任务与扫码为中心组织界面（详见 §2）。

## 2. 信息架构（任务范式，非菜单树）

PDA 首页采用任务范式而非菜单树，由三部分构成：

- **常驻扫码条（ScanBar）**：顶部固定、自动聚焦并在失焦后自动重聚焦，捕获键盘楔入扫码序列，是 PDA 的命脉入口。
- **我的任务**：跨 WMS/MES 的个人待办卡片（TaskCard），让操作员直接看到「该我干的活」。
- **快捷应用墙（AppWall）**：收货/上架/拣货/复核发货/盘点/报工/领料/完工入库/工序执行/巡检点检/报修 等作业入口，以九宫格形式呈现。

扫码结果通过 ScanResultRouter 分流：扫码串 → 识别对象（工单/库位/批次/SKU/设备/容器）→ 直接进入对应作业页。

对象详情、动作表单、页内 Tabs **不作为常驻菜单项**（与 PC 导航硬约束一致）：它们由扫码结果、任务卡或列表行进入，属于页面布局而非导航树。

## 3. 角色与权限

PDA 面向一线操作员角色，默认可见能力收敛为：**我的任务、扫码直达、应用墙**，不暴露 PC 能力目录与完整菜单树。

权限以 **BusinessGateway per-request enforcement 为唯一权威**：Gateway 按当前 bearer token、组织/环境上下文与 operation permission 做每请求授权。前端按 permission catalog / `me` 上下文 / feature flag 对应用墙与任务入口做裁剪，但这只是 UX 优化，不是授权边界；客户端不得因为入口已隐藏或已显示而跳过 401/403 处理。「我的任务」的个人范围在后端个人过滤端点（见 §5 缺口 4）落地前为客户端按工作中心/状态聚合，仅作展示，最终可见性仍以 Gateway 返回为准。

## 4. 分期

里程碑顺序（与 spec §11 一致）：

- **M0 `ui-mobile` 地基**（先行）：`AppShellMobile`（含顶/底/左右三段安全区）+ ScanBar + ListRow + BottomSheet + Result 先跑通；建 `business-core` 骨架与设计 token 接入。
- **M1 PDA 壳**：`business-pda` app + Capacitor APK 基线 + 首页（我的任务/应用墙/扫码分流）+ 登录/会话复用。
- **M2 WMS 一线闭环**：收货/上架/拣货/复核/盘点（依赖后端缺口 1-3，未落地处保持降级）。
- **M3 MES 一线闭环**：报工/领料/完工入库/工序执行。
- **M4 设备轻量**：设备运维 报修/点检/报警查看 已建 (Plan 4)（facade 就绪、无后端阻塞）。`@nerv-iip/business-core` 已落地设备字典点亮（`equipment.repair`/`equipment.inspect`/`equipment.alarms` routeReady=true）、设备 StepFlow（`repairOrderFlow`/`inspectionFlow`）与设备标签（severity/state/priority/工单状态/点检结果中文，镜像 PC `useBusinessEquipment`）；composable 与作业页为后续任务增量。
- **M5 扫码解析增强**：接入扫码 resolve 端点（缺口 5），强化扫码直达。

路线图保留（v1 不实现，仅预留目录与边界）：工位机/平板触摸操作台 `business-workstation`、大屏看板 `business-board`、审批移动端。

## 5. 后端缺口

PDA v1 闭环依赖若干 BusinessGateway facade 能力，当前存在缺口（详见 spec §9 的逐项审计证据）。关键缺口：

- **WMS 拣货/上架/盘点缺独立 list**：当前仅有 create（及盘点 complete），缺对应 `GET list`，建成后无法回看。
- **「我的任务」个人过滤缺失**：现有 list 端点无 `assignedToUserId` 等个人 scope 过滤，`workbench/summary` 只提供 KPI/待办/通知。
- **扫码解析端点缺失**：仅有扫码记录 create+list，缺 `POST barcode/resolve`（扫码串 → 对象类型/ID/目标作业页）；对象搜索当前不支持库存批次/库位/设备类型。

处置口径：上述缺口需整批发一个 consolidated issue 给后端；**issue 落地后在本节回填 issue 号**。缺口落地前，对应 PDA 作业页保持 disabled / feature-flag hidden 或以父单进入，不做半截入口或空跳转；「我的任务」v1 先按工作中心/状态在客户端聚合现有 list，缺口落地后切换为后端个人过滤。

## 6. 验收

PDA 每个页面遵循 PC「金标准」治理级别，过「产品 · 业务 · UX」三关：

- **产品关**：页面服务真实一线任务，不是诊断或演示壳；任务范式与扫码直达落地，不退回菜单树。
- **业务关**：写操作绑定真实 BusinessGateway facade 命令/事件；缺口未落地处保持 disabled/hidden，不做假数据、假分页、空跳转或半截入口。
- **UX 关**：界面无工程语言（operationId/sourceSystem/code/policy/demo/seed/mock/issue 号不进界面）；满足安全区、触控尺寸、扫码即焦点、强反馈闭环、误操作防护与离线/弱网降级等 PDA 硬标准。

门禁三连按 touched 范围执行：`pnpm -C frontend --filter <pkg> typecheck`、`pnpm -C frontend --filter <pkg> test`、`pnpm -C frontend --filter <pkg> build`；触及跨域穿透/缺口降级的页面补 smoke 测试。
