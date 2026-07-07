# 主数据 UX 模板规范（Master Data UX Templates）

> 状态：草案 v1（设计规范，对标 SAP / Oracle / D365 / 同类 MES 的主数据维护范式）。
> 适用：`apps/business-console` 「基础数据」域全部页面，**后续所有主数据模块照此抄、§1.5 评审照此卡**。
> 关联：
> - 应用守则：[`apps/business-console/AGENTS.md`](../../../apps/business-console/AGENTS.md)（§1.5 两轮自检、§3 区块、§4 区块与数据约定）
> - 反馈规范：[`feedback-and-notifications.md`](../feedback-and-notifications.md)（toast vs 内联，单一事实源）
> - 列表基线：[`list-workbench.md`](./list-workbench.md)（FE-2 区块拼装）
> - 创建/确认流：[`../flows/create-dialog.md`](../flows/create-dialog.md)、[`../flows/confirm-destroy.md`](../flows/confirm-destroy.md)
> - 产品依据：[`docs/architecture/master-data-module-product-design.md`](../../../../docs/architecture/master-data-module-product-design.md) §6、[`master-data-dictionary-rules.md`](../../../../docs/architecture/master-data-dictionary-rules.md)

本文给六类页型 +（横切）表单 + IA 判定 + 一致性约定，**每节 = 标准骨架 + 统一约定 + 关键交互 + 空状态**。务实、可直接照抄。

---

## 0. 通用基底（每个页型都先满足）

**区块拼装铁律**（来自 AGENTS §3/§4）：一律用 `@nerv-iip/ui` 的 FE-2 区块拼，**不手搓裸 `<table>` / 裸页头 / 裸 metric `<div>`，不改 shadcn 原版**。
标准外壳：`BusinessLayout`（T 形外框，页面只填内容槽）。

```
BusinessLayout
  PageHeader        面包屑即标题 + count + #actions（[刷新] [+ 新建…]）
  [层级/口径提示]    text-sm text-muted-foreground 一行（如「工厂 → 车间 → 产线 → 工作中心 → 设备」）
  [SectionCards]    可选、非默认（见下「SectionCards 判定」）——只放少量能驱动决策的业务指标
  Toolbar           v-model:search（live）+ #filters（Select）+ #actions（+ 新建）
  [列表加载失败条]   inline text-destructive role="alert"，紧邻表格；不是 toast
  DataTable         columns + #cell-<key> slots；:loading 骨架、empty-message、点列排序
  DataTablePagination  服务端 total
```

> **SectionCards 不是标准骨架必备项**。`PageHeader / DataTable / DataTablePagination` 才是必备（契约 `goldStandardPages.contract.test.ts` 只强制这三个）。SectionCards **按页判断**——下面「SectionCards 判定」逐页拍板。

### SectionCards 判定（何时该有卡 / 何时不该有）

**默认不放。** 只有当这页存在**少量、能驱动决策或暴露问题的业务指标**时才加，并只加真正有用的那几个（不同页不同、可没有）。

- **该有**：指标能让用户一眼判断要不要行动 / 暴露异常（如临期·过期数、待处理量、结构规模一览）。例：`facilities.vue` 的 工厂/车间/产线/工作中心 数（树页的结构规模一览）。
- **不该有**：
  - **维护台（CRUD 列表）一般不需要**——总量已在 `PageHeader` 的 count，再堆一张「总数卡」是冗余。
  - **机械元数据当指标**：`本页启用 / 本页停用 / 本页 X`（分页页局部、会误导）、`关联产线`（不是本实体的指标）、`当前分组条目` 等，**一律不放**。
- **文案铁律（必须说人话）**：description 用业务语言、value **用总量**（不是当前分页页内数）；hint 写业务口径或留空。**禁止出现** `后端分页总数 / 树的根 / 本页 X / 当前分页` 等机械框架词。

**全域统一约定（七条横切，下文不再重复）**

