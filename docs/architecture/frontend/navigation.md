# 前端导航当前架构

本文只描述导航相关的**当前系统边界与职责分配**。用户 IA 见 [`../../product/navigation.md`](../../product/navigation.md)，长期工程约束见 [`../../governance/frontend/navigation.md`](../../governance/frontend/navigation.md)，当前 route/page/permission/facade 的核实入口见 [`../../reference/frontend/navigation-map.md`](../../reference/frontend/navigation-map.md)。

## 应用边界

### Platform Console

`frontend/apps/console` 是平台控制面客户端。它拥有自己的页面、router 与导航模型，通过平台 Gateway/公开契约访问 IAM、AppHub、Ops、Notification、FileStorage 等通用能力。

### Business Console

`frontend/apps/business-console` 是桌面业务客户端。应用拥有业务能力域、域内导航、route→域解析和客户端 RBAC 裁剪；后端访问统一收敛到 `backend/gateway/BusinessGateway` 的公开 facade。导航事实的当前生产者是 `src/navigation.ts` 与 `src/permissions.ts`。

### Business PDA

`frontend/apps/business-pda` 是一线任务客户端。页面由 file-based route 组织，`src/router/index.ts` 与 `src/router/guards/**` 负责 router/访问前置条件；它不共享 Business Console 的复杂菜单树。

## AppShell 边界

`frontend/packages/app-shell` 提供桌面应用共享壳层和稳定导航类型。当前桌面长期形态由 `AppShellT` 及其导出的 `NavDomain`、`NavLink`、`NavGroup`、`SideNav` 等契约承载。

职责分配：

- AppShell：布局、顶部能力域、域内侧边导航、用户菜单等壳层呈现与通用交互；
- 消费方 app：业务导航模型、route→域解析、图标/文案、权限裁剪；
- Gateway：每次请求的真实授权边界；
- Product：角色入口与 IA；
- Governance：哪些规则必须长期遵守。

AppShell 不因为收到 `requiredPermissions` 就成为授权服务；消费方隐藏菜单也不能替代 Gateway enforcement。

## 依赖方向

```text
Product IA
    ↓
consumer app navigation / router / permissions
    ↓                    ↓
@nerv-iip/app-shell      generated client / stable API client exports
                              ↓
                      Gateway public contract
                              ↓
                         backend services
```

导航壳层不得反向依赖具体业务域；业务页面不得在局部实现第二套全局 shell。

## 当前事实来源

Architecture 不维护固定端口、完整菜单、页面交付状态或 facade 覆盖矩阵。上述动态事实的生产者索引见 [`../../reference/frontend/navigation-map.md`](../../reference/frontend/navigation-map.md)。

迁移前把架构、产品 IA、工程规则、代码清单和阶段状态混在一起的旧正文已冻结为 [`../../reports/audits/frontend-navigation-map-pre-m2-n-2026-09-07.md`](../../reports/audits/frontend-navigation-map-pre-m2-n-2026-09-07.md)，不再是现态架构来源。
