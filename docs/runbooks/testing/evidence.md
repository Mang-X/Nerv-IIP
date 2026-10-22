# 测试证据操作 Runbook

规则见 [`../../governance/testing/evidence.md`](../../governance/testing/evidence.md)。本页只描述操作入口与判读顺序，不复制当前 lane 成员数或 CI timeout 数字。

## 本地契约验证

优先运行与改动直接相关的现有脚本测试，例如 TestEvidence、backend shard、provider/lane 或 Script Governance 契约；具体入口从 `scripts/tests/` 与 `nerv.ps1 help` 查询。修改 TestEvidence producer 时至少验证其 focused contract，再由 CI impact plan 决定更广 lane。

## CI 判读

1. 先锁定 exact head SHA 和对应 workflow run。
2. 检查 required `CI Summary`，但不要只停在汇总层。
3. 对本票声明的 lane 查看对应 job/step：实际执行成功才记录 `actual success`；policy skip 明确写 `skipped by policy`。
4. formal evidence 要核对 tested SHA、run/attempt、lane/job provenance、执行数与 collection/cleanup 状态。
5. retained artifact 不含完整失败正文时，到受访问控制的 Actions job log 查原始失败；不要通过放宽 redaction 修复排障体验。
6. PR head/base 变化后，旧 exact-head run 只保留历史参考，不能继续作为合并证据。

## 基线与 timing

TestEvidence baseline 的生成命令与参数以 `scripts/generate-test-evidence-baseline.ps1 -?`/源码为准；只有治理语义变化才有意更新受治理 baseline。backend shard timing 由现有 timing producer 自动刷新或降级估值，它是 report-only 缓存，不要求人工提交刷新。
