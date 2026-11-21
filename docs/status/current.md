# Nerv-IIP 当前状态

> 本页只维护全仓级重点、阻塞、统一入口和少量跨域注意事项。功能完成记录、Issue 正文、详细验证命令、事故过程和字段实现不进入本页。

发布、里程碑、进度和验收证据见 [GitHub Issues](https://github.com/Mang-X/Nerv-IIP/issues) 与 [Linear](https://linear.app/mangax)。

## 当前阻塞

- MES 主线受 [世界观种子拆除：产品线开工前的清理](https://linear.app/mangax/project/世界观种子拆除产品线开工前的清理-36e197ef2445) 门控：触及 `Application/Seed/` 的任务等待该项目 L2；无文件冲突的产品面任务可以并行。

## 全仓级统一入口

- 开工与 Agent 路由：[`/AGENTS.md`](../../AGENTS.md)。
- 项目定位与快速开始：[`/README.md`](../../README.md)。
- 文档任务路由：[`/docs/README.md`](../README.md)。
- 当前架构：[`/docs/architecture/README.md`](../architecture/README.md)。
- 当前 Runbook：[`/docs/runbooks/README.md`](../runbooks/README.md)。
- 长期决策：[`/docs/adr/README.md`](../adr/README.md)。
- 本地开发与排障：[`local-development.md`](../runbooks/local-development.md)。

## 跨域注意事项

1. 当前实现行为以代码、配置、公开契约、测试和命令帮助为准；本页不能替代实现核查。
2. 当前进度、负责人、依赖和验收证据以 GitHub/Linear 为准；本页不复制 Issue 正文。
