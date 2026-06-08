# 基础数据模块 · 产品业务设计（business-console Master Data）

> 状态：草案 v1（PM + 业务领域 + UI/UX 多视角讨论综合，基于代码事实）
> 适用：`frontend/apps/business-console` 的「基础数据」域，后端 `Business/MasterData` 服务 + `BusinessGateway` master-data facade。
> 关联文档：
> - 字段/治理矩阵：[`business-master-data-field-matrix.md`](./business-master-data-field-matrix.md)
> - 流程制造补充：[`business-master-data-process-manufacturing-supplement.md`](./business-master-data-process-manufacturing-supplement.md)
> - 导航总图：[`frontend-navigation-map.md`](./frontend-navigation-map.md)
>
> 本文是「产品业务文档」基础，后续基础数据模块重构以本文为准。

---

## 0. 为什么重做（现状问题，已核代码事实）

1. **模块只增不改**：后端 14 个聚合**全部只有创建端点**，无更新/停用/删除/详情。前端 4 个页面里只有物料能新建，其余只读 → 用户「物料无法维护、客户供应商无维护、工厂资源无维护」属实。
2. **新建物料弹窗是 demo 残留**：副标题「用于减振器成品、零部件和原材料建档」是某客户业务措辞；产品分类是模板里**硬编码的 5 个减振器专用项**，来路不明、与字典零打通；`shelfLifePolicyCode/storageConditionCode/defaultBarcodeRuleCode` 是**自由文本输入**且把工程命名直接漏到 UI。
3. **工厂资源混成一张表**：Site/产线/工作中心/设备/班次/日历/班组/技能 8 类被压成一张只读「资源」表，层级丢失、无法分开维护。
4. **伙伴角色靠猜**：列表不回 `partnerType`，前端按 code 含 `cust/sup` 子串猜角色，命名不规范者一律「未分配」。
5. **列表信息贫乏**：唯一读取端点把任何实体压扁成 `{resourceType, code, displayName, active, snapshotVersion}` 五字段，**丢弃所有领域字段**（siteCode、capacity、partnerType、category…）。
6. **搜索误导**：分页是服务端，但搜索是「只过滤当前页」，占位却写「搜索…」让用户以为搜全量。

---

## 1. 模块定位与边界

**定位**：基础数据是平台**唯一的受控值与对象来源**。所有交易类业务（工单、库存、采购/销售、质检、设备点检）引用的「名词」都在这里建档、维护、受控；下游只引用、不新建。

> 一句话：**「基础数据是平台唯一的受控值与对象来源；下游只引用、不新建。」**

**设计原则**
- **受控优于自由**：能用字典约束的字段不放自由文本。
- **一个实体一个权威页**：杜绝「把 8 类压成一张表」。
- **生命周期完整**：主数据价值在「能改、能停用、能追溯版本」，不止新增。
- **诚实暴露后端边界**：后端没有的能力/字段，前端**不伪造、不猜**，用禁用入口 + 说明占位。

**进（In Scope）**：物料与产品（Sku）；工厂/产线/工作中心/设备（Site/ProductionLine/WorkCenter/DeviceAsset）；客户/供应商/承运商（BusinessPartner）；字典与受控值（ReferenceDataCode）；计量单位与换算（UnitOfMeasure/UomConversion）；组织与排班（Department/Team/Shift/WorkCalendar/PersonnelSkill）。

**不进（Non-Goals）**：BOM/工艺路线/工序版本（属产品工程，`/master-data/process` 归 engineering，不动）；库存余额/库位实物（inventory/wms）；价格/合同/账期（ERP）；用户/权限（平台管理）；主数据审批工作流、数据质量评分、跨组织主数据治理（远期）。

---

## 2. 概念数据模型

### 2.1 物理建模层级（空间/资源维度）

