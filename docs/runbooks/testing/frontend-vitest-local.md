# 前端 vitest 本地跑法与红结果判读 Runbook

本页只覆盖**本地**运行前端单测时的操作与红结果归因。CI 上每个 workspace 是独立 job / 独立 runner，不共享本机 CPU，本页的争用判据不适用于 CI 判读；CI 判读见 [`evidence.md`](evidence.md)。等待与确定性规则见 [`../../governance/testing/determinism.md`](../../governance/testing/determinism.md)。

命令参数以 `pnpm -C frontend/apps/<app> exec vitest run --help`、各 package 的 `package.json` scripts 与当前 `vite.config.ts` 为准。

## 命令入口

- 跑整个 package：`pnpm -C frontend/apps/<app> test`。
- **收窄到指定文件必须绕开 package script**：`pnpm -C frontend/apps/<app> exec vitest run <file> [<file>…]`。
  各 app 的 `test` script 形如 `vp test run src`，`src` 是写死的位置过滤参数；在 `pnpm test` 后追加文件名只是多加一个过滤项，`src` 仍然匹配全部测试文件，结果是**整包照跑**而不是只跑那几个文件。追加 flag（`--maxWorkers=…` 等）不受影响。
- 并发度相关 flag：`--maxWorkers <n>`、`--no-file-parallelism`；默认 pool 为 `forks`，默认并发取本机可用并行度。

## 留证

判读的前提是有完整失败清单，事后无法补。

- 把整段输出落盘（`> run.log 2>&1`），需要逐用例名时用 `--reporter=verbose`。
- **不要用 `tail` / `head` 截断后再丢弃原始输出**。一旦只留了汇总行，重跑转绿就永远说不清是争用还是真红，只能按「未归因」挂账。
- `--reporter=basic` 在 vitest 4 已被移除。传它会得到 `Startup Error: Failed to load custom Reporter from basic` 并 `exit 1`，且**一个用例都没跑**；靠退出码判红绿会全假红。可用 reporter 以 `vitest run --help` 的 `--reporter` 列表为准。

## 判据：资源争用假红 vs 真红

本机同时有多棵 worktree / 多个 vitest 进程在跑时，fork 数会数倍于物理核数，用例拿不到 CPU 而撞上默认 `testTimeout`，表现为一批与本次改动无关的文件超时。按下列顺序判定，**任一步指向真红即停止，不再往下走**。

0. 没有完整失败清单 → 不可归因。既不能声称争用，也不能声称真红；按未留证记录。
1. **失败正文形状**：全部失败都是 `Error: Test timed out in <testTimeout>ms.`，没有断言 diff、没有抛出的异常。出现任何一条断言失败或异常 → 真红。
2. **本次 run 的 `Duration`**：与同机空载下同 SHA、同命令的一次跑相比是否显著膨胀。没有膨胀却出现超时 → 不是争用。
3. **失败集合稳定性**（旁证，不单独定性）：重复跑之间失败文件集漂移，且含与改动面无交集的文件。
4. **判定步**：把第 1 步列出的失败文件在**空载**下串行重跑
   `pnpm -C frontend/apps/<app> exec vitest run <失败文件…> --no-file-parallelism --maxWorkers=1`。
   仍红 → 真红；全绿 → 判为资源争用假红。

承重的是第 4 步。第 1 步单独**零鉴别力**：一个真正挂死的用例（例如 `await` 一个永不 settle 的 promise）产出的正文、堆栈与行号标记和争用超时完全一致，只有空载重跑能分开两者。第 1–3 步的价值只是在付出重跑成本前廉价地筛掉明显的真红。

第 4 步不是「重跑碰绿」：它换的是运行条件（空载 + 串行 + 单 worker），不是把同一条命令再赌一次。同一条命令原样重跑直到变绿，不构成任何归因。

## 多进程并行时的跑法

同时跑多份前端单测（多 worktree、或同一 worktree 起多个 run）时，给每个 run 显式限并发，使 `--maxWorkers × 并发 run 数` 不超过本机物理核数：

```
pnpm -C frontend/apps/<app> test --maxWorkers=<核数 / 并发 run 数>
```

超额订阅不仅制造假红，墙钟总时长也更长——把并发收到核数以内通常同时更快更稳。

不改仓库默认并发：CI 每个 workspace 独占 runner，本地单跑在默认并发下最快，降并发只在「本机确实有多个 run 同时在跑」这一条件下成立，属于跑法而不是配置。

## 不做

- 不用重复跑同一条命令碰绿代替归因。
- 不为了让超时消失而上调 `testTimeout`：它会同时掩盖真实挂死。
