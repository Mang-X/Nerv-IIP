# SCR-0 工业数据大屏地基 — 设计（回填）

- 日期：2026-06-26
- 状态：已实现可运行（mock 数据），工程收尾中
- 关联：GitHub Epic #562、issue #563；Linear 项目「工业数据大屏 · 生产指挥中心」

> 说明：本 spec 为事后回填。原计划走 brainstorming → spec → writing-plans，但应用户要求先快速搭建并启动看效果，故先实现、后补本文档，记录已落地的设计与决策。

## 背景

面向公共展示 / 指挥中心的数据大屏（挂墙、投影、自动轮播），是 `@nerv-iip/ui` 的 `screen/` 层（深色 `--sb-*` 工业令牌 + 28 组件）的首个生产级落地。SCR-0 是其余各屏（SCR-1~5）的地基。

## 关键决策

1. **承载**：独立 app `frontend/apps/screen`（Vite-plus + Vue3 + vue-router auto-routes + Tailwind v4），端口 5128。命名 `screen` 取代 `frontend-structure.md` 原 `business-board` 占位。
2. **布局**：全屏 `ScreenLayout`（脱离 BusinessLayout），强制 `<html class="dark">` + `--sb-*`，不挂 ThemePicker（screen token 本就独立于主题系统）。1920×1080 基准 + `ScreenScaler` 等比缩放（letterbox）。
3. **数据**：先全部 mock（`src/mock/`），`useScreenData` 包 mock fetcher；后端聚合 API（#570）就绪后只换 fetcher。理由：后端缺车间维度聚合，无真实数据可用。
4. **鉴权**：先用本地 mock store（`stores/auth.ts`），`@nerv-iip/auth` 真接入与 `configureAuthenticatedApiClient` 留 `main.ts` TODO；demo 大屏暂不强制 `requiresAuth`，便于直接预览。

## 交付物

- 脚手架：`package.json`/`vite.config.ts`(双网关 proxy)/`tsconfig.json`/`index.html`/`assets/main.css`/`App.vue`/`test/setup.ts`
- `screen-kit/`（后续 SCR-7 上提 `@nerv-iip/ui`）：
  - `scale.ts` — `computeScale(fit|width|stretch)` 纯函数
  - `ScreenScaler.vue` — 缩放容器（resize 监听 + letterbox 居中）
  - `ScrollBoard.vue` — rAF 自动滚动列表 + 悬停暂停 + 无缝循环
  - `useScreenData.ts` — 轮询 + 页面隐藏暂停 + 失败保活（不清空旧数据）
- `layouts/ScreenLayout.vue` — `ScreenScaler` + `ui:ScreenHeader`（实时时间/班次/在线）
- `pages/index.vue` — 工厂总览 demo（4 KPI + 6 车间状态矩阵 + OEE 三环 + 实时告警滚动，mock）
- `pages/login.vue` — 深色科技风登录页（流光球 + 网格 + 玻璃卡 + 克制动效，`prefers-reduced-motion` 兜底）
- AppHost 注册（`AddViteApp("screen", 5128)` 双网关引用）、`nerv.ps1` 端口矩阵、网关 CORS 默认值

## 验收

- `pnpm -C frontend/apps/screen test` → 13 passed（缩放计算 7 + useScreenData 6）
- `pnpm -C frontend/apps/screen typecheck` → 通过
- 真机 `localhost:5128`：登录页 + 工厂总览大屏渲染、零 console/server 错误、mock 轮询生效

## 待办（后续 issue）

- 接真后端鉴权（`@nerv-iip/auth`）+ 真实数据（依赖 #570 聚合 API）、开启 `requiresAuth`
- 生产 serving：两后端（PlatformGateway + BusinessGateway）路由模型（同 business-console 待定，暂不加 `PublishAsStaticWebsite`）
- 业务组合件（KpiHeroRow / LineStatusMatrix / DeviceStatusWall …）随 SCR-1+ 落地
- `screen-kit` 三件接口稳定后上提 `@nerv-iip/ui`（SCR-7）
