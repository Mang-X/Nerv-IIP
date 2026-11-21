# 前端目录路由

- **命令或版本：** 以 `frontend/package.json` 和受影响 package 的 `package.json` 为准。
- **页面、路由、状态、API 消费或 app/package 边界：** 先读 `docs/architecture/frontend/workspace-structure.md`；只有涉及 PDA/Capacitor 运行时再补读 `docs/architecture/mobile/capacitor.md`，不要默认加载整个 Architecture 目录。
- **界面设计、主题、组件与交互模式：** 从[独立设计文档站](apps/design-system/docs/guide/introduction.md)进入，按桌面 PC、移动 PDA、车间一体机或挂墙大屏查规范与示例。涉及导入、命名、原版冻结或跨包边界时，同时遵循最近子树的 `AGENTS.md` 和站内链接到的工程治理。
- **本地跑单测、判读红结果或多 worktree 并行跑测：** 读 `docs/runbooks/testing/frontend-vitest-local.md`。
- **API 契约：** 先读 `docs/governance/api/contracts-and-codegen.md`；运行时关系见 `docs/architecture/integration/api-contracts.md`，生成操作见 `docs/runbooks/api-codegen.md`。
- **用户可见页面或流程：** 按 `docs/adr/2026-07-07-0021-product-docs-information-architecture.md` 评估产品文档影响。
- **KnownException / Gateway 用户可见错误：** 读 `docs/governance/errors/user-visibility.md`，不要用前端 toast 证明服务端错误已稳定传输。
