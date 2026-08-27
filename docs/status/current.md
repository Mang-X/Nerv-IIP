# Nerv-IIP 当前状态

> 本页只维护全仓级重点、阻塞、统一入口和少量跨域注意事项。功能完成记录、Issue 正文、详细验证命令、事故过程和字段实现不进入本页。

- Last reviewed: 2026-08-27
- Baseline: [`main@b3301a31`](https://github.com/Mang-X/Nerv-IIP/commit/b3301a31465e32c685f18f4ce5d4ed1db73eb57f)
- Work tracking: [GitHub Issues](https://github.com/Mang-X/Nerv-IIP/issues) / [Linear 工作区](https://linear.app/mangax)

## 当前重点

1. [MES 产品化：从派工到完工的现场闭环](https://linear.app/mangax/project/mes-产品化从派工到完工的现场闭环-03a908df5c89)  
   当前唯一产品主线；按 F0–F6 推进派工、开工、领料、报工、完工入库，以及现场防错、首件、巡检、安灯和停机归因。
2. [#2286：ADR / Architecture / 状态文档治理](https://github.com/Mang-X/Nerv-IIP/issues/2286)  
   下一阶段分别由 [#2290](https://github.com/Mang-X/Nerv-IIP/issues/2290)、[#2291](https://github.com/Mang-X/Nerv-IIP/issues/2291) 与 [#2292](https://github.com/Mang-X/Nerv-IIP/issues/2292) 跟踪。
3. [#2157：清理 scripts/CI 影子框架](https://github.com/Mang-X/Nerv-IIP/issues/2157)。
4. [#1222：拆除 BusinessGateway 跨域 Client 巨石](https://github.com/Mang-X/Nerv-IIP/issues/1222)。

## 当前阻塞

- MES 主线受 [世界观种子拆除：产品线开工前的清理](https://linear.app/mangax/project/世界观种子拆除产品线开工前的清理-36e197ef2445) 门控：触及 `Application/Seed/` 的任务等待该项目 L2；无文件冲突的产品面任务可以并行。
- 文档治理最终兼容收口 [#2292](https://github.com/Mang-X/Nerv-IIP/issues/2292) 需等待分类迁移 [#2290](https://github.com/Mang-X/Nerv-IIP/issues/2290) 与 ADR 收敛 [#2291](https://github.com/Mang-X/Nerv-IIP/issues/2291)。

## 全仓级统一入口

- 开工与 Agent 路由：[`/AGENTS.md`](../../AGENTS.md)。
- 项目定位与快速开始：[`/README.md`](../../README.md)。
- 文档任务路由：[`/docs/README.md`](../README.md)。
- 当前架构：[`/docs/architecture/README.md`](../architecture/README.md)。
- 长期决策：[`/docs/adr/README.md`](../adr/README.md)。
- 本地开发与排障：[`local-dev-troubleshooting.md`](../architecture/local-dev-troubleshooting.md)。

## 跨域注意事项

1. 当前实现行为以代码、配置、公开契约、测试和命令帮助为准；本页不能替代实现核查。
2. 当前进度、负责人、依赖和验收证据以 GitHub/Linear 为准；本页不复制 Issue 正文。
3. [`../architecture/implementation-readiness.md`](../architecture/implementation-readiness.md) 仅为旧路径兼容入口。
4. [`archive/`](archive/) 中的日期化文件是时点快照，不得用来覆盖当前事实，也不得原地追加修订。
5. 完成的重点从本页删除；历史通过 Git、GitHub/Linear 与冻结快照追溯。
