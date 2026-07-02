# 产品工程模块 · 产品/IA/UX 设计（重构基准）

> 继「基础数据」之后重构的第二个业务模块。经成熟 PLM/MES 对标（SAP PLM/S4、Teamcenter、Windchill、Oracle Agile、D365、主流 MES）+ 后端代码事实核账 + 套用 `frontend/DESIGN/patterns/pages/master-data-templates.md` UX 模板。数据流定位：`基础数据(SKU/工作中心) → 产品工程(EBOM/MBOM/工艺路线/生产版本) → 需求计划(MRP) / MES(执行)`。

## 0. 为什么重做（现状问题）
- 现 `pages/engineering/index.vue` 把 **MBOM / 工艺路线 / 生产版本三个关系弱的独立实体塞进一排平铺 Tab**——正是基础数据被否的「杂物抽屉 Tab」反模式（违反 UX 模板 §6）。
- 隐性 bug：index.vue 用 `Released` 过滤状态，但后端版本枚举是 `Published`（对不上）。
- 工程数据有「受控对象 + 修订迭代 + 生效日期」的一等语义，平表 + Tab 表达不了。

## 1. 定位与核心认知
**产品工程不能复用基础数据的「列表 + 增删改」页型。** 工程数据 = **受控对象 + 修订（Draft/Published/Archived）+ 时间有效性（EffectiveDate/ValidFrom-To）**。成熟系统统一用三层骨架：**对象主页 → 结构/关系（多层 BOM 树 / 工序序列）→ 生命周期与变更（ECO 受控发布）**。「草稿/已发布/失效 + 修订血缘 + 生效日期」要做成贯穿全模块的视觉语言（状态徽章 + 时间区间 + 「发新修订」而非直接编辑）。

可见标签：只做 MBOM/路线/版本的页用「工艺工程」；含文档/物料/ECO 时用「产品工程(PLM)」（对齐导航地图）。

## 2. 实体模型 + 后端真实能力总账（§1.5：以代码事实为准）
端点见 `backend/services/Business/ProductEngineering/.../Endpoints/{ProductEngineering/ProductEngineeringReleaseEndpoints.cs, ProductionVersions/ProductionVersionEndpoints.cs}`；SDK 见 `frontend/packages/api-client/src/generated/business-console/sdk.gen.ts`。

| 实体 | 写 | 读 | 逐行编辑 | 关键缺口 |
|---|---|---|---|---|
| 工程文档 EngineeringDocument | `register`（按修订登记 + fileId） | **无 list/get** | — | 没读端点→登记完看不到 |
| 工程物料 EngineeringItem | `create-revision`（带 Release 标志） | **无 list/get** | — | 没读端点 |
| EBOM | `release`（整批 Lines[]，内部 Draft→Release 一气呵成） | `list`（**不含行明细**） | **不可** | list 无行、无 get-by-id |
| MBOM | `release`（整批 MaterialLines[]+RecipeLines[]，须引用已发布 EBOM） | `list`（**含 MaterialLines**，不含 RecipeLines） | **不可** | 无 RecipeLines、无 get |
| 工艺路线 Routing | `release`（整批 Operations[]=seq/workCenter/name/stdMinutes） | `list`（**不含工序明细**） | **不可** | 无工序、无 get |
| 工程变更 ECO/ECN | `release`（Open→Approve→Affect→Release 一步到位，带 AffectedVersions[]） | `list` + `get` + `impact-preview` | — | 无真实审批态；发布前影响预览只读 |
| 生产版本 ProductionVersion | `create`/`update`/`archive` | `list`（全字段）+ `resolve`（SKU+日期+批量→命中版本） | n/a | **唯一完整 CRUD** |
| MES 就绪度 | — | `getMesProductEngineeringReadiness` | — | — |

**两条决定页型的硬事实**：
1. **EBOM/MBOM/Routing/ECO = 查看 + 受控发布,不是维护编辑。** 无草稿态、无逐行 CRUD、无 update；「修改」= 填整套行/工序 + 新修订号 + 生效日 → `release` 出**新不可变版本**。页面核心动作是**「发布新版本」向导**,不是表格改单元格。
2. **生产版本是唯一真 CRUD 实体**（状态 `active/archived`，update 要求所绑 MBOM/Routing 为 `Published`）→ **最适合做示范页型**。

