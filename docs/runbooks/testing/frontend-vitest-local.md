# 前端 vitest 本地跑法与红结果判读 Runbook

本页只覆盖**本地**运行前端单测时的操作与红结果归因。CI 上每个 workspace 是独立 job / 独立 runner，不共享本机 CPU，本页的争用判据不适用于 CI 判读；CI 判读见 [`evidence.md`](evidence.md)。

判据的取证做在单个 package 上（#2951），依赖的机制是：同时在跑的 fork 总数超过物理核数，用例拿不到 CPU 而撞上默认 `testTimeout`。该机制与具体 package 无关，但本页没有在其它 package 上取过证。命令、参数、默认值与运行行为一律以当前 `--help`、`package.json`、`vite.config.ts` 和代码为准，读数以你自己机器上的为准，不要照抄别处的秒数或核数。

## 命令入口

- 跑整个 package：`pnpm -C frontend/<apps|packages>/<name> test`。少数 package 没有 `test` script（例如 `frontend/apps/design-system`），以各自 `package.json` 为准。
- **收窄到指定文件必须绕开 package script**：`pnpm -C frontend/<apps|packages>/<name> exec vitest run <file> [<file>…]`。
  各 package 的 `test` script 形如 `vp test run src`（部分另带 `--environment jsdom`），`src` 是写死的位置过滤参数；vitest 的文件过滤是 OR 语义，在 `pnpm test` 后追加文件名只是多加一个过滤项，`src` 仍然匹配全部测试文件，结果是**整包照跑**而不是只跑那几个文件。追加 flag（`--maxWorkers=…` 等）不受影响。
- 并发度相关 flag：`--maxWorkers <n>`、`--no-file-parallelism`；默认 pool 为 `forks`，非 watch 模式下默认 worker 数为 `max(os.availableParallelism() - 1, 1)`。

## 留证

判读的前提是有完整失败清单，事后无法补。

- 把整段输出落盘（`> run.log 2>&1`），需要逐用例名时用 `--reporter=verbose`。
- **不要用 `tail` / `head` 截断后再丢弃原始输出**。一旦只留了汇总行，重跑转绿就永远说不清是争用还是真红，只能按「未归因」挂账。
- 失败会按来源打在**互相独立的横幅**下（见下一节），只 grep `FAIL` 或只看 `Failed Tests` 会漏掉其余横幅下的失败。
- `--reporter=basic` 在 vitest 4 已被移除。传它会得到 `Startup Error: Failed to load custom Reporter from basic` 并 `exit 1`，且**一个用例都没跑**；靠退出码判红绿会全假红。可用 reporter 以 `vitest run --help` 的 `--reporter` 列表为准。

## 判据：资源争用假红 vs 真红

本机同时有多棵 worktree / 多个 vitest 进程在跑时，fork 总数会数倍于物理核数，用例拿不到 CPU 而撞上默认 `testTimeout`，表现为一批与本次改动无关的文件超时，同时整个 run 的 `Duration` 显著变长。按下列顺序判定。

### 0. 先确认有完整失败清单

没有 → 不可归因。既不能声称争用，也不能声称真红；按未留证记录。

### 1. 按输出横幅 + 失败正文分流

vitest 把失败按来源打在独立横幅下，先看横幅再看正文，不要凭印象判「这条像不像环境问题」：

| 横幅 | 含义 | 处置 |
| --- | --- | --- |
| `Failed Tests <n>` | 用例级失败 | 正文是断言 diff、或被测代码自己抛出的异常 → **真红**，到此为止；正文是 `Error: Test timed out in <testTimeout>ms.` → 不定性，进第 2 步 |
| `Unhandled Errors` | 测试进程里逃逸出用例边界的错误（worker/pool 生命周期、teardown、游离定时器等） | 不定性，进第 2 步 |
| `Source Errors` | 与测试文件无关的加载/求值期错误 | 不定性，进第 2 步 |
| **其它任何横幅** | 上面没列到的来源（`Failed Suites`、`Async Leaks`、`Startup Error` 等） | 不定性，进第 2 步 |

上表**不是穷举，也无法穷举**：vitest 有一个横幅名取自运行时错误对象的调用点（`printErrorType(type, ctx)` → `errorBanner(type)`），可能出现的横幅不是静态封闭集合。所以判据靠的是最后那条兜底行——**只有第一行的前半可以就地定性，其余一律进第 2 步**，新增或未见过的横幅不会掉进空隙。

「失败信息里含 `Test timed out`」这条单独**零鉴别力**：一个真正挂死的用例（例如 `await` 一个永不 settle 的 promise）产出的正文、堆栈与行号标记和争用超时完全一致，只有重跑能分开两者。

### 2. 空载重跑失败文件，不加并发 flag

```
pnpm -C frontend/<apps|packages>/<name> exec vitest run <失败文件…>
```

**空载下跑，不加任何并发 flag**：不主动关闭包内并行，让共享端口/临时路径这类并行下才暴露的真红仍有机会复现；改变的只有「本机有没有别的 run 在抢 CPU」。

- 全绿 → **资源争用假红**。
- 仍红 → **真红**。

这不是「重跑碰绿」：换掉的运行条件是本机负载，绿/红各自都能归因。同一条命令在同样的负载下原样重跑直到变绿，不构成任何归因。

不要在这一步加 `--no-file-parallelism --maxWorkers=1`：那会在本机负载之外再改掉包内并行拓扑，于是「同包两个文件抢同一固定端口/临时路径导致并行下崩溃」这类**必须修的真红**会随串行重跑一起转绿，被误判成争用。

### 3. 仍红时定位（不改第 2 步的定性）

第 2 步仍红即已判定真红。要进一步区分是用例自身的缺陷还是包内并行拓扑（跨文件共享端口、临时目录、全局状态）的缺陷，再跑一次：

```
pnpm -C frontend/<apps|packages>/<name> exec vitest run <失败文件…> --no-file-parallelism --maxWorkers=1
```

转绿说明致因是包内并行，仍是真红、仍要修；不要据此改回「争用假红」。

### 旁证（不单独定性）

重复跑之间失败文件集漂移、失败文件与改动面无交集，都只提高争用的可能性，不能定性——真红同样可能只在部分跑次里触发（时序相关的用例），而「与改动面无交集」也不排除本次改动破坏了共享依赖。

## 多进程并行时的跑法

同时跑多份前端单测（多 worktree、或同一 worktree 起多个 run）时，给每个 run 显式限并发，使 `--maxWorkers × 并发 run 数` 不超过本机物理核数：

```
pnpm -C frontend/<apps|packages>/<name> test --maxWorkers=<核数 / 并发 run 数>
```

超额订阅不仅制造假红，墙钟总时长也更长——把并发收到核数以内通常同时更快更稳。

不改仓库默认并发：`maxWorkers` 是单个 vitest 进程内的决策，看不见同机其它 run，所以超额订阅的分母（本机同时在跑几个 run）在写配置时不可知；任何仓库级默认只能取一个固定分数，既牺牲单跑场景又在并发数大时照样超额。而 CI 每个 workspace 独占 runner，本地单跑也无需降并发。所以这是**跑法**而不是配置。

## 不做

- 不用重复跑同一条命令碰绿代替归因。[`determinism.md`](determinism.md) 的「常见定位顺序」同款要求：「不要用重复运行“碰绿”替代结构修复」。
- 不为了让超时消失而上调 `testTimeout`：它会同时掩盖真实挂死。
