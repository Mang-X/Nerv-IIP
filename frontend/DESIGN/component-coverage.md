---
# Component Coverage & Gaps — 组件覆盖度与缺口
# 四场景矩阵视图（PC / 移动 / 一体机 / 大屏）+ 对照 Arco Design (PC) 与 TDesign Mobile。
# 配套规范见 motion-interaction.md；本表回答"哪个表面有、还缺什么、先补什么"。
---

## 四场景覆盖矩阵

同一 UX 概念在四个表面各实现一次（表面决定目录 / token 命名空间 / 触控尺寸；跨表面拆两件，
绝不"一件双态"，ADR 0020 §1.2）。`—` = 该表面无对应件（真实缺口，非占位）；括注为复用说明。
文档站每个组件页头部的**场景可用性徽章**由此矩阵的跨场景族驱动（`docs/.vitepress/theme/scene-map.ts`）：
**单概念行**（按钮 / 输入框 / 选择器 / 搜索 / 徽标 / 开关…）是同一组件的各表面实现，徽章在这些表面间互链；
**斜杠分组行**（时间线 / 步骤、加载 / 骨架、仪表 / 翻牌…）把同品类下的**不同**组件并列，各自表面专属、不互链。

图例：✅ 已建 · `—` 缺口 · （复用 PC）= 沿用桌面件放大。

### 操作 / 表单

| UX 概念     | 桌面 PC (`--nv-*`, 36–40px)   | 移动 PDA (`--nv-m-*`, 40–48px)            | 一体机 touch (`--nv-t-*`, 56–72px) | 大屏 screen (`--nv-scr-*`) |
| ----------- | ----------------------------- | ----------------------------------------- | ---------------------------------- | -------------------------- |
| 按钮        | `NvButton`                    | `NvMobileButton`                          | `NvTouchButton`                    | `NvScreenButton`           |
| 分段切换    | `NvTabs`（分段样式）          | `NvMobileTabs`（分段样式）                | `NvTouchSegmented`                 | `NvScreenSegmented`        |
| 数量步进    | InputNumber（P2 待建）        | `NvStepper`                               | `NvQtyStepper`                     | `—`                        |
| 输入框      | `NvInput`                     | `NvMobileInput`                           | （复用 PC）                        | `NvScreenInput`            |
| 选择器      | `NvSelect`                    | `NvPicker`（滚轮）                        | `—`                                | `NvScreenSelect`           |
| 复选 / 单选 | `NvCheckbox` · `NvRadioGroup` | `NvMobileCheckbox` · `NvMobileRadioGroup` | `—`                                | `—`                        |
| 开关        | `NvSwitch`                    | `NvMobileSwitch`                          | `—`                                | `NvScreenSwitch`           |
| 滑块        | `NvSlider`                    | `NvMobileSlider`                          | `—`                                | `—`                        |
| 日期        | `NvDatePicker`                | `NvMobileDatePicker`                      | `—`                                | `—`                        |
| 搜索        | `NvCommand`（⌘K）             | `NvSearchBar`                             | `—`                                | `NvScreenSearch`           |

### 数据展示

| UX 概念       | 桌面 PC                                                 | 移动 PDA                        | 一体机 touch                       | 大屏 screen                                                                                    |
| ------------- | ------------------------------------------------------- | ------------------------------- | ---------------------------------- | ---------------------------------------------------------------------------------------------- |
| 表格          | `NvDataTable`                                           | `—`（用 Cell 列表）             | （复用 PC）                        | `NvScreenTable`                                                                                |
| 分页          | （`NvDataTable` 内置）                                  | —（移动用无限滚动，见长列表）   | `—`                                | `NvScreenPagination`                                                                           |
| 标签页        | `NvTabs`                                                | `NvMobileTabs`                  | `NvTouchSegmented`（触控用分段件） | `NvScreenTabs`                                                                                 |
| 描述列表      | `NvDescriptions`                                        | （用 `NvCell`）                 | `—`                                | `—`                                                                                            |
| 时间线 / 步骤 | `NvTimeline`                                            | `NvMobileSteps`                 | `—`                                | `—`                                                                                            |
| 卡片 / 面板   | `NvCard`                                                | （用 `NvCell`/`NvListRow`）     | （复用 `NvCard`）                  | `NvScreenPanel` · `NvBorderPanel`                                                              |
| 指标 KPI      | `NvMetricCard`                                          | `—`                             | `NvStatTile`                       | `NvKpiBar` · `NvOeeHero`                                                                       |
| 图表          | `NvBarChart`/`NvLineChart`/`NvAreaChart`/`NvDonutChart` | `—`                             | （复用 PC）                        | `NvScreenBarChart` · `NvScreenDonut` · `NvScreenPareto` · `NvSparkline` · `NvScreenTrendChart` |
| 甘特          | `—`                                                     | `—`                             | `—`                                | `NvTaktGantt`                                                                                  |
| 状态指示      | `NvStatusDot` · `NvStatusBadge`                         | （用 `NvMobileTag`）            | （复用 PC）                        | `NvScreenStatusLight` · `NvScreenStatusCard` · `NvScreenStatusTag`                             |
| 徽标 / 标签   | `NvBadge`                                               | `NvMobileBadge` · `NvMobileTag` | `—`                                | `NvScreenStatusTag`                                                                            |
| 头像          | （原版 Avatar）                                         | `NvMobileAvatar`                | `—`                                | `—`                                                                                            |

