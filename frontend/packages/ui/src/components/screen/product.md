# 大屏 / 控制室组件层 · 产品定位（product.md）

> `@nerv-iip/ui` 的 `screen/` 层 —— 中央控制室、车间指挥大屏的组件库。独立深色「工业蓝」令牌（`--nv-scr-*`），只遵循统一设计哲学，**不复用** PC/移动的浅色令牌。AI 编码与人工开发在改动本层前先读本文件。

## 一句话定位

给数米外观看、暗光车间、常亮高亮 LED 拼接屏用的 MES 看板组件 —— 远距可读、克制发光、工业冷静。

## 受众与场景

- **观看者**:车间管理者、值班长，数米外扫视，不近距离阅读。
- **操作者**:控制室操作台少量交互（按钮 / 开关 / 筛选 / 搜索），鼠标或触控。
- **环境**:暗光车间、24 小时常亮，避免大面积亮色刺眼。
- **硬件**:大尺寸 LED / 拼接屏，高亮度、可能有像素间距。
- **与 PC/移动的区别**:观看距离更远 → 字号更大、辉光点缀关键数据;环境更暗 → 固定深色不跟随亮暗。故独立成层。

## 设计原则

1. **克制发光** —— cyan 辉光只给「活数据」（实时值、运行态）;标题、坐标轴、静态文字不发光。
2. **远距可读** —— 关键数字大字号 + 克制 `text-shadow` 辉光;次要信息降噪（`--nv-scr-muted` / `--nv-scr-faint`）。
3. **独立深色令牌** —— 只用 `--nv-scr-*`，不混入共享 `--*`;无亮色模式。
4. **动效统一、减速无回弹** —— 见下方动效契约，手感与 PC/移动完全一致。
5. **数据驱动** —— 组件零 props 可渲染示例，接真实数据只传值;严禁写死业务文案当占位。

## 动效契约（与全系统统一）

大屏视觉独立，但**动效语言必须和 PC/移动同源** —— 同一种「高级减速、绝不回弹」的手感（见 `theme.css`:_No bounce/elastic; premium motion decelerates_）。

| 用途                                  | 令牌                       | 值                              | 对应系统令牌          |
| ------------------------------------- | -------------------------- | ------------------------------- | --------------------- |
| 日常过渡（颜色 / 边框 / 位移 / 勾选） | `--nv-scr-ease`            | `cubic-bezier(0.25, 1, 0.5, 1)` | `--nv-ease-out-quart` |
| 强调（大位移 / 数据增长 / 进出）      | `--nv-scr-ease-emphasized` | `cubic-bezier(0.16, 1, 0.3, 1)` | `--nv-ease-out-expo`  |

**时长** —— 沿用 PC 层惯例，行内书写;**不引入 screen 专属时长令牌**，以免和 pro（同样行内）产生新的不一致:

- press ≈ `0.12s`;控件过渡 `0.15–0.22s`;滑动 / 展开 `0.26–0.28s`;数据增长 `0.6s`。

**press 反馈** —— 收缩、绝不膨胀、绝不位移、绝不回弹:

- 按钮 `:active` → `scale(0.985)`（纯缩放;大屏上**不做**垂直位移,`translateY` 在暗底看着像跳动）
- 开关手柄 `:active` → `scale(0.86)`（同 `SwitchPro`，**收缩**而非膨胀）
- 勾选类如有 → `scale(0.88)`（同 `CheckboxPro` / `RadioGroupProItem`）

**滑动指示** —— 分段（`ScreenSegmented`）与标签（`ScreenTabs`）用滑动 thumb / 下划线（`--nv-scr-ease-emphasized`），与 PC 的滑动语言一致;thumb 从激活项**实测宽度**定位,**不做**硬背景切换。

**浮层** —— 下拉 / 弹层（`ScreenSelect` 等）必须 `Teleport` 到 `<body>` 并 `position:fixed` 锚定触发器,否则会被 `ScreenPanel` 的 `overflow:hidden` 裁掉。