```
Site 工厂/厂区            一个物理生产基地；库存/组织/报表的顶层边界
 └─ ProductionLine 产线   一条物料/工艺流（SMT 线、总装线）；排产主承载单元
     └─ WorkCenter 工作中心/车间   一组能力等价、可互换的产能（排产的产能桶/调度对象）
         └─ DeviceAsset 设备       具体一台机器；OEE/点检/防错追溯的采集点
```

| 层级 | 制造业语义 | 在 MES/排程中的角色 | 维护方 |
|---|---|---|---|
| Site 工厂 | 物理生产基地 | 工单归属厂区、库存组织边界 | 工厂运营 |
| ProductionLine 产线 | 有节拍/产能的物料流 | **排产主承载**：工单下达到产线 | 生产技术/工艺 |
| WorkCenter 工作中心 | 能力等价可互换的产能桶 | **APS 有限产能调度对象**；工艺路线工序指向它 | 工艺/车间 |
| DeviceAsset 设备 | 具体机台 | 精排到机台、OEE、点检保养、追溯 | 设备/TPM |

**层级靠字符串 code 关联**（非数据库外键）：`production-line.siteCode→site`；`work-center.plantCode→site`、`lineCode→production-line`；`device.lineCode→production-line`、`workCenterCode→work-center`。

**术语澄清（中文工厂语境）**：口语「车间」(Workshop) 是空间+管理单元；ERP/MES 的「工作中心」(Work Center) 是排产概念，一个车间常含多个工作中心。本平台**不新建独立「车间」实体**，用 `WorkCenter` 承载，可用字典 `work-center-type`（车间/工段/工作中心/工位组）区分粒度。

### 2.2 组织维度（人/时间）

```
Department 部门  ── Team 班组（departmentCode↑、shiftCode）
Shift 班次（作息时段）   WorkCalendar 工作日历（工作日/假期，驱动可用产能）
PersonnelSkill 人员技能（技能矩阵，上岗资格校验）
```

下游关系：`WorkCalendar + Shift` 决定每个产线/工作中心**每天可用产能小时**（APS 输入）；`PersonnelSkill` 用于派工的上岗资格校验。

### 2.3 物料与单位

SKU 持有 6 个 UoM code（基本/库存/采购/销售/制造），创建时默认全部 = 基本单位。非基本单位必须有到基本单位的换算（UomConversion）。详见 §5。

---

## 3. 目标信息架构（IA）

### 3.1 当前 → 目标

| 当前（4 页，混乱） | 目标（6 页，职责清晰） |
|---|---|
| 物料与产品 `/skus`（建+读） | **物料与产品** `/skus` |
| 客户与供应商 `/partners`（只读、猜角色） | **业务伙伴** `/partners`（角色筛选 + 角色列 + 新建带角色） |
| 工厂资源 `/resources`（8 类压一张只读表） | **工厂与产线** `/facilities`（Tabs：工厂｜产线｜工作中心） + **设备台账** `/devices` |
| 字典 `/reference-data`（只读扁平） | **数据字典** `/reference-data`（CodeSet 主从，可新增） |
| —（班次/日历/班组/技能埋在 resources） | **组织与日历** `/organization`（Tabs：部门｜班组｜班次｜工作日历｜人员技能） |

### 3.2 导航树

```
基础数据 (master-data)
├── 物料与产品    /master-data/skus           [Sku]
├── 业务伙伴      /master-data/partners       [BusinessPartner]   角色筛选 + 角色列
├── 工厂与产线    /master-data/facilities     [Site/Line/WorkCenter]  Tabs: 工厂｜产线｜工作中心
├── 设备台账      /master-data/devices        [DeviceAsset]
├── 数据字典      /master-data/reference-data [ReferenceDataCode] CodeSet 主从
└── 组织与日历    /master-data/organization   [Dept/Team/Shift/Calendar/Skill]  Tabs
```

> **取舍**：工厂/产线/工作中心是同一层级链，放同一页用页内 Tabs（同一心智里维护工厂结构）；设备数量级大、检索维度多、归属常变，单独成页；班次/日历/班组/技能与产能层级无关，迁到「组织与日历」。**页内 Tabs 不进菜单树**（符合导航约束）。

