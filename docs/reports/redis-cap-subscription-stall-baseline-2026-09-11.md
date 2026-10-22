# Redis/CAP 首轮订阅停顿：修复前连续量基线（2026-09-11 冻结）

本报告冻结 **#3236 根因修复（S1–S4）落地之前** 的连续量基线，供修复后用同一把尺子复测。
它不定义当前实现事实，也不给出任何修复方案。当前口径回到
`scripts/measure-redis-cap-observation-baseline.ps1` 与
`scripts/tests/redis-cap-observation-baseline.Tests.ps1`。

- 票：#3349（#3236 拆解 5/5）
- 基线基准提交：`112c177c7`
- 样本登记表：`scripts/redis-cap-observation-baseline-samples.json`
- 复算入口：`./scripts/measure-redis-cap-observation-baseline.ps1 -ArtifactRoot <解压目录> -OutputPath <json>`

## 样本从哪来

`Redis/CAP Transport Tests` job 的 `Upload Redis/CAP dependency summary` 步骤是 **`if: always()`**
（`112c177c7` 的 `.github/workflows/ci.yml:1052-1054`），因此 **红绿两侧的 run 都上传了
`observation/` 产物**。本基线全部来自这些既有 artifact，没有为取样重跑任何 run，也没有新建采集。

- 采集链自 `711f7d509`（PR #3264，2026-09-08）落 main，因此产物历史起点是 2026-09-08。
- 2026-09-11 枚举 2026-09-08 起的 226 次 CI run，其中 `Redis/CAP Transport Tests` 未被 skipped 的
  run 共产出 124 个 `redis-cap-dependency-summary-*` artifact（含同一 run 的多个 attempt），
  含 `observation/` 目录的有 88 个，全部 `expired=false`，已全部下载解压复算；
  其余 36 个只含 `summary.json`（抽验 2 个确认无 `observation/`）。
- 其中 **4 个是 attempt≥2 的重跑**（`34339996525-2`、`34480813215-2`、`34480813215-3`、
  `34517685346-2`），按「禁止重跑取样」排除；**登记 84 个自然 run（attempt=1）**。
- 红/绿取自各 run 对应 attempt 的 `Redis/CAP Transport Tests` job 结论，不从产物反推。

**红 24 / 绿 60。**

## 度量口径

目标成员固定为 `mes-asset-unavailable-redis-cap`，其 testhost 进程号由 `observations.jsonl` 的
`kind=testhost` 记录解析；同一 lane 内其它成员的 testhost 与 CSV 不参与任何读数。

- `inner_subscribe` 耗时：`fixture-phases.jsonl` 中同一 `(fixtureId, consumerId)` 的
  `inner_subscribe_started` 与终态（`inner_subscribe_succeeded` / `inner_subscribe_failed`）之间的
  Stopwatch 差，按记录自带 `timestampFrequency` 换算。
- **窗口**：该样本中**最长**一次 `inner_subscribe` 的 `[started.utc, terminal.utc]`。
- 窗口内/窗口外计数器读数来自同进程的 `clr-<pid>.csv`（dotnet-counters，1 秒刷新）。
  CSV 时间戳无时区，脚本强制要求 `status.json` 的 `counterTimestampTimeZone` 为 `Etc/UTC`，
  否则直接失败，不做时区猜测。
- 窗口短于 3 秒的样本（红 2 / 绿 13），其窗口内统计量由 1–3 行 CSV 决定，
  逐样本 `windowCounterInterpretable=false`，聚合段的计数器统计量已排除它们；
  逐样本表仍原样保留，引用时必须带上该标志。

## 聚合读数

