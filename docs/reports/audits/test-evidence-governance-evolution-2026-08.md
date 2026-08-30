# TestEvidence 治理演进审计（冻结）

本报告冻结 M2-H 前 `test-evidence-governance.md` 中的历史形成过程，不是当前测试证据规范。当前规则见 [`../../governance/testing/evidence.md`](../../governance/testing/evidence.md)。

## 冻结结论

- MAN-661 建立仓库自有 VSTest/TRX retained evidence、skip/quarantine/zero-execution 与 provenance 语义。
- MAN-669 将 backend 快速测试拆为多个物理 shard 后，evidence owner 随物理执行者变化；稳定 aggregate 不执行测试。
- 后续 FullChain 接线明确 planning、v1 authority、shadow、equivalence 与 stable aggregate 是不同责任，正式 evidence 只由实际 worker 产生。
- #1507 清除了“把测试耗时 snapshot 当治理资产”的错误边界：timing 变成可自动刷新/降级的 report-only cache，policy 身份仍失败关闭。
- 多轮 ordinal、privacy、baseline schema、CI budget 与 rerun authority 走查通过机器契约收口；当前覆盖面必须回到 producer/tests，而不是引用本报告中的旧计数。

## 历史来源

M2-H 拆分前完整正文冻结于 Git：

`6e8747a8f93a6398c45c8eb2f2a33ad3a7b64019:docs/architecture/test-evidence-governance.md`

其中出现的 run ID、SHA、成员数和时点通过结果只对当时基线成立，不得用于证明当前 PR/main。