**循环类** —— 状态灯呼吸、流光分割、水位波浪用 `ease-in-out` 或 SVG SMIL;`prefers-reduced-motion` 一律停。

**铁律**

- ❌ 不用 bounce / elastic / spring / overshoot —— 任何回弹。
- ❌ 不自创第四条缓动曲线 —— 只 `--nv-scr-ease` 与 `--nv-scr-ease-emphasized`。
- ✅ 每个会动的元素都要有 `@media (prefers-reduced-motion: reduce)` 降级。

## 颜色语义

| 令牌                                         | 值        | 用途                     |
| -------------------------------------------- | --------- | ------------------------ |
| `--nv-scr-cyan`                              | `#00e5ff` | 活数据、主强调、运行辉光 |
| `--nv-scr-indigo`                            | `#a78bfa` | 计划值 / 次要序列        |
| `--nv-scr-green`                             | `#00e676` | 运行 / 达成 / 正常       |
| `--nv-scr-amber`                             | `#ffd600` | 待机 / 预警              |
| `--nv-scr-red`                               | `#ff1744` | 报警 / 异常 / 越限       |
| `--nv-scr-text` / `-2` / `-muted` / `-faint` | 白 → 灰阶 | 主 / 次 / 弱 / 极弱文字  |

底色:`--nv-scr-bg #080c16`、`--nv-scr-panel-a/b` 面板渐变、`--nv-scr-line/-2` 发丝边、`--nv-scr-divider` 分隔。

## 组件清单

| 分类        | 组件                                                                                                                                 |
| ----------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| 容器 / 外壳 | `ScreenPanel` · `BorderPanel` · `TechFrame` · `TitleBar` · `ScreenHeader` · `GlowDivider`                                            |
| 指标 / 图表 | `OeeHero` · `RingGauge` · `WaterLevel` · `CapsuleBar` · `DigitalFlop` · `Sparkline` · `TrendChart` · `TaktGantt`                     |
| 数据 / 状态 | `StatusCard` · `KpiBar` · `AlarmTable` · `StatusLight` · `StatusTag`                                                                 |
| 控件        | `ScreenButton` · `ScreenInput` · `ScreenSelect` · `ScreenSearch` · `ScreenTable` · `ScreenTabs` · `ScreenSegmented` · `ScreenSwitch` |

**容器策略**:裸内容组件（`OeeHero` / `RingGauge` / `WaterLevel` / `CapsuleBar` / `DigitalFlop` / `Sparkline` / `KpiBar`）放进 `ScreenPanel`;自带容器组件（`TrendChart` / `TaktGantt` / `AlarmTable` / `StatusCard`）直接用。

文档与可交互预览见设计系统站「大屏」分区（`/components/screen/`）。

## Do / Don't（Don't 更重要）

**Don't**

- ❌ 别在 screen 用共享 `--*` 令牌 —— 一律 `--nv-scr-*`。
- ❌ 别给静态文字 / 标题 / 坐标轴加辉光 —— 辉光只属于活数据。
- ❌ 别加亮色模式 —— 本层固定深色。
- ❌ 别用回弹动效，别自创缓动曲线。
- ❌ 别堆叠 `backdrop-filter` / 大面积高斯模糊 —— 大屏渲染环境吃不消，用半透明渐变 + 发丝边模拟材质。
- ❌ 别用大数字模板 / 侧边色条 / 渐变文字 —— 与整体设计哲学冲突。

**Do**

- ✅ 数据增长 / 大位移用 `--nv-scr-ease-emphasized`，其余用 `--nv-scr-ease`。
- ✅ press 反馈对齐 pro 的 scale 体系（收缩、无回弹）。
- ✅ 真实工厂数据看效果（产线名、`WO-` 工单、OEE / 节拍 / 达成率）。