| 度量（窗口内，除注明外） | 红 n=22 | 绿 n=47 |
|---|---|---|
| 最长 `inner_subscribe` 秒（全样本 n=24 / n=60）min / p50 / p90 / max | 0.02 / 10.8 / 16.61 / 19.75 | 0.02 / 16.03 / 22.0 / 24.73 |
| `ThreadPool Thread Count` 窗口内中位数的 p50 | 19.0 | 19.0 |
| `ThreadPool Thread Count` 窗口内峰值 min–max | 10.0 – 26.0 | 18.0 – 42.0 |
| `ThreadPool Thread Count` **窗口外**中位数的 p50 | 29.5 | 42.0 |
| `ThreadPool Queue Length` 窗口内中位数的 p50 | 17.25 | 18.0 |
| `ThreadPool Queue Length` 窗口内峰值 min–max | 18.0 – 36.0 | 11.0 – 45.0 |
| `ThreadPool Queue Length` **窗口外**中位数的 p50 | 5.0 | 2.5 |
| `threadpool-completed-items-count`(rate) 窗口内中位数的 p50 | 5.0 | 6.0 |
| `threadpool-completed-items-count`(rate) 窗口内峰值 min–max | 43.0 – 128.0 | 42.0 – 267.0 |
| `threadpool-completed-items-count`(rate) **窗口外**中位数的 p50 | 68.25 | 131.0 |
| `monitor-lock-contention-count`(rate) 窗口内中位数的 p50 | 0.0 | 0.0 |
| `monitor-lock-contention-count`(rate) 窗口内峰值 min–max | 15.0 – 61.0 | 16.0 – 89.0 |
| `monitor-lock-contention-count`(rate) **窗口外**中位数的 p50 | 30.0 | 52.0 |
| `CPU Usage (%)` 窗口内中位数的 p50 | 1.11 | 0.94 |
| `CPU Usage (%)` 窗口内峰值 min–max | 11.89 – 40.04 | 7.0 – 41.05 |
| `CPU Usage (%)` **窗口外**中位数的 p50 | 4.16 | 14.09 |

## 三条会影响验收口径的观察

### 1. 停顿不是红 run 独有：绿 run 同样发生，且更长

最长 `inner_subscribe` ≥ 5 秒的样本：**红 22/24，绿 47/60**。绿侧 p50（16.03s）高于红侧
p50（10.80s），两侧分布完全重叠。

⇒ 该缺陷的机制在绝大多数 run 上都现形；lane 红不红取决于**是否有某条 Redis 命令跨过 5s
`SyncTimeout`**，不取决于这一次 `inner_subscribe` 本身多长。

⇒ 对验收的影响：`inner_subscribe` 耗时**适合做修复前后对比**（修复后预期 < 500ms，与两侧
基线都完全分离），**不适合用来区分红绿**。把「绿 run 上的耗时」当成健康对照会读反。

**修正 #3236 评论 12 §五的一处算术前提**：那里写「红基线 mean=10.49 s / sd=1.15 s」，取自
评论 8 的 4 条读数。本次 24 条红样本的实测跨度是 0.02–19.75 s，远宽于 sd=1.15；据此算出的
`d≈9` 与「4 前 + 4 后完全分离即得精确 p=0.014」偏乐观。**结论（连续量比红绿计数样本效率高）
仍成立**，但具体效应量应按本报告的分布重估。

### 2. `monitor-lock-contention-count` 在停顿窗口内是 0，它不追踪本缺陷

#3349 票面把它列为「最贴身的读数 —— 它直接计 `Monitor.Wait`」。实测相反：

- 窗口内中位数的 p50 = **0**（红绿皆然）
- 窗口外中位数的 p50 = 30（红）/ 52（绿）

即停顿期间锁竞争计数**低于**非停顿期间。窗口内峰值 15–61 出现在窗口首尾两秒（进入/退出
停顿的过渡帧），不在停顿平台段。

**实读到此为止。** 「因为 SE.Redis 的同步等待不是 `Monitor` 竞争而是事件等待，所以该计数器
不动」是**推断，未验证**，本报告不主张。可主张的只有：**这条度量不要用作本缺陷的前后对比。**

### 3. `ThreadPool Thread Count` 窗口内平台 = 19，窗口外继续注入

