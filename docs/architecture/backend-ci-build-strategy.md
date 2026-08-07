# 后端 CI 构建策略（MAN-669 PR-B 实测裁决）

> 本文回答一个问题：后端快速门禁的四条 shard，应该各自 restore/build 自己的项目集合，
> 还是把构建提到一个 build job 里做一次、再把产物分发给各 shard？
> **结论：保持"每片精确构建自己的项目集合"，不采用 build-once/产物复用。**
> 结论来自 2026-08-06/07 的四次 hosted-runner 实测（热缓存两次、冷缓存两次），
> 不是推断。数字全部可回溯到下面列出的 run id。

## 结论一句话

`dotnet build backend/Nerv.IIP.sln` 的**整解决方案构建本身（196–233 s）就比任何一片自己的
restore+build（64–181 s）更贵**。build-once 把这段成本从"四片并行各付一份"改成"全流水线串行付一份"，
因此即使产物传输完全免费，关键路径也会变长。实测传输并不免费：产物原始 3.03 GB、
tar.zst 后 1.01 GB，打包 16 s、上传 6–14 s、下载 11–57 s、解包 5–6 s。

## 度量方式

四次 CI run，每次同时跑：

1. **现状**：四个真实 shard job，各自 `./scripts/run-backend-test-shard.ps1`，即
   `dotnet test <自己的 .slnf> --configuration Release`（一次 MSBuild 调用完成 restore+build+test）。
2. **方案 3 的显式拆分探针**：`dotnet restore <.slnf>` → `dotnet build <.slnf> -c Release --no-restore`
   → 再跑同一个受治理的 shard runner（此时构建已是增量空转），得到 restore / build / test 三段耗时。
3. **方案 2 的 build-once 探针**：`dotnet restore backend/Nerv.IIP.sln` →
   `dotnet build backend/Nerv.IIP.sln -c Release --no-restore` → 量 `bin`/`obj` 体积 → tar.zst 打包 → 上传 artifact。
4. **方案 2 的消费端探针**：另一个 job 下载该 artifact、解包，再跑 `business-core-a` shard。

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
| | test | 190.5 | 154.2 | 183.5 | 169.6 |
| Platform | restore | 18.6 | 17.0 | 57.4 | 21.0 |
| | build | 136.2 | 133.8 | 123.5 | 122.4 |
| | test | 102.9 | 114.0 | 103.9 | 99.8 |
| Business Core A | restore | 16.4 | 22.8 | 20.0 | 16.8 |
| | build | 125.2 | 141.5 | 123.5 | 121.2 |
| | test | 172.8 | 177.9 | 165.1 | 148.7 |
| Business Core B | restore | 11.5 | 12.1 | 17.4 | 13.3 |
| | build | 52.4 | 50.6 | 52.4 | 53.5 |
| | test | 148.6 | 153.9 | 158.3 | 150.4 |

各片自己的 restore+build：**63.9–180.9 s**。四片合计（runner 时间，非关键路径）
463 / 469 / 500 / 472 s——这就是"重复 restore/build"的总量。**关键路径上只有 max() 算数，
即 124–181 s。**

各片自己构建后 `backend/` 下的产物体积（`du -sb`，含 46 MB 源码，可忽略）：
BusinessGateway 0.80 GB、Platform 1.78 GB、Business Core A 1.33 GB、Business Core B 0.41 GB。
四个数都远小于整解决方案的 3.03 GB——**这就是"没有 shard 为跑少数项目重建整个 solution"的实测证据**，
不是靠读 `ci.yml` 断言。该性质现在由 `scripts/verify-backend-test-shards.ps1` 显式拒绝
"把 shard 的 `solutionFilter` 指成 `backend/Nerv.IIP.sln`"来守住。

**显式拆分本身更慢**：`restore` + `build --no-restore` + `dotnet test`（三次 MSBuild 求值）在
Business Core A 上是 287–342 s，而现状一次 `dotnet test` 只要 235–270 s。因此 PR-B 也**不**把
shard 内部拆成 `--no-restore` / `--no-build` 三步。

## build-once：生产端（秒 / 字节）

| 指标 | H1 | H2 | C1 | C2 |
|---|---|---|---|---|
| `dotnet restore backend/Nerv.IIP.sln` | 30.9 | 17.5 | 18.8 | 44.1 |
| `dotnet build … -c Release --no-restore` | 193.1 | 178.2 | 183.9 | 189.1 |
| restore+build 合计 | **224.0** | **195.7** | **202.7** | **233.2** |
| `bin`/`obj` 原始体积 | 3.0285 GB（326 个目录） | 同 | 同 | 同 |
| tar.zst 后体积 | 1.0084 GB | 1.0083 GB | 1.0004 GB | 1.0083 GB |
| 打包耗时 | 16.0 | 15.9 | 15.5 | 15.8 |
| `upload-artifact` 步骤耗时 | 14 | 10 | 6 | 10 |
| build job 墙钟 | 266 | 242 | 238 | 276 |

