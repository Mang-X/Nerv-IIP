# 前端工作区当前架构

本文只描述 `frontend/` 的**当前 app/package 职责、依赖方向、配置分层以及路由/状态/API 组织边界**。命令、Node/pnpm/依赖版本和测试入口以 `frontend/package.json`、各 app/package 的 `package.json`、Vite 配置及相应 Runbook 为准；产品 IA/角色旅程不在本文维护。M2-L 拆分前的混合正文冻结于 [`../../reports/m2-l-frontend-structure-pre-split-2026-09-07.md`](../../reports/m2-l-frontend-structure-pre-split-2026-09-07.md)。

为何采用 pnpm `apps/*` / `packages/*` 工作区、配置分层与生成代码隔离，见 [ADR 0006](../../adr/0006-frontend-workspace-structure.md)；为何采用 Vue Router 文件路由与页面共置，见 [ADR 0007](../../adr/0007-vue-router-file-routing-colocation.md)。本页不复制替代方案与历史理由，只随当前仓库结构更新现态。

## 工作区边界

```text
frontend/
  apps/
    console/            平台控制台
    business-console/   PC 业务控制台
    business-pda/       PDA 一线作业 / Capacitor Android 宿主
    design-system/      设计系统文档与预览
    docs/               最终用户产品文档站
    screen/             只读工业大屏
  packages/
    api-client/         生成契约与稳定 API 导出
    ui/                 PC/大屏共享 UI 与 token
    ui-mobile/          PDA/触摸密度组件
    business-core/      PC/PDA 共用业务类型、SOP、CodeSet 与命令构造
    app-shell/          应用壳公共能力
    auth/               跨 app 认证会话编排
```

`apps/*` 是独立可运行入口；共享行为只有出现真实跨 app 复用压力后才进入 `packages/*`。页面业务逻辑不得通过把 app 私有目录变成事实共享层；生成代码与手写代码保持隔离。

## 应用职责

- `console` 负责平台控制面，不承载 Business CRUD。
- `business-console` 负责 PC 业务域页面，经 BusinessGateway facade 消费公开业务契约，不直接依赖业务服务 URL 或内部实现。
- `business-pda` 负责手持 PDA 作业，和 PC 共用稳定 `api-client`/`business-core` 能力，但拥有独立移动交互层；其运行时边界进一步见 [`../mobile/capacitor.md`](../mobile/capacitor.md)。
- `screen` 是只读展示 app，复用 `ui` 的 screen 层和 token，不依赖 `ui-mobile`。
- `design-system` 与 `docs` 是独立文档/预览 app，不成为生产业务逻辑宿主。

## Package 职责与依赖方向

- `api-client`：由 Gateway/OpenAPI producer 生成或封装稳定客户端导出；app 不手写第二套跨服务 DTO。
- `ui`：PC/大屏视觉与基础组件；app 通过稳定导出消费，不深链内部组件文件。
- `ui-mobile`：触摸/PDA 密度组件，可复用 `ui` 的设计 token，但不直接 import PC 页面区块。
- `business-core`：无页面依赖的业务类型、状态/SOP、CodeSet 和命令构造器，可同时被 PC/PDA 使用。
- `auth`：app-agnostic 的登录恢复、refresh、logout/session revoke、unauthorized orchestration 和 route helper；具体 storage key、store id、API client、登录路径与文案由 app 注入。
- `app-shell`：跨 app 壳层公共 API；页面或单个业务域不得反向成为 app-shell 的依赖。

依赖总体从可运行 app 指向稳定 packages；packages 不反向依赖 app 页面。业务前端只经 Gateway/公开 client 契约访问后端，不通过共享数据库、服务内部 DTO 或硬编码服务地址形成隐式耦合。

## 配置分层

根级 workspace 配置负责 workspace 成员、共享 TypeScript/Vite+ 基线和聚合任务；应用级配置负责 app 的 Vue/Router、alias、构建与 app 私有类型；包级配置负责各公共包的稳定导出、生成器和自身类型/测试边界。

精确命令、聚合任务、端口和运行版本不在 Architecture 手抄，发生冲突时以 package scripts、配置和命令帮助为准。

## 路由、页面与状态边界

1. `src/pages` 是页面入口，显式 `src/router/index.ts` 负责 router 装配；文件路由由 Vue Router 官方能力生成。
2. `src/layouts` 承载布局；`src/components` 承载共享或局部视图；跨组件/跨页面复用逻辑进入 `src/composables`。
3. Pinia `src/stores` 只保存真正的客户端/会话状态；服务端状态与请求生命周期由 query/mutation 层管理，页面保持薄。
4. `src/api` 是 app 侧 API 组装层，复用 `@nerv-iip/api-client` 稳定导出，不复制生成 DTO。
5. route meta 可以描述访问控制、布局、feature flag 和标题，但真实授权仍由 Gateway/IAM 逐请求校验；前端裁剪入口不是授权边界。
6. 页面私有组件优先与页面共置；只有出现跨页面或跨 app 的真实复用才提升到更高层级。

## 与其它文档类型的边界

- 工作区、配置分层与生成隔离的决策理由：[ADR 0006](../../adr/0006-frontend-workspace-structure.md)。
- 文件路由与页面共置的决策理由：[ADR 0007](../../adr/0007-vue-router-file-routing-colocation.md)。
- 视觉 token、共享组件规则：[`../../governance/frontend/design-system.md`](../../governance/frontend/design-system.md)。
- API/OpenAPI/codegen：[`../integration/api-contracts.md`](../integration/api-contracts.md) 与 [`../../governance/api/contracts-and-codegen.md`](../../governance/api/contracts-and-codegen.md)。
- 产品导航、角色旅程、页面语义：[`../../product/README.md`](../../product/README.md)。
- 本地测试与命令操作：[`../../runbooks/testing/frontend-vitest-local.md`](../../runbooks/testing/frontend-vitest-local.md) 及各 package script。
