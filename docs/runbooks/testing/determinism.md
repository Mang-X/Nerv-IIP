# 测试确定性操作 Runbook

规则见 [`../../governance/testing/determinism.md`](../../governance/testing/determinism.md)。

## 校验入口

- 先运行当前 backend determinism checker；需要验证 baseline/schema/扫描器自身时再运行 verifier 与对应契约测试。
- 具体参数、扫描范围和退出码以 `scripts/check-backend-test-determinism.ps1`、`scripts/verify-backend-test-determinism.ps1` 和脚本帮助为准。
- 不通过手改 baseline 把新发现变绿；先判断是应消除的真实债务、需要独立 owner 的 expiring debt，还是机制/自测的 permanent 位点。

## 常见定位顺序

1. 时间类失败先确认被测行为是否应该使用注入时钟；真实依赖可见性再看 `Eventually`/bounded observation。
2. 假时钟永久等待先确认 timer-registration edge，而不是增加 `Task.Yield` 或 sleep。
3. timeout 先区分 caller cancellation 与 helper-owned timeout，再看内层 socket/transport 信息。
4. 共享状态污染先查 Culture、环境变量、静态 resolver 与 `GlobalTestStateScope` 生命周期。
5. 观测窗口 teardown race 先确认每次 observe 是否自持资源，外层对象是否在晚到观测结束前被释放。
6. 修复后运行 focused tests，再按 CI impact plan 扩大验证面；不要用重复运行“碰绿”替代结构修复。
