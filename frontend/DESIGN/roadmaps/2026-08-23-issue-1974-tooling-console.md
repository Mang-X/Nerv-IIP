# 工装与模具维护台视觉证据

本文档是 Issue #1974 / #2064 工装与模具维护台视觉证据的持久文档所有者。截图保存在
[`assets/2026-08-23-issue-1974-tooling-console/`](./assets/2026-08-23-issue-1974-tooling-console/)，
生成入口为
[`issue1974-tooling-visual.spec.ts`](../../apps/business-console/e2e/issue1974-tooling-visual.spec.ts)。

## 证据边界

截图由 Playwright 驱动真实 Chromium，在 1440×900 viewport 中渲染。规格通过
`page.route` 提供 mock API 契约响应，因此这些证据只证明截图所示前端渲染、交互和
计算样式；未连接真实后端、数据库或消息基础设施，不属于 FullChain 证据。

## 截图目录

| 场景                           | 证据                                                                                                                                |
| ------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------- |
| 工装维护台列表、寿命与排程状态 | [01-tooling-workbench.png](./assets/2026-08-23-issue-1974-tooling-console/01-tooling-workbench.png)                                 |
| 工装详情及适用工作中心、SKU    | [02-tooling-detail.png](./assets/2026-08-23-issue-1974-tooling-console/02-tooling-detail.png)                                       |
| 注册表单必填与正整数错误态     | [03-register-validation.png](./assets/2026-08-23-issue-1974-tooling-console/03-register-validation.png)                             |
| 退役终态确认及原因必填         | [04-retire-confirmation.png](./assets/2026-08-23-issue-1974-tooling-console/04-retire-confirmation.png)                             |
| 完成保养时累计使用次数清零披露 | [05-maintenance-completion-disclosure.png](./assets/2026-08-23-issue-1974-tooling-console/05-maintenance-completion-disclosure.png) |