红绿两侧窗口内中位数的 p50 都是 **19**，与 #3236 评论 8 记的「线程数钉在 18–19 且约 1/s
缓慢注入」一致；窗口外中位数升到 29.5（红）/ 42（绿）。同一窗口内 CPU 中位数约 1%、
队列中位数 17–18、完成率谷 5–6/s —— **队列有货、CPU 空转、线程数钉住**的形状在两侧都成立。

⇒ 这条是 #3236 裁定里「区分根治与补丁」的判别式：A/B/F 落地后该平台值应下降，
`SetMinThreads` 类补丁不会。

## 值域边界（如实声明）

1. **覆盖**：目标成员 testhost 的 `inner_subscribe` 耗时，以及该进程 `clr-<pid>.csv` 里的
   `ThreadPool Thread Count`、`ThreadPool Queue Length`、`ThreadPool Completed Work Item Count`
   (rate)、`Monitor Lock Contention Count` (rate)、`CPU Usage (%)`。
2. **不覆盖 —— `WORKER Busy/Min/Max`、`POOL QueuedItems`、`IOCP`、SE.Redis 异常文本里的 `in:`**：
   它们**不在任何 artifact 里**，只存在于 job 日志文本（#3236 评论 8 的报缺至今未闭合）。
   本基线不依赖它们。⚠️ `ThreadPool Thread Count` 与 SE.Redis 的 `WORKER Busy` **不是同一个量**，
   不得互相代入；#3236 评论 11 表格里的 `Busy=19/20` 与本报告的「线程数中位 19」数值接近是
   巧合，不构成同一读数的两次测量。**若要求用这些原始字段做前后对比，那是证据不足，需另立取证票。**
3. **不覆盖 —— fullstack 射程**：`Upload FullChain failure diagnostics` 是 `if: failure()`
   （`112c177c7` 的 `.github/workflows/ci.yml:1615-1617`），绿侧无产物 ⇒ 该射程
   **不做定量验收，只做机制同源论证**（#3236 2026-09-11 裁定）。不要读成「fullstack 也已定量证明改善」。
4. **不覆盖**：Redis 服务端读数（`observations.jsonl` 的 `kind=redis` 记录）与非目标成员的 testhost。
5. **时间边界**：`observation/` artifact 保留 14 天。本报告的冻结读数在产物过期后仍可引用，
   但届时无法再从 GitHub 取回产物复算同一批样本。
6. **样本构成**：84 个样本来自 18 条不同 head branch 的 run（52 次 `pull_request` + 32 次 `push`），未按分支分层；
   不同分支的代码差异未被控制，本报告只主张「修复前的总体分布」，不主张任一分支的单独结论。

## 修复后如何复测（配方，执行前须重新核实当前脚本帮助与 CI 配置）

1. 枚举修复后的自然 run 与其 `Redis/CAP Transport Tests` job 结论：
   `gh api repos/Mang-X/Nerv-IIP/actions/runs/<runId>/attempts/<attempt>/jobs`。
2. 按同一 schema 写一份新的样本 manifest（只登记 `runAttempt=1`）。
3. 下载并解压：`gh api repos/Mang-X/Nerv-IIP/actions/artifacts/<id>/zip`，
   解到 `<root>/redis-cap-dependency-summary-<runId>-<attempt>/`。
   ⚠️ `gh` 必须在仓库目录里跑。
4. `./scripts/measure-redis-cap-observation-baseline.ps1 -ArtifactRoot <root> -SampleManifestPath <新 manifest> -OutputPath <json>`。
5. 与本报告的聚合表按同一列对照。⚠️ 不得以「连续 N 次绿」替代连续量对照（#3236 评论 12 §五）。

## 逐样本读数

窗口可解释=N 的行，其后四列由 1–3 行 CSV 决定，不得单独引用。

