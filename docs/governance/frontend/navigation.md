# 前端导航治理

本文只维护**当前必须遵守的导航工程规则**。产品 IA 见 [`../../product/navigation.md`](../../product/navigation.md)，当前事实查询见 [`../../reference/frontend/navigation-map.md`](../../reference/frontend/navigation-map.md)，壳层/应用边界见 [`../../architecture/frontend/navigation.md`](../../architecture/frontend/navigation.md)。

## 当前规则

1. **消费应用拥有导航模型。** `@nerv-iip/app-shell` 提供壳层与稳定导航类型，Console / Business Console 等应用负责自己的导航模型、route→域解析、RBAC 裁剪和 feature flag。
2. **前端裁剪不是授权边界。** `requiredPermissions` 只用于展示裁剪；Gateway 的 per-request enforcement 始终是最终授权来源，客户端必须正确处理 401/403。
3. **Business Console 不直连业务服务。** 业务页面通过 `backend/gateway/BusinessGateway` 的公开 facade 与 generated/stable client 消费能力，不因导航便利绕过 Gateway。
4. **菜单表达任务，不复制后端结构。** 后端 bounded context、服务名、operationId、sourceSystem 等工程概念不得直接成为用户导航 IA。
5. **菜单层级受控。** 一级能力区 + 域内模块/页面为主要结构；对象详情、页内 Tabs、创建/编辑/审批/打印/报工等动作不进入菜单树。
6. **route-ready 才能进入当前可见导航。** 新条目必须已有可达 route、页面和对应权限裁剪输入；不得用“规划”“后端已落地”等状态标签提前暴露不存在的入口。
7. **PDA 不复制 PC 菜单树。** PDA 以任务、扫码和上下文直达为主；具体产品规则回到 Product。
8. **当前清单不得手抄到 Governance。** route、page、facade、permission、端口和完成状态均回到代码/配置 producer 或 Reference 入口。

## 事实生产者

- Business Console 导航模型：`frontend/apps/business-console/src/navigation.ts`
- Console 导航/布局：`frontend/apps/console/src/layouts/DefaultLayout.vue` 及其路由代码
- AppShell 公共导航类型与壳层：`frontend/packages/app-shell/`
- 页面与路由：各 app 的 `src/pages/` 与路由生产机制
- BusinessGateway facade：`backend/gateway/BusinessGateway/` 的代码与 OpenAPI
- 权限事实：IAM catalog / Gateway endpoint authorization / 前端 permission 映射各自的 producer

## 变更纪律

导航规则变化若影响长期产品 IA，先更新 Product；若影响壳层/应用边界，同步 Architecture；若只是当前 route/page/facade 事实变化，不在本文登记状态，只让 producer 与 Reference 指针保持可查。

不得为导航治理新增第二份 registry、生成器、scanner、固定端口清单或独立 CI step。