### 3.3 各页职责（操作：查/增/改/停用）

| 页面 | 实体 | Phase 1 可做 | 受阻于后端 |
|---|---|---|---|
| 物料与产品 | Sku | 查、**新建**（重做表单，§6.2）、只读详情抽屉 | 编辑/停用/详情字段 |
| 业务伙伴 | BusinessPartner | 查（角色筛选）、**新建（显式选角色）** | 列表回角色、编辑/停用 |
| 工厂与产线 | Site/Line/WorkCenter | 各 Tab 查 + **新建**（产线选所属工厂、工作中心选工厂+产线） | 层级归属可视化、编辑/停用 |
| 设备台账 | DeviceAsset | 查、**新建**（选所属产线/工作中心） | 按产线过滤、编辑/停用 |
| 数据字典 | ReferenceDataCode | CodeSet 主从浏览、**新增码值/CodeSet** | 按 CodeSet 查询、编辑、种子 |
| 组织与日历 | Dept/Team/Shift/Calendar/Skill | 各 Tab 查 + **新建** | 编辑/停用 |

---

## 4. 关键产品决策

1. **产品分类（category）字典化**：从硬编码 demo 选项改为受控字典 `product-category` 驱动；用户在「数据字典」页维护分类，物料表单即可选到。Phase 1 用去 demo 的前端常量兜底 + 文案声明数据源为字典；Phase 2 切「按 CodeSet 拉取」端点，UI 不变。层级分类树（物料组）作为远期 Roadmap。
2. **`...Code` 自由文本 → 选字典**：`shelfLifePolicyCode/storageConditionCode/defaultBarcodeRuleCode` 由 `Input` 改为 `Select`，UI 只见业务词（保质期管理/存储条件/默认条码规则），取值受字典约束。
3. **平台枚举 vs 工厂字典**：`materialType/batchTrackingPolicy/serialTrackingPolicy/shelfLifePolicy` 带系统行为语义 → 平台预置枚举，只能启停不可改语义（前端常量即可）；`product-category/storage-condition/barcode-rule/quality-reason` 偏业务 → 平台预置常用值 + 工厂可维护。
4. **伙伴角色诚实处理**：列表不回 partnerType，**不再猜 code 子串**。Phase 1 用「角色筛选 + 角色列（含『未分配』并标注推断口径）+ 新建时显式选角色」；Phase 2 后端列表回 partnerType 后角色展示才精确。同一主体可兼多角色（客户+供应商互供），建议 `partnerType` 演进为多角色（§7.4 issue）。
5. **字典做成受控值中心**：字典页改 CodeSet 主从结构，成为唯一受控值来源，被物料表单消费，形成「维护→消费」闭环。
6. **诚实的能力分级**：编辑/停用/详情字段等后端未就绪的入口，**保留可见但禁用 + tooltip 说明**，绝不放会失败的假按钮。

---

## 5. 字典体系与种子数据

### 5.1 平台应预置的 CodeSet（受控值清单）

