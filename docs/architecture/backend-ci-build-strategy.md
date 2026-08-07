# 后端 CI 构建策略（MAN-669 PR-B / PR-C 实测裁决）

> 本文分两篇。
> **上篇（PR-B）**：后端快速门禁的四条 shard，应该各自 restore/build 自己的项目集合，
> 还是把构建提到一个 build job 里做一次、再把产物分发给各 shard？
> **结论：保持"每片精确构建自己的项目集合"，不采用 build-once/产物复用。**
> **下篇（PR-C）**：connector-hosts、OpenAPI drift、ERP 三个专项 job 又在构建什么，
> 其中有多少是它们不需要的？**结论：三个都已是精确构建，构建命令一律不改。**
> 两篇的结论都来自 hosted-runner 实测（PR-B 四次：热两次冷两次；PR-C 两次 A/B 探针 +
> 三次 in-situ 观测），不是推断。数字全部可回溯到文中列出的 run id 与它们日志里的
> `MAN669PROBE` / `MAN669PRC` 原始行。
>
> **本文是这些实测数字与论证的唯一权威。** `ci.yml` 注释、`scripts/verify-*.ps1` 的
> docstring/注释、契约测试注释、`implementation-readiness.md` 一律**只写结论并指回本文**，
> 不复述数字、run id 与推导过程 —— 复述过的事实在 MAN-664 重测时必然只改一处、剩下三处
> 变成陈旧断言（PR-B 那轮走查已经在 `test-evidence-governance.md` 的「8m step budget」上
> 抓到过一次同样的漂移）。要改数字，改这里；别处只更新指针。

## 结论一句话

`dotnet build backend/Nerv.IIP.sln` 的**整解决方案构建本身（195.7–233.2 s）就比任何一片自己的
restore+build（62.7–180.9 s）更贵**。build-once 把这段成本从"四片并行各付一份"改成
"全流水线串行付一份"，因此即使产物传输完全免费，关键路径也会变长。实测传输并不免费：
产物原始 3.03 GB、tar.zst 后 1.01 GB，打包 15.5–16.0 s、上传 6–14 s、下载 11–57 s、解包 5.2–6.1 s。

## 度量方式

四次 CI run，每次同时跑：

1. **现状**：四个真实 shard job，各自 `./scripts/run-backend-test-shard.ps1`，即
   `dotnet test <自己的 .slnf> --configuration Release`（一次 MSBuild 调用完成 restore+build+test）。
2. **方案 3 的显式拆分探针**：`dotnet restore <.slnf>` → `dotnet build <.slnf> -c Release --no-restore`
   → 再跑同一个受治理的 shard runner，得到 restore / build / test 三段耗时。
   注意第三段仍是完整的 `dotnet test`（受治理的 runner 不接受 `--no-build`），因此该段
   **含一次增量空转构建**，是纯测试时间的上界。
3. **方案 2 的 build-once 探针**：`dotnet restore backend/Nerv.IIP.sln` →
   `dotnet build backend/Nerv.IIP.sln -c Release --no-restore` → 量 `bin`/`obj` 体积 →
   tar.zst 打包 → 上传 artifact。
4. **方案 2 的消费端探针**：另一个 job 下载该 artifact、解包，再跑 `business-core-a` shard。

> **口径声明（必须先读）**：第 4 条的 job/step 名里带 "no-build"，但它调用的
> `scripts/run-backend-test-shard.ps1` 只拼 `dotnet test <slnf> --configuration Release …`，
> **没有传 `--no-build`，也没有 `--no-restore`**。所以消费端跑的是一次完整的 restore+build+test，
> 只是起点上躺着解包出来的产物。**票面方案 ② 的字面定义（"测试 shards 下载后 `--no-build --no-restore`"）
> 因此没有被直接实测**，第 4 条测到的是它的一个上界。下文「裁决」一节按最有利于 build-once 的
> 口径把这部分重复构建扣掉后重算，结论不变。

探针 job 是临时的，采集完即从 `ci.yml` 删除；它们从不是 required check。

| 代号 | run id | commit | NuGet 缓存 |
|---|---|---|---|
| H1 | `31139435243` | `5003a4d5c` | 热（命中） |
| H2 | `31139971326` | `ed165201c` | 热（命中） |
| C1 | `31140517256` | `21e9e27ba` | 冷（key 前缀临时改为 `man669cold1-`，日志确认 `Cache not found for input keys`） |
| C2 | `31141123938` | `63d751ee6` | 冷（同上，前缀 `man669cold2-`；必须换新前缀，否则 C1 结束时保存的缓存会让第二次变热） |

冷缓存做法与还原：把 `ci.yml` 里全部 `${{ runner.os }}-nuget-` 前缀临时改为
`${{ runner.os }}-nuget-man669coldN-`（`restore-keys` 用同一前缀，避免回落到旧缓存），
跑完即还原。合并进 main 的 `ci.yml` 中 cache key 与 PR-B 之前**逐字节相同**。

**样本不同质，如实登记**：H1 跑在 `5003a4d5c`，此时 `Nerv.IIP.Contracts.Mes` 还在 `bin/Debug`
产出（见本文末尾"PR-B 实际落地的改动"）；H2/C1/C2 在修复之后。也就是说"现状"这四个样本
跨了一次构建行为变更。该项目只是一个无依赖的 contracts 程序集，量级上不足以解释任何一条结论，
但四个样本不是同一份构建图，引用时应知道。

## 现状：四片各自构建（秒）

`job` 为 job 墙钟，`step` 为 `Test … shard` 步骤耗时。

| shard | H1 job/step | H2 job/step | C1 job/step | C2 job/step |
|---|---|---|---|---|
| BusinessGateway | 281 / 257 | 309 / 268 | 297 / 271 | 293 / 267 |
| Platform | 219 / 182 | 233 / 186 | 215 / 190 | 207 / 184 |
| Business Core A | 279 / 245 | 262 / 235 | 268 / 241 | 300 / 270 |
| Business Core B | 221 / 194 | 205 / 173 | 163 / 141 | 240 / 201 |
| **关键路径（最大 job）** | **281** | **309** | **297** | **300** |

## 每片的 restore / build / test 拆分（秒）

