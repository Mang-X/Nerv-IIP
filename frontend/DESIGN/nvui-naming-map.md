# NvUI 命名与 token 映射表

本页是 [ADR 0020：NvUI 组件命名、token 场景命名空间与样式隔离](../../docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md)
的**参考数据**。命名规则本身仍在那篇 ADR：`Nv` 前缀边界、PC 素名规则、R1–R5 判定流程、
1.3 废弃处置、token 场景命名空间的划分依据、契约层永久冻结名单。本页只承载按那套规则
逐件算出的**映射结果**——组件旧名 → 新名全表、`--sb-*` → `--nv-scr-*` 30 项、PC 自有
语义 token 的 nv 化清单。

分开住的理由是两者变更频率不同：规则只被新决策取代，映射表却随代码增长。附录 A 冻结后
已因新增组件增补四次（下文四个「收口后新增」块：MAN-439/#793 的 combobox 家族、
PR #1093 的指标家族件、演示走查整改前端批 1、演示走查整改 MES 前端批），也就是每加一个
组件都要编辑一次决策记录。**新增或改名组件时更新本页，不要回头编辑 ADR**；若映射与规则
冲突，或需要改动判定规则本身，那是决策变更，走 ADR。

小节名保留搬迁前的批次括注（如「MAN-433/435 执行输入」），那是当时事实。

---

## 附录 A：组件旧名 → 新名完整映射表（MAN-433/435 执行输入）

判定依据列：`PC` = PC 素名规则（去 Pro/裸名升级）；`R1`–`R5` 见 1.2；`废` = 1.3 处置。

> **收口后新增（无旧名，完全不含 Pro）。** MAN-439（#793）在 `pc/combobox/` 新增两个
> 全新组件，直接以 `Nv*` 规范名称落地（R1），不涉及旧名映射：
>
> - `NvCombobox`（输入联想框：文本输入即过滤建议，允许自由录入）
> - `NvSearchSelect`（弹出选择框：可搜索的弹出单选，仅选不填）
>
> 二者的**文件名即 `NvCombobox.vue`/`NvSearchSelect.vue`**（不带 `Pro` 后缀），且内部**不含**
> `data-slot="*-pro"` / `.ds-*` / `.sb-*`（纯 Tailwind utility 类 + ARIA `role` 语义）——即
> 直接落在 S4 收口（#896）后的 `pc/` 目标形态，不引入任何 `pro→nv` 债。已并入
> `nvui-naming.contract.test.ts` 的冻结规范集合。

> **收口后新增（无旧名，完全不含 Pro）。** PR #1093 在 `pc/card/` 新增两个指标家族件，
> 直接以 `Nv*` 规范名称落地：
>
> - `NvMetricRing`（环形构成指标：多色分段环 + 图例联动，表达「部分与整体」）
> - `NvMetricStrip`（横向多指标条：一卡多口径，分隔线代卡缝）
>
> 判定依据（§1.2 尾「新组件归属判定」→ §1.1(2)）：先按交互密度/视距归属——二者是
> **PC 指针紧凑表面**的件，故落 `pc/`；其次二者与既有 `NvMetricCard` 是**结构不同的独立件**
> 而非其变体（`NvMetricRing` 是横向 gauge + 图例的构成图，`NvMetricStrip` 是横排多 cell 的
> 指标条，均无法并入 `NvMetricCard` 的纵向单值骨架，不走"一件多模式"）。定名走 **§1.1(2)
> PC 层取素名**：`Nv` + 素名（`MetricRing`/`MetricStrip`）——§1.2 的 R1–R5 只用于
> screen/touch/mobile 候选名，PC 件不适用。与大屏层既有 `NvRingGauge`（纯仪表原语、
> `--nv-scr-*` 深色）不构成冲突：二者分属不同表面，按 §1.2 尾“跨两个表面拆两件”各自
> 实现、各自命名。文件名即 `NvMetricRing.vue`/`NvMetricStrip.vue`，内部无
> `*-pro`/`.ds-*`/`.sb-*`。已并入 `nvui-naming.contract.test.ts` 冻结规范集合。
> `NvMetricCard` 的各 variant 下半区拆为 `pc/card/parts/` 内部实现件，命名
> `NvMetric*Part`（`NvMetricTipPart` 等 7 件）：保持品牌层 `Nv` 前缀统一语义，但
> **不导出、不入 barrel、不入冻结集合**——`*Part` 后缀即「某公开件的私有组成部分」。

