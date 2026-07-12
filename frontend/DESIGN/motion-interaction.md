---
# Motion & Interaction — 组件打磨与一致性规范
# 适用于 @nerv-iip/ui (Pro + touch) 与 @nerv-iip/ui-mobile。
# 目标：高质感、高一致性。任何新组件 / 改动都应过一遍本清单。
---

## 1. 缓动与时长令牌（唯一来源，勿手写 cubic-bezier）

令牌定义在 `packages/ui/src/styles/theme.css`（`:root`），通过 `@theme inline` 暴露为
Tailwind `ease-*` 工具类，也可在 scoped CSS 里 `var(--…)` 直接引用：

| 令牌                                            | 值                                                                          | 用途                                   |
| ----------------------------------------------- | --------------------------------------------------------------------------- | -------------------------------------- |
| `--ease-out-quart`                              | `cubic-bezier(0.25,1,0.5,1)`                                                | 微交互：hover / focus / 颜色 / 小位移  |
| `--ease-out-expo`                               | `cubic-bezier(0.16,1,0.3,1)`                                                | 入场揭示、覆盖层、滑动指示器、进度填充 |
| `--ease-in-out-quart`                           | `cubic-bezier(0.76,0,0.24,1)`                                               | 双向状态切换 / 往返位移                |
| `--ease-out-back`（回弹，含轻微 overshoot 1.4） | 仅用于"出现"型强调：勾选/图标 pop、激活图标放大。**禁用于** 列表/布局位移   |
| 时长                                            | `--duration-fast 150ms` / `--duration-base 220ms` / `--duration-slow 320ms` | 微交互→入场。整体落在 150–320ms        |

scoped CSS 里务必带 fallback：`var(--ease-out-expo, cubic-bezier(0.16,1,0.3,1))`（组件库可能被无令牌环境消费）。

## 2. 每个交互组件必须具备的状态

参考 impeccable「product」寄存器：default / hover / focus-visible / **active(按压)** / disabled / loading / selected。
不要只做一半。

- **按压反馈（移动/触摸必做）**：实心按钮 `active:scale(0.97)` 或 `active:opacity-.6`；行/单元格 `active:bg-accent`（即时，无 transition 延迟）。
- **聚焦**：`focus-visible:ring-[3px] ring-brand/30`（表单类）或 `ring-ring/50`。不要用 `:focus`（鼠标点击也会触发）。
- **触摸卫生**：可点触摸元素加 `-webkit-tap-highlight-color: transparent` + `touch-action: manipulation`；可横滑区用 `touch-action: pan-y` 并在 JS 里只在水平手势时 `preventDefault`，保留竖向滚动。

## 3. 动效原则

- **动效传达状态，而非装饰**：状态变化 / 反馈 / 加载 / 揭示，仅此。无理由的循环动画一律不要。
- **reduced-motion 全降级**：任何 `transform` / `@keyframes` / 大位移动画，必须有
  `@media (prefers-reduced-motion: reduce){ … transition:none / animation:none / transform:none }`。
  颜色/阴影过渡、功能性加载 spinner 可豁免。tw-animate-css 的 `animate-in/out` 已自带 reduced-motion 守卫。
- **入场**：`opacity 0→1` + `scale(.4–.95)` 或 `translate`，用 `--ease-out-back`(pop) 或 `--ease-out-expo`(滑入)。
- **离场**：更快（~120–150ms），可仅 fade。

## 4. 触摸尺寸（按表面区分，勿混用）

| 表面                                 | 主按钮高                     | 行高                              | 说明                                                        |
| ------------------------------------ | ---------------------------- | --------------------------------- | ----------------------------------------------------------- |
| 桌面 PC (`components/pc`)           | 36–40px (h-9/h-10)           | 紧凑                              | 指针精度，font-medium，sheen/微缩反馈                       |
| 手机 (`ui-mobile`)                   | 40–48px (MobileButton md/lg) | `min-h-touch 48` / `min-h-row 56` | 原生尺度；开关用 iOS 比例 51×31；输入 44px+15px(防聚焦缩放) |
| 平板/一体机看板 (`components/touch`) | 56–72px (TouchButton lg/xl)  | 大                                | 远距可读、戴手套可点；动作语义色                            |

**不要把看板大尺寸控件（TouchButton/QtyStepper）直接用到手机**——会"肿大"。手机用 `ui-mobile` 紧凑件。

## 5. 视觉细节规约（踩过的坑）

- **节点压线**：步骤条/时间线的圆点必须有**实心底**（`bg-card`/`bg-brand`），否则连接线会从透明圆心穿透显示。连接线用 `track + brand fill`，fill 宽度过渡表达进度。
- **分隔线**：iOS 列表分隔线**左缩进**（`::after { left: 1rem }`）、`:last-child` 隐藏；不要整组都画全宽 border（双线）。
- **形变要动**：搜索栏聚焦出「取消」用 `max-width 0↔Xrem + opacity` 过渡（带动相邻 flex 项平滑收缩），不要 `v-if` 硬切。
- **指示器滑动**：顶部 Tabs 下划线测量活动项位置 + `left/width` transition（`--ease-out-expo`），而非每个 tab 各自显隐。
- **状态切换有动画**：勾选/单选打勾用 scale pop；激活图标轻微 `scale-110 -translate-y-px`；卡片悬停 `translateY(-2px)` + 阴影升级。
- **令牌而非裸值**：颜色走语义令牌（`--brand`/`--success`…），透明度用 `/10 /25` 或 `color-mix`，不写裸 hex / 调色板类名。

## 6. 跨包工程约束

- `ui-mobile` 组件**不要用 cva**（无该依赖）；用 `computed` 映射类名。需要 `useVModel/reactiveOmit` 时确保 `@vueuse/core` 在 deps 里。
- v-model 优先 `defineModel`（Vue 3.5）；`defineModel('open')` 在 script 内赋值用 `.value`。
- 复制重建、**绝不改原版 shadcn-vue 组件**（见 foundation.md）。新件命名避免与 `@nerv-iip/ui` 导出冲突（如 ui-mobile 的 `Badge` ≠ ui 的 `Badge`，勿同文件混入）。
- 验证手势用合成 PointerEvent 时，Vue DOM 更新异步——读 style 前 `setTimeout` 等一拍。

## 7. 提交前自检清单

- [ ] 用了缓动令牌（无手写 bezier，除 fallback）
- [ ] active 按压反馈 + focus-visible 环
- [ ] 所有 transform/keyframes 有 reduced-motion 降级
- [ ] 触摸尺寸符合所在表面
- [ ] 节点不压线 / 分隔线缩进 / 形变有过渡
- [ ] 颜色走语义令牌
- [ ] typecheck + 对应包 test 通过 + fmt