| CodeSet | 用途 | 示例码值（code=中文名） | 类别 |
|---|---|---|---|
| `material-type` | 物料类型 | raw-material=原材料 / semi-finished=半成品 / finished-goods=成品 / packaging=包装物 / consumable=辅料 / spare-part=备件 / tooling=工装刀具 | 半枚举（可增不可改语义） |
| `product-category` | 产品/物料分类 | electronic=电子料 / mechanical=机械件 / plastic=塑胶件 / hardware=五金件 / chemical=化学品 / assembly=组装件 | 工厂可维护 |
| `uom-dimension` | 计量量纲 | count=计数 / length=长度 / area=面积 / volume=体积 / weight=重量 / time=时间 | 系统枚举 |
| `batch-tracking-policy` | 批次策略 | none=不管理 / optional=可选 / mandatory=强制 | 系统枚举·不可改 |
| `serial-tracking-policy` | 序列策略 | none=不管理 / on-receipt=入库赋序 / on-production=生产赋序 / on-shipment=出货赋序 | 系统枚举·不可改 |
| `shelf-life-policy` | 保质期策略 | none=无 / fifo=先进先出 / fefo=先到期先出 / expiry-controlled=到期管控 | 系统枚举·不可改 |
| `storage-condition` | 仓储条件 | ambient=常温 / cold-2-8=冷藏 / frozen=冷冻 / dry=干燥防潮 / esd=防静电 / hazardous=危化 | 预置+可维护 |
| `barcode-rule` | 条码规则 | gs1-128=GS1-128 / code128-internal=内部条码 / qr-traceability=追溯二维码 / customer-spec=客户指定 | 工厂可维护 |
| `partner-type` | 伙伴角色 | supplier=供应商 / customer=客户 / carrier=承运商 | 系统枚举·不可改 |
| `quality-reason` | 质量原因/不良代码 | scratch=划伤 / dimension-ng=尺寸不良 / missing-part=缺件 / solder-defect=焊接不良 / contamination=污染 | 工厂可维护 |
| `compliance-tag` | 合规标签 | rohs=RoHS / reach=REACH / msd=湿敏元件 / ul=UL认证 | 预置+可维护 |
| `device-status` | 设备状态 | running=运行 / idle=待机 / maintenance=保养 / fault=故障 / scrapped=报废 | 系统枚举 |
| `line-type` | 产线类型 | flow=流水线 / cell=单元线 / discrete=离散 | 系统枚举 |
| `work-center-type` | 工作中心粒度 | workshop=车间 / section=工段 / work-center=工作中心 / station-group=工位组 | 系统枚举 |

**治理**：CodeSet 名称平台保留；系统枚举类只允许启停、不允许改 code 与语义；工厂可维护类只能停用、不能物理删除。

### 5.2 种子：计量单位（含量纲）

| Code | 中文名 | 量纲 |
|---|---|---|
| PCS 个/件 · SET 套 · PR 双 · BOX 箱 · ROLL 卷 | | count |
| M 米 · MM 毫米 | | length / M2 平方米=area |
| L 升 · ML 毫升 | | volume |
| KG 千克 · G 克 · T 吨 | | weight |
| H 小时 · MIN 分钟 | | time |

换算种子：`1 BOX=N PCS`（工厂填 N）、`1 M=1000 MM`、`1 KG=1000 G`、`1 T=1000 KG`、`1 L=1000 ML`、`1 H=60 MIN`、`1 PR=2 PCS`。

### 5.3 种子：组织/班次/日历
- Shift：`DAY=白班(08:00-20:00)`、`NIGHT=夜班(20:00-08:00,跨天)`、`NORMAL=常白班(08:30-17:30)`。
- WorkCalendar：`STANDARD=标准日历（周一至周五工作，周末休息，中国法定节假日）`。

---

## 6. UI/UX 规范

### 6.1 通用页面骨架（FE-2 区块）

```
PageHeader（基础数据 / <页面名>，count，[刷新] [+ 新建…]）
SectionCards（2~4 张概览：总数/启用/停用 等，hint 标口径）
Toolbar（搜索 + 筛选 Select；搜索表述见 §6.6）
DataTable（… | 状态 StatusBadge | 版本 | 操作 RowActions）
DataTablePagination（服务端 total）
```
所有页**新增操作列**（现四页都没有），用 `RowActions`（§6.5）。

### 6.2 新建/编辑物料 Dialog 重做（核心）

**标题/副标题（去 demo）**：
- 标题：`新建物料`（编辑态 `编辑物料 · {名称}`）
- 副标题：`为采购、生产、库存和销售建立统一的物料档案。带 * 为必填项。` （删除一切「减振器」等专名）

**字段（4 分组，工程术语→业务语言）**：

*基础信息*：物料编号（只读「保存后由系统分配」）｜物料名称\*（Input）｜产品分类\*（**Select·字典 `product-category`**，旁置「去数据字典维护 →」链接）｜物料类型\*（Select 枚举：成品/半成品/原材料/包材/服务）

