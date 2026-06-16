# AGENTS.md — business-console（业务前端）

> 本文件是仓库根 `AGENTS.md` 在 business-console 应用的**子目录补充/覆盖**，根目录规则仍然适用。
> **business-console** = 工厂业务操作台（基础数据 / MES / WMS / 库存 / 质量 / 设备 / 经营管理 等业务域的统一前端）。
> 对照：`frontend/apps/console` 是**平台管理台**（IAM/运维/通知/文件），只承载通用控制面，**不放业务 CRUD**。

这是整个业务平台开发推进的重要一环——前端是产品/业务/UX 落地的临界面，做法直接决定平台能否快速对接真实工厂。

---

## 0. 开工前
1. 读根 `docs/architecture/implementation-readiness.md`。
2. 读所改业务域的**产品业务文档**（§2 清单），以它为业务/IA/UX 事实依据。
3. 后端能力以 **facade 代码事实**为准，不臆测——**必要时登录后探针真实接口看真返回**（曾踩坑：以为 MES facade 回 GUID、实为人读编码 `WO-…/WC-…/SKU-…`，导致把现成可读单号藏成"待接入"占位；探一下就避免了）；缺什么就发 issue（§2）。

## 1. 三大支柱：产品 · 业务 · UX（最高准则）

**取舍优先级（顺序很重要）**：
① **产品 / UI / UX / PM 视角优先**——先把"这页好不好用、有没有真正的产品感"想透；**永远不要被现有标准组件 / FE-2 区块限制**。产品需要时就**大胆设计新组件、新交互**去拿到产品感（仍守"shadcn 原版零改动"——要新件就**复制重建 / 新建**，而不是迁就现有件削足适履）。
② **其次**才是真实业务场景 + 行业标准兼容（对标 SAP / Oracle / D365 / 同类 MES 的成熟范式与真实操作流）。
③ **最后**才是程序 / 实现便利。
三者冲突按此序取舍；**任何疑问或更优解一定当场提出来**，不闷头挑最省事的实现，也不默默绕过问题。

每个页面/改动都要同时过这三关，**不是机械套区块**：
- **产品**：先想清楚"这页给谁用、解决什么、主操作是什么"，逐页打磨，而非把接口平铺成表。
- **业务**：符合制造业领域真相（实体关系、单据流、术语、角色）。方向不清或域较大时，**引入 PM / 业务 / UX 子代理讨论出方案**再落地。
- **UX**：信息层级清晰、空状态给"去新建/去维护"出路、术语说人话（工程术语→业务语言）、操作有反馈闭环；复用 `@nerv-iip/ui` 区块组件（见 §3）。
- 三者冲突或方向不明时：**先讨论 + 更文档，再写代码**。
- **每次改动都要过 §1.5 的两轮自检（实现前 + 完成后），这是硬要求、不是建议。**

## 1.5 UX / IA 自检（实现前 + 完成后，各一轮，强制）
> 下面每条都是**真实踩过的坑**。每次实现**前**过 A、**完成后**过 B；每轮都明确写下结论：「这真的好用、对路、看得清吗？」。typecheck/test 绿 ≠ 做对了。

**A. 实现前（方向 / 产品 / 业务 / IA）**
1. **给谁用、主操作是什么、符合该角色真实业务心智吗**？不是把接口平铺成表。
2. **IA 载体用对了吗**：父子**层级**（如 工厂▸车间▸产线▸工作中心）用**树 / 逐级下钻**，**不要用平铺 Tab 切层级**（把层级拍平了）；Tab 只用于"同一对象的不同视图 / 真正平级的分类"。关系弱的一堆实体别塞进一个"杂物抽屉" Tab。
3. **对标成熟系统**：SAP / Oracle / D365 / 同类 MES 这块怎么做？——**「讨论过 ≠ 选对了」**，自洽不够，要对标成熟范式 + 真实操作流。
4. **砍范围要显式**：只做名称/先用兜底/暂不做某能力等，**必须摆出来让用户拍板**，不偷偷缩（曾踩坑：编辑悄悄只改名）。
5. 方向不清 / 域较大 → **先拉 PM / 业务 / UX 子代理议方案 + 更产品文档，再写代码**。

