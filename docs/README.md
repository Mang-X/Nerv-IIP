# Nerv-IIP 文档入口

本页只负责按任务路由文档，不复制项目状态、架构正文或操作命令。先读取目标路径适用的 Agent 指令，再选择必要文档。

## 按任务选择

| 任务 | 首要入口 | 说明 |
| --- | --- | --- |
| 普通局部实现、修复、测试或重构 | 当前 Issue/spec、目标代码、配置与测试 | 不读取全局状态或历史快照 |
| 发布、里程碑规划或跨域能力盘点 | [`status/current.md`](status/current.md) | 只含全仓级摘要；细节回到 GitHub/Linear |
| 服务边界、目录、数据所有权、跨域调用或公开契约 | [`architecture/README.md`](architecture/README.md) | 再按主题读取相关当前架构文档 |
| 当前工程规则、授权/Schema/文档/后端/前端/错误/持久化治理 | [`governance/README.md`](governance/README.md) | Governance 只写现态约束，不保存事故史或时点状态 |
| 查询 Schema、码表、矩阵、权限目录、术语或技术资料 | [`reference/README.md`](reference/README.md) | Reference 只做人工索引；精确事实回到 producer |
| 引入、推翻或复评长期决策 | [`adr/README.md`](adr/README.md) | ADR 解释为什么选择；当前实现回到 Architecture / Governance / Runbook |
| 本地启动、部署、迁移、恢复或排障 | [`runbooks/README.md`](runbooks/README.md) | Runbook 只维护当前可执行操作，精确命令回到脚本/CLI help |
| 产品、角色、IA 或 UX 设计 | [`product/README.md`](product/README.md) | Product 只维护当前产品语义，不从状态页推断产品事实 |
| 调查、实验、审计或修复历史 | [`reports/README.md`](reports/README.md) | 报告只证明声明的时点与范围，不构成当前规则 |
| 历史阶段、计划或规格核对 | `superpowers/`、[`status/archive/`](status/archive/) | 历史记录不自动构成当前执行入口 |

## 文档类型

| 类型 | 回答的问题 | 更新方式 |
| --- | --- | --- |
| ADR | 为什么在多个可行方案中选择了这个？ | 历史不就地改写；变化时新 ADR 取代或部分修订 |
| Architecture | 系统现在是什么样，边界和交互是什么？ | 随当前系统原地更新 |
| Governance | 当前工程工作必须遵守什么规则？ | 只写现态、适用范围与例外原则，不保存逐轮事故故事 |
| Runbook | 如何启动、部署、迁移、恢复或排障？ | 随当前操作入口更新 |
| Reference | 当前有哪些字段、Schema、路由、矩阵、权限导航、码表、术语和资料入口？ | 随真正 producer 更新；人工解释不得覆盖 producer |
| Status | 当前重点、阻塞和全仓级入口是什么？ | 高频替换，不累积已完成历史 |
| Report | 某次调查、实验、审计或修复发现了什么？ | 完成后冻结；当前事实回到代码与现态文档 |
| Product | 给谁使用、解决什么问题、业务与 UX 如何工作？ | 随产品裁决更新 |

## 当前入口与历史

- 当前全仓级状态：[`status/current.md`](status/current.md)。
- 当前 Architecture 入口：[`architecture/README.md`](architecture/README.md)。
- 当前 Governance 入口：[`governance/README.md`](governance/README.md)。
- 当前 Product 入口：[`product/README.md`](product/README.md)。
- 当前 Runbook 入口：[`runbooks/README.md`](runbooks/README.md)。
- 当前 Reference 入口：[`reference/README.md`](reference/README.md)。
- 冻结报告入口：[`reports/README.md`](reports/README.md)。
- 阶段与状态快照：[`status/archive/README.md`](status/archive/README.md)。
- `architecture/implementation-readiness.md`：旧链接兼容入口，不再承载状态或裁决。

## 权威来源纪律

1. 当前命令、版本、目录、生成入口和实现行为优先以代码、配置、脚本、帮助输出和测试为准。
2. 当前项目进度、负责人、阻塞细节和验收证据留在 GitHub/Linear。
3. Governance 规定约束，不复制精确运行时账本；Reference 中的 Schema、码表、矩阵和权限导航必须标明 producer。
4. 同一事实只维护一个权威住所；其它文档使用链接和简短上下文，不复制正文。
5. 历史快照和报告可以保留当时判断，但不得冒充当前入口。
6. 新增文档前先判断其变更频率；无法明确类型时，先在 Issue/PR 中完成分类裁决。