## 3. 修订后的 IA（页面树 + 载体 + 分期）
```
产品工程 (/engineering)
Phase 1（后端已支持，本轮做）
├─ 生产版本   /engineering/production-versions  [列表-详情/主从 + resolve]  ★示范页（唯一完整CRUD）
├─ MBOM       /engineering/mbom                 [列表 + 行展开看物料行 + 发布向导]
├─ 工艺路线   /engineering/routings             [列表 + 工序序列 + 发布向导]
└─ EBOM       /engineering/ebom                 [列表 + 发布向导（查看仅版本头，明细待后端）]
Phase 2（codex 已补 list/get，本轮完成页面）
├─ 工程物料   /engineering/items                [列表 + 新建修订向导 + get 详情]  ✅
├─ 工程文档   /engineering/documents            [列表 + 登记文档（fileId 文本，文件上传待接入）+ get]  ✅
└─ 工程变更   /engineering/eco                  [列表 + 发布变更（Open→Approve→Release 一步）+ get]  ✅
Phase 3（#628 / MAN-337 已补）
├─ BOM 分析   /engineering/bom-analysis         [多级树 + 爆炸 + 反查 + EBOM/MBOM 版本结构对比] ✅
└─ ECO 预览   /engineering/eco                  [发布前 affected downstream preview：MBOM/Routing/PV/MRP/MES/APS 候选] ✅
规划中（依赖后端 #397，未交付前用字典过渡，不假做）
└─ 标准工序   /engineering/standard-operations  [工序主数据：默认工作中心+标准工时]  ⏳ #397
注：ECO 后端为一步 release，无独立草稿/审批中间态；页面只呈现「已发布」真实态，不假做审批看板。影响预览不自动修改下游单据。文档 fileId 为文本登记，文件上传端点未接入。

### 标准工序（工序主数据，⏳ 规划，依赖 #397）
- **问题**：当前工艺路线的工序名走通用数据字典（`reference-data`，`codeSet=operation`），只有 code+名，**无默认工作中心/标准工时**；建路线时每行工作中心、工时仍逐行手填。成熟系统（SAP 标准工序 CA21 / 参考工序集 CA11、Oracle Standard Operations）把工序建模为**独立工程主数据**，预绑默认工作中心+标准工时+控制码，选工序即带出默认值。
- **终态**：后端补 `StandardOperation` 实体 + facade（#397）后，前端独立成「标准工序」工程页（与生产版本/EBOM 同级，从「数据字典」迁出）；工艺路线发布向导的工序来源由字典切换为标准工序，选工序自动**预填**该行 `workCenterCode`/`standardMinutes`（仍可逐行覆盖）。
- **过渡**：#397 交付前，routings.vue 继续用 `codeSet=operation` 字典（受控工序名，防自由手写），不假做主数据页。
```
- **生产版本（示范）**：列表-详情；详情主从（主=版本头，从=绑定的 MBOM+路线只读卡）。三件套全可用：新建（选 SKU + 已发布 MBOM + 已发布路线 + 有效期/批量/优先级/默认）、编辑（改绑定，校验须 Published）、归档（带 reason + 二次确认）。保留 `resolve` 解析卡（验证给定条件 MES 选中哪版）。顶部可放「已就绪供 MES 消费」真实指标卡。
- **MBOM**：list 带 MaterialLines → 行展开/Drawer 看物料行（SkuCode/Qty/UOM/ScrapRate）；RecipeLines 标「待后端明细」。发布向导：选已发布 EBOM（必填）+ SKU + 修订 + 物料行表 + 配方行表 → release。
- **工艺路线**：发布向导内**有序工序编辑器**（拖拽排序，每序指派**基础数据的工作中心**）；查看明细待后端 get。
- **EBOM**：列表 + 发布向导（父项 + 修订 + 生效日 + 组件行表）；查看仅版本头（明细待后端 get）。
- **纠反模式**：index.vue 三 Tab 拆成上述独立页后，旧 `/engineering` 收敛/下线。状态枚举统一改用后端真值 `Draft/Published/Archived`（修 `Released` bug）。

