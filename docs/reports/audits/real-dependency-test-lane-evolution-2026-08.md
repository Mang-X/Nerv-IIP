# 真实依赖测试 Lane 演进审计（冻结）

当前规则见 [`../../governance/testing/real-dependency-lanes.md`](../../governance/testing/real-dependency-lanes.md)。本报告只冻结 M2-H 前 lane 架构形成与 CI 接线证据。

- NERV-688/#1256 冻结 `postgres`、`redis-cap`、`full-chain` 三条稳定 lane，并要求按“证明什么”唯一归属。
- NERV-673/后续工作把 FullChain 从单一路径发展为 planning → v1/shadow → equivalence → stable aggregate，并保持实际 v1 worker 的 evidence ownership。
- PR exact-head 成功与 merge-SHA main 成功被明确区分；历史上曾出现 exact-head 绿而 merge-SHA main 新失败的案例，这形成当前“两阶段证据不能替代”的规则。
- main/nightly/workflow_dispatch 的覆盖集合持续由 machine-readable manifests 与 workflow 演进；本报告不冻结当前成员数。

完整 pre-M2-H 正文：

`6e8747a8f93a6398c45c8eb2f2a33ad3a7b64019:docs/architecture/real-dependency-test-lanes.md`