1. **列**：编码（`code`，`font-medium`）｜名称（`displayName`）｜…典型 typed 列…｜状态（`StatusBadge`）｜更新时间（`snapshotVersion` 经 `formatDateTime`）｜操作（`RowActions`，`align: end`）。缺值显示「无」，不显示空白或 `null`。
2. **状态**：一律 `StatusBadge`，`:value="row.active === false ? 'disabled' : 'active'"`（启用/停用）。不自己画徽标。
3. **行操作**：用 `components/masterData/MasterDataRowActions.vue`（查看详情只读 Dialog / 编辑 emit 给页面 / 停用·启用走 `AlertDialog` 二次确认 + toast）。后端未就绪的能力 `disabled` + tooltip，**绝不放会失败的假按钮**。
4. **说人话**：UI 不暴露工程语言（`operationId / code(术语) / resourceType / sourceSystem / #号 / organizationId / environmentId / demo / seed / mock`）；码值显示**中文**（英文种子用 `masterDataReference.ts` 的 `mergeReferenceOptions` 常量**兜底覆盖**）。术语对照见产品文档 §6.7。
5. **空状态分两种**：
   - 有筛选/搜索无结果 →「没有符合条件的 {X}」+ [清空筛选]（出路 = 退出筛选）。
   - 首次无数据 →「还没有 {X}，点击创建第一条」+ [+ 新建]（**仅有创建能力的页**给新建出路；只读页给「去 {上游页} 维护 →」）。
6. **主题/设计 token**：颜色用 token（`--primary` / `bg-accent` / `text-muted-foreground` 等）**跟随主题切换**（中性也是一种主题）；**不写死颜色**；**不堆 AI 味装饰**（无意义的竖条/光晕/渐变；高亮用 `--primary`）。
7. **诚实数据**：系统编号 vs 人工编码如实呈现（编辑态显示真实编号，不写「保存后分配」之类假话于已存在记录）；**不做假分页、不做假数据**；搜索若仅过滤当前页，占位与 hint 必须如实说「在当前页内筛选」。

---

## 1. 树-详情页型（层级对象：工厂结构 / 部门）

**何时用**：对象是**父子层级**且层级是用户的主心智（工厂▸车间▸产线▸工作中心；部门▸子部门）。**层级用树/逐级下钻，绝不用平铺 Tab 切层级**（那是把层级拍平）。

### 标准骨架

```
PageHeader（面包屑 + 节点总数 + [刷新] [+ 新建根节点]）
[基地过滤 Select]（多基地时置于 PageHeader #actions 或树头）
两栏：md:grid-cols-[300px_minmax(0,1fr)]
  ├─ 左 TreePanel（h-fit 卡片/边框）
  │    搜索框（过滤树，命中高亮 + 自动展开祖先）
  │    [全部展开/折叠] 切换
  │    树体：可下钻、可折叠；选中行 bg-accent text-accent-foreground
  │    节点行尾：悬浮「+ 子级」按钮（就地新建子节点）
  └─ 右 DetailPanel
       面包屑（根 ▸ … ▸ 当前节点，可点回跳）
       详情字段（只读 dl）或编辑表单（见 §5）
       子级计数 +「查看 N 个子级」
```

### 统一约定
- **基地过滤**：多基地工厂用 `siteCode` Select 过滤树根集合；单基地隐藏该过滤器。
- 树节点 = `{ code, displayName, active, children }`；状态停用的节点名旁挂小号 `StatusBadge` 或置灰。
- 选中态是**单选**且持久（切页签/刷新尽量保留选中 code）。
- 关系靠字符串 code 关联（非 DB 外键，见产品文档 §2.1），新建子级时父 code 作只读归属带入。

### 关键交互
- **选中父级就地新建子级**：左树节点行的「+ 子级」或右详情的「+ 新建下级」→ 打开新建表单，**归属字段（父 code）自动带入且只读**，用户不必再选父级。新建成功后树就地插入并选中新节点。
- **面包屑**：右侧顶部恒显「根 ▸ … ▸ 当前」，每段可点回跳并切换选中。
- **搜索**：输入即过滤树；命中节点高亮，自动展开其所有祖先，无命中走空状态。
- **小厂层级压扁**：
  - **隐藏单一根**：若整棵树只有一个根（单工厂），默认隐藏根层、直接展示其子级，避免「点一下才看到内容」的多余层级。
  - **默认展开**：节点总数少（如 < 50）时默认全展开；大树默认只展开第一层 + 选中路径。
  - 层级链中某层在该工厂不存在（如无「车间」）时，跳层连接父子并在提示里说明，不显示空中间层。

