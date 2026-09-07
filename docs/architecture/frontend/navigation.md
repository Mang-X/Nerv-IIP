# 前端导航当前架构

本文只描述当前导航系统的**架构边界与责任分配**，不维护产品菜单清单、route 状态、权限目录或项目进度。

## 责任边界

- `@nerv-iip/app-shell` 提供壳层、布局与稳定导航类型；它不拥有各应用的业务导航模型，也不承担授权强制。
- Console / Business Console 等消费应用拥有自己的导航配置、route→域解析、权限裁剪输入和应用级用户菜单。
- 页面路由由各 app 的路由生产机制与页面代码拥有；Architecture 不复制 route 表。
- 业务前端通过 Gateway facade 和 generated/stable client 访问服务能力；导航模型不得成为服务发现或授权旁路。
- Gateway 每请求授权是最终安全边界；前端导航裁剪只影响展示。
- PDA 作为独立应用维持任务/扫码导航模型，不依赖 PC 菜单树作为运行时输入。

## 依赖方向

```text
Product IA
   ↓ 约束用户入口与任务组织
Application navigation model ──→ AppShell types / shell rendering
          │
          ├──→ app routes / pages
          ├──→ frontend permission mapping（仅展示裁剪）
          └──→ generated/stable client ──→ Gateway facade ──→ service

Gateway/IAM authorization ───────────────────────────────→ 每请求强制
```

AppShell 不反向读取业务应用的 route registry；Architecture 也不建立第二份 route/facade registry。

## 当前实现入口

- Business Console 导航模型：`frontend/apps/business-console/src/navigation.ts`
- Business Console 页面：`frontend/apps/business-console/src/pages/`
- Platform Console：`frontend/apps/console/`
- Business PDA：`frontend/apps/business-pda/`
- AppShell：`frontend/packages/app-shell/`

精确当前事实及核对路径见 [`../../reference/frontend/navigation-map.md`](../../reference/frontend/navigation-map.md)。

## 相关文档

- 产品 IA：[`../../product/navigation.md`](../../product/navigation.md)
- 当前治理规则：[`../../governance/frontend/navigation.md`](../../governance/frontend/navigation.md)
- 前端 workspace 架构：[`workspace-structure.md`](workspace-structure.md)

项目阶段、完成状态、固定端口和历史形成过程不属于 Current Architecture。