**不盲目上传整个仓库 `bin`/`obj`**：这里上传的是单个 tar.zst（`compression-level: 0`，
因为 zstd 已经压过），而不是把三万多个文件交给 artifact 自己打 zip；这已经是 build-once
能拿到的最好形态，仍然不够。

## build-once：消费端（秒）

下载同一份 artifact、解包，再跑 `business-core-a` shard：

| 指标 | H1 | H2 | C1 | C2 |
|---|---|---|---|---|
| `download-artifact` 步骤 | 11 | 35 | 46 | 57 |
| 解包 | 5.2 | 6.1 | 5.6 | 5.6 |
| shard 运行 | 231.2 | 172.2 | 277.5 | 269.0 |
| consume job 墙钟 | 260 | 231 | 345 | 347 |

对照同一 run 里 Business Core A 自己的 step（245 / 235 / 241 / 270 s）：产物复用把该片
自身耗时省下的量在 −33 s 到 +36 s 之间，**小于 runner 抖动**。原因可查：`actions/checkout`
会把源码 mtime 打成本 job 的 checkout 时刻，而解包出的产物 mtime 来自 build job，
MSBuild 的增量判定因此仍会重做相当一部分工作——产物复用不是解包完就自动生效的。

## 裁决

关键路径对比（build-once 的两段是**串行**的：consume 依赖 build）：

| run | 现状关键路径 | build-once 关键路径（build job + consume job） | 差 |
|---|---|---|---|
| H1 | 281 | 266 + 260 = 526 | +87% |
| H2 | 309 | 242 + 231 = 473 | +53% |
| C1 | 297 | 238 + 345 = 583 | +96% |
| C2 | 300 | 276 + 347 = 623 | +108% |

四次实测一致：**build-once 更慢，且差距远大于抖动**。票面写明"只有实测更快才采用"、
"不强制采用某一种 artifact/cache 设计"，因此 PR-B 的交付是**不采用**，并把上面这组数据固化在本文。

同时不采用的还有：shard 内部拆成 `--no-restore`/`--no-build` 三步（实测更慢，见上）。
既然没有采用任何跨 job 的产物复用，也就没有"用别的 SHA/配置的产物跑测试"这条风险面——
每片仍然在自己的 job 内、从自己这次 checkout 的源码构建。**"防陈旧产物验证"因此不适用**，
不做成一条恒真断言摆着。PR-B 落地的是**同一族的另一个真实缺陷**：配置一致性，见下节。

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

**冷热差落在 runner 抖动之内**（同一 commit 上同一片的两次热运行本身就能差 20+ s）。
NuGet 缓存值得留着（它几乎不花钱），但它**不是**这条流水线的杠杆，任何"加缓存提速"的方案
都要先跨过这个事实。

## PR-B 实际落地的改动

1. **`backend/common/Contracts/Nerv.IIP.Contracts.Mes` 加入 `backend/Nerv.IIP.sln`。**
   它是 163 个后端项目里唯一没登记进解决方案的一个，只能被 9 个 `.Web` 项目经
   `ProjectReference` 传递引用。MSBuild 通过解决方案的 configuration map 解析项目配置，
   不在 map 里的项目回落到自身默认配置，于是**四条 shard 的 `--configuration Release`
   构建都把它产出到 `bin/Debug`**：Release 测试程序集链接的是 Debug 依赖，而构建输出里
   没有任何东西会失败。run `31136085020` 的四条 shard 日志各有一行
   `-> …/Nerv.IIP.Contracts.Mes/bin/Debug/net10.0/Nerv.IIP.Contracts.Mes.dll`。
2. **`scripts/verify-backend-test-shards.ps1` 把"必须是解决方案成员"从 `*.Tests.csproj`
   扩到 `backend/**` 下的每个 `csproj`**，并在失败信息里写明 Release/Debug 后果。
   自测在 `scripts/tests/backend-test-shards.Tests.ps1`：种一个**非测试**项目
   （`backend/common/Nerv.IIP.TemporarySolutionMembership`），它对旧的 Tests-only 规则不可见，
   因此把新检查削弱掉该用例立刻红。
3. **拒绝把 shard 的 `solutionFilter` 指成整个解决方案**，同一份自测里用临时 manifest 覆盖。

## 遗留 / 下一步

- 关键路径仍是"最慢那一片"，当前 281–309 s。继续压缩要靠**配平**（MAN-664，必须用配平后的
  实测重定权重）和**单片内部并行**（MAN-663 已在 BusinessGateway 上做过），不是靠构建策略。
- ERP job 的重复构建去重是 MAN-669 PR-C，本文不涉及。
- 本文的数字来自 hosted `ubuntu-latest`。换 runner 规格（例如更大规格的 runner）会同时改变
  build 与 test 两侧，届时需要重测，不能直接沿用这里的比值。
