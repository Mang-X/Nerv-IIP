# ADR 0028：退役纵切脚本与当前基础设施验证入口

- 状态：已接受
- 日期：2026-08-25

## 背景

早期第一至第四阶段纵切使用 `scripts/verify-first-slice.ps1`、`scripts/verify-second-slice-ops.ps1`、`scripts/verify-third-slice-console.ps1` 和 `scripts/verify-fourth-slice-real-infra.ps1` 验证阶段性链路。#2157 将这些脚本退役为保留原路径的无副作用、明确失败兼容墓碑，但 ADR 0009 的历史后果第 2 条仍保留了第四阶段脚本可作为本地门禁的当时表述。

## 决策

1. 四个纵切脚本自 #2157 起不是当前本地、provider 或 FullChain 门禁；它们只保留路径并明确失败，以阻止旧入口被静默重新使用。
2. 当前本地开发与全栈生命周期使用 `nerv.ps1 dev`、`nerv.ps1 fullstack run` 及对应的 stop/status/wait/logs/describe 入口。
3. 当前 OpenAPI/api-client 漂移使用 `scripts/verify-openapi-client-drift.ps1`；真实 provider 证明使用专用 CI lane；具体当前入口以架构文档和 readiness 台账为准。
4. `docs/superpowers/plans/` 与 `docs/superpowers/specs/` 中既有计划和规格是历史设计记录，按 superpowers 规划治理不得原地改写；它们不构成当前可执行门禁登记。当前状态以本 ADR、`docs/architecture/implementation-readiness.md` 和脚本治理矩阵为准。

## 理由

退役脚本无条件失败，继续把它们列为当前门禁会产生可直接复现的文档-入口矛盾。保留路径和失败诊断可以让旧调用快速暴露，同时不扩大本 PR 到真实测试、业务代码或当前 provider lane。

## 实施说明

1. #2157 保留四个脚本路径，声明 `SideEffects: None`、`Writes: None`，并检查非零退出及 `retired under #2157` 诊断。
2. 当前入口由 `docs/architecture/fourth-vertical-slice-real-infra.md`、`docs/architecture/implementation-readiness.md`、`docs/architecture/script-automation-governance.md` 和 `docs/architecture/api-contract-and-codegen.md` 承接。
3. ADR 0009 通过头部修订依据和实施说明链接本 ADR；其原后果第 2 条文本不改写。

## 已考虑的替代方案

1. 继续把退役脚本作为当前本地门禁：否决，因为脚本会明确失败且当前基础设施验证已有专用入口。
2. 删除四个脚本路径：否决，因为本批规格要求先保留路径并让旧调用明确失败。
3. 原地改写 ADR 0009 的后果第 2 条：否决，因为会丢失当时决策记录；改用本 ADR 部分取代。

## 后果

1. 复制历史计划中的旧命令不会被视为当前门禁；执行会得到明确退役诊断。
2. 当前验证入口需要按对应架构文档选择，PR/CI hosted 证据仍必须绑定 exact head，skipped 不计 passed。
3. 后续删除墓碑时，必须同步移除治理测试和历史入口索引，不在本 PR 扩大范围。

## 范围之外

1. 本 ADR 不改变业务代码、当前 CI workflow、provider 实现或 FullChain 选择逻辑。
2. 本 ADR 不把任何 hosted skipped lane、旧 run 或 merge-SHA main 状态宣称为通过。
