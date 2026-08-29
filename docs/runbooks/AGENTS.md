# Runbook 文档规则

本目录维护当前可执行操作，不维护项目状态、阶段历史或事故叙事。

- 修改前从 `docs/runbooks/README.md` 选择对应操作，并读取 `docs/governance/docs/language.md`。
- 命令、参数、端口、版本、目录和副作用以当前脚本、CLI help、配置与代码为权威；发现冲突时修正文档，不修改生产者去迎合旧 Runbook。
- 每份 Runbook 必须能回答：执行前要满足什么、用哪个受治理入口、何时停止、如何恢复/回滚、证据放在哪里或如何识别。
- 不在 Runbook 里复制 GitHub/Linear 状态、CI run 总账、阶段完成史或一次性调查计数。
- 旧 Architecture 兼容页和 `docs/runbooks/implementation-readiness.md` 只做迁移导航，不得长回正文。
- 不新增 Runbook registry、生成器、自然语言 checker 或独立 CI step。
