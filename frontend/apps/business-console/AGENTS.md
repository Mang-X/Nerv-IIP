# AGENTS.md — business-console（业务前端）

> 本文件是仓库根 `AGENTS.md` 在 business-console 应用的**子目录补充/覆盖**，根目录规则仍然适用。
> **business-console** = 工厂业务操作台（基础数据 / MES / WMS / 库存 / 质量 / 设备 / 经营管理 等业务域的统一前端）。
> 对照：`frontend/apps/console` 是**平台管理台**（IAM/运维/通知/文件），只承载通用控制面，**不放业务 CRUD**。

这是整个业务平台开发推进的重要一环——前端是产品/业务/UX 落地的临界面，做法直接决定平台能否快速对接真实工厂。

---

## 0. 开工前
1. 读根 `docs/architecture/implementation-readiness.md`。
2. 读所改业务域的**产品业务文档**（§2 清单），以它为业务/IA/UX 事实依据。
3. 后端能力以 **facade 代码事实**为准，不臆测；缺什么就发 issue（§2）。

## 1. 三大支柱：产品 · 业务 · UX（最高准则）
每个页面/改动都要同时过这三关，**不是机械套区块**：
- **产品**：先想清楚"这页给谁用、解决什么、主操作是什么"，逐页打磨，而非把接口平铺成表。
- **业务**：符合制造业领域真相（实体关系、单据流、术语、角色）。方向不清或域较大时，**引入 PM / 业务 / UX 子代理讨论出方案**再落地。
- **UX**：信息层级清晰、空状态给"去新建/去维护"出路、术语说人话（工程术语→业务语言）、操作有反馈闭环；复用 `@nerv-iip/ui` 区块组件（见 §3）。
  - **反馈与通知有硬规则**：操作结果（成功/失败，含网络/服务器错误）一律 **toast**（`@/utils/notify` 的 `notifySuccess`/`notifyError`，后者把 `502`/`downstream-invalid-response` 等映射成人话）；字段校验才用内联红框+汇总；**不在弹窗/页面留常驻错误条**。详见 `frontend/DESIGN/patterns/feedback-and-notifications.md`，照它做、评审照它卡。
- 三者冲突或方向不明时：**先讨论 + 更文档，再写代码**。

## 2. 文档及时性（不是事后补，是改动的一部分）
改业务模块时**同步更新**文档；文档落后于代码 = 未完成：
- **模块产品业务文档**：每个业务域一份，是该域产品/IA/UX/分期/验收的依据。范例：`docs/architecture/master-data-module-product-design.md`（基础数据）。新域开工先立此文档。
- **导航总图**：`docs/architecture/frontend-navigation-map.md`——IA/导航变更必须同步。
- **后端缺口**：发现 facade 缺端点，**整批审计后发 consolidated issue** 给 codex（不要遇到一个发一个），并在模块文档「后端缺口」回填 issue 号。
- 顺序铁律：**先有/先更文档方案 → 再重构**。

## 3. 东西在哪里（让 agent 快速定位修改点）
```
src/pages/<域>/                 业务页面；路由即文件，definePage 配 title/权限
src/composables/useBusiness*.ts 每域一个数据 composable（封装 useQuery/useMutation）
src/data/*.ts                   前端受控值/字典常量（Phase 1 字典源，如 masterDataReference.ts）
src/navigation.ts               导航树 + 域解析 + 权限钩子（单一事实源）
src/components/                  应用级组件（非 @nerv-iip/ui 原版）
@nerv-iip/api-client            generated/（codegen，勿手改） + business-console.ts（curated barrel，手工接出）
@nerv-iip/ui                    区块组件库（见下）+ shadcn 原版基础件（Button/Input/Select/Dialog/Tabs…，禁改）
```
> **区块组件（项目内习称「FE-2 区块」，FE-2 = 前端重建第 2 阶段编号）**：在 shadcn 原版基础件之上**复制重建**的、可复用的**页面级组件**——`PageHeader` / `SectionCards` / `Toolbar` / `DataTable` / `DataTablePagination` / `StatusBadge` / `RowActions`（源码 `frontend/packages/ui/src/components/blocks/`，从 `@nerv-iip/ui` 导出）。`@nerv-iip/ui` 分两层：**原版基础件**（shadcn 拷入，**零改动**）+ **区块组件**（在其上重建）。做业务页一律用区块拼，不手搓裸 `<table>`/裸页头，也不改原版（要定制就复制重建）。