*单位与计量*：基本单位\*（Select·字典 `uom`/前端常量兜底）｜多单位换算（**进阶折叠**，Phase 2）

*追踪与合规*：批次追踪\*（Select：不追踪/按需/必须）｜序列号追踪\*（Select 同上）｜投产前需质检（Checkbox）｜质量/合规标签（Input 逗号分隔，Phase 2 升级 chips）

*存储与条码*：保质期管理（**Select·`shelf-life-policy`**，原 `shelfLifePolicyCode` 自由文本）｜存储条件（**Select·`storage-condition`**）｜默认条码规则（**Select·`barcode-rule`**，进阶折叠）

> 默认只展开「基础信息~追踪」的必填项；多单位换算/合规标签/条码规则收进「进阶设置」折叠，降噪。

**编辑入口诚实处理**：行「查看」打开**只读详情抽屉**（展示现有 5 字段 + 「更多属性建设中」占位）；「编辑」按钮 `disabled` + tooltip「编辑功能即将上线」。

### 6.3 工厂与产线页（拆分）
- Tabs：`工厂｜产线｜工作中心`，每 Tab 一张独立列表（独立分页/搜索）。
- 新建产线 → 选所属工厂（siteCode）；新建工作中心 → 选所属工厂 + 产线。
- SectionCards：工厂数/产线数/工作中心数。
- 「查看该工厂下的产线 →」下钻：Phase 1 因列表不回归属字段，仅做意图跳转 + 标注；真实按工厂过滤为 Phase 2。

### 6.4 业务伙伴页（角色诚实）
- **角色筛选 + 角色列（不用 Tabs）**：Toolbar 加「角色：全部/客户/供应商/承运商/未分配」筛选；角色列用 Badge（客户 info / 供应商 success / 未分配 neutral，未分配带 tooltip「待后端返回角色字段」）。
- 概览卡的客户/供应商计数标 hint「按编码推断，待后端返回角色字段」。
- 新建伙伴 Dialog：名称\*｜**伙伴角色\*（多选 Checkbox：客户/供应商/承运商）**｜简称｜联系人/电话（进阶）｜统一社会信用代码（进阶）。底部 muted 提示「角色已保存；列表角色展示将在后端返回角色字段后生效」。

### 6.5 数据字典页（受控值中心）
- 左 CodeSet 列表（Phase 1 前端约定常量，§5.1）+ 右选中 CodeSet 的码值表（主从）。
- 新建字典条目 Dialog：所属字典（Select，预填当前 CodeSet）｜编码\*｜显示名称\*｜启用（默认开）｜备注（进阶）。
- 与物料表单闭环：Phase 1 共用同一前端常量；Phase 2 物料下拉改 `listReferenceDataByCodeSet` 实时拉取。

### 6.6 通用模式
- **RowActions**：至少「查看详情」可用；「编辑/停用」Phase 2 禁用 + tooltip；停用用 `AlertDialog` 二次确认。
- **空状态分两种**：有筛选无结果 →「没有符合条件的 X」+ [清空筛选]；首次无数据 →「还没有 X，点击创建第一条」+ [+ 新建]（仅有创建能力的页给新建出路）。
- **搜索诚实表述**：Phase 1（后端无 keyword）搜索占位「在当前页内筛选」+ 下方 hint「当前仅在本页 N 条内筛选，全量搜索即将上线」；Phase 2 接服务端 `keyword`。「共 N 条」始终用服务端 total。
- **新建反馈**：统一 `toast.success('X「{名称}」已创建')`，失败 `toast.error`。

### 6.7 术语对照（工程术语 → 业务用语，模块内禁止暴露左列）

