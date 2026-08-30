# Claude 指令 — business-console（业务前端）

请阅读并遵循 [AGENTS.md](./AGENTS.md)。

仓库根的 [CLAUDE.md](../../../CLAUDE.md) 与 [AGENTS.md](../../../AGENTS.md) 仍然适用；
本目录的 [AGENTS.md](./AGENTS.md) 是业务前端（business-console）的补充与覆盖。

## 开始前

1. 先读当前 Issue/spec 与所改业务域的**产品业务文档**（见本目录 AGENTS.md「文档及时性」一节），以它们为业务 / IA / UX 的事实依据。
2. 后端能力以 facade、生成客户端、公开契约和测试的当前事实为准，不凭旧状态文档假设。
3. 发布、里程碑规划或跨域能力盘点时读取 `docs/status/current.md`；端口与 worktree 动态地址分别通过 `.\nerv.ps1 ports` 和 `.\nerv.ps1 describe <resource>` 获取。

完整指引（三大支柱、目录定位、权限同步、工厂对接、门禁、Done 定义）见 [AGENTS.md](./AGENTS.md)。
