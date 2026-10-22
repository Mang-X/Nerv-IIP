# 测试确定性治理演进审计（冻结）

本报告冻结 M2-H 前 `backend-test-determinism.md` 中的修复批次、实测与形成过程。当前规则见 [`../../governance/testing/determinism.md`](../../governance/testing/determinism.md)。

## 冻结演进

- MAN-662 将测试时间、等待、超时、网络诊断和全局状态隔离从散落习惯收敛为共享测试原语和 baseline/checker。
- #1470 分批清偿固定 `Task.Delay`：领域时间转 `TimeProvider`，真实依赖可见性转 bounded observation，并发边沿转显式 gate/probe。
- #1471 将裸 static/global state 修改迁入受治理作用域，并区分 expiring debt 与机制/自测 permanent 位点。
- #1482 等后续走查补强 observation resource lifetime、诊断降级和异常路径；#1491/MAN-808 收敛假时钟 timer-registration edge 与锚定起点问题。
- 后续 CAP teardown、PostgreSQL advisory-lock probe 等回归进一步证明“实际可观察边沿优先于 sleep”的原则。

## 历史来源

完整 pre-M2-H 总账冻结于：

`6e8747a8f93a6398c45c8eb2f2a33ad3a7b64019:docs/architecture/backend-test-determinism.md`

旧运行目录、通过数量、耗时和当时 baseline 状态不属于当前 Governance。