> **收口后新增（无旧名，完全不含 Pro）。** 演示走查整改（前端批 1）在 `blocks/` 新增两个
> 跨模块复用的选择类区块件，直接以 `Nv*` 规范名称落地：
>
> - `NvEntityPicker`（实体选择弹窗：可搜索的实体选择对话框，仅选不填；名称 + 编码 +
>   辅助信息三列展示，底部注明数据来源，适合上百条的主数据目录）
> - `NvCascadePicker`（级联选择器：一行多级依赖选择，如 车间→产线→设备；每级为可搜索
>   弹出单选，选上级自动清空下游，选项过滤由调用方组装）
>
> 判定依据（§1.2 尾「新组件归属判定」→ §1.1(2)）：二者是 **PC 指针紧凑表面**的
> 页面级复用区块（组合 `pc/` 的 `NvDialogContent` / `NvSearchSelect` 而成），故落
> `blocks/`；定名走 **§1.1(2) PC 层取素名**：`Nv` + 素名（`EntityPicker`/`CascadePicker`），
> R1–R5 不适用。文件名即 `NvEntityPicker.vue`/`NvCascadePicker.vue`，内部无
> `*-pro`/`.ds-*`/`.sb-*`，`data-slot` 用 `nv-` 前缀。已并入
> `nvui-naming.contract.test.ts` 冻结规范集合。

> **收口后新增（无旧名，完全不含 Pro）。** 演示走查整改（MES 前端批）在 `blocks/` 再增一件：
>
> - `NvGroupPanel`（可折叠分组面板：把长列表按业务父级——工单 / 客户 / 设备 / 批次——
>   切成若干组，每组常驻标题行 + 可折叠内容区；只管呈现与展开态，不承担取数与分页）
>
> 判定依据同上（§1.2 尾「新组件归属判定」→ §1.1(2)）：PC 指针紧凑表面的页面级复用区块，
> 故落 `blocks/`；定名走 §1.1(2) PC 层取素名：`Nv` + 素名（`GroupPanel`），R1–R5 不适用。
> 未取 `NvCollapsible` 是为避让 shadcn 原版同名件的语义——本件不是原版 Collapsible 的重建版，
> 而是「分组容器」这一业务语义的区块件。文件名即 `NvGroupPanel.vue`，`data-slot` 为
> `nv-group-panel`。已并入 `nvui-naming.contract.test.ts` 冻结规范集合，
> 文档站页 `components/desktop/group-panel`。

### A1. PC 素名层 — `pc/`（35 目录，116 个组件导出）

