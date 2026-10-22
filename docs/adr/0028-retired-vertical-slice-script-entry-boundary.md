# ADR 0028：退役纵切脚本兼容墓碑边界

- 状态：已接受
- 日期：2026-08-25

## 背景

早期第一至第四阶段纵切使用 `scripts/verify-first-slice.ps1`、`scripts/verify-second-slice-ops.ps1`、`scripts/verify-third-slice-console.ps1` 和 `scripts/verify-fourth-slice-real-infra.ps1` 验证阶段性链路。#2157 将这些路径退役为无副作用、明确失败的兼容墓碑，但 ADR 0009 的历史后果第 2 条仍保留了第四阶段脚本可作为本地门禁的当时表述。

本记录只裁决退役路径能否继续充当当前门禁、为何暂时保留兼容墓碑，以及它对 ADR 0009 的精确修订范围。当前可执行命令、验证 lane、交付状态和 CI 证据会持续变化，不属于决策记录。

## 决策

1. 四个纵切脚本不是当前本地、provider 或 FullChain 门禁；在物理删除前，它们只作为保留旧路径并明确失败的兼容墓碑，防止历史入口被静默重新使用。
2. ADR 不维护替代命令或参数。当前操作者必须从 [`../runbooks/script-automation.md`](../runbooks/script-automation.md)、`nerv.ps1 help`、目标脚本 `Get-Help` 和当前源码选择入口，不能从本记录或历史计划复制命令。
3. 当前 OpenAPI、provider、FullChain 与其它验证 lane 的选择、运行条件和证据要求，由当前脚本、CI 配置、Governance 与 Runbook 决定；本 ADR 不把任何具体 lane 或命令冻结为长期契约。
4. `docs/superpowers/plans/` 与 `docs/superpowers/specs/` 中既有计划和规格是冻结的历史设计记录，不构成当前可执行门禁登记。
5. 本记录仅部分取代 ADR 0009 后果第 2 条中“第四阶段真实依赖纵切脚本可作为本地门禁”的表述；ADR 0009 的迁移、发布、seed、回滚与真实 provider 证明原则仍有效。

## 理由

退役脚本无条件失败，继续把它们列为当前门禁会形成可直接复现的文档与入口矛盾。保留旧路径并给出失败诊断，可以让旧自动化、历史文档或人工调用快速暴露过期依赖，而不是悄悄执行一条已失去证明范围的路径。

当前命令和验证 lane 由实现与运行环境决定，变化频率高于架构决策。把这些信息留在 ADR 会迫使团队通过“修订历史”维护日常操作，最终同时产生过期 ADR 和第二份 Runbook。因此，本记录只保留墓碑边界与取代范围。

## 实施说明

1. 墓碑路径的当前分类、静态约束和允许行为以 [`../governance/script-automation.md`](../governance/script-automation.md)、脚本源码及治理测试为准。
2. 当前操作入口与排障流程以 [`../runbooks/script-automation.md`](../runbooks/script-automation.md) 和脚本帮助为准。
3. 当前项目重点、阻塞和全仓级入口见 [`../status/current.md`](../status/current.md)；墓碑是否仍存在、何时删除以及 exact-head CI 证据以关联 Issue、PR 和代码为准。
4. ADR 0009 通过头部修订依据和实施说明链接本记录；其原后果第 2 条文本不原地改写。

## 已考虑的替代方案

1. **继续把退役脚本作为当前本地门禁。** 否决。脚本会明确失败，且其历史阶段证明范围不能代表当前平台、provider 或 FullChain 验证。
2. **在本 ADR 中维护当前替代命令。** 否决。命令、参数和 lane 会随脚本与 CI 演进，应由 Runbook、帮助输出和当前 producer 维护；复制到 ADR 会形成第二份易漂移操作手册。
3. **立即删除四个脚本路径。** 暂不采用。保留墓碑能让仍依赖旧路径的消费者明确失败并得到诊断；物理删除需要先证明活跃消费者与治理测试已经迁移。
4. **原地改写 ADR 0009 的历史后果。** 否决。那会丢失当时裁决；本记录通过部分取代保留历史并给出新边界。

## 后果

1. 历史计划中的旧命令不会被误认为当前门禁；在墓碑仍存在时，调用会明确失败。
2. 操作者必须从当前 Runbook、脚本帮助、代码和 CI 配置选择验证入口，不能把 ADR 当命令目录。
3. 墓碑需要继续保持无副作用、明确失败，直到独立变更完成消费者清查、治理测试迁移和物理删除。
4. 项目状态与 CI 证据只在 Status、Issue、PR 和运行结果中维护，本 ADR 不随每次交付推进追加状态日志。

## 复评触发条件

当四个墓碑路径准备物理删除，或需要改变“保留路径并明确失败”的兼容策略时，必须复评本记录；仅替换当前命令、参数或 CI lane 不触发 ADR 修订。

## 范围之外

1. 本 ADR 不改变业务代码、CI workflow、provider 实现或 FullChain 选择逻辑。
2. 本 ADR 不声明任何具体 hosted lane、历史 run、PR head 或 main 状态已经通过。
