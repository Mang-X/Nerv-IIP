# Testing Governance 入口

本目录承载测试体系**当前必须遵守的规则**。规则按“语义有效性、确定性、证据、真实依赖、PDA 测试边界”拆分；运行命令进入 `docs/runbooks/testing/`，易漂移的清单与 producer 导航进入 `docs/reference/testing/`，历史修复与时点运行证据进入 `docs/reports/audits/`。

| 主题 | 当前权威 |
| --- | --- |
| 断言来源与证明范围 | [`validity.md`](validity.md) |
| 等待、时间、隔离与确定性 | [`determinism.md`](determinism.md) |
| CI/TRX、skip、zero-execution 与 retained evidence | [`evidence.md`](evidence.md) |
| PostgreSQL / Redis-CAP / FullChain lane 边界 | [`real-dependency-lanes.md`](real-dependency-lanes.md) |
| business-pda 自动化、live、模拟器与真机证明边界 | [`mobile-pda.md`](mobile-pda.md) |

## 权威顺序

1. 业务/公共合同的期望语义先回到 ADR、公开契约、领域规则或已确认回归样本。
2. 实际测试身份、lane、manifest、CI 接线、脚本参数和运行结果以代码、配置、workflow 与机器可读 producer 为准。
3. 本目录只定义稳定规则和解释边界，不复制动态成员数、用例数、run ID、某次通过状态或逐轮修复账本。
4. 同一规则只在一个 Governance 页面定义；其它页面使用链接，不改写近似版本。
5. fixture/local、PR exact-head、merge-SHA main、nightly 与真机 smoke 是不同证据边界，不能相互替代。
