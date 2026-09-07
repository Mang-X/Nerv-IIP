# 前端导航治理

本文只定义 Nerv-IIP **当前必须遵守的导航工程规则**。产品 IA 见 [`../../product/navigation.md`](../../product/navigation.md)，当前壳层/应用边界见 [`../../architecture/frontend/navigation.md`](../../architecture/frontend/navigation.md)，route/page/permission/facade 的可复核入口见 [`../../reference/frontend/navigation-map.md`](../../reference/frontend/navigation-map.md)。

## 1. 应用边界

1. Platform Console 只承载 IAM、AppHub、Ops、FileStorage、Notification、平台监控等通用控制面，不加入 MES、WMS、ERP、质量、设备维护等业务 CRUD 或业务工作流。
2. Business Console 只能通过 `backend/gateway/BusinessGateway` 的 `/api/business-console/v1/**` facade 消费业务服务；不得从浏览器直连业务服务 URL，也不得 deep import generated client 绕过稳定导出入口。
3. Business PDA 是独立任务端，不复制 PC 的复杂菜单树；页面和权限仍必须走当前 PDA router、permission guard 与 Gateway 契约。

## 2. 导航结构

1. 桌面端使用共享 `@nerv-iip/app-shell` 壳层，不在业务页面内再实现第二套全局导航、顶栏或用户菜单。
2. 长期桌面结构为顶部能力域 + 左侧域内导航。业务应用的导航模型由消费方应用拥有，AppShell 负责呈现，不拥有业务权限裁决。
3. 逻辑菜单最多三级：域 → 模块/任务组 → 页面。对象详情、页面 Tabs、创建/编辑/审批/确认/打印/报工等动作不得升级为全局菜单项。
4. 每个可见导航项都必须能够回到真实 route；新增大能力域或页面时先完成产品 IA、route、权限与当前验证，再接入导航生产者。

## 3. 权限与范围

1. 导航裁剪是体验优化，不是授权边界。Gateway 的 per-request enforcement 始终权威；客户端不得因为菜单已经隐藏或显示而跳过 401/403 处理。
2. Business Console 的 `requiredPermissions` 必须来自当前 permission producer；不得在文档或页面局部手抄第二套权限目录。
3. 客户端可基于当前用户、组织、环境、权限和 feature flag 预裁剪导航，但任何缓存都不得被当成服务端放行依据。
4. 数据行级、对象级或组织范围可见性只有在对应 facade 明确实现后才能在产品中宣称支持。

## 4. 当前事实不得手抄

下列事实必须回到 Reference 所列生产者，不在 Product / Governance / Architecture 复制：

- 应用或服务固定端口；
- 当前 route/page 全量清单；
- facade 覆盖率和“已有/尚未”总账；
- “已落地/过渡/前端待建/规划”等交付状态；
- permission catalog、生成客户端 operation 清单。

代码、OpenAPI、generated client、permission producer 与文档冲突时，以生产者为准并修正文档入口；不得通过维护第二份手工矩阵消除冲突。

## 5. 文档更新规则

- 改产品 IA、角色入口或上下文穿透：更新 [`../../product/navigation.md`](../../product/navigation.md)。
- 改导航工程规则：更新本文；若属于长期架构取舍，按 ADR 治理判断是否需要决策记录。
- 改 route/page/permission/facade：先改生产者，再按需更新 [`../../reference/frontend/navigation-map.md`](../../reference/frontend/navigation-map.md) 的生产者索引；不要复制动态清单。
- 改 AppShell 或应用边界：更新 [`../../architecture/frontend/navigation.md`](../../architecture/frontend/navigation.md)。
- 项目进度、CI run、阶段验收与一次性审计进入 GitHub/Linear、Status 或 Reports，不写回本文。

旧入口 `docs/architecture/frontend-navigation-map.md` 在 M2/M4 期间只保留兼容导航；迁移前正文冻结于日期化 Audit，不再维护。