| 目录              | 旧名                                 | 新名                       |
| ----------------- | ------------------------------------ | -------------------------- |
| alert-dialog      | AlertDialogPro                       | NvAlertDialog              |
|                   | AlertDialogProAction                 | NvAlertDialogAction        |
|                   | AlertDialogProCancel                 | NvAlertDialogCancel        |
|                   | AlertDialogProContent                | NvAlertDialogContent       |
|                   | AlertDialogProDescription            | NvAlertDialogDescription   |
|                   | AlertDialogProFooter                 | NvAlertDialogFooter        |
|                   | AlertDialogProHeader                 | NvAlertDialogHeader        |
|                   | AlertDialogProMedia                  | NvAlertDialogMedia         |
|                   | AlertDialogProTitle                  | NvAlertDialogTitle         |
|                   | AlertDialogProTrigger                | NvAlertDialogTrigger       |
| badge             | BadgePro                             | NvBadge                    |
| button            | ButtonPro                            | NvButton                   |
| card              | CardPro                              | NvCard                     |
|                   | CardProAction                        | NvCardAction               |
|                   | CardProContent                       | NvCardContent              |
|                   | CardProDescription                   | NvCardDescription          |
|                   | CardProFooter                        | NvCardFooter               |
|                   | CardProHeader                        | NvCardHeader               |
|                   | CardProTitle                         | NvCardTitle                |
|                   | MetricCardPro                        | NvMetricCard               |
| carousel          | CarouselPro                          | NvCarousel                 |
| chart             | AreaChartPro                         | NvAreaChart                |
|                   | LineChartPro                         | NvLineChart                |
|                   | BarChartPro                          | NvBarChart                 |
|                   | DonutChartPro                        | NvDonutChart               |
| checkbox          | CheckboxPro                          | NvCheckbox                 |
| command           | CommandPro                           | NvCommand                  |
| data-table        | DataTablePro                         | NvDataTable                |
|                   | DataTablePaginationPro               | NvPagination               |
|                   | DataTableToolbarPro                  | NvDataTableToolbar         |
| date-picker       | DatePickerPro                        | NvDatePicker               |
|                   | DateRangePickerPro                   | NvDateRangePicker          |
| descriptions      | DescriptionsPro                      | NvDescriptions             |
| dialog            | DialogPro（reka 再导出）             | NvDialog                   |
|                   | DialogProTrigger（reka 再导出）      | NvDialogTrigger            |
|                   | DialogProClose（reka 再导出）        | NvDialogClose              |
|                   | DialogProContent                     | NvDialogContent            |
|                   | DialogProTitle                       | NvDialogTitle              |
|                   | DialogProDescription                 | NvDialogDescription        |
|                   | DialogProHeader                      | NvDialogHeader             |
|                   | DialogProFooter                      | NvDialogFooter             |
| dropdown-menu     | DropdownMenuPro                      | NvDropdownMenu             |
|                   | DropdownMenuProCheckboxItem          | NvDropdownMenuCheckboxItem |
|                   | DropdownMenuProContent               | NvDropdownMenuContent      |
|                   | DropdownMenuProGroup                 | NvDropdownMenuGroup        |
|                   | DropdownMenuProItem                  | NvDropdownMenuItem         |
|                   | DropdownMenuProLabel                 | NvDropdownMenuLabel        |
|                   | DropdownMenuProRadioGroup            | NvDropdownMenuRadioGroup   |
|                   | DropdownMenuProRadioItem             | NvDropdownMenuRadioItem    |
|                   | DropdownMenuProSeparator             | NvDropdownMenuSeparator    |
|                   | DropdownMenuProShortcut              | NvDropdownMenuShortcut     |
|                   | DropdownMenuProSub                   | NvDropdownMenuSub          |
|                   | DropdownMenuProSubContent            | NvDropdownMenuSubContent   |
|                   | DropdownMenuProSubTrigger            | NvDropdownMenuSubTrigger   |
|                   | DropdownMenuProTrigger               | NvDropdownMenuTrigger      |
|                   | DropdownMenuProPortal（reka 再导出） | NvDropdownMenuPortal       |
| field             | FieldPro                             | NvField                    |
|                   | FieldProContent                      | NvFieldContent             |
|                   | FieldProDescription                  | NvFieldDescription         |
|                   | FieldProError                        | NvFieldError               |
|                   | FieldProGroup                        | NvFieldGroup               |
|                   | FieldProLabel                        | NvFieldLabel               |
|                   | FieldProLegend                       | NvFieldLegend              |
|                   | FieldProSeparator                    | NvFieldSeparator           |
|                   | FieldProSet                          | NvFieldSet                 |
|                   | FieldProTitle                        | NvFieldTitle               |
| filter-bar        | FilterBarPro                         | NvFilterBar                |
| form-section      | FormSectionPro                       | NvFormSection              |
| input             | InputPro                             | NvInput                    |
| kanban            | KanbanPro                            | NvKanban                   |
| loader            | Loader                               | NvLoader                   |
| metric-comparison | MetricComparisonPro                  | NvMetricComparison         |
| navigation-menu   | NavigationMenuPro                    | NvNavigationMenu           |
|                   | NavigationMenuProContent             | NvNavigationMenuContent    |
|                   | NavigationMenuProIndicator           | NvNavigationMenuIndicator  |
|                   | NavigationMenuProItem                | NvNavigationMenuItem       |
|                   | NavigationMenuProLink                | NvNavigationMenuLink       |
|                   | NavigationMenuProList                | NvNavigationMenuList       |
|                   | NavigationMenuProTrigger             | NvNavigationMenuTrigger    |
|                   | NavigationMenuProViewport            | NvNavigationMenuViewport   |
| notify            | NotifierHost                         | NvNotifierHost             |
| popconfirm        | PopconfirmPro                        | NvPopconfirm               |
| radio             | RadioGroupPro                        | NvRadioGroup               |
|                   | RadioGroupProItem                    | NvRadioGroupItem           |
| record-card       | RecordCardPro                        | NvRecordCard               |
| select            | SelectPro                            | NvSelect                   |
|                   | SelectProTrigger                     | NvSelectTrigger            |
|                   | SelectProContent                     | NvSelectContent            |
|                   | SelectProItem                        | NvSelectItem               |
|                   | SelectProGroup（reka 再导出）        | NvSelectGroup              |
|                   | SelectProValue（reka 再导出）        | NvSelectValue              |
| sheet             | SheetPro（reka 再导出）              | NvSheet                    |
|                   | SheetProTrigger（reka 再导出）       | NvSheetTrigger             |
|                   | SheetProClose（reka 再导出）         | NvSheetClose               |
|                   | SheetProContent                      | NvSheetContent             |
|                   | SheetProTitle                        | NvSheetTitle               |
|                   | SheetProDescription                  | NvSheetDescription         |
|                   | SheetProHeader                       | NvSheetHeader              |
|                   | SheetProFooter                       | NvSheetFooter              |
| sidebar           | SidebarProBrand                      | NvSidebarBrand             |
|                   | SidebarProDot                        | NvSidebarDot               |
|                   | SidebarProSub                        | NvSidebarSub               |
|                   | SidebarProUser                       | NvSidebarUser              |
| slider            | SliderPro                            | NvSlider                   |
| status            | StatusDot                            | NvStatusDot                |
|                   | StatusBadgePro                       | NvStatusBadge              |
| switch            | SwitchPro                            | NvSwitch                   |
| tabs              | TabsPro                              | NvTabs                     |
|                   | TabsProContent                       | NvTabsContent              |
|                   | TabsProList                          | NvTabsList                 |
|                   | TabsProTrigger                       | NvTabsTrigger              |
| time-picker       | TimePickerPro                        | NvTimePicker               |
| timeline          | TimelinePro                          | NvTimeline                 |
| tooltip           | TooltipPro（reka 再导出）            | NvTooltip                  |
|                   | TooltipProProvider（reka 再导出）    | NvTooltipProvider          |
|                   | TooltipProTrigger（reka 再导出）     | NvTooltipTrigger           |
|                   | TooltipProContent                    | NvTooltipContent           |