### 空状态
- 整树无数据：左树区「还没有 {根实体}，点击创建第一条」+ [+ 新建工厂]；右详情区「从左侧选择一个节点查看详情」。
- 选中节点无子级：右详情子级区「该 {节点} 下还没有 {子实体}」+ [+ 新建{子实体}]（归属已带入）。
- 搜索无命中：左树「没有匹配「{kw}」的节点」+ [清空搜索]。

---

## 2. 列表 / 列表-详情（主从）页型（平表列表 + 可选主从）

**何时用**：实体是**平级集合**（计量单位、设备台账、单一实体列表）。这是主数据最常见页型，基线即 `list-workbench.md`。

### 标准骨架

```
PageHeader + [SectionCards 可选] + Toolbar + DataTable + DataTablePagination
```
（等同 §0 通用基底；这是「黄金标准列表」的直接落地。**SectionCards 非默认**，按 §0「SectionCards 判定」拍板——多数维护台不需要。）

### 统一约定
- 列序固定（§0 第 1 条）：编码 ｜ 名称 ｜ typed 列 ｜ 状态 ｜ 更新时间 ｜ 操作。
- **SectionCards 非默认**：维护台一般不放（总量已在 PageHeader count）；仅当有能驱动决策 / 暴露问题的少量业务指标才加，并按 §0「SectionCards 判定」选卡。**禁止** `本页启用 / 本页停用 / 后端分页总数 / 树的根 / 当前分页` 等机械元数据；value 用总量，hint 说人话或留空。
- Toolbar：搜索（如后端无 keyword，占位「在当前页内筛选 {字段}」+ hint「全量搜索即将上线」）；`#filters` 放状态/分类 Select。
- 分页 `total` 始终用**服务端 total**（不是当前页长度）；分页用 `usePagedList` / 各域 composable。

### 详情/子表：Drawer 还是行展开？（判定）
| 场景 | 用什么 | 理由 |
|---|---|---|
| 看单条记录的**只读完整属性** | `MasterDataRowActions` 的**查看详情 Dialog**（小弹窗 dl） | 字段少、即看即关，现成组件 |
| 单条记录**字段多 / 需编辑 / 有上下文操作** | **Drawer**（右侧抽屉，不离开列表） | 列表上下文不丢，宽度容纳分段表单 |
| 主记录 + **一对多子表**（如单位×换算、伙伴×联系人） | **行展开 inline** 或 Drawer 内子 `DataTable` | 子表行少用行展开；子表需独立增删改用 Drawer |
| 主记录 + **另一受控集合**（字典 CodeSet→码值） | **左右主从**（见 §7 CodeSet 主从），非行展开 | 主列表需常驻可切换 |

> 默认优先**查看详情 Dialog**（最轻）；字段多或要编辑升级 Drawer；**行展开仅用于行数极少的子表**，不要用行展开塞一整个表单。

### 关键交互 — 行操作（统一三件套）
- **查看**（恒可用）：只读详情 Dialog，展示 typed 字段 +「更多属性建设中」占位（后端未回字段时）。
- **编辑**：`emit('edit', row)` 给页面 → 页面打开**全字段表单**（带回填，编码只读）。后端无 update 时 `disabled` + tooltip「编辑功能即将上线」。
- **停用/启用**：`AlertDialog` 二次确认（文案：「停用后将不能用于新建/计划，已有记录不受影响。」），结果走 toast；**软删除，不物理删**。

### 空状态
- 首次无数据（有创建能力）：「还没有 {X}，点击创建第一条」+ [+ 新建{X}]。
- 筛选无结果：「没有符合条件的 {X}」+ [清空筛选]。
- 只读列表（无创建能力）：「还没有 {X}，去 {上游页} 维护 →」（给出路，不空白）。

---

## 3. 设置表 + 月历页型（班次 / 工作日历）

**何时用**：少量**配置型记录**，且其中**时间维度需要可视化**（日历）。班次=作息时段；工作日历=工作日/假期，驱动可用产能。

### 标准骨架（双形态）
- **班次（设置表）**：就是 §2 平表列表 —— 编码 ｜ 名称 ｜ 起止时间（`08:00–20:00`，跨天标「跨天」）｜ 状态 ｜ 更新时间 ｜ 操作。新建/编辑用分段表单（§5）。
- **工作日历（设置表 + 月历）**：
```
PageHeader + SectionCards（日历数 / 本月工作日数 / 本月休息日数）
两栏：md:grid-cols-[260px_minmax(0,1fr)]
  ├─ 左 日历列表（选中一个 WorkCalendar）+ [+ 新建日历]
  └─ 右 月历视图（CalendarBoard）
       月份切换 ‹ 2026-06 › ＋ [今天]
       7×N 月历网格：工作日/休息日/法定节假日 用 token 底色区分（图例在上）
       点格子切换「工作/休息」（编辑态）；批量：选周末→设休
```