| 工程 | 业务 | 工程 | 业务 |
|---|---|---|---|
| resourceType | 类型 | shelfLifePolicyCode | 保质期管理 |
| code | 编码/编号 | storageConditionCode | 存储条件 |
| displayName | 名称 | defaultBarcodeRuleCode | 默认条码规则 |
| active:false | 停用 | qualityRequired | 投产前需质检 |
| snapshotVersion | 版本 | complianceTags | 质量/合规标签 |
| baseUomCode | 基本单位 | CodeSet | 字典/字典分组 |
| materialType | 物料类型 | ReferenceData | 数据字典 |
| batchTrackingPolicy | 批次追踪 | partnerType / unknown | 角色 / 未分配 |
| serialTrackingPolicy | 序列号追踪 | site/line/work-center/device | 工厂/产线/工作中心/设备 |

（`operationId / idempotencyKey / organizationId / environmentId` 一律不展示。）

---

## 7. 能力 × 后端现实 · 分期与后端缺口

### 7.1 能力分级
- **[现在]** 后端已支持，本期落地：14 实体新建；通用 list（仅 5 字段）+ 分页；物料表单去 demo + 枚举/字典常量下拉；工厂/产线/工作中心/设备/字典/组织拆页建档；伙伴按角色创建。
- **[Phase 2]** 受阻于后端：编辑、停用/启用、详情 by-id、typed 列表字段（partnerType/siteCode/lineCode/category…）、字典按 CodeSet 查询、字典生产种子、SKU `...Code` 字典校验。

### 7.2 Phase 1（本次重构可落地，纯前端 + 接已有 create）
1. IA 重排：4 页 → 6 页（facilities 拆 Tab、devices 独立、organization 迁出、reference-data 主从）。
2. 物料表单去 demo（删副标题/减振器硬编码）；`...Code` 改字典 Select（常量兜底）；进阶折叠；成功 toast；只读详情抽屉。
3. 接通后端已有但 barrel 未接的 create：business-partner / unit-of-measure / production-line / reference-data（barrel 当前仅接了 SKU create）。
4. 伙伴按 partnerType 创建 + 角色筛选/角色列（标注推断口径，移除猜 code 子串逻辑）。
5. 字典 CodeSet 主从骨架 + 新增 CodeSet/码值。
6. 所有「编辑/停用」入口禁用 + tooltip 占位。
7. 全部页通过 frontend-gate（typecheck/单测/build）；新增页纳入 goldStandardPages 契约门禁。

### 7.3 Phase 2（待后端 issue 解锁）
编辑/停用/详情抽屉真值；typed 列表字段（解锁伙伴角色可靠分类、工厂层级可视化、设备按产线过滤、物料按 category 筛选）；字典 by-CodeSet + 种子（解锁物料表单真字典驱动 + 校验闭环）。