**pro 层派生类型/常量随改**：`DataTableProAlign → NvDataTableAlign`、
`DataTableProColumn → NvDataTableColumn`、`DataTableProDensity → NvDataTableDensity`、
`DataTableProFilterOption → NvDataTableFilterOption`、`DataTableProFilters →
NvDataTableFilters`、`DataTableProSort → NvDataTableSort`、`FieldProVariants →
NvFieldVariants`、`fieldProVariants → nvFieldVariants`。
不带 Pro 的独立类型（`LineSeries`、`BarSeries`、`DonutSlice`、`CommandGroup`、
`CommandItem`、`DateRange`、`DescriptionItem`、`FilterField`、`FilterFieldOption`、
`KanbanColumn`、`KanbanTone`、`MetricComparisonSide`、`RecordCardMeta`、
`RecordCardStatus`、`TimelineItem`、`TimelineTone`）与函数
（`messagePro`、`notificationPro`、`dismissNotify`、`useNotifyStore` 及其类型）不改名。

### A2. PC 素名层 — `blocks/`（9 目录，11 个组件导出）

| 旧名                | 新名                                                                  | 依据 |
| ------------------- | --------------------------------------------------------------------- | ---- |
| AppShellInset       | NvAppShellInset                                                       | PC   |
| DataTable           | **不授予 Nv 名，@deprecated** → 迁移到 NvDataTable（原 DataTablePro） | 废   |
| DataTablePagination | **@deprecated** → NvPagination（原 DataTablePaginationPro）           | 废   |
| PageHeader          | NvPageHeader                                                          | PC   |
| RowActions          | NvRowActions                                                          | PC   |
| SectionCard         | NvSectionCard                                                         | PC   |
| SectionCards        | NvSectionCards                                                        | PC   |
| StatusBadge         | **@deprecated** → NvStatusBadge（原 StatusBadgePro）                  | 废   |
| ThemePicker         | NvThemePicker                                                         | PC   |
| ThemeToggle         | NvThemeToggle                                                         | PC   |
| Toolbar             | NvToolbar                                                             | PC   |