### 导航 / 外壳

| UX 概念         | 桌面 PC                          | 移动 PDA                   | 一体机 touch   | 大屏 screen                            |
| --------------- | -------------------------------- | -------------------------- | -------------- | -------------------------------------- |
| 页头            | `NvAppHeader`                    | `NvNavBar`                 | `NvStationBar` | `NvScreenHeader` · `NvTitleBar`        |
| 应用外壳        | `NvAppShellInset` · `NvSidebar*` | `NvAppShellMobile`         | （复用 PC）    | `NvScreenScaler`（舞台缩放）           |
| 面包屑          | `NvBreadcrumb`                   | `—`                        | `—`            | `—`                                    |
| 底部标签栏      | `—`                              | `NvTabBar`                 | `—`            | `—`                                    |
| 导航菜单        | `NvNavigationMenu`               | —（移动导航用 `NvTabBar`） | `—`            | `—`                                    |
| 滚动区 / 滚动板 | `—`                              | `—`                        | `—`            | `NvScreenScrollArea` · `NvScrollBoard` |

### 反馈 / 覆盖层

| UX 概念         | 桌面 PC                                              | 移动 PDA                                | 一体机 touch | 大屏 screen                                      |
| --------------- | ---------------------------------------------------- | --------------------------------------- | ------------ | ------------------------------------------------ |
| 对话框          | `NvDialog`                                           | `NvMobileDialog`                        | `—`          | `—`                                              |
| 警示对话框      | `NvAlertDialog`                                      | （`NvMobileDialog` danger）             | `—`          | `—`                                              |
| 抽屉 / 面板     | `NvSheet`                                            | `NvBottomSheet` · `NvActionSheet`       | `—`          | `—`                                              |
| 下拉菜单        | `NvDropdownMenu`                                     | `NvMobileDropdownMenu`                  | `—`          | `—`                                              |
| 气泡确认        | `NvPopconfirm`                                       | `—`                                     | `—`          | `—`                                              |
| 文字提示        | `NvTooltip`                                          | `—`                                     | `—`          | `—`                                              |
| 轻提示 / 通知   | `messagePro` · `notificationPro`（`NvNotifierHost`） | `NvMobileToast` · `NvNoticeBar`         | （复用 PC）  | `—`                                              |
| 告警表          | `—`                                                  | `—`                                     | `—`          | `NvAlarmTable`                                   |
| 加载 / 骨架     | `NvLoader`                                           | `NvMobileSkeleton` · `NvMobileProgress` | `—`          | `—`                                              |
| 空态 / 结果     | （原版 Empty 复用）                                  | `NvMobileEmpty` · `NvMobileResult`      | `—`          | `—`                                              |
| 仪表 / 翻牌     | `—`                                                  | `—`                                     | `—`          | `NvRingGauge` · `NvCapsuleBar` · `NvDigitalFlop` |
| 装饰边框 / 分割 | `—`                                                  | `NvMobileDivider`                       | `—`          | `NvTechFrame` · `NvGlowDivider`                  |

### 手势 / 移动专属