| shard | 阶段 | H1 | H2 | C1 | C2 |
|---|---|---|---|---|---|
| BusinessGateway | restore | 6.9 | 8.5 | 13.4 | 28.6 |
| | build | 96.2 | 82.8 | 91.9 | 95.2 |
| | test（含增量空转） | 190.5 | 154.2 | 183.5 | 169.6 |
| Platform | restore | 18.6 | 17.0 | 57.4 | 21.0 |
| | build | 136.2 | 133.8 | 123.5 | 122.4 |
| | test（含增量空转） | 102.9 | 114.0 | 103.9 | 99.8 |
| Business Core A | restore | 16.4 | 22.8 | 20.0 | 16.8 |
| | build | 125.2 | 141.5 | 123.5 | 121.2 |
| | test（含增量空转） | 172.8 | 177.9 | 165.1 | 148.7 |
| Business Core B | restore | 11.5 | 12.1 | 17.4 | 13.3 |
| | build | 52.4 | 50.6 | 52.4 | 53.5 |
| | test（含增量空转） | 148.6 | 153.9 | 158.3 | 150.4 |

各片自己的 restore+build（16 个读数）：**62.7 – 180.9 s**。
逐 run 的四片合计（runner 时间，非关键路径）**463.5 / 469.0 / 499.6 / 471.9 s**
——这就是"重复 restore/build"的总量。但四片是并行的，**关键路径上只有 max() 算数**，
逐 run 的 max 为 **154.8 / 164.3 / 180.9 / 143.4 s**，即 **143.4–180.9 s**。
这就是 build-once 最多能攻击的目标。

各片自己构建后 `backend/` 下的产物体积（`du -sb`，含 46 MB 源码，可忽略）：
BusinessGateway 0.80 GB、Platform 1.78 GB、Business Core A 1.33 GB、Business Core B 0.41 GB。
四个数都远小于整解决方案的 3.03 GB——**这就是"没有 shard 为跑少数项目重建整个 solution"的实测证据**，
不是靠读 `ci.yml` 断言。该性质在机器层面由 `scripts/verify-backend-test-shards.ps1` 中
**PR-B 之前就存在**的「solution filter 的项目集必须与 manifest 逐项相等」保证（一个列出全部 163
个项目的 `.slnf` 会因此被拒）；PR-B 只补上一个更窄的分支：拒绝把 `solutionFilter` 直接写成
`backend/Nerv.IIP.sln`（否则会被下游 JSON 解析报成"格式非法"，掩盖真正的问题）。

**显式拆分本身更慢**：`restore` + `build --no-restore` + `dotnet test`（三次 MSBuild 求值）在
Business Core A 上是 287–342 s，而现状一次 `dotnet test` 只要 235–270 s。因此 PR-B 也**不**把
shard 内部拆成三步。

## 方案 ①：单 runner build once 后同 job 多 test 命令（排除，不需另测）

这条不需要单独探针，用已有数据即可关掉：单 job 意味着四片的测试**串行**。
用整解决方案 restore+build 加上四片的 test 段（后者已是上界）：

| run | 四片 test 段之和 | + 整解 restore+build | = 单 job 下界 | 对比现状关键路径 |
|---|---:|---:|---:|---|
| H1 | 614.8 | 224.0 | **838.8** | 281 → 2.98× |
| H2 | 600.0 | 195.7 | **795.7** | 309 → 2.58× |
| C1 | 610.8 | 202.8 | **813.6** | 297 → 2.74× |
| C2 | 568.4 | 233.2 | **801.5** | 300 → 2.67× |

即使把 test 段里的增量空转全额扣掉（每片按最坏 ~60 s 计，共 240 s），仍在 555–599 s，
是现状的 1.8–2.1 倍。方案 ① 等于撤销 PR #1466 的分片本身，**排除**。

## 方案 ②：build-once + artifact（不采用）

生产端（build job）：

| | H1 | H2 | C1 | C2 |
|---|---|---|---|---|
| 整解决方案 restore+build | 224.0 | 195.7 | 202.8 | 233.2 |
| 产物原始体积 | 3.0285 GB（326 目录） | 同 | 同 | 同 |
| tar.zst 后 | 1.0084 GB | 1.0083 GB | 1.0004 GB | 1.0083 GB |
| 打包耗时 | 16.0 | 15.9 | 15.5 | 15.8 |
| `upload-artifact` 步骤 | 14 | 10 | 6 | 10 |
| **build job 墙钟** | **266** | **242** | **238** | **276** |

消费端（下载 + 解包 + 跑 `business-core-a`，**完整 `dotnet test`，非 `--no-build`**）：

| | H1 | H2 | C1 | C2 |
|---|---|---|---|---|
| `download-artifact` 步骤 | 11 | 35 | 46 | 57 |
| 解包 | 5.2 | 6.1 | 5.6 | 5.6 |
| shard 运行 | 231.2 | 172.2 | 277.5 | 269.0 |
| **consume job 墙钟** | **260** | **231** | **345** | **347** |

**不盲目上传整个 `bin`/`obj`**：上传的是单个 tar.zst（`compression-level: 0`，
因为 zstd 已经压过），而不是把三万多个文件交给 artifact 自己打 zip；这已经是 build-once
能拿到的最好形态。

**消费端的重复构建有多贵**：把 consume 端的 shard 运行时间与同一 run 里同一片
（`business-core-a`）拆分探针的 test 段相比——后者是"自己刚构建完再跑一次 `dotnet test`"，
两者的差就是消费端白付的构建：

| | H1 | H2 | C1 | C2 |
|---|---:|---:|---:|---:|
| consume 端 shard 运行 | 231.2 | 172.2 | 277.5 | 269.0 |
| 拆分探针 test 段 | 172.8 | 177.9 | 165.1 | 148.7 |
| **差（白付的构建）** | **+58.4** | −5.6 | **+112.4** | **+120.3** |

真正传了 `--no-build --no-restore` 的话，这段应当趋近于 0。裁决因此按扣除后的口径重算。

## 裁决

关键路径对比（build-once 的两段是**串行**的：consume `needs` build；实测中 consume job
在 build job 完成后 3–5 s 启动）。

**原始口径**（探针实际测到的，含消费端未消除的重复构建）：

| run | 现状 | build job + consume job | 差 |
|---|---|---|---|
| H1 | 281 | 266 + 260 = 526 | +87% |
| H2 | 309 | 242 + 231 = 473 | +53% |
| C1 | 297 | 238 + 345 = 583 | +96% |
| C2 | 300 | 276 + 347 = 623 | +108% |

