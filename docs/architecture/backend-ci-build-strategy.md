# 后端 CI 构建策略（MAN-669 PR-B 实测裁决）

> 本文回答一个问题：后端快速门禁的四条 shard，应该各自 restore/build 自己的项目集合，
> 还是把构建提到一个 build job 里做一次、再把产物分发给各 shard？
> **结论：保持"每片精确构建自己的项目集合"，不采用 build-once/产物复用。**
> 结论来自 2026-08-06/07 的四次 hosted-runner 实测（热缓存两次、冷缓存两次），
> 不是推断。数字全部可回溯到下面列出的 run id 与它们日志里的 `MAN669PROBE` 原始行。

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

> 上半篇回答"四片要不要共享构建"。本节回答票面 scope 第 7 条：
> **connector-hosts、OpenAPI drift 和 ERP 专项这三个 job 各自在构建什么，其中有多少是它们
> 其实不需要的。** 结论：**三个 job 都已经是精确构建，没有任何一个为了跑少数项目去构建整个
> 解决方案，"收敛到精确项目集合"这条收益假设的实际值为 0。** 盘点过程中发现并修复了一个
> 与 PR-B 同族的真实缺陷（ConnectorHost 解决方案的 Release/Debug 配置泄漏）。

## 口径：跨 job 的"重复"与 job 内的"重复"不是一回事

三个专项 job 与四片 fast shard 是**并行**的独立 runner。它们的依赖闭包确实大量重叠，但
**并行 job 之间的重复构建只花 runner 分钟，不花关键路径墙钟**；要把它变成收益，唯一途径是
跨 job 复用产物，而 PR-B 已用四次实测否掉了这条路（更慢 59%~108%，见上半篇）。
**PR-C 因此只把"同一个 job 内部的重复"当作可攻击面**，跨 job 的交集只做登记。

三个 job 都不在关键路径上，这一点先摆在前面（run `31143773140` / `31138913408` 两次实测）：

| job | 墙钟 | 关键路径（最慢 shard） |
|---|---|---|
| ERP Sales Order Demand Acceptance | 232 / 271 s | 281 / 307 s |
| OpenAPI/api-client Drift | 117 / 142 s | 同上 |
| Connector Host Tests | 102 / 117 s | 同上 |

即：**把这三个 job 的构建时间全部清零，CI 的墙钟一秒都不会短。**任何改动只能以
runner 分钟、可维护性或正确性为理由，不能以"提速 CI"为理由。

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

| 项目 | run `31143773140` | run `31138913408` |
|---|---:|---:|
| MasterData.Web | 45.3 | 67.4 |
| Erp.Web | 16.2 | 16.1 |
| DemandPlanning.Web | 6.4 | 6.4 |
| FullChain.Tests | 33.2 | 32.3 |
| DemandPlanning.Web.Tests | 6.6 | 6.4 |
| **构建合计** | **107.6** | **128.6** |
| `Verify ERP …` step | 210 | 249 |
| **构建占比** | **51%** | **52%** |

另一半（约 102 / 120 s）是 compose 起 PostgreSQL+Redis、建一次性数据库、起三个真服务并等
health、跑完整验收剧本、两次 `--no-build` 探针测试、以及清理。这部分是这个 job 的**本体**，
与构建策略无关。

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

耗时：job 墙钟 117 / 142 s，`Verify OpenAPI/api-client drift` step 92 / 118 s；
其中 `export-gateway-openapi.ps1` 65.5 s、`pnpm install` 6.9 s、`generate:api` 4.5 s
（run `31143773140` 的 `Invoke-*` 原始 `durationMs` 行）。

**与 fast shard 的交集**：两个网关的 `.Web` 项目在 `business-gateway` / `platform` 两片的
依赖闭包内（那边是 Release）。同样是并行 job 之间的交集。

**job 内部的可攻击面**：两次独立 MSBuild 调用共享 `backend/common/**` 闭包。
与 ERP 同类，但只有两次而不是五次，量级更小。用探针实测，见下节。

## 探针：把"串行多次调用"的代价量出来

（本节数字由 PR 内的临时探针 job 产出，探针跑完即从 `ci.yml` 删除，从不是 required check。）

## 裁决

## 复评触发条件

## 遗留 / 下一步

- 关键路径仍是"最慢那一片"，当前 281–309 s。继续压缩要靠**配平**（MAN-664，必须用配平后的
  实测重定权重）和**单片内部并行**（MAN-663 已在 BusinessGateway 上做过），不是靠构建策略。
  三个专项 job 都在关键路径之下，改它们不影响这个数字。
- 票面方案 ② 的字面形态（`--no-build --no-restore`）仍未被直接实测。本文按最有利于它的
  上界口径否定了它；若上面的复评触发条件成立而需要重开，第一件事就是补这个直测。
