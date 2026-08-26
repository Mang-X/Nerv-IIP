# Nerv-IIP 文档入口

本页只负责按任务路由文档，不复制项目状态、架构正文或操作命令。先读取目标路径最近的 `AGENTS.md` / `AGENTS.override.md`，再从这里选择必要文档。

## 按任务选择

| 任务 | 首要入口 | 说明 |
| --- | --- | --- |
| 普通局部实现、修复、测试或重构 | 当前 Issue/spec、目标代码、配置与测试 | 不读取全局项目状态台账 |
| 服务边界、目录、数据所有权、跨域调用或公开契约 | [`architecture/README.md`](architecture/README.md) | 再按主题读取相关当前架构文档 |
| 引入、推翻或复评长期决策 | [`adr/README.md`](adr/README.md) | ADR 解释为什么选择；当前实现仍回到 Architecture / Governance / Runbook |
| 本地启动、部署、迁移、恢复或排障 | 当前位于 `architecture/` 的对应 runbook | M2 会迁移到独立 `runbooks/`，迁移前以架构索引路由为准 |
| 产品、角色、IA 或 UX 设计 | 对应 `*-module-product-design.md` 与产品文档 | 不从项目状态页推断产品语义 |
| 测试、脚本或协作治理 | 对应 governance 文档 | 只加载与任务直接相关的规则 |
| 发布、里程碑规划或跨域能力盘点 | [`architecture/implementation-readiness.md`](architecture/implementation-readiness.md) | 仅 M0 迁移期按需使用；普通任务不读，M1 将替换为轻量当前状态入口 |
| 历史计划或规格核对 | `superpowers/` | 历史记录不自动构成当前执行入口 |

## 文档类型

| 类型 | 回答的问题 | 更新方式 |
| --- | --- | --- |
| ADR | 为什么在多个可行方案中选择了这个？ | 历史不就地改写；变化时新 ADR 取代或部分修订 |
| Architecture | 系统现在是什么样，边界和交互是什么？ | 随当前系统原地更新 |
| Governance | 当前工程工作必须遵守什么规则？ | 只写现态，不保存逐轮事故故事 |
| Runbook | 如何启动、部署、迁移、恢复或排障？ | 随当前操作入口更新 |
| Reference | 当前有哪些字段、Schema、路由、矩阵和清单？ | 生成或随权威生产者更新 |
| Status | 当前重点、阻塞和全仓级入口是什么？ | 高频替换，不累积已完成历史 |
| Report | 某次调查、实验、审计或修复发现了什么？ | 完成后冻结 |
| Product | 给谁使用、解决什么问题、业务与 UX 如何工作？ | 随产品裁决更新 |

## M0 迁移说明

当前 `docs/architecture/` 仍混合 Architecture、Governance、Runbook、Product、Reference、Status 和 Report。M0 只建立阅读路由并停止继续扩张，不批量移动文件；物理分类由 [GitHub #2290](https://github.com/Mang-X/Nerv-IIP/issues/2290) 分批完成。

`docs/architecture/implementation-readiness.md` 已停止接收新的 Issue 级交付日志和事故过程。它在 M1 完成前只用于发布、里程碑和跨域能力盘点，不是普通任务的开工前置。

## 权威来源纪律

1. 当前命令、版本、目录、生成入口和实现行为优先以代码、配置、脚本、帮助输出和测试为准。
2. 当前项目进度、负责人、阻塞和验收证据留在 GitHub/Linear，不复制成仓库长期总账。
3. 同一事实只维护一个权威住所；其它文档使用链接和简短上下文，不复制正文。
4. 历史文档可以保留当时判断，但必须通过路由或标题避免冒充当前入口。
5. 新增文档前先判断其变更频率；无法明确类型时，先在 Issue/PR 中完成分类裁决。