**修正口径**（按票面 ② 的字面定义重算，取最有利于 build-once 的假设）：
consume job = 30 s job 固定开销 + download + 解包 + **最慢那一片的 test 段**。
其中 30 s 取自现状四片 job 的实测固定开销 26–46 s 的下界（checkout/setup-dotnet/cache/
resolve-evidence/collect/upload evidence，真正的 shard job 一样要付）；
test 段本身还含一次增量空转，所以这已经是 build-once 的**上界性能**：

| run | 现状 | 修正后 build-once | 差 |
|---|---|---|---|
| H1 | 281 | 266 + (30+11+5.2+190.5=236.7) = **502.7** | **+79%** |
| H2 | 309 | 242 + (30+35+6.1+177.9=249.0) = **491.0** | **+59%** |
| C1 | 297 | 238 + (30+46+5.6+183.5=265.1) = **503.1** | **+69%** |
| C2 | 300 | 276 + (30+57+5.6+169.6=262.2) = **538.2** | **+79%** |

**四次实测、两种口径一致：build-once 更慢 59%~108%，差距远大于抖动。**
票面写明"只有实测更快才采用"、"不强制采用某一种 artifact/cache 设计"，
因此 PR-B 的交付是**不采用**，并把上面这组数据固化在本文。

**为什么必然更慢（结构性原因，不是调参问题）**：整解决方案构建本身
（195.7–233.2 s）就比**任何**一片自己的 restore+build（62.7–180.9 s）更贵。
build-once 把这段成本从"四片并行各付一份"改成"全流水线串行付一份"，
所以即使产物传输完全免费、`--no-build` 完全生效，关键路径也会变长。

同时不采用的还有：shard 内部拆成 `restore` + `build --no-restore` + `dotnet test` 三步
（实测更慢，见上）。既然没有采用任何跨 job 的产物复用，也就没有"用别的 SHA/配置的产物跑测试"
这条风险面——每片仍然在自己的 job 内、从自己这次 checkout 的源码构建。
**"防陈旧产物验证"因此不适用**，不做成一条恒真断言摆着。PR-B 落地的是**同一族的另一个
真实缺陷**：配置一致性，见下文。

### 维护成本（票面裁决轴之一）

现状的维护面是**一处**：`scripts/backend-test-shards.json` 决定每片跑哪些项目，
`.slnf` 与它逐项对齐、由门禁强制。build-once 会新增：一个 build job（及其超时预算与
tier 分类）、一份 1 GB 级 artifact 的命名/保留/清理策略、`needs` 依赖导致的**串行失败面**
（build job 挂掉四片全部不跑、且四片的 MAN-661 证据包一个都产不出来）、
以及产物与 SHA/配置一致性的校验及其自测。这些成本换来的是一个实测更慢的流水线——
即使耗时打平也不值得，何况慢 59% 以上。

## 冷缓存 vs 热缓存

`actions/cache` 恢复步骤：热 4–7 s（命中），冷 0–1 s（未命中，什么都没恢复）。
冷缓存下 `dotnet restore` 需要真下载：整解决方案 18.8–44.1 s，单片 13.3–57.4 s。

四片 test step 的冷热对照（秒）：

| shard | 热（H1, H2） | 冷（C1, C2） |
|---|---|---|
| BusinessGateway | 257, 268 | 271, 267 |
| Platform | 182, 186 | 190, 184 |
| Business Core A | 245, 235 | 241, 270 |
| Business Core B | 194, 173 | 141, 201 |

**冷热差落在 runner 抖动之内**：Business Core B 的四个读数是 194 / 173 / 141 / 201 s，
最快的一次恰好是冷缓存（C1），跨度 60 s；Business Core A 最慢的一次也是冷缓存（C2, 270 s），
但同为热缓存的 H1/H2 自己就差 10 s。NuGet 缓存值得留着（它几乎不花钱），
但它**不是**这条流水线的杠杆，任何"加缓存提速"的方案都要先跨过这个事实。

## PR-B 实际落地的改动

1. **`backend/common/Contracts/Nerv.IIP.Contracts.Mes` 加入 `backend/Nerv.IIP.sln`。**
   它是 163 个后端项目里唯一没登记进解决方案的一个，只能被 9 个 `.Web` 项目经
   `ProjectReference` 传递引用。MSBuild 通过解决方案的 configuration map 解析项目配置，
   不在 map 里的项目回落到自身默认配置，于是**四条 shard 的 `--configuration Release`
   构建都把它产出到 `bin/Debug`**：Release 测试程序集链接的是 Debug 依赖，而构建输出里
   没有任何东西会失败。run `31136085020` 的四条 shard 日志各有一行
   `-> …/Nerv.IIP.Contracts.Mes/bin/Debug/net10.0/Nerv.IIP.Contracts.Mes.dll`；
   修复后 run `31141964632` 四条 shard 日志的 `bin/Debug` 出现次数全部为 0。
2. **`scripts/verify-backend-test-shards.ps1` 把"必须是解决方案成员"从 `*.Tests.csproj`
   扩到 `backend/**` 下的每个 `csproj`**，并在失败信息里写明 Release/Debug 后果。
   自测在 `scripts/tests/backend-test-shards.Tests.ps1`：种一个**非测试**项目
   （`backend/common/Nerv.IIP.TemporarySolutionMembership`），它对旧的 Tests-only 规则不可见，
   因此把新检查削弱掉该用例立刻红。
3. **拒绝把 shard 的 `solutionFilter` 指成整个解决方案**，同一份自测里用临时 manifest 覆盖。
   注意这只是补窄口：一个"列出全部项目的 `.slnf`"早已被 PR-B 之前就存在的
   「filter 与 manifest 逐项相等」检查挡住。

## 复评触发条件（可执行）

本文的结论建立在一个可量化的余量上：**整解决方案 restore+build 减去最慢那一片自己的
restore+build**。四次实测分别是 **69.2 / 31.4 / 21.9 / 89.8 s**（H1/H2/C1/C2）。
C1 只剩 **21.9 s**——余量本来就不厚。

出现下列任一情况时，本文结论**必须重测，不得沿用**：

1. **最慢一片的 restore+build ≥ 整解决方案 restore+build 的 90%**（即余量 < 约 20 s）。
   MAN-664 的重新配平、或把某一片继续拆细，都会把最慢片的构建闭包推向整解；
   一旦推到这个线，build-once 的串行成本就不再天然大于它省下的量。
   量法：把本文方案 3 的拆分探针照抄回 `ci.yml` 跑一次即可，不需要 build-once 探针。
