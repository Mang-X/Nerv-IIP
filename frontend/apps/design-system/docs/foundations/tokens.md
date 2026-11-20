# 设计令牌

页面和组件使用语义令牌表达表面、文字、边框、状态与交互。文档站说明用途，实际值由 CSS、主题预设和组件源码提供。

## 定义位置

| 归属 | 定义与消费 |
| --- | --- |
| 共享契约 | [theme.css](https://github.com/Mang-X/Nerv-IIP/blob/main/frontend/packages/ui/src/styles/theme.css) 定义 shadcn 契约（如 `--background`、`--radius`）和项目共享扩展（如 `--nv-brand`、`--nv-ease-out-quart`）。应用样式入口负责装配，Tailwind `@theme inline` 把语义映射为工具类。 |
| 动态主题 | [useTheme.ts](https://github.com/Mang-X/Nerv-IIP/blob/main/frontend/packages/ui/src/composables/useTheme.ts) 定义主题预设，和[色彩预览](/foundations/color)使用同一份数据。 |
| 移动 PDA | 沿用共享语义，[mobile.css](https://github.com/Mang-X/Nerv-IIP/blob/main/frontend/packages/ui-mobile/src/styles/mobile.css) 提供安全区、触控与行高工具类，组件变体定义实际控件尺寸。移动独有令牌使用 `--nv-m-*`。 |
| 车间一体机 | 沿用共享语义，触控组件独立定义尺寸。工位独有令牌使用 `--nv-t-*`。 |
| 挂墙大屏 | [tokens.css](https://github.com/Mang-X/Nerv-IIP/blob/main/frontend/packages/ui/src/components/screen/tokens.css) 定义 `--nv-scr-*`，涵盖大屏专用表面、色彩、边线、文字、图表、圆角和动效。 |

场景组件使用所属场景的语义。跨场景共用同一语义和取值时，由场景 token 引用共享定义，例如 `--nv-scr-ease` 引用 `--nv-ease-out-quart`。命名与层叠规则见 [ADR 0020](https://github.com/Mang-X/Nerv-IIP/blob/main/docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md)。

## 大屏图表配色

报警系列使用 `--nv-scr-red`。车间趋势中的关注系列使用 `--nv-scr-chart-attention`。用于区分业务系列的局部配色由对应页面维护。计划线默认使用 `--nv-scr-indigo`，用于管控限等不同语义时，通过[趋势图的 `planColor`](/components/screen/trend-chart)单独指定。

## 语义颜色（实时取色，跟随当前主题）

<div class="grid grid-cols-2 sm:grid-cols-4 gap-3 my-6">
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--background)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--background</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--card)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--card</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--muted)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--muted</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--nv-brand)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--nv-brand</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--nv-success)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--nv-success</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--nv-warning)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--nv-warning</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--destructive)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--destructive</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--foreground)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--foreground</div>
  </div>
</div>

> 切换右上角的外观（亮/暗）与动态色，上面的色块会实时跟随——证明组件只依赖语义令牌。

---

## 尺寸与密度

| 场景 | 选择依据 |
| --- | --- |
| 桌面 PC | 优先保证信息密度和键盘可达性。按 [桌面排版与布局](/foundations/desktop-layout) 和 [NvButton](/components/desktop/button) 选择尺寸。 |
| 移动 PDA | 依据单手操作、触控目标和安全区选尺寸。见 [NvMobileButton](/components/mobile/button) 和 [AppShellMobile](/components/mobile/app-shell-mobile)。 |
| 车间一体机 | 面向站立操作及戴手套使用，优先选 [NvTouchButton](/components/touch/touch-button) 等大触控组件。 |
| 挂墙大屏 | 依据现场视距、画布缩放和指标层级保证远读。见 [大屏概览](/components/screen/) 和 [ScreenScaler](/components/screen/screen-scaler)。 |

## 新增令牌

1. 先查共享主题、场景表和组件变体。根据语义决定复用已有令牌、由场景引用共享语义，或新增场景专用令牌。
2. 在所属样式源码定义取值，按场景补亮暗模式。Tailwind 映射引用已有语义。
3. 将只服务于单个组件或画布的布局、图形和设备尺寸留在所属实现。新增共享值时更新本站的角色说明，并验证受影响的组件和现有契约。
