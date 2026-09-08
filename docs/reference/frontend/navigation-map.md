# 前端导航当前事实索引

本文是前端导航的 **Reference 索引**，用于回答“当前 route、页面、权限和 facade 应该去哪里核实”。它不维护第二份 route/端口/交付状态总账；精确事实始终由下面的代码、公开契约和生成物生产。

产品 IA 见 [`../../product/navigation.md`](../../product/navigation.md)，导航工程规则见 [`../../governance/frontend/navigation.md`](../../governance/frontend/navigation.md)，壳层与应用边界见 [`../../architecture/frontend/navigation.md`](../../architecture/frontend/navigation.md)。

## 当前生产者

| 事实 | 权威生产者 | 如何核实 |
| --- | --- | --- |
| Business Console 顶部能力域、域内导航、route→域映射 | `frontend/apps/business-console/src/navigation.ts` | 直接读取 `BUSINESS_DOMAINS`、`DOMAIN_SIDE_NAV` 与 route 解析逻辑；不要从文档手抄菜单清单 |
| Business Console 页面与 route | `frontend/apps/business-console/src/pages/**` + 当前 file-based router 配置 | 以页面文件、route 元数据和实际 router 产物为准 |
| Business Console 导航权限 | `frontend/apps/business-console/src/permissions.ts`、`src/navigation.ts` | 以当前 permission code 与 `requiredPermissions` 绑定为准；服务端授权仍由 Gateway 强制 |
| Platform Console 导航与 route→域 | `frontend/apps/console/src/layouts/DefaultLayout.vue`、`frontend/apps/console/src/pages/**`、`frontend/apps/console/src/router/index.ts` | 以当前 layout/router/page 代码为准 |
| Business PDA 页面与 router | `frontend/apps/business-pda/src/pages/**`、`frontend/apps/business-pda/src/router/index.ts`、`src/router/guards/**` | 以 file-based 页面、router 和 guard 为准 |
| 共享桌面壳层契约 | `frontend/packages/app-shell/src/**` 与包导出 | `NavDomain` / `NavLink` / `NavGroup` / `SideNav` / `AppShellT` 等以当前包导出和测试为准 |
| Business Console facade | `backend/gateway/BusinessGateway` endpoint/facade 代码与 OpenAPI | 以公开 Gateway 契约为准，不从页面文档推断“后端已有” |
| generated client | `frontend/packages/api-client/src/generated/**` 及 codegen 配置 | 由 OpenAPI 生成；generated 文件不手改 |
| IAM / permission catalog | IAM permission producer、Gateway endpoint authorization 与相关测试 | 权限存在、默认角色拥有和 endpoint enforcement 是不同事实，分别回到生产者核实 |

## 为什么不再列固定端口

旧 `frontend-navigation-map.md` 曾人工维护 PlatformGateway、BusinessGateway、各业务服务与前端应用的端口表。端口属于运行配置事实，会随 AppHost、开发配置、容器/部署配置变化；Reference 只指向生产者，不继续复制数值。

需要端口时，从当前 AppHost、launch/config、compose/deployment producer 或相应 Runbook 获取；不得把本页变成第二份端口 registry。

## 为什么不再列“已落地 / 规划”

route、页面、facade 与 permission 是否存在可以直接从代码/OpenAPI/generated client 验证；项目是否完成、某 Phase 是否通过、某次 CI 是否成功属于 Status / GitHub / Linear / Reports。Reference 不把这些不同生命周期重新合成一张手工状态表。

## 使用方式

1. 要改 Business Console 菜单：从 `src/navigation.ts` 和 `src/permissions.ts` 开始。
2. 要确认一个页面是否存在：查对应 app 的 `src/pages/**` 与 router。
3. 要确认某页面能否调用后端：查 BusinessGateway facade/OpenAPI，再看 generated client 与页面消费。
4. 要确认权限：同时核实 permission catalog、客户端导航裁剪和 Gateway endpoint enforcement；三者不能互相代替。
5. 要讨论“用户应该怎样找能力”：回到 Product，而不是把当前代码形状当产品裁决。

迁移前的混合正文已冻结在 [`../../reports/audits/frontend-navigation-map-pre-m2-n-2026-09-07.md`](../../reports/audits/frontend-navigation-map-pre-m2-n-2026-09-07.md)，仅用于追溯当时的状态与形成过程。
