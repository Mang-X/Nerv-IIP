# 前端 vitest 本地跑法与红结果判读 Runbook

本页只覆盖**本地**运行前端单测时的操作与红结果归因。CI 上每个 workspace 是独立 job / 独立 runner，不共享本机 CPU，本页的争用判据不适用于 CI 判读；CI 判读见 [`evidence.md`](evidence.md)。

判据的取证做在单个 package 上（#2951）。它依赖的机制——同时在跑的 fork 总数超过物理核数，用例拿不到 CPU 而撞上默认 `testTimeout`——与具体 package 无关，因此按 package 复用时不需要重新取证；但下面出现的任何**具体数值**都以当前 `--help`、`package.json`、`vite.config.ts` 和你自己机器上的读数为准，不要照抄别处的秒数或核数。

## 命令入口

- 跑整个 package：`pnpm -C frontend/<apps|packages>/<name> test`。少数 package 没有 `test` script（例如 `frontend/apps/design-system`），以各自 `package.json` 为准。
- **收窄到指定文件必须绕开 package script**：`pnpm -C frontend/<apps|packages>/<name> exec vitest run <file> [<file>…]`。
  各 package 的 `test` script 形如 `vp test run src`（部分另带 `--environment jsdom`），`src` 是写死的位置过滤参数；vitest 的文件过滤是 OR 语义，在 `pnpm test` 后追加文件名只是多加一个过滤项，`src` 仍然匹配全部测试文件，结果是**整包照跑**而不是只跑那几个文件。追加 flag（`--maxWorkers=…` 等）不受影响。
- 并发度相关 flag：`--maxWorkers <n>`、`--no-file-parallelism`；默认 pool 为 `forks`，非 watch 模式下默认 worker 数为 `max(os.availableParallelism() - 1, 1)`。

## 留证

判读的前提是有完整失败清单，事后无法补。

- 把整段输出落盘（`> run.log 2>&1`），需要逐用例名时用 `--reporter=verbose`。
- **不要用 `tail` / `head` 截断后再丢弃原始输出**。一旦只留了汇总行，重跑转绿就永远说不清是争用还是真红，只能按「未归因」挂账。
- `--reporter=basic` 在 vitest 4 已被移除。传它会得到 `Startup Error: Failed to load custom Reporter from basic` 并 `exit 1`，且**一个用例都没跑**；靠退出码判红绿会全假红。可用 reporter 以 `vitest run --help` 的 `--reporter` 列表为准。

## 判据：资源争用假红 vs 真红

本机同时有多棵 worktree / 多个 vitest 进程在跑时，fork 总数会数倍于物理核数，用例拿不到 CPU 而撞上默认 `testTimeout`，表现为一批与本次改动无关的文件超时，同时整个 run 的 `Duration` 显著变长。按下列顺序判定。

0. 没有完整失败清单 → 不可归因。既不能声称争用，也不能声称真红；按未留证记录。
1. **按失败正文分流**，只有下面第一类可以就地定性：
   - 断言失败（有 diff）、或被测代码自己抛出的异常 → **真红**，到此为止。
   - `Error: Test timed out in <testTimeout>ms.` → 不定性，进第 3 步。
   - worker / pool 生命周期类错误（worker 启动或退出失败、子进程崩溃、无用例执行的进程级失败等），正文既不是断言 diff 也不是被测代码抛出的异常 → 同样**不定性**，进第 3 步。
     #2951 票面把这一类与超时并列为争用信号；本次取证只复现出超时那一支，未复现出 worker 启动失败，因此这里不给它单独的定性规则，只保证它不会在这一步被误判成真红而错过第 3 步。
2. **失败集合稳定性**（旁证，不单独定性）：重复跑之间失败文件集漂移，且含与改动面无交集的文件。它只能提高争用的可能性，不能定性——真红同样可能只在部分跑次里触发（时序相关的用例），而「与改动面无交集」也不排除本次改动破坏了共享依赖。
3. **判定步**：把第 1 步分流出的失败文件在**空载**下串行重跑
   `pnpm -C frontend/<apps|packages>/<name> exec vitest run <失败文件…> --no-file-parallelism --maxWorkers=1`。
   仍红 → 真红；全绿 → 判为资源争用假红。

承重的是第 3 步。「失败信息里含 `Test timed out`」这条单独**零鉴别力**：一个真正挂死的用例（例如 `await` 一个永不 settle 的 promise）产出的正文、堆栈与行号标记和争用超时完全一致，只有空载重跑能分开两者。第 1 步只当单向排除用（有断言 diff → 真红），不用它证明争用。

第 3 步不是「重跑碰绿」：它换的是运行条件（空载 + 串行 + 单 worker），不是把同一条命令再赌一次。同一条命令原样重跑直到变绿，不构成任何归因。

## 多进程并行时的跑法

同时跑多份前端单测（多 worktree、或同一 worktree 起多个 run）时，给每个 run 显式限并发，使 `--maxWorkers × 并发 run 数` 不超过本机物理核数：

```
pnpm -C frontend/<apps|packages>/<name> test --maxWorkers=<核数 / 并发 run 数>
```

超额订阅不仅制造假红，墙钟总时长也更长——把并发收到核数以内通常同时更快更稳。

不改仓库默认并发：超额订阅的分母（本机同时在跑几个 run）在写配置时不可知，任何仓库级默认只能取一个固定分数，既牺牲单跑场景又在并发数大时照样超额；而 CI 每个 workspace 独占 runner，本地单跑也无需降并发。所以这是**跑法**而不是配置。

## 不做

- 不用重复跑同一条命令碰绿代替归因；结构性修复与「碰绿」的边界见 [`determinism.md`](determinism.md) 的常见定位顺序末条。
- 不为了让超时消失而上调 `testTimeout`：它会同时掩盖真实挂死。
