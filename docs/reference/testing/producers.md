# 测试治理 Producer 导航

| 事实 | 当前 producer |
| --- | --- |
| evidence policy / skip / quarantine | `scripts/test-evidence-policy.json` + `scripts/lib/TestEvidencePolicy.ps1` |
| retained evidence schema、privacy、provenance | `scripts/lib/TestEvidence*.ps1`、`scripts/collect-test-evidence.ps1`、`scripts/tests/test-evidence.Tests.ps1` |
| evidence baseline | `scripts/test-evidence-baseline.json` + `scripts/generate-test-evidence-baseline.ps1` |
| backend fast shards / excluded real-dependency selectors | `scripts/backend-test-shards.json`、`scripts/run-backend-test-shard.ps1`、`scripts/verify-backend-test-shards.ps1` |
| PostgreSQL lane | `scripts/postgres-test-lane.json` + 对应 runner/verifier |
| Redis/CAP lane | 当前 redis-cap policy/runner、`.github/workflows/ci.yml` |
| FullChain scenarios | `scripts/acceptance-scenario-matrix.json`、`scripts/full-chain-test-lane.json`、当前 runner/verifier |
| CI 物理 job / aggregate / timeout | `.github/workflows/ci.yml` + CI contract tests |
| backend determinism | `backend/test-determinism-baseline.json`、determinism checker/verifier、`Nerv.IIP.Testing` 与对应测试 |
| shard timing cache | `scripts/update-backend-test-shard-timings.ps1`、`scripts/report-backend-test-shard-balance.ps1`、`scripts/lib/BackendTestShardTimings.ps1` |

本表只导航稳定 producer 名称；具体成员、数量、状态、版本和运行结果必须从 producer/实际 run 读取。