| run id | 红/绿 | event | head branch | 最长 inner_subscribe (s) | 次长 (s) | 窗口可解释 | 线程数中位 | 队列峰值 | 完成率谷 (/s) | 锁竞争峰 (/s) |
|---|---|---|---|---|---|---|---|---|---|---|
| `34198184196` | red | pull_request | nerv-2127-redis-cap-ci-obs | 9.7988 | 0.0324 | Y | 19 | 25 | 1 | 22 |
| `34199409173` | green | pull_request | nerv-2127-redis-cap-ci-obs | 22.1595 | 0.0165 | Y | 20 | 31 | 0 | 89 |
| `34200311047` | green | pull_request | nerv-2127-redis-cap-ci-obs | 22.4031 | 0.0264 | Y | 19 | 39 | 0 | 49 |
| `34201495616` | red | pull_request | nerv-2127-redis-cap-ci-obs | 14.7114 | 0.0226 | Y | 19 | 25 | 1 | 27 |
| `34207367410` | green | pull_request | nerv-2127-redis-cap-ci-obs | 17.2916 | 0.0225 | Y | 18 | 32 | 0 | 34 |
| `34208988897` | green | push | main | 18.9128 | 0.0377 | Y | 19 | 45 | 1 | 41 |
| `34211319521` | green | pull_request | nerv-2118-mes-company-mate | 2.8151 | 0.0151 | N | 18 | 20 | 3 | 25 |
| `34211364545` | green | pull_request | nerv-2120-erp-receipt-rout | 13.4175 | 0.026 | Y | 19 | 23 | 1 | 26 |
| `34212990135` | green | push | main | 0.7448 | 0.0521 | N | 17 | 22 | 3 | 0 |
| `34213096351` | green | pull_request | nerv-2120-erp-receipt-rout | 20.1332 | 0.014 | Y | 20 | 38 | 0 | 69 |
| `34214703933` | red | push | main | 9.8863 | 0.0654 | Y | 19 | 24 | 1 | 33 |
| `34228850137` | green | pull_request | agent/nerv-2121-wms-receip | 15.2625 | 0.019 | Y | 17 | 22 | 2 | 57 |
| `34231336137` | red | push | main | 0.0804 | 0.0319 | N | 20 | 27 | 1 | 0 |
| `34234984377` | green | push | main | 22.1674 | 0.0668 | Y | 20 | 31 | 1 | 20 |
| `34306809060` | green | pull_request | issue-3097-upload-commit-s | 7.7443 | 0.0737 | Y | 18 | 11 | 3 | 16 |
| `34308040661` | green | pull_request | agent/nerv-2130-erp-receip | 17.7863 | 0.0192 | Y | 19 | 20 | 1 | 48 |
| `34308848278` | green | pull_request | issue-3085-handover-attach | 18.8346 | 0.0159 | Y | 18 | 25 | 1 | 39 |
| `34309239545` | green | pull_request | issue-3229-voucher-no-boun | 22.3543 | 0.02 | Y | 20 | 35 | 0 | 29 |
| `34309660777` | green | pull_request | issue-2870-shard-timeout | 18.2537 | 0.0293 | Y | 19 | 36 | 2 | 28 |
| `34309845492` | green | pull_request | issue-3097-upload-commit-s | 16.8007 | 0.0559 | Y | 18 | 34 | 2 | 47 |
| `34310681488` | green | push | main | 14.2064 | 0.0308 | Y | 19 | 35 | 2 | 41 |
| `34312132608` | green | pull_request | agent/nerv-2131-wms-receip | 17.9306 | 0.0197 | Y | 18 | 30 | 2 | 58 |
| `34324272015` | green | pull_request | issue-3259 | 11.8361 | 0.0209 | Y | 18 | 37 | 3 | 38 |
| `34324596177` | green | pull_request | issue-3042-template-retire | 16.3873 | 0.0366 | Y | 19 | 33 | 3 | 47 |
| `34326065696` | green | push | main | 0.0593 | 0.0367 | N | 20 | 25 | 1 | 0 |
| `34326417218` | green | pull_request | issue-3042-template-retire | 18.3341 | 0.0625 | Y | 17 | 24 | 0 | 25 |
| `34337209468` | red | pull_request | issue-3229-voucher-no-boun | 16.4179 | 0.0525 | Y | 18 | 24 | 1 | 30 |
| `34339996525` | red | pull_request | issue-3229-voucher-no-boun | 9.2183 | 0.0303 | Y | 19 | 27 | 3 | 38 |
| `34343740815` | green | push | main | 13.3848 | 0.0243 | Y | 17 | 21 | 3 | 22 |
| `34343939691` | green | pull_request | issue-3229-voucher-no-boun | 0.0156 | 0.0074 | N | 18 | 19 | 3 | 0 |
| `34345700694` | green | push | main | 15.406 | 0.0218 | Y | 19 | 30 | 3 | 19 |
| `34357157389` | red | push | main | 10.9522 | 0.0285 | Y | 19 | 31 | 1 | 27 |
| `34362511090` | red | pull_request | issue-3112-mes-sku-single- | 9.2503 | 0.0207 | Y | 19 | 23 | 1 | 25 |
| `34367277351` | green | pull_request | issue-3112-mes-sku-single- | 22.0491 | 0.0158 | Y | 20 | 38 | 1 | 55 |
| `34372422224` | green | pull_request | issue-2870-shard-timeout | 18.0085 | 0.0238 | Y | 19 | 28 | 1 | 33 |
| `34374471972` | green | push | main | 24.7283 | 0.027 | Y | 19 | 36 | 1 | 27 |
| `34376354938` | green | pull_request | issue-3097-upload-commit-s | 0.0532 | 0.0289 | N | 20 | 23 | 1 | 0 |
| `34376409292` | green | pull_request | issue-2870-shard-timeout | 19.7269 | 0.0565 | Y | 19 | 34 | 2 | 46 |
| `34376686851` | green | pull_request | issue-3112-mes-sku-single- | 0.0221 | 0.0171 | N | 18 | 15 | 4 | 0 |
| `34378402774` | red | push | main | 11.8466 | 0.0201 | Y | 18 | 31 | 3 | 27 |
| `34380169627` | green | pull_request | issue-2870-shard-timeout | 16.2089 | 0.0174 | Y | 19 | 31 | 1 | 19 |
| `34424109395` | green | pull_request | issue-3112-mes-sku-single- | 21.9931 | 0.0608 | Y | 21 | 42 | 0 | 37 |
| `34425359111` | green | push | main | 20.535 | 0.0644 | Y | 21 | 26 | 0 | 25 |
| `34426934271` | green | push | main | 6.9616 | 0.0176 | Y | 21 | 21 | 2 | 51 |
| `34428118123` | green | push | main | 0.0773 | 0.0337 | N | 20 | 20 | 1 | 7 |
| `34433909052` | green | pull_request | issue-2870-shard-timeout | 0.0663 | 0.0655 | N | 20 | 22 | 3 | 0 |
| `34435207724` | red | push | main | 12.5783 | 0.0441 | Y | 19 | 19 | 1 | 16 |
| `34437117623` | red | push | main | 10.6741 | 0.0078 | Y | 19 | 27 | 4 | 27 |
| `34437757554` | red | pull_request | issue-3085-handover-attach | 5.4149 | 0.0242 | Y | 7 | 18 | 1 | 29 |
| `34438184776` | red | push | main | 16.6939 | 0.0651 | Y | 19 | 28 | 3 | 28 |
| `34438490277` | green | pull_request | issue-3085-handover-attach | 16.394 | 0.078 | Y | 18 | 39 | 0 | 22 |
| `34440250635` | green | pull_request | issue-3085-handover-attach | 14.9012 | 0.0238 | Y | 18 | 24 | 2 | 32 |
| `34440402168` | green | push | main | 12.9136 | 0.0186 | Y | 19 | 29 | 2 | 49 |
| `34444231601` | green | pull_request | issue-3085-handover-attach | 0.0494 | 0.0361 | N | 17 | 25 | 4 | 0 |
| `34446374635` | red | push | main | 15.3886 | 0.0556 | Y | 18 | 25 | 0 | 58 |
| `34446815486` | green | pull_request | issue-3085-handover-attach | 19.2579 | 0.0194 | Y | 19 | 29 | 1 | 44 |
| `34449765762` | green | pull_request | issue-3085-handover-attach | 20.3152 | 0.0123 | Y | 19 | 45 | 1 | 62 |
| `34450824237` | green | pull_request | issue-3085-handover-attach | 13.2417 | 0.0355 | Y | 18 | 21 | 4 | 26 |
| `34451267272` | green | push | main | 15.2952 | 0.0308 | Y | 18 | 35 | 1 | 16 |
| `34452570647` | green | pull_request | issue-3315-mes-quality-hol | 16.524 | 0.0238 | Y | 19 | 32 | 0 | 24 |
| `34452712300` | green | pull_request | issue-3085-handover-attach | 0.0377 | 0.0132 | N | 20 | 22 | 3 | 10 |
| `34454785637` | green | pull_request | issue-3315-mes-quality-hol | 0.9516 | 0.0207 | N | 20 | 16 | 60 | 22 |
| `34456315515` | red | push | main | 0.0233 | 0.018 | N | 43 | 2 | 825 | 324 |
| `34458217625` | red | pull_request | issue-3085-handover-attach | 8.5233 | 0.0233 | Y | 18 | 24 | 3 | 25 |
| `34460812247` | red | pull_request | issue-3318-mes-defect-disp | 13.7912 | 0.0091 | Y | 18 | 33 | 1 | 61 |
| `34463381525` | green | pull_request | issue-3318-mes-defect-disp | 0.0786 | 0.0248 | N | 20 | 29 | 2 | 0 |
| `34468482190` | red | push | main | 10.7992 | 0.0172 | Y | 19 | 30 | 1 | 15 |
| `34470124913` | green | push | main | 17.6584 | 0.0181 | Y | 19 | 17 | 2 | 29 |
| `34472724877` | red | pull_request | issue-3319-inspection-reco | 19.7485 | 0.0265 | Y | 18 | 33 | 1 | 43 |
| `34475129962` | green | pull_request | issue-3319-inspection-reco | 21.9141 | 0.0391 | Y | 19 | 36 | 0 | 89 |
| `34476468370` | green | push | main | 15.8678 | 0.0177 | Y | 19 | 28 | 1 | 33 |
| `34480813215` | red | pull_request | issue-3319-inspection-reco | 16.7161 | 0.0376 | Y | 19 | 36 | 1 | 34 |
| `34481403503` | green | push | main | 16.1842 | 0.0235 | Y | 19 | 35 | 0 | 36 |
| `34489186091` | green | pull_request | issue-3319-inspection-reco | 19.3969 | 0.0157 | Y | 19 | 39 | 1 | 34 |
| `34489625176` | red | push | main | 10.7997 | 0.0366 | Y | 18 | 21 | 3 | 25 |
| `34491469754` | green | pull_request | issue-3319-inspection-reco | 17.4661 | 0.0243 | Y | 19 | 20 | 1 | 24 |
| `34493310117` | green | push | main | 15.523 | 0.0184 | Y | 18 | 31 | 2 | 24 |
| `34495875018` | red | push | main | 5.8506 | 0.0329 | Y | 7 | 19 | 2 | 32 |
| `34505586864` | green | push | main | 0.0463 | 0.0432 | N | 44 | 0 | 209 | 76 |
| `34508318084` | green | push | main | 13.6671 | 0.028 | Y | 19 | 20 | 1 | 18 |
| `34510730399` | green | pull_request | issue-3305-wcs-failure-mes | 14.8 | 0.0239 | Y | 19 | 39 | 0 | 20 |
| `34517685346` | red | pull_request | issue-3305-wcs-failure-mes | 12.3479 | 0.0256 | Y | 19 | 33 | 2 | 39 |
| `34522826541` | green | push | main | 13.915 | 0.0461 | Y | 18 | 21 | 2 | 50 |
| `34522968634` | red | pull_request | issue-3305-wcs-failure-mes | 10.5129 | 0.0149 | Y | 18 | 33 | 2 | 25 |
