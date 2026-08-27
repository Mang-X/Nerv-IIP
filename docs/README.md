# Nerv-IIP 文档入口

本页只负责按任务路由文档，不复制项目状态、架构正文或操作命令。先读取目标路径适用的 Agent 指令，再选择必要文档。

## 按任务选择

| 任务 | 首要入口 | 说明 |
| --- | --- | --- |
| 普通局部实现、修复、测试或重构 | 当前 Issue/spec、目标代码、配置与测试 | 不读取全局状态或历史快照 |
| 发布、里程碑规划或跨域能力盘点 | [`status/current.md`](status/current.md) | 只含全仓级摘要；细节回到 GitHub/Linear |
| 服务边界、目录、数据所有权、跨域调用或公开契约 | [`architecture/README.md`](architecture/README.md) | 再按主题读取相关当前架构文档 |
| 引入、推翻或复评长期决策 | [`adr/README.md`](adr/README.md) | ADR 解释为什么选择；当前实现回到 Architecture / Governance / Runbook |
| 本地启动、部署、迁移、恢复或排障 | 当前位于 `architecture/` 的对应 runbook | M2 迁移前以架构入口路由为准 |
| 产品、角色、IA 或 UX 设计 | 对应 `*-module-product-design.md` 与产品文档 | 不从项目状态页推断产品语义 |
| 测试、脚本或协作治理 | 对应 governance 文档 | 只加载与任务直接相关的规则 |
| 历史计划或规格核对 | `superpowers/`、[`status/archive/`](status/archive/) | 历史记录不自动构成当前执行入口 |

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

## 状态与历史

- 当前全仓级状态：[`status/current.md`](status/current.md)。
- `architecture/implementation-readiness.md`：旧链接兼容入口，不再承载状态或裁决。
- readiness 冻结快照：[`status/archive/implementation-readiness-2026-08-26.md`](status/archive/implementation-readiness-2026-08-26.md)。
- 2026 年 5 月非实时看板：[`status/archive/project-status-dashboard-2026-05-26.html`](status/archive/project-status-dashboard-2026-05-26.html)。

## 权威来源纪律

1. 当前命令、版本、目录、生成入口和实现行为优先以代码、配置、脚本、帮助输出和测试为准。
2. 当前项目进度、负责人、阻塞细节和验收证据留在 GitHub/Linear。
3. 同一事实只维护一个权威住所；其它文档使用链接和简短上下文，不复制正文。
4. 历史快照可以保留当时判断，但不得冒充当前入口。
5. 新增文档前先判断其变更频率；无法明确类型时，先在 Issue/PR 中完成分类裁决。