（`resolveStatus` 函数与 `ResolvedStatus`/`StatusTone`/`PageHeaderCrumb`/
`TrendDirection`/`DataTableAlign`/`DataTableColumn`/`DataTableSort` 类型不改名；
已弃用组件的旧类型随组件在 S4 一并删除。）

### A3. PC 素名层 — `layout/`（8 件）

| 旧名      | 新名        |     | 旧名        | 新名          |
| --------- | ----------- | --- | ----------- | ------------- |
| App       | NvApp       |     | PageAside   | NvPageAside   |
| AppHeader | NvAppHeader |     | PageGrid    | NvPageGrid    |
| Container | NvContainer |     | PageColumns | NvPageColumns |
| Page      | NvPage      |     | PageSection | NvPageSection |

### A4. screen 层（34 件：33 导出 + 1 未导出）

| 旧名                         | 新名                                      | 依据                                                    |
| ---------------------------- | ----------------------------------------- | ------------------------------------------------------- |
| ScreenPanel                  | NvScreenPanel                             | R1                                                      |
| ScreenScrollArea             | NvScreenScrollArea                        | R1                                                      |
| ScreenScaler                 | NvScreenScaler                            | R1                                                      |
| ScreenHeader                 | NvScreenHeader                            | R1                                                      |
| ScreenButton                 | NvScreenButton                            | R1                                                      |
| ScreenTable                  | NvScreenTable                             | R1                                                      |
| ScreenSelect                 | NvScreenSelect                            | R1                                                      |
| ScreenSearch                 | NvScreenSearch                            | R1                                                      |
| ScreenInput                  | NvScreenInput                             | R1                                                      |
| ScreenTabs                   | NvScreenTabs                              | R1                                                      |
| ScreenSegmented              | NvScreenSegmented                         | R1                                                      |
| ScreenSwitch                 | NvScreenSwitch                            | R1                                                      |
| ScreenPagination             | NvScreenPagination                        | R1                                                      |
| ScreenBarChart               | NvScreenBarChart                          | R1                                                      |
| ScreenDonut                  | NvScreenDonut                             | R1                                                      |
| ScreenPareto                 | NvScreenPareto                            | R1                                                      |
| OeeHero                      | NvOeeHero                                 | R4 工业专名                                             |
| TaktGantt                    | NvTaktGantt                               | R4 工业专名                                             |
| DigitalFlop                  | NvDigitalFlop                             | R4 大屏专名                                             |
| RingGauge                    | NvRingGauge                               | R3b 专名图形                                            |
| CapsuleBar                   | NvCapsuleBar                              | R3b 专名主导                                            |
| Sparkline                    | NvSparkline                               | R3b 专名图形                                            |
| ScrollBoard                  | NvScrollBoard                             | R4 大屏专名                                             |
| KpiBar                       | NvKpiBar                                  | R3b 专名(Kpi)主导                                       |
| AlarmTable                   | NvAlarmTable                              | R3b 专名(Alarm)主导                                     |
| TrendChart                   | NvScreenTrendChart                        | R3b 通用修饰(Trend)+原语(Chart)                         |
| StatusCard                   | NvScreenStatusCard                        | R5 Status 家族                                          |
| StatusTag                    | NvScreenStatusTag                         | R5 Status 家族                                          |
| StatusLight                  | NvScreenStatusLight                       | R5 Status 家族                                          |
| TitleBar                     | NvTitleBar                                | R4 大屏装饰语汇（PC 对应位为 NvPageHeader，无真实撞名） |
| TechFrame                    | NvTechFrame                               | R4 大屏装饰专名                                         |
| BorderPanel                  | NvBorderPanel                             | R4 大屏装饰专名                                         |
| GlowDivider                  | NvGlowDivider                             | R4 大屏装饰专名                                         |
| WaterLevel（未导出、零引用） | 待 MAN-435 定：导出为 NvWaterLevel 或删除 | 1.3                                                     |

