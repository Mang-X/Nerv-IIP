# 前端导航事实入口

本文是当前前端导航事实的**查询入口**，不是第二份 route / page / facade / permission 总账。精确事实必须回到生产者；这里仅说明“去哪里查、按什么口径解释”。

## 当前生产者

| 事实 | 权威生产者 | 说明 |
| --- | --- | --- |
| Business Console 顶部能力区、域内侧栏、route→域映射 | `frontend/apps/business-console/src/navigation.ts` | 当前可见导航以代码为准；不要在文档复制完整菜单表 |
| Business Console 页面 | `frontend/apps/business-console/src/pages/` | 页面是否存在、路径与页面元数据从代码复核 |
| Platform Console 壳层与导航 | `frontend/apps/console/src/layouts/DefaultLayout.vue` 及其路由代码 | 平台控制面与业务控制面分离 |
| Business PDA 页面与入口 | `frontend/apps/business-pda/` | PDA 采用任务/扫码范式，不以 PC 菜单表推导 |
| AppShell 导航类型与壳层能力 | `frontend/packages/app-shell/` | `NavDomain`、`NavLink`、`NavGroup`、`SideNav` 等公共契约以包导出为准 |
| BusinessGateway facade | `backend/gateway/BusinessGateway/` + OpenAPI | “页面可调用什么”必须回到 Gateway facade / generated client，不从菜单反推 |
| 权限码与授权 | IAM catalog、Gateway endpoint authorization、前端 permission 映射 | 三者是不同 producer；导航裁剪不等于服务端授权 |
| 运行端口 | AppHost / launch / deployment 配置 | 本文不手抄固定端口；需要时读取当前配置 |

## 常见核对方式

### 新增或调整 Business Console 导航

1. 在 `src/navigation.ts` 核对条目、目标 route 和 `requiredPermissions`；
2. 在 `src/pages/` 核对目标页面真实存在；
3. 核对 Gateway facade / generated client 是否存在对应能力；
4. 核对权限生产者与 Gateway enforcement；
5. 若改变产品 IA，同步 [`../../product/navigation.md`](../../product/navigation.md)；若改变工程规则，同步 [`../../governance/frontend/navigation.md`](../../governance/frontend/navigation.md)。

### 判断“能力是否已落地”

不要使用旧文档的“已落地 / 过渡 / 后端已落地 / 前端待建 / 规划”标签。按当前代码分别回答：route 是否存在、页面是否存在、facade 是否存在、客户端是否消费、权限是否接线、测试/验收是否覆盖。项目完成状态留在 GitHub/Linear，而不是 Reference。

## 不在本文维护

- 固定端口表；
- 手工 route/page/facade 全量清单；
- “已落地/规划”状态台账；
- Phase、Issue、PR、CI run 历史；
- 产品角色/IA 裁决；
- 当前工程强制规则。

这些职责分别回到 producer、GitHub/Linear、Product、Governance 与冻结 Reports。

M2-N 前的混合文档原文已冻结到 [`../../reports/audits/frontend-navigation-map-pre-m2-n-2026-09-07.md`](../../reports/audits/frontend-navigation-map-pre-m2-n-2026-09-07.md)，仅用于历史追溯。