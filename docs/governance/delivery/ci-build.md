# CI 与构建治理

本文定义当前 CI/构建的稳定规则，不复制 workflow 中易漂移的 job 名称、timeout、runner、版本或命令明细。

## 权威 producer

- Workflow 与 job/step：`.github/workflows/ci.yml`。
- 后端 fast shard 分类：`scripts/backend-test-shards.json`。
- 分片执行器：`scripts/run-backend-test-shard.ps1`。
- 分片契约校验：`scripts/verify-backend-test-shards.ps1` 与 `scripts/tests/backend-test-shards.Tests.ps1`。
- solution configuration 契约：`scripts/verify-solution-configuration-membership.ps1` 与对应测试。
- CI impact/required-check 关系：现有 workflow 与其契约测试/脚本。

需要知道“现在到底有几个 job、每片有哪些项目、用什么参数”时必须直接读取这些 producer，不在 Governance 维护副本。

## 后端构建规则

1. Fast backend tests 继续按 `scripts/backend-test-shards.json` 的分片定义，各分片通过受治理执行器对自己的 solution filter 执行 restore/build/test 生命周期。
2. 不把整个 `backend/Nerv.IIP.sln` 的一次全量构建提升为所有 fast shard 的共享前置产物，也不为此引入跨 job 的 build artifact 分发。若要推翻该规则，必须基于新的、可复现的托管 CI 测量重新裁决。
3. connector-hosts、OpenAPI drift、ERP 等专项 lane 的构建范围和命令由它们当前 workflow/runner producer 决定；不得仅凭历史耗时报告统一改成全解决方案构建或共享产物模式。
4. 分片边界、solution configuration membership、excluded test/lane 与 evidence lane 的变更必须同时更新现有机器契约；不新增第二套手工 shard 表。
5. CI 注释和测试注释只能保留稳定结论并指向本规则或冻结报告，不复制时点耗时、run ID、artifact 大小等审计数据。

## 变更纪律

- 改 CI 行为时先改/核对真正 workflow、runner、manifest 与契约测试，再更新本页。
- 只改文档分类时不得顺手调整 cache key、job topology、测试命令、timeout、runner 或 required-check 语义。
- 不为文档分类新增永久 registry、自然语言 scanner、生成器或独立 CI step。
- 受影响的既有 Script Governance、workflow contract、shard/solution contract 与 CI Summary 必须保持通过；具体执行集合由当前 impact producer 决定。

## 历史依据

MAN-669 对“每片精确构建”与“一次全量构建后共享产物”做过托管 runner A/B 测量，并对 connector-hosts、OpenAPI drift、ERP 专项构建范围做过复核。运行 ID、耗时、artifact 体积、探针方法和重新开启条件全部冻结在 [`../../reports/audits/backend-ci-build-strategy-man-669.md`](../../reports/audits/backend-ci-build-strategy-man-669.md)；这些数字不构成今天的实时 CI 状态。