（`scale.ts`/`useScreenData.ts` 的全部导出不改名。）

### A5. touch 层（5 件）

| 旧名           | 新名             | 依据                  |
| -------------- | ---------------- | --------------------- |
| TouchButton    | NvTouchButton    | R1                    |
| TouchSegmented | NvTouchSegmented | R1                    |
| QtyStepper     | NvQtyStepper     | R3b 专名(Qty)主导     |
| StatTile       | NvStatTile       | R4 自造名             |
| StationBar     | NvStationBar     | R3b 专名(Station)主导 |

（`SegmentOption` 类型不改名。）

### A6. `@nerv-iip/ui-mobile`（47 个组件导出，43 件）

| 旧名             | 新名                     | 依据                                                                |
| ---------------- | ------------------------ | ------------------------------------------------------------------- |
| AppShellMobile   | NvAppShellMobile         | R1（词序保持）                                                      |
| MobileButton     | NvMobileButton           | R1                                                                  |
| MobileSwitch     | NvMobileSwitch           | R1                                                                  |
| MobileInput      | NvMobileInput            | R1                                                                  |
| MobileRadioGroup | NvMobileRadioGroup       | R1                                                                  |
| MobileRadioItem  | NvMobileRadioItem        | R1                                                                  |
| MobileTabs       | NvMobileTabs             | R1                                                                  |
| MobileCheckbox   | NvMobileCheckbox         | R1                                                                  |
| MobileDatePicker | NvMobileDatePicker       | R1                                                                  |
| MobileDialog     | NvMobileDialog           | R1                                                                  |
| MobileGrid       | NvMobileGrid             | R1                                                                  |
| MobileToast      | NvMobileToast            | R1                                                                  |
| MobileAvatar     | NvMobileAvatar           | R1                                                                  |
| MobileSkeleton   | NvMobileSkeleton         | R1                                                                  |
| MobileProgress   | NvMobileProgress         | R1                                                                  |
| MobileSlider     | NvMobileSlider           | R1                                                                  |
| MobileImage      | NvMobileImage            | R1                                                                  |
| Badge            | NvMobileBadge            | R2 撞原版 Badge                                                     |
| Empty            | NvMobileEmpty            | R2 撞原版 Empty                                                     |
| DropdownMenu     | NvMobileDropdownMenu     | R2 撞原版 DropdownMenu                                              |
| DropdownMenuItem | NvMobileDropdownMenuItem | R2 撞原版 DropdownMenuItem                                          |
| Tag              | NvMobileTag              | R3（Arco: Tag）                                                     |
| Divider          | NvMobileDivider          | R3（Arco: Divider）                                                 |
| Rate             | NvMobileRate             | R3（Arco: Rate；PC coverage P2 待建）                               |
| Steps            | NvMobileSteps            | R3（Arco: Steps）                                                   |
| Collapse         | NvMobileCollapse         | R3（Arco: Collapse）                                                |
| Result           | NvMobileResult           | R3（Arco: Result；PC coverage P2 待建）                             |
| ScanBar          | NvScanBar                | R3b 专名(Scan)主导（上游钦定例）                                    |
| ListRow          | NvListRow                | R4 自造名                                                           |
| BottomSheet      | NvBottomSheet            | R4 移动专名                                                         |
| NavBar           | NvNavBar                 | R4 移动专名（PC 语汇为 Header/Breadcrumb）                          |
| Cell             | NvCell                   | R4 移动语汇（Vant/TDesign 共识；PC 单元格语汇为 TableCell，不同名） |
| CellGroup        | NvCellGroup              | R4 同上                                                             |
| TabBar           | NvTabBar                 | R4 移动专名                                                         |
| NoticeBar        | NvNoticeBar              | R4 移动专名                                                         |
| SearchBar        | NvSearchBar              | R4 移动专名（PC 搜索语汇为 Command/Input）                          |
| Stepper          | NvStepper                | R4（Arco 无 Stepper，PC 对应件规划名为 InputNumber）                |
| Picker           | NvPicker                 | R4 移动专名（滚轮选择器）                                           |
| ActionSheet      | NvActionSheet            | R4 移动专名                                                         |
| SwipeCell        | NvSwipeCell              | R4 移动专名                                                         |
| PullRefresh      | NvPullRefresh            | R4 移动专名                                                         |
| InfiniteList     | NvInfiniteList           | R4 自造名                                                           |
| VirtualList      | NvVirtualList            | R4 自造名                                                           |
| Fab              | NvFab                    | R4 移动专名                                                         |
| NumberKeyboard   | NvNumberKeyboard         | R4 移动专名                                                         |
| Swiper           | NvSwiper                 | R4 移动语汇（PC 轮播已定名 NvCarousel）                             |
| SwiperItem       | NvSwiperItem             | R4 同上                                                             |