### 统一约定
- **月历用月视图**（非周/列表）；一屏一个月，跨月用 ‹ › 翻页。
- 日类型用**语义 token 底色 + 图例**（工作日=默认、休息日=`muted`、法定假=`accent`/带角标），**不靠纯色硬编码**；色盲友好加文字/角标，不只靠颜色。
- 跨天班次（夜班 20:00–08:00）显式标「跨天」，不让用户算。
- 日历规则（每周工作日、法定节假日来源）在卡片头一句话声明数据口径。

### 关键交互
- 切换日历 → 右侧月历重渲染该日历的工作/休息分布。
- 编辑态点格子翻转日类型；批量操作（整列周末设休、导入法定节假日）给确认。
- 「该日历被哪些产线/工作中心引用」可在详情提示（停用前校验下游引用）。

### 空状态
- 无任何日历：左列表「还没有工作日历，点击创建第一条」+ [+ 新建日历]；右月历区「先在左侧选择或新建一个工作日历」。
- 选中日历但本月无特殊标记：月历正常显示默认工作/休息分布（非空状态）。

---

## 4. 矩阵页型（工人 × 技能）

**何时用**：两个维度的**交叉关系/资格**（工人×技能=技能矩阵；用于派工上岗资格校验）。矩阵让「谁会什么、到期没」一屏可读。

### 标准骨架

```
PageHeader（+ [刷新] [+ 登记技能]）+ SectionCards（在岗工人数 / 技能项数 / 临期/已过期数）
Toolbar
  搜索（搜工人）
  #filters：按技能列筛选（Select：只看某技能） + 按等级/到期状态筛选 + 部门/班组筛选
矩阵网格（横向可滚，首列冻结）
  行 = 工人（首列：姓名 + 工号/部门，sticky left）
  列 = 技能（表头：技能名）
  格子 = 该工人该技能的：等级徽标 + 到期日；空格 = 未持有
  到期高亮：临期(≤30天)=warning、已过期=destructive 底/角标
```

### 统一约定
- **首列（工人）冻结**，技能多时横向滚动；表头技能名可悬浮看全称。
- 格子内容**密度优先**：等级用短徽标（如 L1/L2/L3 或 初/中/高），到期日小字；不在格子里塞长文本。
- 到期状态用 token：有效=默认、临期=warning、过期=destructive；**色 + 角标/文字双编码**。
- 工人 = IAM 用户（经 `WorkerSelect`）；技能/等级来自字典（`personnel-skill` / `skill-level`），中文兜底。

### 关键交互
- **格子内联编辑**：点空格→弹小卡选等级 + 到期日→保存即写（`usePersonnelSkillAssignment`）；点已有格→改等级/到期或移除（移除走二次确认）。
- **按行/列筛选**：选某技能只看持有者（列筛选）；搜工人定位行；按到期状态筛出所有临期格。
- 批量登记：选多工人 + 一项技能一次登记（可选进阶）。

### 空状态
- 无工人或无技能维度：「还没有可登记的工人/技能，先到 {组织与人员/数据字典} 维护 →」。
- 有维度但无任何登记：矩阵显示空格网格 + 顶部提示「点击格子为工人登记技能」。
- 筛选无结果（如某技能无人持有）：「没有持有「{技能}」的工人」+ [清空筛选]。

---

## 5. 新建/编辑表单（横切所有页型）

**何时用**：所有页型的建档/改档。统一**分段 + 视觉层次 + 点提交才标红 + toast 反馈**。

> **承载判定移交（2026-07 W0 起）**：Dialog / Sheet / 独立页的选择以 [`../interaction-patterns.md`](../interaction-patterns.md) §1 为准——**≤3 字段才用 Dialog；4~8 字段或含动态行用 Sheet；复杂多段/多步用独立页**。下文"Dialog 默认"仅指 ≤3 字段场景；本节其余约定（分段/校验时机/反馈/字段约定）不变。