### 7.4 后端缺口 → 已发 issue（给 codex）
- **[#344]** MasterData 主数据写后可维护:update/rename + disable/enable + detail by-code（暴露领域 `Rename`/`Disable`）。（缺口 1/2/3）
- **[#345]** MasterData 列表回传领域字段（typed list）:partnerType、siteCode、lineCode、plantCode、workCenterCode、category、materialType、capacity 等。（缺口 4）
- **[#346]** ReferenceData 字典可用化:按 CodeSet 查询 + list 回 CodeSet + 生产种子（§5.1/5.2/5.3）+ SKU `category/...Code` 字典存在性校验。（缺口 5/6/7）
- **[#347]** BusinessPartner `partnerType` 演进为多角色（同一主体兼客户+供应商）+ 税号唯一去重。（缺口 8，P2）

### 7.5 业务规则（落地铁律）
- **引用完整性**：产线 siteCode 必填且指向启用工厂；工作中心 plantCode 必填、lineCode 选填且需属于同工厂；设备 lineCode/workCenterCode 至少一项且需存在；保存做跨级一致性校验。
- **Code 唯一性**：在 (org, env, 实体类型) 内唯一；ReferenceData 在 (org, env, CodeSet) 内唯一；伙伴税号在 (org, env) 内唯一。Code 创建后不可改（被下游引用），Name 可改。
- **停用而非删除**：主数据与字典一律软删除（置停用）；停用前校验下游无启用引用；新单据只能引用启用项，历史单据保留对停用项引用。
- **单位**：任何非基本 UoM 必须有到基本单位的换算，换算系数 > 0、不重复、不成环。

---

## 8. 痛点 → 方案映射

| # | 用户痛点 | 方案 | 阶段 |
|---|---|---|---|
| 1 | 物料弹窗副标题 demo、表单费解、分类来路不明无处维护 | 删 demo 副标题/硬编码；分类字典 `product-category` 驱动、字典页可维护；字段按「平台枚举固定下拉 / 业务码值选字典」分类；`...Code` 改 Select | 去 demo+枚举【P1】；字典驱动【P2】 |
| 2 | 物料无法维护 | 物料页补查看/编辑/停用入口 + 只读详情抽屉 | 入口/详情【P1】；编辑停用【P2】 |
| 3 | 客户供应商缺失、无维护 | 接 partner create；角色筛选+角色列；新建显式选角色；停止猜 code | 创建+筛选【P1】；角色精确+编辑【P2】 |
| 4 | 工厂资源混在一起且只读 | 拆「工厂与产线」(Tabs 工厂/产线/工作中心) + 「设备台账」+「组织与日历」；各 Tab 可建档 | 拆分+建档【P1】；编辑停用+层级可视化【P2】 |

---

## 9. 验收标准

**Phase 1（本次必须达成，可测）**
1. 「基础数据」下出现 6 页（物料与产品/业务伙伴/工厂与产线/设备台账/数据字典/组织与日历），路由如 §3.2。
2. 原 `/resources`「8 类压一表」不复存在；工厂/产线/工作中心在「工厂与产线」三 Tab 可分别查看与新建；班次/日历/班组/技能/部门迁至「组织与日历」。
3. 物料新建弹窗**无任何 demo/减振器字样**；副标题中性；分类等下拉**不含硬编码 demo 选项**。
4. 业务伙伴页有角色筛选 + 角色列；新建表单**必须显式选角色**；代码中**无靠 code 子串猜角色**逻辑。
5. 物料/伙伴/产线/字典码值 create 在 UI 均可成功提交并刷新（验证 barrel 已接通）。
6. 字典页呈现 CodeSet 主从，可新增 CodeSet 与码值。
7. 「编辑/停用」入口存在但禁用 + 提示，无会失败的假按钮。
8. frontend-gate 全绿；新增/改动页有契约/单测覆盖（参照 goldStandardPages.contract.test）。

**Phase 2（后端 issue 合入后追加）**
9. 任一实体可编辑名称/属性并在列表反映；可停用并以状态徽标区分；详情抽屉显示领域字段。
10. 伙伴角色基于 list 回传 partnerType，刷新后仍正确。
11. 工厂层级（产线归属工厂、工作中心归属产线/工厂、设备归属产线/车间）在列表/详情可见、可按归属过滤。
12. 物料 `...Code` 下拉来自字典 by-CodeSet；全新环境因种子有可选项；后端拒绝不在字典中的码值。

---

## 附：本设计依据的代码事实锚点
- 通用列表压扁 5 字段：`backend/.../MasterData/.../Application/Queries/ListMasterDataResourcesQuery.cs`
- 网关 facade（唯一前端接口面，仅 create）：`backend/gateway/BusinessGateway/.../Endpoints/MasterData/BusinessConsoleMasterDataEndpoints.cs`
- SKU `category/...Code` 自由字符串、无字典校验：`backend/.../MasterData/.../Application/Commands/MasterData/CreateMasterDataCommands.cs`
- 物料弹窗 demo 副标题 + 硬编码分类 + `...Code` 自由文本：`frontend/apps/business-console/src/pages/master-data/skus.vue`
- 伙伴角色猜 code 子串：`frontend/apps/business-console/src/pages/master-data/masterDataPageHelpers.ts`
- 资源 8 类压一表：`frontend/apps/business-console/src/pages/master-data/resources.vue`
- barrel 仅接 SKU create：`frontend/packages/api-client/src/business-console.ts`