（`cn`、`MOBILE_OVERLAY_TARGET` 及全部独立类型（`TabItem`、`MobileTabItem`、
`StepItem`、`ActionItem`、`SwipeAction`、`PickerOption`、`GridItem`、`FabAction`、
`DropdownOption`）不改名。）

### A7. 不参与改名的导出（明确列出，防代码转换误伤）

- `components/ui/` 34 个目录的全部导出（31 个 shadcn 原版目录 + `file-preview`、
  `file-upload`、`date-picker` 三个沿用原版命名的自研目录）：`Button`、`Badge`、`Table`、
  `Dialog`、`Sidebar` 家族、`FileUpload`/`FilePreview` 家族等——零改动零别名；
- `cn`、`useTheme` 家族（`ACCENT_PRESETS`、`initTheme`、`useColorMode`…）、
  `nervMotion`、`toast`（vue-sonner 透传）、`Toaster`；
- `@nerv-iip/app-shell`、`@nerv-iip/business-core` 等非 UI 包的组件不在本 ADR 范围。

## 附录 B：`--sb-*` → `--nv-scr-*` token 全表映射（30 项）

规则：机械替换前缀 `--sb-` → `--nv-scr-`；右值同步做引用链收敛的两项已标注。

| 旧名                 | 新名                     | 备注                                                      |
| -------------------- | ------------------------ | --------------------------------------------------------- |
| --sb-bg              | --nv-scr-bg              |                                                           |
| --sb-bg-accent       | --nv-scr-bg-accent       |                                                           |
| --sb-panel-a         | --nv-scr-panel-a         |                                                           |
| --sb-panel-b         | --nv-scr-panel-b         |                                                           |
| --sb-line            | --nv-scr-line            |                                                           |
| --sb-line-2          | --nv-scr-line-2          |                                                           |
| --sb-divider         | --nv-scr-divider         |                                                           |
| --sb-cyan            | --nv-scr-cyan            |                                                           |
| --sb-cyan-dim        | --nv-scr-cyan-dim        |                                                           |
| --sb-accent-from     | --nv-scr-accent-from     |                                                           |
| --sb-accent-to       | --nv-scr-accent-to       |                                                           |
| --sb-accent-fill     | --nv-scr-accent-fill     |                                                           |
| --sb-accent-edge     | --nv-scr-accent-edge     |                                                           |
| --sb-indigo          | --nv-scr-indigo          |                                                           |
| --sb-green           | --nv-scr-green           |                                                           |
| --sb-amber           | --nv-scr-amber           |                                                           |
| --sb-red             | --nv-scr-red             |                                                           |
| --sb-text            | --nv-scr-text            |                                                           |
| --sb-text-2          | --nv-scr-text-2          |                                                           |
| --sb-muted           | --nv-scr-muted           |                                                           |
| --sb-faint           | --nv-scr-faint           |                                                           |
| --sb-highlight       | --nv-scr-highlight       |                                                           |
| --sb-edge-gradient   | --nv-scr-edge-gradient   |                                                           |
| --sb-value-glow      | --nv-scr-value-glow      |                                                           |
| --sb-edge-glow       | --nv-scr-edge-glow       |                                                           |
| --sb-glow            | --nv-scr-glow            |                                                           |
| --sb-sheen           | --nv-scr-sheen           |                                                           |
| --sb-radius          | --nv-scr-radius          |                                                           |
| --sb-ease            | --nv-scr-ease            | 右值改 `var(--nv-ease-out-quart)`（现为复制的同值字面量） |
| --sb-ease-emphasized | --nv-scr-ease-emphasized | 右值改 `var(--nv-ease-out-expo)`（同上）                  |