### 标准骨架（Dialog 默认；字段多用 Drawer）
```
DialogHeader
  Title：新建{实体}  /  编辑{实体} · {编码}
  Description：中性一句（为采购、生产、库存…建立统一档案）。带 * 为必填项。  ← 无任何客户/产品专名
form（@submit.prevent="submit"）
  [顶部校验汇总] v-if="showErrors && !canSubmit"：「请完整填写带 * 的必填项（已标红）。」
  FormSectionTitle「基础信息」
    FieldGroup（grid sm:grid-cols-2）
      Field（FieldLabel + Input/Select + FieldDescription）…
  FormSectionTitle「单位与计量 / 追踪与合规 / …」
    …字段分组…
  [进阶设置 折叠]（少用字段、Phase 2 能力收进折叠，降噪）
  DialogFooter：[取消] [保存{实体} / 保存修改]（pending 时 Spinner + disabled）
```

### 分段与视觉层次（强制）
- **分段标题必须 ≠ 字段标题**：用 `components/masterData/FormSectionTitle.vue`（主色短条 + 加粗 + 下边线），**不要**两者都 `text-sm font-medium`（曾踩坑：整片无引导）。
- 长表单**分组**（基础/单位/追踪/存储…），不平铺一片；2 列 `FieldGroup` 排布。
- 字段顺序 = 用户填写心智顺序（先身份后属性），必填靠前。

### 校验时机（强制，对齐 feedback 规范）
- **点提交才标红**：用 `showErrors` 门控 `:data-invalid`，**打开弹窗即 `showErrors = false`**（不是一打开就红）。
- **必填空着提交**：`if (!canSubmit) { showErrors = true; return }` —— 点亮内联红框 + 顶部汇总，**且不发请求**。
- 校验**只用内联**（红框 + 汇总），**不用 toast**（toast 留给请求结果）。

### 反馈 toast（强制，对齐 feedback 规范）
- 成功：`notifySuccess('{实体}「{名称}」已创建/已更新。')`，**成功才关弹窗 + reset**。
- 失败：`notifyError(error)`（映射 `downstream-invalid-response`/`502`/`Failed to fetch` 为人话），**弹窗保持打开**让用户改正，**不在弹窗内堆常驻错误 `<p>`**。
- 打开弹窗即重置瞬时态（`showErrors`、上次报错清掉）。

### 字段约定
- **编码只读**：
  - 系统编号实体 → 显示「保存后由系统分配」（仅新建态；编辑态显示真实编号）。
  - 人工编码实体 → 新建可填，**编辑态 `disabled`**（编码创建后不可改，被下游引用）。
- **归属字段**：父级 code 用 Select 取上游列表；树/主从就地新建时**自动带入且只读**。缺上游给「缺少 {X}？先到「{页}」新建」提示。
- **字典字段实时取 + 常量兜底中文**：分类/单位/存储条件/条码规则等用 `Select`（**不是自由文本**），选项**实时取字典**（`listReferenceDataByCodeSet`），失败/未就绪用 `masterDataReference.ts` 常量**兜底**；旁置「去数据字典维护 →」链接。
- **平台枚举 vs 工厂字典**：带系统行为语义的（materialType/批次/序列/保质期策略）= 前端常量枚举（只启停不改义）；偏业务的（product-category/storage-condition/barcode-rule/quality-reason）= 字典驱动可维护。
- **砍范围要显式**：只做名称/先用兜底/某能力暂不做，**写进 Description 或 muted 提示让用户知情**（曾踩坑：编辑悄悄只改名）。

### 空/边界
- 上游空（无可选工厂/单位）：Select 区给「先到「{上游页}」新建」出路，不留空下拉。

---

## 6. 导航 vs 页内 Tab vs 树 的判定规则（IA，硬约束）

> 来自 AGENTS §1.5-A2 与产品文档 §3.2。**用错载体把层级拍平 = 评审打回。**

| 关系形态 | 用什么 | 例 | 禁止 |
|---|---|---|---|
| **父子层级**（A 含 B，B 含 C） | **树 / 逐级下钻**（§1） | 工厂▸车间▸产线▸工作中心；部门▸子部门 | ❌ 用平铺 Tab 切层级（拍平层级） |
| **同一对象的不同视图 / 真正平级的分类** | **页内 Tab**（属布局，**不进菜单树**） | 工厂与产线页的 工厂｜产线｜工作中心（同一「工厂结构」心智里的平级实体）；同一记录的 详情｜历史 | ❌ 把关系弱的一堆实体塞进一个「杂物抽屉」Tab |
| **独立实体 / 数量级大 / 检索维度多** | **独立菜单页**（进导航树） | 设备台账（量大、归属常变）、计量单位、业务伙伴 | ❌ 与不相关实体硬塞同页 |
| **主记录 → 受控子集合** | **主从（左右）** | 数据字典 CodeSet ▸ 码值 | — |

