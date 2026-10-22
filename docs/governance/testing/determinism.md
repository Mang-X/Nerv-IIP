# 测试确定性与隔离治理

本文定义后端与共享测试基建当前必须遵守的时间、等待、超时、网络诊断、可变全局状态和资源生命周期规则。当前扫描面、基线债务与 helper API 以 `backend/test-determinism-baseline.json`、`scripts/check-backend-test-determinism.ps1`、`scripts/verify-backend-test-determinism.ps1`、`backend/common/Nerv.IIP.Testing/**` 及其测试为准；本文不维护已清偿位点或逐批修复历史。

## 时间与等待

| 被测行为 | 当前规则 |
| --- | --- |
| 调度、租约、过期、续期、保留期 | 注入 `TimeProvider`，测试使用可控时钟推进；不等待真实时间证明领域时间行为。 |
| 真实 transport、容器、进程、外部消费者可见性 | 使用真实时钟 + `Eventually` 有界轮询可观察事实；禁止固定 sleep 猜完成时刻。 |
| 单次可能挂起的异步操作 | 使用 `TestTimeout.RunAsync` 或当前共享超时原语，并区分 helper 自身 timeout 与 caller cancellation。 |
| 负向稳定窗口 | 只有被测对象没有更强可观察边沿时才使用 `Consistently.StaysAsync`；窗口内持续观察，不能“睡一次再断言一次”。 |

### 假时钟推进屏障

推进 `FakeTimeProvider` 前必须先确认目标 timer 已经注册。`Task.Yield()`、`StartAsync()` 返回、业务结果出现等都不天然证明 timer registration 已发生。优先等待当前测试基建暴露的“timer registered”边沿，再 `Advance`；若一个计数型观测器被多个注册方共享，调用点必须有可执行不变量证明计数仍代表目标组件。

## 有界观测与资源生命周期

- `observe` 每次返回稳定、可读、脱敏的值快照；不要把随后会被释放或并发修改的活对象交给诊断描述。
- 每次观测需要数据库连接、command、stream 等资源时，观测本身拥有并释放资源，避免窗口结束后被遗留任务继续使用外层已释放对象。
- 超时/违例必须保留 condition/operation、尝试次数、已用时间和最后一次脱敏观测。
- 诊断描述本身失败时不得覆盖原始 timeout/violation 结论；具体降级语义由共享 helper 和测试定义。

## Cancellation 与网络失败

调用方取消和 helper 自己放弃必须分开：调用方 token 已取消时原样传播；helper 自身超时才归类为 timeout。取消语义优先于异常内部偶然出现的 socket 错误码，不能用内层传输细节覆盖“谁拥有这次放弃”。

HTTP、Npgsql 和裸 socket 的测试诊断共享稳定的失败类别，但生产程序集不得为了复用测试 helper 引用测试程序集；必要的边界重复由镜像行为测试保持一致。连接预算与请求预算分开配置，阶段信息属于诊断，不因此无限扩充稳定失败枚举。

## 可变全局状态

Culture/UI culture、环境变量、全局 resolver 等可变进程状态只能通过当前 `GlobalTestStateScope` 或等价受治理作用域修改：

1. 进入时串行化共享状态变异；
2. 捕获“不存在 / 空字符串 / 有值”等原始状态；
3. `DisposeAsync` 恢复并释放作用域；
4. 不在测试体散落手工 preserve/restore 或裸 static setter；
5. 机制自身的必要写入与自测可由基线按当前 schema 精确分类，不为它们编造到期日期。

## Determinism baseline

基线只登记机器扫描当前无法直接消除、但仍需要治理的发现。分类、owner、到期与永久机制语义由当前 baseline schema/checker 定义；“某类当前为 0”不是永久豁免，也不应写成 Governance 状态。

- expiring debt 必须有独立 owner 与到期约束；
- permanent 只用于机制实现/自测等结构上不会通过后续业务重构消失的位点；
- checker 通过只证明代码与基线闭合，不证明历史债务是否曾存在，也不替代实际测试运行。

操作与排障见 [`../../runbooks/testing/determinism.md`](../../runbooks/testing/determinism.md)，历史清偿与走查见 [`../../reports/audits/test-determinism-governance-evolution-2026-08.md`](../../reports/audits/test-determinism-governance-evolution-2026-08.md)。