2. **runner 规格变化**（例如换更大规格的 hosted runner 或自托管 runner）。
   它同时改变 build 与 test 两侧，本文的比值不可直接沿用。
3. **artifact 传输成本出现数量级改善**（例如 `upload/download-artifact` 大版本变更、
   或改用运行器本地缓存分发）。当前 1 GB 级传输是 11–57 s 下载，还不是主要项；
   若它降到接近 0，重算的仍是第 1 条的余量。
4. **测试总时长大幅下降**（例如某片的重程序集被再次拆分，类似 MAN-663 对
   BusinessGateway 做的事）。测试变短会让构建成本在关键路径中的占比上升，
   从而放大 build-once 的潜在收益。

不满足以上任何一条时，请直接引用本文，不要从头再论证一遍。

---

# 三个专项 job 的构建盘点（MAN-669 PR-C）

> 上半篇回答"四片要不要共享构建"。本节回答票面 scope 第 7 条**前半句**：
> **connector-hosts、OpenAPI drift 和 ERP 专项这三个 job 各自在构建什么，其中有多少是它们
> 其实不需要的。** 结论：**三个 job 都已经是精确构建，没有任何一个为了跑少数项目去构建整个
> 解决方案，"收敛到精确项目集合"这条收益假设的实际值为 0。** 盘点过程中发现并修复了一个
> 与 PR-B 同族的真实缺陷（ConnectorHost 解决方案的 Release/Debug 配置泄漏）。
>
> **scope 7 的后半句「跨业务验收统一后由 scenario runner 精确构建需要的服务」没有被本文实现，
> 也不由 MAN-669 承担。** 它自带前置条件（"跨业务验收统一后"），而那个前置条件是仍然 OPEN 的
> **[#1240](https://github.com/Mang-X/Nerv-IIP/issues/1240)
> 「[P1][CI/Integration] 建立统一跨业务验收矩阵，收编 ERP 单链路特例」**。
> 在 #1240 落地之前不存在"统一的跨业务验收"，也就不存在可以承担精确构建职责的 scenario runner。
> 本文对 ERP job 的裁决（不改构建命令）正是在"它仍然是一个单链路特例、自己硬编码五个项目"
> 这个前提下做出的；#1240 落地时应当连同下面的复评触发条件一起重看。

## 口径：跨 job 的"重复"与 job 内的"重复"不是一回事

三个专项 job 与四片 fast shard 是**并行**的独立 runner。它们的依赖闭包确实大量重叠，但
**并行 job 之间的重复构建只花 runner 分钟，不花关键路径墙钟**；要把它变成收益，唯一途径是
跨 job 复用产物，而 PR-B 已用四次实测否掉了这条路（更慢 59%~108%，见上半篇）。
**PR-C 因此只把"同一个 job 内部的重复"当作可攻击面**，跨 job 的交集只做登记。

三个 job 都不在关键路径上，这一点先摆在前面（三次实测，job 墙钟，秒）：

**口径与本文上半篇一致：关键路径 = 该 run 里最大的 job，不是最慢的 shard。**
这不是措辞问题——run `31145563813` 的最大 job 是 `Frontend Typecheck and Build`（305 s），
比该 run 最慢的 shard（287 s）还长。按"最慢 shard"算会低估余量，并让下面的复评触发条件偏早触发。

| | run `31143773140` | run `31138913408` | run `31145563813` |
|---|---:|---:|---:|
| **关键路径（最大 job）** | **298**（Business Core A） | **307**（BusinessGateway） | **305**（Frontend） |
| 参考：最慢 shard | 298 | 307 | 287 |
| ERP Sales Order Demand Acceptance | 232 | 271 | 229 |
| OpenAPI/api-client Drift | 117 | 142 | 95 |
| Connector Host Tests | 102 | 117 | 118 |

即：**把这三个 job 的构建时间全部清零，CI 的墙钟一秒都不会短**——它们分别比关键路径短
36–76 s、165–210 s、187–196 s。任何改动只能以 runner 分钟、可维护性或正确性为理由，
不能以"提速 CI"为理由。

## 逐 job 盘点

### 1. ERP Sales Order Demand Acceptance

**它构建什么**（`scripts/verify-erp-sales-order-demand-planning.ps1`，非推测：脚本里是硬编码的
五个 `csproj` 路径 + 一个 `foreach`）：

```
dotnet build <MasterData.Web|Erp.Web|DemandPlanning.Web|FullChain.Tests|DemandPlanning.Web.Tests> -m:1 -nr:false
```

五次**串行**、单进程（`-m:1`）、不复用 MSBuild node（`-nr:false`）、不带 `--configuration`
（因此全程 Debug，与后续 `dotnet run --no-build` / `dotnet test --no-build` 一致）。
**没有 `dotnet build backend/Nerv.IIP.sln`，也没有任何一次整解构建。**

**耗时拆分**（`Invoke-DotNet` 的 `durationMs` 原始行，两次 run）：

| 项目 | run `31143773140` | run `31138913408` | run `31145563813` |
|---|---:|---:|---:|
| MasterData.Web | 45.3 | 67.4 | 42.0 |
| Erp.Web | 16.2 | 16.1 | 16.7 |
| DemandPlanning.Web | 6.4 | 6.4 | 7.1 |
| FullChain.Tests | 33.2 | 32.3 | 33.7 |
| DemandPlanning.Web.Tests | 6.6 | 6.4 | 6.9 |
| **构建合计** | **107.6** | **128.6** | **106.4** |
| `Verify ERP …` step | 210 | 249 | 207 |
| **构建占比** | **51%** | **52%** | **51%** |

另一半（约 100–120 s）是 compose 起 PostgreSQL+Redis、建一次性数据库、起三个真服务并等
health、跑完整验收剧本、两次 `--no-build` 探针测试、以及清理。这部分是这个 job 的**本体**，
与构建策略无关。

**先记住这个抖动量**：同一份工作在三次 run 里是 107.6 / 128.6 / 106.4 s，跨度 **22.2 s**。
下面探针测出来的收益要放在这个尺度上读。

**与 fast shard 的交集**：`DemandPlanning.Web.Tests` 同时属于 fast shard `business-core-b`
（那边是 Release）；`FullChain.Tests` 属于 heavy lane `full-chain`，不在任何 fast shard 里；
三个 `.Web` 服务项目在 `business-gateway` / `business-core-b` 两片的依赖闭包内。
按上面的口径，这些都是并行 job 之间的交集，不构成可攻击面。

**job 内部的可攻击面**：五次独立 MSBuild 调用对同一个重叠闭包各求值一次。这正是票面背景段
"会再次串行构建 …" 指的东西。收益用探针实测，见下节，**不靠估算**。

### 2. Connector Host Tests

**它构建什么**：一次 `dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln --configuration
Release`——一次 MSBuild 调用、一个解决方案，而该解决方案里的每个项目要么本身是被测程序集，
要么是被测程序集的依赖。**没有多余项目，也没有 job 内重复调用，无可攻击面。**

耗时：job 墙钟 102 / 117 s，`Test connector host solution` step 72 / 75 s；
该 lane 的 committed baseline TRX 为 55.2 s，即 restore+build 约 17–20 s。

**与 backend 的交集**：闭包里有 8 个 `backend/common/**` 项目（SDK 与 contracts）。
这不是新引入的跨引用——`ProjectReference` 本来就在，解决方案里本来就登记了其中 5 个。
`connector-hosts/` 与 `backend/` 仍是两个独立 `.sln`，本 PR 没有合并它们，也没有新增任何
跨目录引用（AGENTS.md "Do NOT" #2 未被触碰）。

**盘点中发现的真实缺陷（已修）**：那 8 个里只有 5 个是解决方案成员
（`Sdk.Core`、`Sdk.Auth`、`Sdk.ConnectorProtocol`、`Contracts.ConnectorProtocol`、`ServiceAuth`）。
另外 3 个——`Sdk.Ops` → `Contracts.Ops` → `Contracts.IntegrationEvents`——只能经
`ConnectorHost.Application` 的 `ProjectReference` 传递到达。MSBuild 通过解决方案的
configuration map 解析项目配置，不在 map 里的项目回落到自身默认配置，于是
**`--configuration Release` 的构建把这三个产出到 `bin/Debug`**：Release 测试程序集链接
Debug 依赖，构建输出里没有任何东西会失败。

两次独立 run 各三行原始日志（run `31143773140` job `92758931855`、
run `31138913408` job `92744407293`）：

```
Nerv.IIP.Contracts.IntegrationEvents -> …/backend/common/Contracts/…/bin/Debug/net10.0/….dll
Nerv.IIP.Contracts.Ops               -> …/backend/common/Contracts/…/bin/Debug/net10.0/….dll
Nerv.IIP.Sdk.Ops                     -> …/backend/common/Sdk/…/bin/Debug/net10.0/….dll
```

这与 PR-B 在 `backend/Nerv.IIP.sln` 里发现的 `Nerv.IIP.Contracts.Mes` 是**同一个缺陷类**，
只是发生在另一个解决方案上。PR-B 的门禁挡不住它：那条规则是**目录规则**（`backend/**` 下
每个 `csproj` 必须是 `backend/Nerv.IIP.sln` 的成员），按构造只认识一个解决方案，而本次泄漏的
三个项目恰恰是 `backend` 解决方案的合法成员。

### 3. OpenAPI/api-client Drift

**它构建什么**（`scripts/verify-openapi-client-drift.ps1` → `scripts/export-gateway-openapi.ps1`）：

```
dotnet build <PlatformGateway.Web|BusinessGateway.Web> -o artifacts/openapi-export/<name> /p:UseSharedCompilation=false
```

两个项目，两次调用，不带 `--configuration`（Debug），随后各起一个进程抓 `swagger.json`；
之后是 `pnpm install --frozen-lockfile` + `pnpm generate:api` + `git diff`。
**没有整解构建，也没有构建任何一个不需要的项目**——导出 OpenAPI 就是要真跑这两个网关。

耗时：job 墙钟 117 / 142 / 95 s，`Verify OpenAPI/api-client drift` step 92 / 118 / 76 s；
其中 `export-gateway-openapi.ps1`（两次构建 + 起两个网关 + 抓两份 swagger）65.5 s（run
`31143773140`）与 44.2 s（run `31145563813`），`pnpm install` 6.9 / 5.5 s、`generate:api`
4.5 / 3.7 s——原始 `Invoke-*` `durationMs` 行。**导出脚本本身占该 step 的绝大部分，而构建又占
导出脚本的绝大部分**（探针实测两次构建单独就要 48.4 / 57.9 s，见下节；它跨越了 in-situ 那两个
读数，说明这里 runner 抖动大于任何可比结构差异）。

**与 fast shard 的交集**：两个网关的 `.Web` 项目在 `business-gateway` / `platform` 两片的
依赖闭包内（那边是 Release）。同样是并行 job 之间的交集。

**job 内部的可攻击面**：两次独立 MSBuild 调用共享 `backend/common/**` 闭包。
与 ERP 同类，但只有两次而不是五次，量级更小。用探针实测，见下节。

## 探针：把"串行多次调用"的代价量出来

四个临时探针 job，各自独占一个 fresh runner、同一个 commit、同一个 NuGet cache key，
跑完即从 `ci.yml` 删除；它们从不是 required check。样本取自
run `31145563813`（S1）与 run `31145875145`（S2）。
`openapi-consolidated` 只有 S2 一个样本，因为它是在 S1 之后才加进来的——如实登记。

### ERP：5 次串行调用 vs 1 次合并调用

`serial` 臂逐字复刻脚本现状（5 次 `dotnet build <csproj> -m:1 -nr:false`）；
`consolidated` 臂用一个只含这 5 个项目的临时 `.slnf` 做一次 `dotnet build`。

| | S1 (`31145563813`) | S2 (`31145875145`) |
|---|---:|---:|
| serial 合计 | 133.7 | 125.1 |
| ├ MasterData.Web | 67.3 | 59.5 |
| ├ Erp.Web | 17.2 | 17.3 |
| ├ DemandPlanning.Web | 7.3 | 7.2 |
| ├ FullChain.Tests | 34.8 | 34.2 |
| └ DemandPlanning.Web.Tests | 7.1 | 6.9 |
| consolidated | 117.3 | 93.8 |
| **差** | **−16.4 s（−12.3%）** | **−31.3 s（−25.0%）** |

**两臂的产出规模一致**：`bin/Debug/net10.0/*.dll` 均为 **2460** 个、`bin/Release` 均为 0。
**这是计数相等，不是逐文件哈希或清单比对**——它足以排除"少构建了东西换来的假收益"这一种解释，
不足以证明字节级等价。真要采用，等价性应当用文件清单 + 哈希重新验证一次，本次没有做。

**变量分离状况**：`consolidated` 臂除了把 5 次调用合并成 1 次，**同时**丢掉了 `-m:1` 与
`-nr:false`（MAN-517 引入脚本时就带着的两个开关），所以 16.4–31.3 s 里有多少来自"少求值四次
依赖图"、多少来自"放开 MSBuild 并行"，**本次没有分离**。详见下面的裁决与复评触发条件。

### OpenAPI：2 次串行调用 vs 1 次合并调用

`split` 臂逐字复刻 `export-gateway-openapi.ps1`（每个网关一次
`dotnet build … -o artifacts/openapi-export/<name> /p:UseSharedCompilation=false`）。

| | S1 | S2 |
|---|---:|---:|
| split 合计 | 48.4 | 57.9 |
| ├ PlatformGateway | 24.9 | 32.9 |
| └ BusinessGateway | 23.5 | 25.1 |
| consolidated | — | 46.3 |
| **差** | — | **−11.6 s（−20.0%）**；对 S1 的 split 则是 **−2.1 s（−4.3%）** |

这里第二个网关几乎和第一个一样贵（23.5 / 25.1 s），因为 `-o` 换了输出目录就要把整条闭包
重新拷贝一遍——这就是这个 job 内部真正的重复项。

**两臂产出不同**：`split` 全进 `-o` 目录，`bin/Debug` 与 `bin/Release` 都是 0 个 dll；
`consolidated` 落回各自 `bin/Debug`（219 个 dll）。因此 OpenAPI 这一侧**不是 drop-in**：
真要采用，`export-gateway-openapi.ps1` 得改成从每个项目自己的 `bin/Debug/net10.0` 启动网关，
而不是从共享的 `-o` 目录。

**变量分离状况（与 ERP 同样如实登记）**：这两臂之间变了**两个**东西——调用次数（2→1）
**和** 输出布局（`-o <dir>` → 默认 `bin/`）。`-o` 换目录本身就要求把整条闭包重拷一遍，
所以 2.1–11.6 s 里**分不清**多少来自"少一次 MSBuild 求值"、多少来自"少一次全量拷贝"。
另外 `consolidated` 只有 S2 一个样本（它是 S1 之后才加进来的探针），
而 `split` 的两个样本本身就差 9.5 s——**这个收益区间的下沿完全落在单臂抖动之内**。

## 裁决：三个 job 的构建命令都不改

**1. "收敛到精确项目集合"的收益 = 0。** 三个 job 没有一个在构建它不需要的东西，
也没有一个执行 `dotnet build backend/Nerv.IIP.sln`。票面验收里"没有 shard 为运行少数项目
重新执行整解构建"这一条，对这三个 job 同样成立，而且是从脚本源码与 run 日志两侧核过的。

**2. "消除 job 内串行重复调用"的收益是真的，但买不到任何东西。**

| | 实测收益 | 该 job 距关键路径（最大 job） | CI 墙钟收益 |
|---|---|---|---|
| ERP | 16.4–31.3 s | 低 36–76 s | **0** |
| OpenAPI | 2.1–11.6 s | 低 165–210 s | **0** |
| Connector Host | 无重复可消 | 低 187–196 s | **0** |

ERP 的 16.4–31.3 s 还要放在它自己的抖动尺度上读：同一份串行构建在三次真实 run 里是
107.6 / 128.6 / 106.4 s（跨度 22.2 s），probe 的两次是 133.7 / 125.1 s。
**收益的下沿（16.4 s）小于这份工作自己的样本间跨度。**

**3. 采用的代价高于收益。**

- ERP：项目清单会从"脚本里一个硬编码数组"变成"脚本 + `.slnf` 两处必须一致"。
  清单漂移的后果不对称——多列一个只是白构建，少列一个会让后续
  `dotnet run --no-build` / `dotnet test --no-build` 跑到陈旧或缺失的产物上，
  而 `--no-build` 恰恰是这个 job 全程依赖的前提。要安全采用就得再加一条门禁 + 自测，
  换来 0 s 关键路径。
- ERP 还有一个未测变量：`consolidated` 臂**同时**丢掉了 `-m:1`（单进程）与 `-nr:false`
  （不复用 node）。这两个开关是 MAN-517 引入时就带着的。因此实测的 16.4–31.3 s
  里有多少来自"少求值四次依赖图"、多少来自"放开 MSBuild 并行"，**本次没有分离**。
  后者在这个 job 里不是免费的：同一台 runner 上还并存着 Docker、PostgreSQL、Redis
  和三个真服务进程。**保留 `-m:1 -nr:false` 的合并构建这一臂没有被测量**，如实登记。
- OpenAPI：不是 drop-in（见上），要改导出脚本的启动路径，收益 2.1–11.6 s。
- Connector Host：无事可做。

**因此 PR-C 不改这三个 job 的任何构建命令。**这与 PR-B 的裁决同源：
在这条流水线上，构建策略的改动只有落在关键路径上才有意义。

## PR-C 实际落地的改动

盘点本身不产生代码。落地的是盘点过程中发现的**一个真实缺陷**及其门禁：

1. **`Sdk.Ops`、`Contracts.Ops`、`Contracts.IntegrationEvents` 加入
   `connector-hosts/Nerv.IIP.ConnectorHost.sln`。** 修复前后各有 CI 实测：
   run `31143773140` / `31138913408` 的 Connector Host job 日志各有三行
   `-> …/bin/Debug/net10.0/…`；修复后 run `31145563813`（job `92764223035`）
   该 job 日志里 `bin/Debug` 出现次数为 **0**，三个项目全部落在 `bin/Release`。
   这不是新增跨引用：`ProjectReference` 本来就在，解决方案里本来就有 5 个同类
   `backend/common` 成员（`implementation-readiness.md` 明确写了两个解决方案可以共同引用
   这些公开契约/SDK 项目），本次只是把配置映射补全。两个 `.sln` 仍然独立。
2. **新增 `scripts/verify-solution-configuration-membership.ps1`**。
   真正决定一个项目用哪个 Configuration 的，是解决方案的**配置映射**
   （`GlobalSection(ProjectConfigurationPlatforms)` → `CurrentSolutionConfigurationContents`），
   **不是** `Project(...)` 声明行。一个项目有两种方式逃出这张映射表，产生**同一个** bin/Debug 症状：

   - **形态 1：根本不是成员。**只经传递 `ProjectReference` 到达，既没有 `Project(...)` 行，
     也没有映射条目。`Contracts.Mes`（PR-B）与本次三个项目都是这一形态。
   - **形态 2：是成员，但映射缺失或反向。**有 `Project(...)` 行——所以任何基于声明行的规则
     都会放行——但 `ProjectConfigurationPlatforms` 里缺它某个解决方案配置的 `.ActiveCfg`，
     或者把 `Release|*` 指向了 `Debug|*`。

   本脚本**两种都查**。形态 2 在本仓库尚未真实发生，但它离发生只有一次手改的距离：修形态 1
   意味着手写每项目 12 行映射（本 PR 三个项目 42 行），漏掉任何一行就在所有声明行规则全绿的
   情况下把缺陷装了回去。走查代理用一个两项目夹具解决方案实跑复现过这一形态
   （`--configuration Release` 下 `Lib -> …/bin/Debug/…`，补全映射后变 `bin/Release`）。

   与 PR-B 的关系：PR-B 那条是**目录规则**（`backend/**` 每个 `csproj` 必须在 `backend`
   解决方案里），按构造只认识一个解决方案，而本次泄漏的三个项目恰恰是 `backend` 解决方案的
   合法成员，所以它**看不见**形态 1 的这一例；它读的又只是 `Project(...)` 行，所以也**看不见**
   形态 2。两条规则互补保留：目录规则还能抓到"没人引用的孤儿项目"，本规则抓这两种形态。

   **解决方案是扫描发现的，不是列出来的**——否则将来第三个 `.sln` 会静默不受检，
   那正是本文批评 PR-B 规则的那种"按构造看不见"。
   支持 MSBuild glob 展开（仓库里 `Nerv.IIP.MigrationGovernance.Tests` 用了
   `..\..\services\**\*.Infrastructure.csproj`；把它当字面路径会报一个不存在的项目，
   并把真实发现全埋在后面）。**无 allowlist、无 owner-issue 逃生口**，理由与 PR-B 的目录规则相同。

   失败时把每条发现打到 stdout 再 `exit 1`（与 `check-script-governance.ps1` 同形），
   不用 `throw`：PowerShell 的错误格式化器会按终端宽度硬折行并给续行加 `|` 前缀，
   把 `Release|Any CPU` 这类标识符拦腰截断，既难读又让任何下游匹配依赖终端宽度。

3. **新增 `scripts/tests/solution-configuration-membership.Tests.ps1`**：8 步，全部是**行为断言**
   （跑 verifier、看它的真实输出），**没有一条源码文本断言**。
   真仓库必须通过，且必须从 verifier 自己的输出里看到**两个解决方案都被报告**——
   而不是"文件里提到过这两个路径"，后者在默认范围被收窄、路径残留在注释里时仍然是绿的。
   夹具覆盖：传递非成员必红且点名双方、登记后必绿、**声明成员缺 Release 映射必红**、
   **`Release` 映射到 `Debug` 必红**、映射完整必绿、glob 必须展开（`**` 跨目录、叶子模式不误伤、
   字面 glob 文本不得被当成路径）、以及**未登记的 `.sln` 丢进树里也必须被发现并检查**。
   五条变异验证（关闭形态 2 检查、关闭反向映射检查、把默认范围收窄成 backend-only 并把
   connector-host 路径留在注释里、glob 展开返回空集、发现跳过某个子树）**逐条实跑确认变红**。
4. **`docs/architecture/script-automation-governance.md`** 登记这两个新脚本。
5. **`ci.yml`**：上面两个脚本接入 required check `Script Governance`。
   该 job 是 tier B，不要求 `sum(step) < job`；step 预算合计 33m → 43m，
   最大单步 5m 仍小于 10m 的 job 预算，job 预算不变（本机实测 1–5 s）。

## 复评触发条件（可执行）

本节结论建立在"这三个 job 都远低于关键路径"上。出现下列任一情况时**必须重测，不得沿用**：

1. **任一 job 的墙钟接近或超过该 run 的最大 job。**当前余量 36–210 s。ERP 最薄（36 s），
   它又是唯一依赖 Docker 镜像拉取的 job，镜像变大或验收剧本变长都会吃掉这段余量。
   一旦 ERP 成为关键路径，上面实测的 16.4–31.3 s 立刻变成真收益，应当采用。
   注意用**最大 job** 判定而非"最慢 shard"：`Frontend Typecheck and Build` 已经在
   run `31145563813` 上超过了最慢 shard。
2. **想采用 ERP 合并构建时，必须先补两件事**：(a) 保留 `-m:1 -nr:false` 的第三臂，
   把"少求值四次依赖图"与"放开 MSBuild 并行"分开——只有前者的收益是无风险的；
   (b) 两臂产出的**文件清单 + 哈希**比对，本次只比了 dll 计数。
3. **ERP 验收脚本的项目清单继续变长。**当前 5 个；每多一个就多一次全闭包求值，
   合并调用的相对收益随之上升。
4. **`export-gateway-openapi.ps1` 改掉 `-o` 输出布局**（例如为别的原因改成默认 `bin/`），
   那时 OpenAPI 的合并构建就变成 drop-in，2.1–11.6 s 可以顺手拿走；同时这也会把当前
   "调用次数"与"输出布局"两个变量分开，让那个区间第一次变得可解释。
5. **#1240 落地、跨业务验收统一。**scope 7 后半句的 scenario runner 那时才有落点，
   ERP job 也不再是硬编码五个项目的单链路特例，本节对它的裁决前提随之失效。

## 遗留 / 下一步

- 关键路径当前 298–307 s，通常是最慢那一片，但**已经不总是**：run `31145563813` 上是
  `Frontend Typecheck and Build`（305 s）。后端侧继续压缩要靠**配平**（MAN-664，必须用配平后的
  实测重定权重）和**单片内部并行**（MAN-663 已在 BusinessGateway 上做过），不是靠构建策略；
  再往下压之前，先确认压的是当次真正的最大 job。三个专项 job 都在关键路径之下，
  改它们不影响这个数字。
- 票面方案 ② 的字面形态（`--no-build --no-restore`）仍未被直接实测。本文按最有利于它的
  上界口径否定了它；若上面的复评触发条件成立而需要重开，第一件事就是补这个直测。
- scope 7 后半句（scenario runner 精确构建需要的服务）阻塞于
  [#1240](https://github.com/Mang-X/Nerv-IIP/issues/1240)，**MAN-669 不承担**。

# 走查收尾（PR-B #1494 / PR-C #1495 合并后）

两个 PR 的双轴走查均**无阻断、维持合并现状**；下面四条是那两轮留下的判断题与微瑕的处置。
裁决本身（build-once 不采用、三个专项 job 不改构建命令）**未被触碰**。

1. **叙述收敛（两份走查各自提出）。**#1494:「实测数字同时写入 ci.yml 注释、verify 脚本注释、
   implementation-readiness、strategy 文档四处；前三处可只留结论 + 指针」；#1495:「『漏一行即
   复现』论证在 verifier docstring、测试注释、治理文档、readiness 四处近逐字重复；建议保
   strategy 文档一处权威」。已按本文开头的单一权威声明收敛：`ci.yml` 的 build-strategy 注释块、
   `verify-backend-test-shards.ps1` 的整解拒绝注释、`verify-solution-configuration-membership.ps1`
   的 docstring、`solution-configuration-membership.Tests.ps1` 的步骤 4 注释、
   `implementation-readiness.md` 的 PR-B/PR-C 段落全部改为结论 + 指回本文。
   **`ci.yml` 顶部超时策略块里的 tier A/B 算术依据（`3+5+8+3+10+5+5 = 39 < 45` 及其权重推导）
   原样保留** —— 那不是复述，而是 `Test-NervCiWorkflowBudgets` 的可审计依据，删掉门禁就失去底稿。

2. **整解拒绝分支改用真规范化。**#1494 微瑕 1：「`backend//Nerv.IIP.sln`（双斜杠）或绝对路径
   拼法会绕过新分支、落回『JSON 非法』误报」。原实现只剥一层 `^\./` 前缀。现在两侧都经
   `[System.IO.Path]::GetFullPath()` 相对仓库根规范化后比较，八种拼法（原样 / `./` /
   反斜杠 / 全小写 / 双斜杠 / `/./` / `../` 回绕 / 绝对路径）逐一实测落进整解分支，
   契约测试同时断言「不得被报成 invalid JSON」——只断言失败的话，删掉整条分支也是全绿。
   这从来不是安全洞（八种拼法本来就全部 `exit 1`），修的是诊断质量。

3. **两个 checker 的失败输出统一成 stdout + `exit 1`。**#1494 微瑕 2：「整 sln 回退用例只在异常
   Message 上 `Contains(...)`，折行截断会假红」；判断题 2 同源。

   **本条是「折行」与「退出码」两段论证的唯一权威**，两个 verifier、两个契约测试、
   `script-automation-governance.md` 与 `ci.yml` 都只留一句结论 + 指回这里。

   **为什么不用 `throw`。** PowerShell 的错误格式化器把抛出的消息**按终端宽度硬折行**，
   并给每条续行加 `| ` 前缀。后果有两层：长标识符（`Release|Any CPU`、长 csproj 路径）会被
   拦腰截断；即使断在词边界，被注入的 `| ` 也会插进任何跨行的长句中间。于是下游任何
   `Contains(...)` 匹配都变成**终端宽度的函数**——宽 runner 上绿、窄 runner 上红——只能退而
   匹配短到不会跨行的片段。改成把每条发现 `Write-Host` 到 stdout 再 `exit 1`（与
   `check-script-governance.ps1` 同形）之后，子进程 stdout 是原样文本，断言可以匹配整句。
   契约测试仍然折叠捕获文本的空白，那只是让「verifier 在哪里断行」不进入契约，与本问题无关。

   **因此调用方必须查退出码，而且不能与别的脚本共用一个 `run:` 块。** `exit` 在被点斜杠调用的
   `.ps1` 里**不会**中止调用方脚本；调用方继续往下跑，而后续脚本里任何 native 调用都会覆写
   `$LASTEXITCODE`。实测确认「前一个脚本 `exit 1` + 后一个脚本成功」的组合**整体退出 0**——
   失败被完全吞掉。`ci.yml` 里 `Backend Test Shard Governance` job 原本正是把 checker 与其契约
   测试写在同一个 `run:` 块里，已拆成两个 step（各 5m，step 预算合计仍是 18m，job 15m 不变）。
   > 这条「用 `exit 1` 报错的脚本必须独占一个 `run:` step」目前**只有约定、没有门禁**，
   > 走查建议另开票补，不在本 PR 范围内。

   落地：`verify-backend-test-shards.ps1` 与 `verify-solution-configuration-membership.ps1` 同形；
   `backend-test-shards.Tests.ps1` 的断言统一走 `Invoke-ShardValidator`（判退出码 + 完整 stdout），
   删掉了原先「从命令日志目录捞 stdout.log/stderr.log 补全被折断文本」的兜底，断言从短片段
   升级成整句。

4. **解决方案发现跳过 git 忽略的路径**（[#1496](https://github.com/Mang-X/Nerv-IIP/issues/1496)，
   #1495 走查的唯一新发现）。实测：过滤前从仓库根发现 **10** 个 `.sln`（**测于 2026-08-07 的
   一台开发机，该数字只反映当时本机有几个 agent 工作副本，明天就不是 10**），其中 8 个来自
   `/worktrees/` 下的工作副本；过滤后为该 checkout 自己的 2 个。

   采用 issue 的**修法 B**（跳过 `git check-ignore` 命中的路径），理由是 issue 自己给修法 A
   的那条批评：黑名单是**枚举式**的，下一个新工具目录还会漏，而 `.gitignore` 已经是这个问题
   的单一来源（`/worktrees/`、`/.claude/worktrees/`、`/.agents/worktrees/`、`/.codex/worktrees/`
   都在里面）。**注意不要把这条理由记错对象**：issue 的修法 A 写的是
   `` \.claude|\.agents|\.codex|worktrees ``，其中的 `worktrees` 能命中本次全部 8 条泄漏路径，
   所以「修法 A 修不了这次的泄漏」是**错的**；修不了的是 #1495 评审正文里那句「排除项补
   `\.claude`」—— 在 macOS/Linux 上 `.claude/` 是点目录，`Get-ChildItem -Recurse` 不带 `-Force`
   本来就不下钻，checker 眼里那底下的 `.sln` 数量是 0，补它一条都命中不了。

   CI 是干净 checkout，不受影响；发现语义不变——git 跟踪的一切仍然被发现，包括没人登记过的
   第三个 `.sln`。空扫描仍然失败（不得 vacuously pass），契约测试用「同一棵树 + 有/无忽略
   规则」的成对断言证明该过滤削弱即红。