**新业务能力标准落点链**：facade → generated（`pnpm -C frontend generate:api`）→ barrel 手工 re-export → composable hook → page。任一环漏接都会导致"后端有、前端用不上"。

## 4. 区块与数据约定
- 复用 `@nerv-iip/ui` 区块组件（PageHeader/DataTable/Toolbar… 定义见 §3）；**禁止改 shadcn 原版组件**（要定制就复制重建为应用级组件）。
- 列表统一 `PageHeader + SectionCards + Toolbar + DataTable + DataTablePagination`；分页用 `usePagedList`。页内 Tabs 属布局、**不进菜单树**。
- **不做假分页、不做假数据**：后端无分页/无端点就如实处理（整列表渲染 或 入口禁用+说明），并发 issue，不糊弄。
- **UI 不暴露工程语言**：operationId / sourceSystem / code / policy / resourceType / demo / seed / mock / GitHub issue 号 等不进业务界面（`goldStandardPages.contract.test.ts` 会拦 demo/seed/mock/样例 等并校验必备区块）。
- 业务取值优先**字典/常量驱动**，少硬编码（§6）。

## 5. 权限：拆分与同步（务必仔细）
- `src/navigation.ts` 的 `requiredPermissions` 是 RBAC 钩子（当前多为宽松默认）；**网关 BusinessGateway 的授权校验才是权威**。
- 新增页面/操作时：① 明确该页/该操作所需**权限码**，与后端 `BusinessGatewayPermissions` 对齐；② 在 nav 项与 `definePage` 挂权限；③ 按角色画像（计划员/班组长/仓管/质量/设备/采购销售财务，见导航图角色矩阵）裁剪可见域与页。
- **前端隐藏 ≠ 后端放行**：能写的操作必须有对应权限语义，前端可见性与网关授权**一起改、保持一致**，不留宽松空洞。
- 权限拆分（域级 / 页级 / 操作级）与业务角色**同步设计**；权限相关改动在 PR 里说明前后端如何对齐。

## 6. 工厂对接：可配置、易调整、易定位
平台要对接千差万别的真实工厂，业务逻辑**尽量配置驱动、少写死**，便于快速调整：
- 业务取值走**字典 / CodeSet / 常量模块**（如 `masterDataReference.ts`），**禁止**在页面写死某客户/某产品专名（已踩坑：减振器 demo 文案与硬编码分类）。
- 流程 / SOP、状态机、字段可见性优先做成**数据驱动或集中配置**；**SOP 须充分设计后再敲定固化**，避免把流程逻辑散落在多处反复改。
- 即使必须改代码：坚持"一域一 composable、一页一文件、受控值集中、命名贴业务"，让 agent 能**按 域 / 页 / 字典常量 快速定位**改点。
- 兼容性：新增字段/状态/分类优先扩字典而非改枚举；跨域协作走上下文链接 / Drawer / 工作台，不以服务名作导航断点。

## 7. 命令与门禁
```powershell
pnpm -C frontend/apps/business-console typecheck   # 最快单项检查
pnpm -C frontend/apps/business-console test         # vitest（vp test run src）
pnpm -C frontend/apps/business-console build        # 生产构建
pnpm -C frontend generate:api                       # 从 Gateway OpenAPI 快照重生成 api-client
```

## 8. "Done" 定义（提交前自检）
- typecheck + test + build 全绿；新增/改动页有契约或单测覆盖（参照 `goldStandardPages.contract.test.ts`）。
- 三大支柱过关；UI 无工程语言、无假分页/假数据。
- 涉及 IA / 业务逻辑 / 权限的改动，**对应文档已同步更新**。
- 后端缺口已发 consolidated issue 并在模块文档回填。
- 回复用中文（用户偏好）。
