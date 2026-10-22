# FullChain man-440 SIGKILL 调查报告（#1878）

- 调查日期：2026-08-22
- 调查票：[#1878](https://github.com/Mang-X/Nerv-IIP/issues/1878)
- 母票：[#1664](https://github.com/Mang-X/Nerv-IIP/issues/1664)
- 重新定级票：[#2018](https://github.com/Mang-X/Nerv-IIP/issues/2018)

## 结论

`maintenance-runtime-hours`（`man-440`）的间歇性 137 **不是 kernel 或 cgroup
OOM-kill**。#1877 的内存见证落地后，hosted runner 至少捕获了七次同时带信号分类与内存
快照的 `man-440` 137；七次 `/proc/vmstat oom_kill` 增量与
`memory.events.oom_kill` 都为 0，失败 run 的 cgroup `memory.peak` 也低于既有成功
run。

失败时序进一步证明，SIGKILL 来自 FullStack guardian 提前执行的 session cleanup：活动
coordinator 仍在等待并最终上抛受管子进程 137，但 session 已先被推进到 `Stopping`，AppHost
也已停止。共同触发边沿是 guardian 的 60 秒观察周期，不是 `man-440` 的业务断言、冷启动顺序
或内存峰值。

因此本 spike 裁决：不收窄 AppHost 资源集、不调整场景顺序、不修改 GC 配置。后续工作转向
[#2018](https://github.com/Mang-X/Nerv-IIP/issues/2018)，让 guardian 改用可审计的身份状态并
修复误清理路径。

## 证据判定

### 七次 man-440 137 均无 OOM 计数增量

下表数据来自各 run 的 `full-chain-dependency-summary-<run>-<attempt>` artifact；峰值按
artifact 中的原始 bytes 换算为十进制 GB，保留两位小数。

| Run / attempt | 被终止进程 | cgroup `memory.peak` | `/proc/vmstat oom_kill` 增量 | `memory.events.oom_kill` |
| --- | --- | ---: | ---: | ---: |
| [32385700422](https://github.com/Mang-X/Nerv-IIP/actions/runs/32385700422) / 1 | `man-440` `dotnet test` | 12.35 GB | 0 | 0 |
| [32385700422](https://github.com/Mang-X/Nerv-IIP/actions/runs/32385700422) / 2 | `man-440` `dotnet test` | 12.16 GB | 0 | 0 |
| [32443528667](https://github.com/Mang-X/Nerv-IIP/actions/runs/32443528667) / 1 | `man-440` `dotnet test` | 12.43 GB | 0 | 0 |
| [32453681748](https://github.com/Mang-X/Nerv-IIP/actions/runs/32453681748) / 2 | `man-440` `dotnet test` | 12.65 GB | 0 | 0 |
| [32495410997](https://github.com/Mang-X/Nerv-IIP/actions/runs/32495410997) / 1 | `man-440` `dotnet test` | 12.65 GB | 0 | 0 |
| [32497580453](https://github.com/Mang-X/Nerv-IIP/actions/runs/32497580453) / 1 | `man-440` `dotnet test` | 12.41 GB | 0 | 0 |
| [32509648288](https://github.com/Mang-X/Nerv-IIP/actions/runs/32509648288) / 1 | `man-440` `dotnet test` | 12.30 GB | 0 | 0 |

既有成功 run [32328161494](https://github.com/Mang-X/Nerv-IIP/actions/runs/32328161494)
的峰值为 14.34 GB，高于上述全部失败 run。失败 run 既没有全局 OOM 计数增长，也没有触发
cgroup OOM 事件，并且失败时高水位反而更低；“137 由内存峰值越线造成”与实测不符。

`memory.max = max` 意味着该 hosted runner 没有本 cgroup 上限，故
`memory.events.oom_kill = 0` 本身不能排除全局 OOM；本结论依赖的是 #1877 新增的全局
`/proc/vmstat oom_kill` 前后差值同样为 0。

### guardian 在 coordinator 之前进入清理

run [32495410997](https://github.com/Mang-X/Nerv-IIP/actions/runs/32495410997)
attempt 1 保留了最清楚的时序：

| 时刻（UTC） | 事件 |
| --- | --- |
| `15:09:11.928` | `startup-network-inspect` 完成，session 启动阶段即将结束 |
| `15:09:31.048` | 受治理的 `fullstack-...-guardian-stop` 命令开始执行 |
| `15:09:46.976` | `MaintenanceRuntimeHoursPostgresRedisAcceptanceTests` 的 `dotnet test` 启动 |
| `15:09:52.417` | `guardian-stop` 日志记录执行 `aspire stop` |
| `15:10:08.413` | `dotnet` 收到 SIGKILL，退出码 137 |
| `15:10:08.540` 起 | coordinator 收集日志时，Aspire CLI 报告 AppHost 已不存在 |
| `15:10:12.550` | session summary 记录状态为 `Stopping` |

`fullstack-...-guardian-stop` 的 artifact 目录时间与 stdout 直接证明 guardian 先启动清理、
执行 `aspire stop`，约 16 秒后 `dotnet` 才收到 SIGKILL；这不再只依赖“56.5 秒接近 60 秒”
的时序推断。coordinator 在 SIGKILL 后仍能继续运行、捕获错误并收集诊断，因此它并未真实退出；
但在它进入失败清理前，session 已被 guardian 推进到 `Stopping` 并停止 AppHost。

guardian 当前接入的 `Test-NervProcessIdentity` 把以下情况全部折叠为 `false`：

- PID 不存在；
- PID 已复用且 StartTime 不匹配；
- `Get-Process` 或 StartTime 读取抛错。

`Invoke-NervFullStackGuardian` 又把这个布尔值直接当作 `coordinatorMissing`，第一次判为
`false` 就进入 stop。仓库已经存在返回 `Active / Absent / Mismatched / Unknown` 的
`Get-NervProcessIdentityStatus`，并已用于 owner 回收通道，但 guardian 尚未接入。现有 artifact
也没有保留当时到底是哪一种状态；因此“guardian 误清理”已由时序证实，guardian 的状态接线与
触发证据持久化仍由 #2018 跟踪。

## 四个调查问题的回答

### 1. 137 是否确为 OOM-kill

否。七次完整见证的全局与 cgroup OOM 计数增量均为 0；失败峰值低于成功峰值。直接终止者是
提前执行 cleanup 的 guardian，而不是 kernel/cgroup OOM-kill。

这里不把“不是 kernel OOM”扩大成“hosted runner 永远没有内存压力”。成功 run 的 14.34 GB
峰值说明余量确实有限，但它与本批 137 没有因果证据。

### 2. AppHost 资源集收窄如何实现，代价是什么

仓库固定的 Aspire CLI `13.4.6` 中，`aspire start --help` 没有选择资源子集的参数；它只能
启动 AppHost 当前注册的资源图。Aspire 提供
[`WithExplicitStart()`](https://learn.microsoft.com/en-us/dotnet/api/aspire.hosting.resourcebuilderextensions.withexplicitstart?view=dotnet-aspire-13.0)，
可让资源不随 AppHost 自动启动，再通过 `aspire resource <resource> start` 启动单个资源；另一种
做法是在 AppHost 读取显式 scenario profile，并条件注册不同资源图。

两种做法都不是 runner 侧加一个无侵入 CLI 参数：

- `WithExplicitStart()` 需要重新裁决哪些资源默认启动，并处理依赖资源是否随目标资源启动；它会影响
  普通 `nerv.ps1 dev`、全部 FullStack 场景和 Dashboard 操作语义。
- 条件注册需要把当前单个 `Program.cs` 中交错的数据库、消息、服务引用与反向引用改造成 profile
  可组合资源图。`business-maintenance` 直接依赖 `business-industrial-telemetry`、PostgreSQL 与
  Redis；IndustrialTelemetry 又引用 Ops，不能只按 readiness 列出的两个项目名机械裁剪。
- 两者都会减少一次 FullChain 场景实际证明能够共同启动的服务集合，改变验收真实性边界。

由于本批 SIGKILL 已证实来自 guardian cleanup，收窄资源集不命中根因，当前不实施，也不为它创建
生产改动票。若未来基于容量目标重新提出资源 profile，应作为独立架构决策重新定级。

### 3. 调整 man-440 顺序是否能改变成败

没有支持该方案的证据。完整采集期间，137 既发生在第一个场景 `man-440` 的 `dotnet test`，也发生
在第二个场景 `man-528` 的 Playwright（例如 run
[32490962741](https://github.com/Mang-X/Nerv-IIP/actions/runs/32490962741) 与
[32492436576](https://github.com/Mang-X/Nerv-IIP/actions/runs/32492436576)）。两类被杀进程
不同，公共边沿都是各自 session 的 guardian 观察周期。

既有成功 run 的高水位又只由 `man-440` 抬升，换序不会降低峰值本身；它最多改变冷/热态，不能修复
guardian 对单个 session 的误判。因此本 spike 未执行已失去辨识力的换序实验，也不修改
`scripts/full-chain-test-lane.json` 的冻结顺序。只有在 guardian 修复完成后，同一 runner 画像的
受控对照再次显示顺序会独立改变失败率或峰值时，才复评本裁决。

### 4. 是否有比资源收窄更便宜的降峰手段

GC 配置或其他降峰手段不应作为本故障的修复：失败峰值比成功峰值低约 1.7–2.2 GB，OOM 计数也没有
增长。更便宜且命中根因的路径是修正 guardian 身份观察与 cleanup 策略，详见 #2018。
若后续样本出现 `/proc/vmstat oom_kill` 增量，或引入了明确的内存上限/容量目标且场景实际越线，
再把 GC 与其他降峰手段作为独立容量问题复评；不能用当前无 OOM 见证的 137 触发该工作。