**B. 完成后（实现质量 / 交付前）**
1. **把页面拉起来看再说"完成"**：截图 / 真机刷新。**本 app 的浏览器预览不可靠（会卡、可能连到陈旧实例，给假读数）→ 验证以 `build` + 查编译产物 + 真机刷新为准**，别拿预览读数下结论。
2. **视觉层次**：分段标题 ≠ 字段标题（曾踩坑：都 `text-sm font-medium`，整片无引导）→ 多分段表单用 `components/masterData/FormSectionTitle.vue`；长表单要分组，不平铺一片。
3. **反馈闭环**：操作结果（成功/失败/网络/5xx）一律 **toast**（`@/utils/notify`，错误映射人话）；字段校验用内联红框+汇总；**不留常驻错误条**。详见 `frontend/DESIGN/patterns/feedback-and-notifications.md`。
4. **校验时机**：**点提交才标红**（不是一打开就红）；必填空着点提交 → 要有提示**且不发请求**。
5. **主题 / 设计 token**：颜色用 token（`--primary` 等）**跟随主题切换**（中性也是一种主题）；**不写死颜色**；**不堆"AI 味"装饰**（无意义的竖条/光晕/渐变/标记——曾踩坑：菜单左竖条被指"AI 标志"）。
6. **说人话**：UI 不暴露工程语言（operationId / code / resourceType / `#`号）；码值显示中文（英文种子用常量**兜底覆盖**，见 `masterDataReference.ts` 的 `mergeReferenceOptions`）。
7. **数据真相**：系统编号 vs 人工编码如实呈现（编辑态显示真实编号，不写"保存后分配"）；不做假分页 / 假数据。
8. **KPI / SectionCards 非默认**：不要每页机械套概览卡。**按页判断**这页是否真需要少量能驱动决策 / 暴露问题的业务指标（维护台一般不需要——总量已在 PageHeader count）；要放就**说人话、用总量**（不是本分页页内数），**不堆机械元数据**（曾踩坑：'后端分页总数' / '树的根' / '本页启用·停用' / '当前分组条目'）。判定见 `frontend/DESIGN/patterns/pages/master-data-templates.md` §0「SectionCards 判定」。

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
- **区块是标准页的基线、不是上限**：常规列表/工作台用区块拼（快、一致）；当产品 / UX 需要区块给不了的呈现或交互时，按 §1 优先级**大胆新建组件 / 交互**（复制重建，绝不改原版），别为迁就现有件牺牲产品感。
- 列表骨架 `PageHeader + Toolbar + DataTable + DataTablePagination`（**SectionCards 可选、非默认**，见 §1.5-B 第 8 条）；分页用 `usePagedList`。页内 Tabs 属布局、**不进菜单树**。
- **页面靠 UI/UX 引导、不堆说明书**：删"用途说明"段落与冗余"本页 N"计数；**展示 facade 返回的真实人读编码**（`WO-…/WC-…/SKU-…`），ID 本身即点开详情，别用"查看X"按钮或"待接入/名称待接入"占位遮蔽真值。硬约束见 `frontend/DESIGN/patterns/pages/list-workbench.md` 与 `blocks/app-shell.md`（含**每个导航项必须带 icon**）。
- **不做假分页、不在 UI 伪造能力 / 不假装闭环**：后端无分页/无端点就如实处理（整列表渲染 或 入口禁用+说明）；别编 `WO-PLAN-xxx` 之类冒充下游单据假装跨域闭环（曾踩坑：planning"接受建议"后端并不建 MES 工单）；发 issue，不糊弄。
- **但要 seed / mock 真实感数据看效果**：页面做完必须拉起来看真实数据——后端有接口就脚本 seed（如 `tmp_seed_*.py`），缺接口就前端 mock（E2E `page.route` / 本地桩）跑通视觉；"看不到实际效果就不算完"。**seed/mock 的数据要像真实业务**（真实物料 / 工序 / 单号口径），**绝不写"测试 / test / 样例 / demo / foo / bar"或一眼假的文字**——"不做假数据"指不在 UI 伪造能力，不是不准为验证而造真实感数据。
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
- 三大支柱过关（含 §1 优先级取舍）；UI 无工程语言、无假分页、不假装闭环、无"待接入"空壳。
- **拉起来看过真实数据**：seed / mock 真实感业务数据后，真机刷新 / 截图确认页面实际效果——不靠想象，不留空壳。
- 涉及 IA / 业务逻辑 / 权限的改动，**对应文档已同步更新**。
- 后端缺口已发 consolidated issue 并在模块文档回填。
- 回复用中文（用户偏好）。
