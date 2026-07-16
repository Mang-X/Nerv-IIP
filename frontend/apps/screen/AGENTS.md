# AGENTS.md — screen（大屏 / 挂墙）

> 根 `AGENTS.md` 仍然适用；本文件只补充大屏子树的差异。
> screen = 中央控制室 / 车间挂墙大屏：数米外观看、暗光环境、24h 常亮 LED
> 拼接屏。设计规格：`docs/superpowers/specs/2026-06-26-screen-foundation-design.md`、
> `2026-07-06-screen-m1-core-dashboards-design.md`。

## Commands

```powershell
pnpm -C frontend --filter @nerv-iip/screen typecheck
pnpm -C frontend --filter @nerv-iip/screen test
pnpm -C frontend --filter @nerv-iip/screen build
pnpm -C frontend --filter @nerv-iip/screen dev   # 端口 5128
```

## 设计硬门禁（新建/改组件前必读）

- **先读 `frontend/packages/ui/src/components/screen/product.md`** —— 本层的
  产品定位、动效契约、颜色语义、Do/Don't 全在那里，改 screen 层组件前必读。
- 令牌只用 `--nv-scr-*`（`components/screen/tokens.css`），无亮色模式；克制
  发光（辉光只给活数据）；动效只用 `--nv-scr-ease` / `--nv-scr-ease-emphasized`，
  press 收缩不回弹，每个动效都有 `prefers-reduced-motion` 降级。
- 组件真实 props 以 design-system 文档站「大屏」分区
  （`frontend/apps/design-system/docs/components/screen/`）+ 组件源码为准，
  不凭记忆。
- shadcn / 既有原版零改动；产品需要新件就复制重建 / 新建。

## 数据 seam（三段式）

- `src/data/` = `contracts` / `mock` / `real` / `fetchers`；页面只消费类型化
  fetcher 接口，不直接碰 api-client。
- 切真实数据**只改 `data/fetchers/*` 与 `data/real/*`**；mock 形状必须对齐
  `@nerv-iip/api-client` 的 `types.gen.ts`，禁止 `as` / 内联标注绕过契约。
- real 模式由登录 principal 注入 org/env（`main.ts`）；mock 模式不触碰鉴权与
  api-client 配置。
- **诚实标注，不做假绿**：无真实端点的字段显式占位标注（badge/tooltip），
  数据新鲜度用 `IsSourceFresh` 驱动"失联灰条"（`data/contracts/equipment.ts`），
  断线绝不能看起来像"运行正常"。

## Hard Rules

1. 真实感数据看效果：产线名、`WO-` 工单号、OEE/节拍/达成率 —— 不用占位
   假文案（product.md Do/Don't）。
2. 不堆叠 `backdrop-filter` / 大面积高斯模糊 —— 大屏渲染环境吃不消；用
   半透明渐变 + 发丝边模拟材质。
3. 浮层（下拉/弹层）必须 Teleport 到 `<body>` 并 `position:fixed` 锚定，
   否则被 `ScreenPanel` 的 `overflow:hidden` 裁掉。
