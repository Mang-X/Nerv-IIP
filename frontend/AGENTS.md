# 前端目录路由

- **命令或版本：** 以 `frontend/package.json` 和受影响 package 的 `package.json` 为准。
- **页面、路由、状态或 API 消费：** 先读 `docs/architecture/frontend-structure.md`。
- **共享设计系统、shadcn-vue、语义 token、公共组件导入边界：** 读 `docs/governance/frontend/design-system.md`。
- **NvUI 组件、导入、命名、token 或冻结源码：** 先读 `frontend/DESIGN/governance.md`、`docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md` 与最近子树的 `AGENTS.md`；NvUI 专属规则优先于通用设计系统规则。
- **API 契约：** 先读 `docs/architecture/api-contract-and-codegen.md`，使用受治理的生成链路。
- **用户可见页面或流程：** 按 `docs/adr/0021-product-docs-information-architecture.md` 评估产品文档影响。
- **KnownException / Gateway 用户可见错误：** 读 `docs/governance/errors/user-visibility.md`，不要用前端 toast 证明服务端错误已稳定传输。