| UX 概念 | 组件                                                                                                |
| ------- | --------------------------------------------------------------------------------------------------- |
| 手势    | `NvSwipeCell`（侧滑） · `NvPullRefresh`（下拉刷新） · `NvSwiper`（轮播）                            |
| 长列表  | `NvVirtualList` · `NvInfiniteList`                                                                  |
| 快捷    | `NvMobileGrid`（宫格） · `NvFab`（悬浮按钮） · `NvScanBar`（扫码） · `NvNumberKeyboard`（数字键盘） |

> 缺口不是遗漏，是**尚无该场景需求**：例如一体机大量复用 PC 件而不重复造轮子；大屏几乎不做表单，
> 故无 Checkbox/Slider/DatePicker 的大屏版。补任何新件先走 [ADR 0020 §1.2 命名判定](https://github.com/Mang-X/Nerv-IIP/blob/main/docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md) 定场景归属与名字。

## 经验总结（这轮重构沉淀）

1. **三表面分层，尺寸/交互各自适配，勿混用**：桌面 `@nerv-iip/ui` 的 Pro 层（指针、紧凑 36–40px）/ 手机 `@nerv-iip/ui-mobile`（原生触控 40–48px）/ 平板看板 `ui/components/touch`（大触控 56–72px）。把看板大件用到手机会"肿大"。
2. **原版 shadcn 零改动**：所有定制走"复制重建 + 令牌"，不改 `components/ui/*`。新件命名避免与 ui 导出撞名。
3. **令牌单一来源**：颜色只用语义令牌；填充色（`--brand`…）与**文字专用色（`--*-strong`）分离**，后者保证小号色调文字 ≥4.5:1（impeccable 对比度硬规则）。
4. **动效成体系**：缓动令牌（`--ease-out-*`）、传达状态而非装饰、所有位移类动画带 `prefers-reduced-motion` 降级。手势件（SwipeCell/PullRefresh/Picker）pointer 驱动、仅水平手势接管。
5. **验证不只看测试**：vitest 不编译 CSS——`text-success→-strong` 漏改测试断言、CSS 注释含 `*/` 致整页白屏，都是"测试过但 dev/build 炸"。每轮跑 typecheck + 三包 test + **浏览器实测**（含亮暗/375）+ fmt。
6. **善用 impeccable**：跑它的 `audit`（尤其对比度脚本）能抓出凭感觉漏掉的真问题。

## PC 覆盖度（对照 Arco Design）

✅ 已建（Pro/原版）：Button · Badge/Tag · Card/Statistic(MetricCard) · Input · Select · Checkbox · Radio · Switch · TimePicker · **DatePicker(新)** · Tabs · Tooltip · Dialog(Modal) · Drawer(Sheet) · Popover · Dropdown · Breadcrumb · Pagination · **DataTablePro(新)** · **Descriptions(新)** · **Timeline(新)** · **Popconfirm(新)** · Progress · Skeleton · Spin(Loader) · Avatar · Alert · Message · Notification · Command(⌘K) · Table(基础) · Calendar · Charts(面积/折线/柱/环) · Empty(mobile有,PC可复用)

⚠️ 部分 / ❌ 缺（按优先级）：

- ~~**P1 DataTable Pro**~~ ✅ 已建 `pro/data-table/`，**三件独立可组合**：`DataTableToolbarPro`（标题+实时计数 · 快捷筛选分段 · 搜索 · 字段筛选 text/enum+chips+pip · 密度 · 刷新 · 更多菜单导出/打印 · 操作槽）、`DataTablePaginationPro`（可点击页码 · 首末/上下页 · **多页省略号，… 悬停切双 V 跳 5 页** · 每页 · 跳页）、`DataTablePro`（组合前两者 + 行选择/上下文批量条 + 排序 + 空态/加载骨架 + sticky 表头 + `tabs`/`tabKey` 快捷标签实时计数）。客户端管线，旧 `blocks/DataTable` 保留向后兼容。坑：reka Checkbox 是 `<button>`（labelable），套 `<label>` 会双触发抵消——选项行改纯 `<button role=checkbox>` + 装饰性 Checkbox；多语句内联 handler 会被 fmt 拆行致 Vue 解析失败，须收敛为命名方法。
- ~~**P1 Descriptions 描述列表**~~ ✅ 已建 `pro/descriptions/`：键值详情，开放网格 + 带边框记录两态、列数响应式（窄屏单列）、item 跨列（带边框模式末项自动补满整行）、横/纵 label 布局、`#<key>` 值插槽。
- ~~**P1 Timeline 时间线**~~ ✅ 已建 `pro/timeline/`：工序流转日志，不透明 tone 节点（线不穿节点）+ 连接线 + 标题/时间 label/描述、可选脉冲"进行中"尾节点、自定义 icon/空心点、`#<key>` 内容插槽。
- ~~**P2 Popconfirm 气泡确认**~~ ✅ 已建 `pro/popconfirm/`：行内危险操作二次确认，Popover 锚定触发槽 + 警示图标 + 取消/确定（brand/danger 调）、`v-model:open` 可控 + async `loading`。
- **P2 Form 表单容器**：label/校验/错误信息编排（现有 Field 原版，未做 Pro 编排）。
- **P2 InputNumber / Slider / Rate**：数值/范围/评分录入。
- **P2 Result 结果页（PC）**：成功/失败/空大状态（mobile 已有，PC 未做）。
- **P3 Cascader / TreeSelect / Tree / Transfer**：层级选择（本域用得少，后置）。
- **P3 Carousel / Image 预览 / Affix / BackTop**。

## 移动覆盖度（对照 TDesign Mobile）

✅ 已建（ui-mobile）：Button · Tag/Badge · Cell/CellGroup · Switch · Checkbox · Radio · Input · Stepper · Picker · **DatePicker(新)** · NavBar · TabBar · Tabs · SearchBar · NoticeBar · Steps · Collapse · SwipeCell · PullRefresh · InfiniteList(加载更多) · VirtualList(虚拟滚动) · BottomSheet(Popup) · ActionSheet · **MobileDialog(居中确认,新)** · **MobileGrid(宫格,新)** · **Fab(悬浮按钮,新)** · **MobileToast(居中提示,新)** · Result · Empty · ScanBar · AppShellMobile

⚠️ 部分 / ❌ 缺（按优先级）：

- ~~**P1 Dialog 居中确认弹窗**~~ ✅ 已建 `ui-mobile/dialog/MobileDialog`：iOS 风居中确认（确认/取消），紧凑居中卡片 + hairline 分隔按钮行、brand/danger 调、默认模态（遮罩点击不关，可 `closeOnOverlay`）、zoom+fade 入场。
- ~~**P2 Grid 宫格**~~ ✅ 已建 `ui-mobile/grid/MobileGrid`：图标+文字宫格，N 列、可选 hairline 边框/方形、角标(数字/点)、emit select。
- ~~**P2 Toast 居中提示**~~ ✅ 已建 `ui-mobile/toast/MobileToast`：居中暗色 HUD（区别于顶部 messagePro），text/loading/success/error 图标态、自动消失(loading 持续)、可选遮罩、`v-model:show`。
- ~~**P2 Fab 悬浮按钮**~~ ✅ 已建 `ui-mobile/fab/Fab`：锚定容器右下角(absolute，留在设备框内)，单动作或速拨(actions)、扩展 pill 文字、brand/default 调、位置变体。
- **P2 Skeleton / Progress（mobile 专版）**：可先复用 ui 的，必要时做移动尺度。
- **P3 Swiper 轮播 · IndexBar 索引 · TextArea · Upload · CountDown · Sticky**。

## 建议补全顺序

1. ~~**DataTable Pro**（PC，P1）~~ ✅ 完成（工具栏/字段筛选/列设置/密度/行选择/可点击页码分页）。
2. ~~**Descriptions + Timeline**（PC，P1）~~ ✅ 完成（键值详情 + 工序流转）。
3. ~~**移动 Dialog（居中确认）+ Popconfirm（PC）**（P1/P2）~~ ✅ 完成（确认交互闭环）。
4. ~~**Grid / Fab / Toast（移动）**（P2）~~ ✅ 完成（移动信息架构与快捷操作）。
5. 其余 P2/P3 按业务需要再补（PC Form/InputNumber/Slider/Rate/Result · 移动 Skeleton/Swiper/Upload 等）。

> 补任何新件务必过 `motion-interaction.md` 自检清单（缓动令牌 / 按压+聚焦态 / reduced-motion / 触摸尺寸 / 对比度 / 令牌）。
