# 冻结报告入口

本目录保存已经完成的调查、实验、审计与修复记录。报告用于回答“当时在什么基线、证据和证明范围下得出了什么结论”，不定义当前 Architecture、Governance、项目状态或发布就绪程度。

## 类型

- `investigations/`：事故、故障和兼容问题调查。
- `spikes/`：时间盒实验与可行性预研。
- `audits/`：时点盘点、交付审计和证据核对；M2-H 的 TestEvidence、determinism、真实依赖 lane 与 PDA 测试形成史也冻结于此；M2-J 的 MAN-669 CI 构建实测与迁移前部署基线审计也从此进入。
- `remediation/`：一次性数据或实现修复过程及证据。

部分包含大量相对源码链接的冻结报告暂保留在 `docs/reports/` 根目录，以维持迁移前相同的链接深度和原始正文；这不是新的分类例外或项目状态总账。根目录中的短兼容指针只用于保持这些冻结报告原有的同目录链接可访问。

## 治理文档迁移追溯

已退役的 Architecture 治理入口不再恢复为当前规则或机器登记表。冻结记录中的旧路径文字按记录时点的 Git 树追溯：

| 主题 | 冻结记录 | 迁移前原文 |
| --- | --- | --- |
| 测试证据 | [演进审计](audits/test-evidence-governance-evolution-2026-08.md) | [M2-H 前正文](https://github.com/Mang-X/Nerv-IIP/blob/6e8747a8f93a6398c45c8eb2f2a33ad3a7b64019/docs/architecture/test-evidence-governance.md) |
| 脚本自动化 | [演进审计](audits/script-automation-governance-evolution-2026-08.md) / [signal 与 memory 调查](investigations/script-automation-signal-memory-2026-08.md) | [M2-G 前正文](https://github.com/Mang-X/Nerv-IIP/blob/26e88a62e2223ba7da2443c6471b34d971d4ad28/docs/architecture/script-automation-governance.md) |

当前规则分别从[测试证据治理](../governance/testing/evidence.md)和[脚本自动化治理](../governance/script-automation.md)进入；历史文字、旧命令和历史通过结果不能替代这些当前入口及其生产者。

## 使用规则

1. 报告中的提交、运行 ID、版本、数量和通过结果只对报告声明的基线与范围成立。
2. 当前实现事实回到代码、配置、公开契约、测试和当前 Architecture / Governance / Runbook。
3. 报告完成后正文冻结；需要补充新结论时创建新的日期化报告或在当前文档中说明，不把旧报告改写成现态规则。
4. 报告内的复现命令可能已经退役；执行前必须从当前脚本帮助、Runbook 和 CI 配置重新核实。
5. 当前项目优先级、负责人、阻塞和验收进度留在 GitHub/Linear。
6. 不为报告目录建立生成器、永久分类 manifest、自然语言 scanner 或独立 CI step。

M2 迁移期的兼容页和根目录链接指针最终删除条件由 M2-M 汇总后交给 M4；不得在兼容页中重新复制报告正文。
