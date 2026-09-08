# 真实依赖测试操作 Runbook

规则见 [`../../governance/testing/real-dependency-lanes.md`](../../governance/testing/real-dependency-lanes.md)。当前 lane/member/scenario 参数以 `scripts/*-test-lane.json`、acceptance matrix、runner 脚本帮助与 workflow 为准。

## 本地前置

- PostgreSQL 基础变量指向受治理的管理入口；每个测试/runner 只创建并删除自己拥有的临时数据库/schema。
- Redis/CAP 使用当前 session/run 唯一 namespace/version；不要 `FLUSHALL`、删除未知 key 或抢占其它 worktree consumer group。
- 多 worktree 共享本地依赖时，不停止共享服务来“清理”一个会话；只回收本次 invocation 明确拥有的资源。

## 执行

1. 从 manifest/runner help 确认目标 lane/member/scenario 与所需环境变量。
2. 先做 protocol/readiness probe；依赖不可达时停止并修复环境，不把被选中真实依赖测试改成 skip。
3. 使用现有 runner 执行，保留自然退出码。
4. 失败时先采集脱敏的数据库/Redis/CAP/业务状态和 cleanup 证据，再精确回收当前 run 的数据库、namespace 和子进程。
5. runner 中断后的残留只用当前受治理 cleanup 脚本预览/清理；具体命名、最小年龄和安全条件以脚本帮助为准。

## 判读

只有实际 job/lane 的 passed/failed 运行时结果能证明该真实依赖执行过；合同测试、planning、stable aggregate 或另一个 provider 的成功都不能替代。

## Redis/CAP hosted 观测

现有 Redis/CAP job 在原 `-AllActiveMembers` 窗口运行 `scripts/observe-redis-cap-lane.ps1`，复用 dependency summary artifact 的 `observation/` 子目录。只适用于该 job 的 Linux 进程树及 loopback Redis 服务；本地容器不是相同 hosted 环境。关闭采集使用脚本 `-Disabled`，不会改变 lane 的成员、顺序、断言或测试预算。

先读 `status.json` 和原 `summary.json`，分别判断采集与测试结果。`observations.jsonl` 的 testhost 记录由正式 `dotnet test` 的祖先链、manifest project/filter 和精确结果目录共同关联；discovery 不参与。`processStartTicks` 是 Linux `/proc` 启动身份，`utc` 是观察时刻，不是 CLR 启动或订阅时刻。`fixture-phases.jsonl` 是从原 TRX 白名单提取的目标 wrapper 阶段，以 PID/UTC 与 CLR 曲线关联，不能代表全部 CAP listener。

| 产物/读数 | 语义与单位 |
| --- | --- |
| `clr-<PID>.csv` ThreadPool thread/queue | legacy EventCounters 的 `Metric` gauge，线程/工作项；缺样未知 |
| completed work items / lock contention | `Rate`，每个采样间隔的增量；附加前累积基线未知 |
| CPU Usage / time-in-gc | 工具原百分比读数；不能用 MinWorkerThreads 推导 CPU 数 |
| `cpu-<PID>.csv` `dotnet.process.cpu.count` | .NET `Environment.ProcessorCount` 的 `Metric` gauge；工具未输出则未知 |
| Redis `used_cpu_*` / commandstats calls/usec | 服务启动以来累积秒/次数/微秒，首样为本次观察基线；不是 CLR 读数 |
| connected/blocked clients | 连接数 gauge |
| Redis `latency_percentiles_usec_*` | 白名单命令的慢耗时分位元数据，微秒；不是逐条 SLOWLOG，也不含命令参数 |

CLR 每秒采样；Redis 采集完成后至少间隔两秒，记录实际采集耗时。工具不允许同名 provider 混用 Metrics 与 EventCounters，因此每个 testhost 有两个会话。附加、IPC、采集进程、Redis INFO 命令均有扰动；附加前和截止后的缺口不外推。服务实际版本见原 summary，image ID/digest 见 `service-images.json`，runtime/架构来自实际 testhost 映射/ELF。

采集有自身 1150 秒截止，每个 CSV 由 `prlimit` 限制 8 MiB，JSONL 写入前检查固定 8 MiB 上限。CSV 由工具在会话结束时落盘；`finally` 先向自有采集进程发送 SIGINT 并有界等待落盘，再由既有 helper 精确回收。触及大小上限或强制停止时，CSV 可能缺失或不完整。失败/取消后核对 `remainingCollectors`；状态缺失、提前退出、无样本或 cleanup 失败不能解释为完整观测。`observerCpuSeconds`/`observerFinalWorkingSetBytes` 仅量化 observer 自身，不含工具与目标进程开销，也不是内存峰值。安装或采集无法放入原预算时停止并上报，不加时、不自动重跑或提高 worker。一次全绿仅证明该次取证链可用，不证明历史根因修复，也不自动解锁业务 PR。