## 4. 与基础数据的衔接（引用关系，写清避免误解）
- **工程物料 EngineeringItem ≠ 基础数据 SKU**：非同源。MBOM/PV 用 `skuCode` 字符串引用**基础数据 SKU**；EngineeringItem（itemCode+修订）是工程自管对象，主要供 EBOM。表单 SKU 选择器从基础数据 SKU 列表取。
- **工艺路线工序 → 基础数据工作中心**：`WorkCenterCode` 指向 facilities 树叶子；选择器从基础数据工作中心取，缺则给「去基础数据维护」出路。
- **生产版本 → MES/计划**：PV 是工程域出海口，`resolve` + readiness 供 MES/计划消费，是工程↔执行的契约边界。

## 5. 后端缺口（consolidated issue 给 codex；Phase 1 不假做）
1. **全实体无 get-by-id**；EBOM/Routing list 不回行/工序明细，MBOM list 缺 RecipeLines → 查看详情只能看版本头。补 `GET .../engineering-boms/{code}/{rev}`、`.../routings/{code}/{rev}` 等返回完整明细。
2. **工程文档无 list/get**（仅 register）→ 文档页 Phase 1 建不了。补 `GET .../documents`。
3. **工程物料无 list/get**（仅 create-revision）→ 物料页 Phase 1 建不了。补 `GET .../items`。
4. **ECO 无真实审批态**（后端一步 release）→ 页面不能假造草稿/待审状态；若要真审批流则拆 Open/Approve/Release 状态机。
5. **BOM 对比 / 影响预览**：#628 / MAN-337 已补 `GET .../engineering/boms/diff` 与 `POST .../engineering/engineering-changes/impact-preview`，只做结构化预览，不自动修改下游单据。
6. 状态枚举对账：前端统一用 `Published`（非 `Released`）。
7. **数据字典 codeSet 审计**（**#397**，已按标准系统建模定结论）：reference-data 里被塞进字典的主数据/配置对象 → 丢结构。
   - 🔴 `operation` 标准工序 → 升 `StandardOperation` 主数据（默认工作中心+标准工时+控制码，对标 SAP CA21/CA11）；交付前 routings 用字典过渡。
   - 🔴 `product-category` 产品/物料分类 → 升**分类树**主数据（SAP 产品层级/物料组、Oracle Category 分层；字典扁平存不了父子）。
   - 🔴 `quality-reason` 质量原因 → 升**分组原因目录**（SAP QM Code Group→Code 两级 + 严重度/默认处置）。
   - 🟡 `skill` 技能 → 升**分组技能目录**（+证书有效期，对标资质目录），低优先；现扁平 skill + skill-level 为过渡。
   - 🔴 `barcode-rule` 条码规则 → 真实 BarcodeRule 实体在 business-barcode-label 服务（ruleCode/barcodeType/prefix/length/checksum），字典存的是 `code128` 码制替身（影子）。SKU 应引用真实 ruleCode；**前置依赖**：barcode-label 须种标准规则/开放维护（现无 Seeder），否则改接会卡 SKU 必填字段 → 协调后切，不盲改。
   - ✅ `storage-condition` 保留字典（SAP 中即扁平判定键，温区/危害属性在仓型/危险品主数据上）；其余 10 个 codeSet 字典恰当。

## 6. UX 与重构顺序
页型套 `master-data-templates.md`：生产版本=列表-详情/主从；MBOM/路线/EBOM=列表 + 发布向导（大 Drawer/全屏，非行内改单元格）；工序序列=有序行拖拽编辑器。全程「受控发布」语义显性化（状态徽章 + 生效日期 + 「发新修订」入口，不给「编辑已发布版本」）。

**重构顺序**：① 生产版本（真 CRUD 示范）→ ② MBOM（查看+发布，list 带明细）→ ③ 工艺路线 / EBOM（查看+发布，明细待后端）→ ④ 拆 index.vue 平铺 Tab + 下线 → ⑤ 物料/文档/ECO（待后端读端点，Phase 2）。