类名同步：`.sb-scroll → .nv-scr-scroll`、`.sb-tbl → .nv-scr-tbl`、
`.sb-at-tbl → .nv-scr-at-tbl`，及 screen 组件内部全部 `sb-` 前缀类。
`apps/screen/src/assets/main.css` 中与 `tokens.css` 不一致的第二份 `.sb-scroll` 定义在
S2（screen 应用代码转换）时消除——保留 tokens.css 单一定义，应用差异若确需保留则以
`@layer app` 内 `.nv-scr-scroll` 覆盖表达。

## 附录 C：PC 自有语义 token nv 化清单（契约层之外的全部 `:root`/`.dark` 自有名）

| 旧名                                                                                                  | 新名                                                                                                                 |
| ----------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| --brand / --brand-foreground / --brand-strong                                                         | --nv-brand / --nv-brand-foreground / --nv-brand-strong                                                               |
| --success / --success-foreground / --success-strong                                                   | --nv-success / --nv-success-foreground / --nv-success-strong                                                         |
| --warning / --warning-foreground / --warning-strong                                                   | --nv-warning / --nv-warning-foreground / --nv-warning-strong                                                         |
| --destructive-strong（--destructive 本体是契约名，不动）                                              | --nv-destructive-strong                                                                                              |
| --ease-out-quart / --ease-out-expo / --ease-in-out-quart / --ease-fast-invoke / --ease-point-to-point | --nv-ease-out-quart / --nv-ease-out-expo / --nv-ease-in-out-quart / --nv-ease-fast-invoke / --nv-ease-point-to-point |
| --duration-fast / --duration-base / --duration-slow / --duration-fast-invoke / --duration-fade        | --nv-duration-fast / --nv-duration-base / --nv-duration-slow / --nv-duration-fast-invoke / --nv-duration-fade        |
| --shadow-xs / --shadow-sm / --shadow-md / --shadow-lg                                                 | --nv-shadow-xs / --nv-shadow-sm / --nv-shadow-md / --nv-shadow-lg                                                    |
| --shadow-glow-brand                                                                                   | --nv-shadow-glow-brand                                                                                               |

Tailwind 桥（`@theme inline`）左侧名保持（`--color-success`、`--color-brand-strong`、
`--shadow-sm`、`--ease-out-quart` 等 utility 契约不变，业务模板 `text-success`/
`bg-brand`/`shadow-sm`/`ease-out-quart` 零影响），右值切到新名。`.ds-overlay-content`
的局部变量（`--ds-overlay-*`）随类名改为 `.nv-overlay-content` / `--nv-overlay-*`。
契约层冻结名单（永不加前缀）：`--background`、`--foreground`、`--card(-foreground)`、
`--popover(-foreground)`、`--primary(-foreground)`、`--secondary(-foreground)`、
`--muted(-foreground)`、`--accent(-foreground)`、`--destructive(-foreground)`、
`--border`、`--input`、`--ring`、`--chart-1..5`、`--sidebar-*`、`--radius`、
`--font-sans`、`--font-heading`。
