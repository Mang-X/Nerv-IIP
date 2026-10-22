# 脚本受信号终止与内存证据调查（冻结）

> 类型：历史 investigation
>
> 冻结基线：`main@26e88a62e2223ba7da2443c6471b34d971d4ad28`
>
> 原始混合文档：`docs/architecture/script-automation-governance.md`

本报告冻结 M2-G 拆分前已经记录在脚本治理大文档中的 signal / memory 事故背景。它只说明当时为什么补充了诊断能力，不定义当前脚本入口、当前 CI 状态或事故根因。

## 观察到的问题

历史 FullChain `man-440` 运行中，内层 `dotnet test` 曾被 SIGKILL 终止，进程退出码为 `137`（`128 + 9`）。当时外层只报告类似 `Command 'pwsh' exited with 1` 的包装失败，导致读者容易把“内层进程被信号终止”误判为场景断言失败、普通测试失败或偶发环境抖动。

这个缺口推动了两类诊断能力：

1. **信号退出继承**：受管命令把 signal/exit code 渲染成上层可识别的 `NERV-SIGNAL-EXIT` 单行标记；上层若捕获到该标记，应把内层 signal 结论保留在自己的失败信息中。
2. **FullChain 内存维度证据**：在场景 entrypoint 前后采集资源快照，失败时追加尽可能多的内存/OOM 诊断，帮助区分“进程被 kill”与“为什么被 kill”。

## 当时登记的内存证据维度

M2-G 前文档记录的 FullChain 证据包括：

- `/proc/meminfo`；
- cgroup v2 `memory.current`、`memory.max`、`memory.peak`、`memory.events`；
- `/proc/vmstat` 的全局 `oom_kill` 计数；
- 场景失败时 best-effort 获取内核 OOM 相关信息；
- entrypoint 前后快照差值，而不是只读绝对累计值。

该设计特别强调：hosted runner 的 cgroup 与宿主全局 OOM 证据不是一回事，不能因为 cgroup `memory.events.oom_kill` 为 0 就排除宿主层 OOM；同样，某个计数增长也必须结合本次运行的时间窗和其它证据解释。

## 证据采集不能改变 verdict

历史治理明确要求资源采证 **best-effort**：读取不到某个 `/proc`、cgroup 或内核信息时，记录 unavailable/原因，但不能把原本成功的被测 lane 弄失败，也不能用采证错误覆盖原始失败。

快照位置应贴近被测 entrypoint 前后，避免只在 lane 首尾采样后把场景峰值与其它初始化/清理活动混在一起。

## 证明范围

上述改动证明的是：

- 后续 signal death 可以被更准确地识别和跨包装层传播；
- FullChain 可以保留更丰富的内存/OOM 诊断维度。

**它不证明 #1664 的根因已经被确认是 OOM，也不证明该根因已经被修复。** SIGKILL 是终止方式，不是根因；memory/OOM evidence 是调查输入，不是自动归因器。

## 当前事实如何核实

不要从本报告复制命令或实现细节。当前行为应回到：

- `scripts/lib/ScriptAutomation.ps1`；
- 当前 FullChain lane/evidence producer；
- `scripts/tests/script-automation-signal-exit.Tests.ps1`；
- 当前 memory-evidence 合同测试；
- [`../../governance/script-automation.md`](../../governance/script-automation.md)；
- [`../../runbooks/script-automation.md`](../../runbooks/script-automation.md)。

完整 M2-G 前叙事可从 Git `26e88a62e2223ba7da2443c6471b34d971d4ad28:docs/architecture/script-automation-governance.md` 追溯。本报告完成后冻结。