**判定口诀**：
1. 是**父子**吗？→ **树/下钻**，不用 Tab。
2. 是**同一对象的视图**或**真平级分类**吗？→ **页内 Tab**（且 Tab 数 ≤ 5、彼此真平级、共享同一心智）。
3. 是**关系弱的一堆实体**被硬凑吗？→ **拆成独立页**，别做杂物抽屉 Tab。
4. 是**主记录带一个受控子集合**吗？→ **主从布局**。
5. **Tab 永不进菜单树**；菜单树只放业务域与独立实体页。

---

## 7. CodeSet 主从页型（数据字典）+ 一致性约定收口

### 7.1 CodeSet 主从骨架（字典）
```
PageHeader（字典分组数 + [刷新] [+ 新建字典条目（选中可维护分组才启用）]）
两栏：md:grid-cols-[220px_minmax(0,1fr)]
  ├─ 左 CodeSet 列表（nav，选中 bg-accent；按治理分级 system-enum/platform-preset/factory-custom）
  └─ 右 选中 CodeSet 的码值表（编码 ｜ 名称 ｜ 状态 ｜ 操作）+ Toolbar（在该字典内筛选）+ 分页
```
- **治理分级守门**：system-enum（系统枚举）→ 只读不可新增（[+ 新建] 禁用、Select 项 disabled）；platform-preset / factory-custom → 可新增。
- 新建条目：所属字典（Select，预填当前 CodeSet）｜编码\*｜显示名称\*｜启用（默认开）。编辑态：所属字典/编码只读，**只改名**（治理：Name 可改、code 不可改）。
- **闭环**：字典是受控值唯一来源，被物料等表单消费（维护→消费闭环）。

### 7.2 一致性约定（全主数据收口清单 — §1.5 评审逐条卡）
- [ ] **列序统一**：编码 ｜ 名称 ｜ typed 列 ｜ **状态(StatusBadge)** ｜ **更新时间(`formatDateTime`)** ｜ 操作(RowActions)。
- [ ] **空状态给出路**：筛选无果→[清空筛选]；首次无数据→[去新建]（有创建能力）或「去 {上游} 维护 →」（只读）。绝不空白。
- [ ] **说人话**：无工程术语（operationId/resourceType/code 字样/#/org/env/demo/seed/mock）；**码值全中文**（常量兜底）。
- [ ] **校验时机**：打开不红、点提交才红、必填空着不发请求 + 内联提示。
- [ ] **反馈闭环**：结果走 toast（`@/utils/notify`，错误映射人话）；校验走内联；**无常驻错误条**；列表加载失败用紧邻表格的内联条。
- [ ] **主题 token**：颜色用 token 跟随主题，不写死、不堆 AI 味装饰；高亮用 `--primary`。
- [ ] **诚实**：真实编号/无假分页假数据；后端缺的能力禁用 + tooltip，不放假按钮；搜索口径如实表述。
- [ ] **区块拼装**：全用 FE-2 区块（`PageHeader/SectionCards/Toolbar/DataTable/DataTablePagination/StatusBadge/RowActions`），不手搓裸表、不改 shadcn 原版、无深导入。

---

## 落地参照（现有实现，照抄起点）
- 平表列表 + 主从 + 多 Tab：`pages/master-data/facilities.vue`、`reference-data.vue`
- 行操作三件套：`components/masterData/MasterDataRowActions.vue`
- 分段标题：`components/masterData/FormSectionTitle.vue`
- 工人选择器 / 技能登记：`components/masterData/WorkerSelect.vue`、`composables` 的 `usePersonnelSkillAssignment`
- 黄金标准列表契约：`pages/goldStandardPages.contract.test.ts`

> 新建主数据页前：先按 §6 判定 IA 载体 → 选对应页型骨架（§1–§5/§7）→ 实现前过 AGENTS §1.5-A、完成后过 §1.5-B + 本文 §7.2 清单。
