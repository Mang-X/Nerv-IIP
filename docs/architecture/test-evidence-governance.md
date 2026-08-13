# 测试证据治理

MAN-661 为后端与 Connector Host 的 VSTest 运行提供仓库自有证据链路。责任方是 **Nerv-IIP 平台 CI/测试治理**（Nerv-IIP Platform CI/Test Governance）。本文档是操作契约；已批准的架构仍以 MAN-661 设计为准。

## 运行时与保留产物

CI 以 `--logger trx` 正常运行 `dotnet test`。测试步骤不使用 `continue-on-error`、shell 管道或状态恢复包装器，因此其自然退出码仍是权威结果。采集与上传使用 `if: always()`，使失败运行在存在规范化证据时仍能发布诊断。

自 MAN-669 起，后端快速门禁改为四个分片作业，不再由单个作业运行。每个分片作业运行 `scripts/run-backend-test-shard.ps1`，将 `trx;LogFilePrefix=<job id>` 输出到自身作业本地原始目录，随后针对自己拥有的唯一执行通道调用同一个单通道采集器（single-lane collector）：

| 执行通道 | CI 作业 | 分片 |
| --- | --- | --- |
| `backend-shard-1` | `Backend Tests - BusinessGateway` | `business-gateway` |
| `backend-shard-2` | `Backend Tests - Platform` | `platform` |
| `backend-shard-3` | `Backend Tests - Business Core A` | `business-core-a` |
| `backend-shard-4` | `Backend Tests - Business Core B` | `business-core-b` |
| `connector-host` | `Connector Host Tests` | — |
| `postgres` | `PostgreSQL Provider Tests` | `inventory-postgres-profile`、`masterdata-postgres-profile`、`scheduling-postgres-profile`、`apphub-postgres-profile`（`test-owned`）、`barcodelabel-postgres-profile`、`filestorage-postgres-profile`、`industrialtelemetry-postgres-profile`、`quality-postgres-profile`、`mes-postgres-profile`、`wms-postgres-profile`（`test-owned`）、`erp-postgres-profile`、`demandplanning-postgres-profile`（`test-owned`）、`acceptance-postgres-profile`、`maintenance-device-pause-postgres`（拆解②与③全八批） |
| `redis-cap` | `Redis/CAP Transport Tests` | `demandplanning-sales-order-redis-cap`（拆解④） |

`Backend Tests` 仍是稳定的必需聚合作业。它不运行测试、不拥有证据执行通道，只断言分片治理与全部四个分片作业成功。`scripts/verify-backend-test-shards.ps1` 从结构上强制执行该接线：执行通道/作业绑定、仅存原始结果的目录、精确的采集器参数，以及每个分片作业恰好一个脱敏证据产物。若分片作业上传原始目录、声称拥有另一条执行通道、通过 shell 管道包装运行器，或将采集降级为 `success()`，该门禁就会失败。

失败或超时的分片会在**脱敏后**将缓冲的 stdout/stderr 输出到 Actions 作业日志（Actions job log）；这些缓冲内容绝不会写入上传文件。

`run-backend-test-shard.ps1` 使用 `FullyQualifiedName!~` 排除真实依赖选择器，因此这些测试不会出现在分片 TRX 中，而不是以已登记跳过项出现。以下门禁确保该排除诚实可信，不会变成私自绕过门禁的入口：

- **政策闭合与责任执行通道推导。**每个快速分片排除选择器都必须解析到至少一个政策测试身份，其规则必须是带真实依赖 `requiredLane` 的 `environment-gated`。除非本文档的跳过政策已登记某项测试，否则不得将其移出默认门禁。分片声明的 `excludedTestLanes` 随后必须等于这些 `requiredLane` 值通过 `heavyLanes[].policyLane` 映射到的高成本执行通道，因此分片不能把 `redis-cap` 或 `full-chain` 排除归给真实 PostgreSQL 责任脚本。当前全部 63 个选择器都解析为 `postgres`、`redis-cap` 或 `full-chain`；强制机制位于 `verify-backend-test-shards.ps1`。NERV-688 拆解②与③八批已把 Inventory 的 1 个、MasterData 的 5 个、Scheduling 的 6 个、AppHub 3 个、BarcodeLabel/FileStorage/Maintenance 各 1 个、IndustrialTelemetry 7 个、Quality 8 个、MES 11 个、WMS 9 个、ERP 4 个、DemandPlanning 3 个、跨业务 Acceptance 3 个，共 63 个用例接入 hosted `postgres` job，拆解④把 DemandPlanning 的 2 个 Redis/CAP 用例接入 hosted `redis-cap` job；其余 PostgreSQL 登记仍由按需 `scripts/verify-backend-real-postgres-tests.ps1` 承接，须在拆解③继续逐服务接入，不能据此宣称全量已运行。
- **选择器锚定。**VSTest `!~` 是子串匹配，因此类选择器输出时带尾随点（`FullyQualifiedName!~Ns.XTests.`），不会误吞仅共享前缀的兄弟类。方法选择器保持无锚定，以便参数化用例继续匹配；治理通过扫描已登记的 MAN-661 源文件，并拒绝名称是该文件其他成员前缀的方法选择器来补偿这一点。
- **逐项目执行。**分片运行后，它分类的每个项目都必须在该分片自身 TRX 中出现，且至少有一个已执行结果；分片也不得执行未由其分类的程序集。该检查读取与采集器相同的 `UnitTest/@storage` 属性。它有意**不**扫描 dotnet 控制台文本：该文本会本地化，在任何非英文运行器上使用短语匹配都会失败后放行，而这正是该边界要阻止的静默放行。

作业运行期间，原始文件只存在于 `artifacts/test-evidence-raw/<run>/attempt-<n>/<lane>/`。原始 TRX（raw TRX）、stdout、stderr、附件、采集器载荷、请求/响应正文和任意结果文件绝不上传。保留产物经脱敏后写入：

```text
artifacts/test-evidence/<run>/attempt-<n>/<lane>/
├── trx/                 # reconstructed, normalized TRX only
├── tests.jsonl          # one schema-v1 record per runtime test
├── summary.json
├── summary.md
└── diagnostics.log
```

保留前会替换凭据 URL 用户信息、bearer 授权、带引号或不带引号的 password/token/secret/client_secret 值、PEM 块，以及名为 `customerName`、`phone`、`email` 和 `address` 的字段。名为 `body`、`requestBody` 或 `responseBody` 的参数化显示名称值按大小写不敏感方式匹配，并替换为不可逆的 16 位十六进制摘要标记；嵌套值、转义值和多个正文参数在结构上保持有界，而方法身份和非正文参数仍可用于跳过政策匹配与实例区分。原始失败测试消息和未登记的跳过原因按构造省略；已批准跳过原因限制为 512 个字符。采集器失败时会发布保留证据包，其中包含 `collectionStatus: failed`、`evidence-collection-failed` 摘要、经过允许清单且有界的运行身份/诊断，以及非零退出码。若请求的输出目录已包含文件，失败证据包使用第一个可用的确定性同级目录（`.failure`、`.failure-2`，……），并通过采集器步骤输出报告该精确路径；上传步骤使用该输出，绝不覆盖既有目录。保留期为 14 天。GitHub 权限仅为 `actions: read` 和 `contents: read`。

## CI 超时预算

只有作业存活到采集步骤，证据采集才可达。作业级 `timeout-minutes` 会取消**整个**作业，包括 `if: always()` 步骤，因此触及自身预算的作业不会发布任何证据——MAN-799 的 `Connector Host Tests` 挂起耗费了 28 分钟，且没有产出。

以下两条规则适用于 `.github/workflows/ci.yml` 中每个作业，第三条只适用于存在证据的作业：

1. **每个作业都声明 `timeout-minutes`。**未声明该值的作业会继承 GitHub 的 360 分钟默认值，并可能因死锁浪费完整的运行器小时计费块。
2. **每个显式步骤都声明 `timeout-minutes`**——包括 checkout、SDK/pnpm 设置、缓存恢复以及证据采集/上传步骤。`if: always()` 不会免除步骤预算要求。
3. **发布证据的作业必须使步骤预算总和严格小于作业预算。**这样作业预算实际上才不可达：某个步骤必须先超过自身预算，而步骤超时只会使该步骤失败，因此作业会继续进入 `Collect …` / `Upload …`，脱敏包仍可发布。只要存在一个无预算步骤，就会重新打开作业预算先触发并连同产物一起取消的路径。

规则 3 有意限定范围。四个后端快速分片（`backend-tests-business-gateway`、`backend-tests-platform`、`backend-tests-business-core-a`、`backend-tests-business-core-b`）、`postgres-provider-tests`、`erp-sales-order-demand-acceptance` 和 `connector-host-tests` 都有 `if: always()` 采集/上传步骤，因此确有可能损失的内容；它们的作业预算远高于任何真实运行时间，以覆盖步骤预算总和。`backend-tests`（MAN-669 留下的不运行测试的聚合作业）、`backend-test-shard-governance`、`frontend`、`openapi-client-drift` 和 `script-governance` 没有能在前一步失败后继续运行的步骤：活得比自身预算更久也不会保留任何内容，因此将预算抬高到步骤总和以上只会把快速失败变成缓慢失败。

因此在 B 级中，**作业**预算是根据观测运行时间设定的快速失败上限，步骤预算仅是逐步骤上限。这里有意不声称各步骤预算都可独立触发：`frontend` 在 20 分钟作业内的步骤预算合计为 58 分钟，因此其 15 分钟构建预算绝不可能自行触发——作业预算总会先到。门禁对 B 级强制执行的唯一规则，是任何调度下都不可能生效的情形：步骤预算大于或等于整个作业预算。两级都保留步骤预算；在 B 级中，它既记录健康步骤的成本，也是在作业只有一个长步骤时真正触发的上限。

### 如何判定作业级别

门禁读取每个步骤的 `if:`，只判断一个问题：同一作业中前面的步骤失败或超时后，该步骤是否仍可运行？`always()`、`!cancelled()` 和 `failure()` 在任何合法写法下答案都是肯定的，例如 `${{ always() }}`、`always() && github.event_name == 'push'` 或尾随 `# comment`。按照 GitHub 自身规则，不包含任何状态检查函数的 `if:` 表达式按 `success() && (expression)` 求值，因此答案是否定的。

分类会**失败关闭**：凡读取器无法从步骤本身判定的情况——例如延续到后续行的块标量 `if:`、YAML 别名或无法识别的函数调用——均按更严格的 A 级处理。该门禁第一版只匹配字面字符串 `always()`，其他每种写法都会悄然把作业降为 B 级并完全关闭规则 3；当前分类为每种写法都提供夹具，包括不得升级的反向对照。

### 步骤预算的取值及余量覆盖范围

步骤预算是留有余量的整数上限，并非某个数值的统一倍数：长步骤（`dotnet test`、`pnpm build`、验证脚本）约为近期运行历史中观测最大值的 2 倍；checkout、`setup-dotnet`/`setup-node`/`pnpm/action-setup` 和缓存恢复等短暂固定成本步骤，则使用健康运行远不会接近的 3–8 分钟下限。把“约为观测值 2 倍”套到 10 秒的 checkout 上是错误的。

只有预算背后的观测仍然成立时，预算才是快速失败上限。当变更使某步骤显著加快时，必须根据新测量值重新推导预算，而不能继承旧值；否则作业保留的上限仍对应已不存在的运行时间，与没有上限无法区分。MAN-663 是具体示例：它将 BusinessGateway 分片的测试步骤从 14.7 分钟缩短到 1.0 分钟，因此该步骤从“约为观测值 2 倍”的规则转为固定成本下限（35 分钟 → 8 分钟），作业预算也从 70 分钟降到 43 分钟。注意 A 级不变量不允许做什么：作业预算绝不能低于必需步骤预算总和，因此作业预算是结构下限，而非运行时间声明；分片挂起时真正触发的是测试步骤预算。（MAN-669 PR-A 随后又针对重新配平的拓扑重新推导了两者；当前值为下一段的步骤 10 分钟 / 总和 39 分钟 / 作业 45 分钟。）

MAN-669 PR-A 重新配平分片内容时，同一规则第二次适用。旧的逐分片差异（BusinessGateway 8 分钟、Platform 10 分钟、Business Core A 15 分钟、Business Core B 12 分钟）来自一个分片承担 357 秒 TRX elapsed、另一个仅承担 23 秒的拓扑。按实测成本重新安置项目后，该差异消失。在分支的三次运行（`31114441118`、`31115903098`、`31116998822`）中，分片测试步骤依次实测为 3.5 / 3.0 / 4.2 / 2.4 分钟，4.5 / 3.0 / 4.2 / 3.3 分钟，以及 4.7 / 3.1 / — / 3.1 分钟。同一提交在托管运行器上的波动可达数十个百分点，并会改变最慢分片；这本身就说明应共享一个预算而非使用四个预算，否则逐分片预算会编码噪声。四个分片现均采用 10 分钟测试步骤预算（约为三次运行最大值 4.7 分钟的 2 倍）、39 分钟步骤预算总和和 45 分钟作业预算。跨拓扑变更继承预算，与加速后继承预算属于同一失败模式：它描述的是已不存在的事物。

规则 2 覆盖 `steps:` 中的**显式**步骤——只有这些步骤能携带 `timeout-minutes`。GitHub 还会运行不在 `steps:` 中、因而无法分配预算的步骤：首个步骤前的 `Set up job`，以及组合 action 的隐式后置步骤（`actions/cache` 的 post-save、`setup-node` 的 post-cache）。作业预算在 `Set up job` 前开始计时，并持续到后置步骤结束，因此作业剩余余量——作业预算减去步骤预算总和——必须吸收这些成本。相对于实测仅数十秒的隐式开销，当前四个后端快速分片的余量各为 6 分钟，`erp-sales-order-demand-acceptance` 和 `connector-host-tests` 各为 9 分钟。

证据结论不受该缺口影响：隐式后置步骤安排在最后一个显式步骤**之后**，因此运行时 `Upload … test evidence` 已经发布产物。它们会消耗作业预算，但不会使证据丢失。它们可能在每个步骤都处于自身预算内的情况下仍使作业触及作业预算；因此设计必须考虑余量，而不仅是严格不等式。

### 强制机制

这不是只存在于注释中的约定。`scripts/lib/CiWorkflowBudgets.ps1` 从结构上读取工作流；遇到 `missing-job-timeout`、`missing-step-timeout`、`evidence-job-budget-not-above-step-sum` 或 `job-budget-not-above-largest-step` 时，`scripts/tests/test-evidence.Tests.ps1` 会失败。Script Governance CI 作业直接运行该测试套件，因此违规会传播真实的非零退出码。读取器会把解析出的步骤数与原始文件交叉核对，数量不一致就抛错，因此无法解析的工作流会失败关闭，而非报告零违规；无法读取的作业标题也基于同一理由抛错，因为跳过它会把该作业的步骤和预算合并进前一个作业。交叉核对会把每个六空格序列项解析到自身所属的作业级键，而不是只相信缩进——`needs:` 和 `strategy.matrix` 项与步骤条目恰好位于同一列，把它们计为步骤会让每个使用这些项的工作流硬失败，并报告解析错误而非真实发现。同一测试套件为每个违规代码和每种 `if:` 写法提供反向夹具，因此“`ci.yml` 零违规”是实际结果，而不是空集通过。

对工作流编辑者的直接影响是：在 A 级作业中新增或重新排序步骤时，必须增加该步骤预算，**并且**提高作业预算以确保步骤预算总和严格小于它；在 B 级作业中新增任何 `if:` 能在失败后继续运行的步骤——无论采用何种写法——都会将该作业提升为 A 级，其预算也必须相应提高。

## Schema v1 结构

| 区域 | 必填字段 |
| --- | --- |
| 测试记录 | `schemaVersion`、`workflowRunId`、`runAttempt`、显式 `headSha` 和 `testedSha`、执行通道/项目/程序集、方法身份、有界参数化 `displayName`、稳定的 `definitionId`/`testInstanceId`、精确的 `durationTicks` 及派生的 `durationMilliseconds`、结果、已批准的 `skipReason`、脱敏次数 |
| 结果 | `passed`、`failed` 或 `skipped` |
| 摘要 | 运行/作业/运行器/产物名称、保留天数和保留位置；`selectedLanes` 与 `selectedLaneResults`；每个逻辑选中执行通道及执行通道+程序集的 passed/failed/skipped/executed/total；测试耗时总和；独立的 TRX elapsed 耗时；最慢测试/程序集；跳过聚合；具体基线来源及兼容的只报告差值，或结构化 `unavailableReason`；脱敏次数；尝试分类；违规项 |
| 执行通道 | `<family>` 或 `<family>-shard-<positive-integer>` |

`headSha` 是 GitHub 事件报告的分支头。`testedSha` 是实际 checkout 并接受测试的提交（`git rev-parse HEAD`）。在 `pull_request` 中，`testedSha` 可能是 GitHub 的合成合并提交，因此与 `headSha` 不同；在受支持的非 PR `push` 事件中，两者必须相同。EvidenceRoot 刷新和旧版 GitHub 控制台导入都从独立下载的作业日志推导 `testedSha`，而不是复制运行头或相信产物自证。当前工作流在 checkout 后记录 `tested-sha=<sha>`；历史控制台兼容模式只接受精确的 checkout 命令 `git log -1 --format=%H` 及其下一行 SHA。checkout 权威缺失或冲突时失败关闭。PR checkout 来源证明可以保留不同的分支头 SHA 与合成合并 SHA，但 PR 运行没有资格生成已提交基线。规范化 TRX 根属性同时携带两者，并且必须与解析器运行元数据一致。

`testInstanceId` 优先使用持久化的 TRX `executionId`；只有缺少有效执行 ID 的来源 TRX 才使用确定性兜底值。`durationTicks` 是用于重建规范化 TRX 的可逆 100 ns 表示，可避免浮点毫秒漂移。因此，`backend-shard-1` 是普通的 schema-v1 执行通道。MAN-669 只新增了分片执行通道调用：没有分片信封、没有第二个采集器，也没有改变记录或摘要 schema。

规范化 TRX 是确定性的保留交换格式，不是原始运行器时间线。其 `Times` 元素使用固定的合成起点 `2000-01-01T00:00:00Z`，并且只根据保留的 TRX elapsed 耗时推导结束时间；使用方不得将这些时间戳解释为墙钟执行时间。原始 TRX 仍只存在于作业本地，绝不上传。

可空以及带路径的程序集身份使用命名空间限定的 `assemblyIdentity` 标记。任何标记都要求同时存在两个规范 SHA 属性；`null`/`empty` 要求标准 `storage` 为空，`verbatim` 要求 `storage` 非空且包含路径分隔符。其他命名空间中的保留本地名称、不完整的标记集合，或 writer 永远不会生成的标记/`storage` 形状都会失败关闭。

规范化 TRX 文件名在 `OrdinalIgnoreCase` 下唯一且不超过 240 个字符时保留旧版清洗名称。预留这些兼容名称后，其余身份按序数身份顺序分配完整 SHA-256 身份摘要；若旧版/哈希或哈希/哈希候选已被占用，则附加确定性的冲突序号。这样可在大小写不敏感文件系统上无损保留有效身份，同时保持普通历史名称，并使文件名选择不受输入顺序影响。

全脚本序数门禁对 `New-NervTestEvidenceSummary` 中的 `Group-Object { Get-NervRetainedSkipReason $_ }` 保留唯一具名例外：skip reason 是面向人的说明文本，合并视觉等价说明是有意语义。例外同时精确绑定函数名与完整表达式，必须在全树中恰好命中一次；移动、复制或改宽表达式都会失败。其后的保留结果排序、`skipClassification`、`skipPolicyId`、测试身份和文件名仍必须使用序数比较，该例外不传播到任何 identity 字段。

## 跳过政策

`scripts/test-evidence-policy.json` 包含 `{ schemaVersion, lanes[], sources[], rules[] }`。来源行通过路径、从 1 开始的序号和锚定的来源原因模式，标识一个仓库相对路径下的 C# `Skip =` 赋值。规则标识来源、分类、锚定的运行时测试/原因模式、带 `expectedRuntimeTestCount` 的精确 `testIdentities` 集合、允许的执行通道/操作系统、可选的必需执行通道和隔离元数据。来源/规则引用在两个方向上都闭合，因此使用共享 Fact 特性的新方法不能静默消耗既有的类级预算。

每个运行时跳过都必须恰好匹配一条适用于当前上下文的规则。当前清单包含 40 个来源赋值：

- `optional`：未选择某项能力；选择其能力执行通道后，该跳过即不合法。
- `environment-gated`：未选择某个真实依赖；选择 `requiredLane` 后必须执行。
- `quarantined`：仅可临时使用，必须带责任 Issue、ISO 到期日期和可衡量的退出条件。

每个来源赋值按仓库相对文件、从 1 开始的 `Skip =` 序号和锚定原因登记。这样有意防止共享 Fact 特性静默扩大运行时预算，但也带来明确的维护成本：在同一文件较早位置插入 `Skip =` 会移动后续序号，因此作者必须审核并更新所有受影响的来源行。

语义硬门禁恰好有三项：

- `unregistered-skip`：跳过匹配缺失、存在多条、原因不匹配或在当前上下文中不合法。
- `illegal-quarantine`：元数据缺失/无效，或隔离已到期。
- `zero-execution`：选中的 `realDependency: true` 执行通道没有 passed 或 failed 运行时结果；skipped 不算执行。

采集器是单执行通道采集器：一次调用拥有一个物理 `-Lane`。`-SelectedLanes` 可以指定该物理分片或其逻辑基础执行通道；不得使用同级分片选择器声称该调用分别认证了每个同级分片。zero-execution 仅按逻辑基础执行通道对多个选中同级选择器分组，以避免重复/虚假的同级分片失败；它能识别基础选择器对应的当前分片执行，并且在选中当前分片确实没有 passed/failed 结果时仍然失败。MAN-669 新增了执行通道名称与调用，但没有改变该采集器契约。当前 CI 接入 `backend-shard-1` … `backend-shard-4` 和 `connector-host`（均为 `realDependency: false`），并接入 `postgres`（`realDependency: true`）顺序执行 14 个 core manifest member（Inventory、MasterData、Scheduling、AppHub、BarcodeLabel、FileStorage、IndustrialTelemetry、Quality、MES、WMS、ERP、DemandPlanning、跨业务 Acceptance、Maintenance 设备停机），共 63 个冻结用例。其余 PostgreSQL 成员以及 FullChain、性能和 Connector 真实依赖作业仍属后续工作。契约测试只证明 zero-execution 函数；只有 hosted `postgres` job 的实际运行与证据才证明该真实依赖通道已经执行。

耗时、趋势、跳过总数、基线差值和 `recovered-after-rerun` 都只用于报告（`report-only`）。恢复标签要求通过已认证的 GitHub Actions 查询取得精确的前一次尝试和执行通道允许清单中的作业名称，并匹配工作流运行、当前尝试和分支头 SHA；同时必须存在失败的前一作业，以及当前成功的原生测试步骤，且当前步骤执行数非零、失败测试为零、政策违规为零。`Get-NervTestEvidenceLaneJobs` 是恢复标签与基线权威共用的唯一允许清单。它恰好有六项——四个后端分片执行通道、`connector-host` 和 `postgres`——且每项只绑定一个作业名称，因此分片绝不能认证同级分片，`postgres` 也只能由 `PostgreSQL Provider Tests` 认证。该 job 可以在同一个物理 `postgres` lane 中顺序运行多个 manifest member，但 runner 的聚合 summary 必须逐成员证明 expected/discovered/passed/skipped/cleanup，不能用其中一个成员的成功认证另一成员。未分片的 `backend` 执行通道被有意**省略**：自 MAN-669 起没有作业产出它；若仍将其映射到 `Backend Tests`，不运行测试的聚合作业就能认证一个从未运行的执行通道。`backend` 仍是 `-SelectedLanes` 和政策 `allowedLanes` 的有效逻辑基础执行通道，只是不再可认证。因此，重跑分类按分片进行：一个分片重跑后恢复不会重新标记其他分片。生产采集器不公开由调用方提供或仅测试使用的权威替换参数；测试直接调用纯响应验证器。查询不可用时，摘要写入 `prior-attempt-unavailable`；仅凭尝试次数绝不能证明恢复。

## 耗时数据是缓存，不是受治理资产（Timing data is a cache, not a governed asset）

MAN-661 最初在一个受治理文件中保存两类不同事物：一类是**政策**清单——哪些跳过已登记、哪些隔离合法、欠有哪些确定性债务；另一类是**测量值**——各程序集耗时多久。只有前者是资产。政策由人写下并由门禁约束执行；测量值来自观测，没有人决定测试套件应该多快。

治理测量值正是 #1507 所移除故障的来源。已提交快照以 `lane + assembly` 作为耗时行键，因此完全不涉及测试内容的变更也会使键失效：MAN-663 修改共享 BusinessGateway 宿主，MAN-669 PR-A 在分片间重新安置 64 个后端程序集中的 17 个；两次都需要人工重新生成并提交快照，才能清除由此产生的 `lane-assembly-not-in-baseline` 行。这个仪式本身才是缺陷，而不是数据漂移。

成熟实现都把耗时视为缓存：CircleCI `--split-by=timings`、Jest `--shard`、pytest-split 和 Knapsack 都读取最近一次成功运行的产物；条目陈旧或缺失只会让分片略微不均。它们都不会对耗时文件计算哈希、为其设置刷新触发器或以其作为门禁。反之，真正受治理的清单——Chromium 测试预期、Kubernetes 不稳定测试隔离——以测试名称或路径为键，从不以运行器拓扑为键，因此重新分片不会丢键。

因此，本仓库的边界如下：

| | 耗时（测量值） | 政策（受治理清单） |
| --- | --- | --- |
| 键 | 程序集 | 测试全名 / 来源路径 + 序号 |
| 存储 | `artifacts/backend-test-shard-timings.json`，被 Git 忽略且从不提交 | `scripts/test-evidence-policy.json`、`backend/test-determinism-baseline.json` |
| 来源 | 从近期成功的 `main` 运行自动聚合 | 人工编写并审核 |
| 刷新 | 读取时自动执行，无人工步骤 | 在相关变更的同一 PR 中有意编辑 |
| 条目缺失 | 只报告警告并使用兜底估值 | 硬门禁 |

具体规则如下：

- `scripts/update-backend-test-shard-timings.ps1` 将最近 **5** 次成功的 `main` push CI 运行所保留的 TRX 证据产物聚合进缓存。选择 5 次是因为同一提交在托管运行器上的波动可达数十个百分点，并会改变最慢分片；一次运行只是样本而非测量值。5 个样本可让**中位数**吸收嘈杂邻居或滚动升级中的运行器镜像，无需离群值规则；按本仓库的 `main` push 频率，5 次运行也完全处于 14 天产物保留期内。若一个程序集在同一次运行的两个执行通道中被观测两次，则先求和，使其计为一个样本而非两个半样本。这两个数字都是默认值，论证位于 `scripts/lib/BackendTestShardTimings.ps1`，`-RunCount` 可覆盖窗口。
- `scripts/report-backend-test-shard-balance.ps1` 输出逐分片总值和离散程度。它会自行刷新超过 24 小时的缓存，因此不存在人工刷新步骤。**它不能因耗时数据而失败。**没有观测值的程序集使用估值参与配平：优先取自身分片内已测程序集的中位数；若该分片没有已测程序集，则取全部已测程序集的中位数；若完全没有测量值，则使用固定最终兜底值，并报告 `timing-assembly-missing` 警告。来源完全不可用时报告 `timing-source-unavailable`。两者都以 0 退出。唯一的非零退出来自结构上不可用的 manifest，因为那是受治理文件的缺陷。
- 降级是正常路径，不是错误路径。缺少 GitHub CLI、缺少 token、运行器离线或产物过期时，都只产生警告，先回退到最近提交的证据快照，再回退到估值。因此，配平报告有意**不**作为门禁接入 CI。
- `New-NervTestEvidenceSummary` 内的比较键现为**仅程序集**。执行通道仍作为来源证明保留在行中，仅用于区分快照在两个执行通道下都记录过的程序集；当前行不属于其中任一执行通道时，报告 `ambiguous-assembly-in-baseline`，而不是随意选择。快照从未记录过的程序集报告 `assembly-not-in-baseline`；没有任何可比较行的摘要报告 `no-compatible-assembly`。这些结果都只用于报告。
- 三项语义硬门禁完全不受上述变化影响：`unregistered-skip`、`illegal-quarantine` 和 `zero-execution` 仍会失败关闭，且以测试身份、来源路径和逻辑执行通道为键——重新安排分片无法改变这些维度。`scripts/verify-backend-test-shards.ps1` 仍是分片 manifest 的政策门禁，完全不读取耗时数据；`scripts/tests/backend-test-shards.Tests.ps1` 以 AST 判定来断言该边界（不 dot-source 耗时库、不调用其定义的函数、求值字符串中不出现耗时文件名），而不是扫描原始源码，因为注释中仅提及耗时文件并不构成依赖。判定覆盖门禁**以及门禁 dot-source 的每个 `scripts/lib` 库**；库集合从门禁自身 dot-source 语句推导而非手工列出，因为将耗时调用向共享库移动一跳仍是同一依赖，入口点 AST 却无法看到。该测试套件还包含模拟分片重排，而且有意采用*强*重排：将项目连同其排除选择器及由选择器推导的 `excludedTestLanes` 一起移动，从而触及分片 ID 与 MAN-661 执行通道相遇的唯一规则；随后以进程方式把重排后的 manifest 交给真实政策门禁，并要求通过。政策键由生产 helper（`Get-BackendTestShardPolicyIdentityMatches` / `Get-BackendTestShardPolicyIdentityKey`）推导，而不是在测试中重新实现；三个对照确保结果不是无条件成立：把执行通道拼回键的同一推导会在此次重排中明确丢键，旧的执行通道键控耗时查找也会明确丢失多项，而将 `excludedTestLanes` 留在原处的同一重排则会在精确耦合点被拒绝。

跨重排比较键*集合*是必要条件，但并不充分，因为多数破坏键的方法本身也与重排无关——例如丢掉测试身份并退化为 `source|rule` 的键，在重排两侧仍然相等；返回常量的键亦然。同一测试套件通过三项进一步契约封闭该缺口：把键重新拆为各段，并分别与生成它的匹配项比较，因此键必须可逆地恰好为 `sourceId|ruleId|identity`（这使“不携带执行通道、不携带分片”成为强制声明而非注释，也会拒绝 `requiredLane` 等第四字段）；不同的 `(sourceId, ruleId, identity)` 三元组必须产生不同键，因此任何一段都不能被静默丢弃；还使用与冻结身份仅大小写不同的选择器探测 `Get-BackendTestShardPolicyIdentityMatches`，并要求不得匹配，因此 ordinal 比较不能放宽为 `OrdinalIgnoreCase`，从而静默扩大每一项排除。

耗时键也相应调整。测试套件断言每个程序集在重排前后解析到相同键和相同测量值；并在为每个已分类程序集赋予不同耗时的合成查找中，精确断言被移动程序集的毫秒数从提供方分片总值移出并进入接收方分片总值，使检查绝不依赖已提交快照的完整程度。它有意**不**断言每个程序集都有测量值：覆盖缺口是 `timing-assembly-missing`，按构造只用于报告；若以此作为门禁，就会重新施加该 Issue 已移除的人工快照刷新仪式。缺口数改为输出到作业日志。

64 个键中丢失 17 个的问题由此次迁移解决，而不是再次刷新：使用仅程序集键后，分片重排已无可使之失效的内容，因此该概念不再存在，#1228 也不会重新打开。

### 执行通道是适用条件，不是身份键

`Test-NervRuleApplies` 仍读取 `allowedLanes` 和 `requiredLane`，乍看之下像是本节所称已移除的耦合，但事实并非如此；以下区别值得明确记录：

- **身份键**回答“这是哪一行？”。对耗时而言是程序集；对政策而言，是测试全名加登记来源路径和序号。两者都不包含拓扑信息，因此重新安置项目不会使行失效。
- **适用条件**回答“该规则是否适用于眼前的运行？”。由于未选择 PostgreSQL 而合法的跳过，在选中 PostgreSQL 执行通道后立即不再合法。执行通道就是该条件；删除它也会连带删除第三项硬门禁：`zero-execution` 正是为了要求选中的真实依赖执行通道必须实际执行某些内容。

两者不会相遇，因为到达适用条件的是**逻辑**执行通道。`Test-NervRuleApplies` 在比较前移除所有 `-shard-N` 后缀（`^…-shard-[1-9][0-9]*$`），因此在发生任何匹配时，`backend`、`backend-shard-1` 和 `backend-shard-4` 已经是同一个值，分片维度已经消失。因此，重新分片无法改变规则判定——这是由断言保证而非仅由注释声称：`scripts/tests/backend-test-shards.Tests.ps1` 在两种运行器操作系统上，针对每个逻辑执行通道及其多种分片写法评估每条政策规则，并要求判定完全相同。

同一测试套件还会跨模拟重排本身比较三项硬门禁的判定：对被移动测试身份在提供方和接收方执行通道下的合成运行时跳过，运行生产引擎 `Get-NervTestEvidenceViolations`。两组结果都可以合法为空——允许执行通道中的已登记跳过不是违规——因此加入第三次评估作为对照，在规则不允许的执行通道下必须产生 `unregistered-skip`；否则“判定相同”只不过是两个空集相等。

该夹具只会产生 `unregistered-skip`，若单独使用，它对另外两项门禁只会比较空集与空集，无法测试它们。因此，每项门禁都有自身的真实夹具，并在比较判定前先断言结果非空：

- `illegal-quarantine`——将一个探测政策行分类为 `quarantined`，并提供已过期的 `expiresOn`，分别在提供方和接收方执行通道下评估。隔离合法性从行自身元数据读取，不涉及执行通道，因此两组判定必须相同；同一行使用未过期日期再次运行时必须不产生结果，以防夹具在始终触发的门禁上也能通过。
- `zero-execution`——该门禁只涉及 `realDependency: true` 执行通道，因此完全无法通过快速分片执行通道触达。它改在 `postgres` 执行通道上执行，并先后把认证工作归给两个不同快速分片；判定不得改变。对照条件包括：选中执行通道*内*的 passed 结果必须清除门禁，而且逻辑执行通道及其多种 `-shard-N` 写法必须产生相同认证结果。

### 一个已知的只报告伪影

程序集键控比较仍有一种残留情况，记录于此以免日后重新发现。当已提交快照只在执行通道 A 下为某程序集保存一行，而当前运行将该程序集拆到执行通道 A 和 B 时，当前两行都会解析到同一基线行；因此两者分别与*完整的*上次测量值比较，并报告夸大变化的差值。这是真正存在歧义的比较所产生的显示伪影，不是丢键；它与其他差值一样只用于报告：没有门禁依赖它，它产生的任何缺口也不会让作业变红。另一种方案是完全拒绝比较被拆分的程序集，但这会用“没有数字”替换“略有偏差的数字”，对差值的唯一用途而言更差。

## 耗时快照来源证明与历史

**第一份**基线由实施期间可用的最新合格来源生成：GitHub Actions CI 向 `main` 的 push，运行 `30819675007`、第 1 次尝试、Backend Tests 作业 `91706113150`、头/受测提交 `9dafb512c992b240222c8d9b5ada43e4bfc8ac3d`，运行与作业结论均为成功。权威 Actions 作业日志从历史 `git log -1 --format=%H` 输出解析受测 checkout，将托管运行器解析为 `ubuntu24@20260720.247.2`，SDK 解析为 `10.0.302`；`ubuntu-latest` 或 `10.0.x` 等选择器会被拒绝作为基线来源证明。该基线是旧版控制台导入基线，使用 `granularity: project` 和 `durationMetric: project-wall-clock`，因此无法与测试粒度 `trx-elapsed` 证据比较，也不是耗时门禁。此后它已被替换两次，详见下方刷新历史；自 2026-08-05 起，已提交基线一直是测试粒度 `trx-elapsed`。

只有 `scripts/generate-test-evidence-baseline.ps1` 可以写入 `scripts/test-evidence-baseline.json`。最初的来源命令如下：

```powershell
pwsh scripts/generate-test-evidence-baseline.ps1 -Repository Mang-X/Nerv-IIP -GitHubRunId 30819675007 -GitHubJobId 91706113150 -OutputPath scripts/test-evidence-baseline.json
```

该控制台命令仅作历史记录；它已无法生成可用基线（见下文）。已提交基线自 2026-08-05 刷新起采用测试粒度，并于 2026-08-07 再次刷新；两次都使用：

```powershell
pwsh scripts/generate-test-evidence-baseline.ps1 -EvidenceRoot artifacts/test-evidence -OutputPath scripts/test-evidence-baseline.json
```

**现已不存在任何强制刷新触发器（There are no longer any mandatory refresh triggers）。**本段过去承载的规则——“MAN-663 修改共享宿主 profile 后，以及 MAN-669 修改执行通道/分片拓扑后，必须刷新”——正是 #1507 删除的仪式：两个纯粹改变“如何运行测试”的变更因此欠下一次测量值重生成，而重新分片还能使任何测试都未触及的键失效。已提交文件现在是供离线和无 token 场景使用的**兜底快照**，与自动缓存一样按程序集键读取。重新生成只属可选维护——有人希望获得更新的离线默认值时值得执行，但拓扑变更绝不因此欠下刷新，也绝不作为门禁。主要耗时来源是上文所述自动缓存。

2026-08-05 的刷新采用运行 `30999368607`（main push、合并提交 `92d7f1ddc`、第 1 次尝试、成功）——这是合并后首个携带完整 `backend-shard-1`..`backend-shard-4` 加 `connector-host` 产物集合的合格运行——并以 `granularity: test` / `durationMetric: trx-elapsed` 的 71 个执行通道+程序集行替换 64 个 `lane: backend` project-wall-clock 行。比较重新变为 `available`；使用该运行自身的分片 1 证据重跑采集器验证为 `unavailableReason: null`、自比较差值 0.0%。

该刷新早于 MAN-669 PR-A 的分片重新配平，因此随后需要第二次刷新。当时，**冻结执行通道名称并不能保留比较键**：身份由执行通道加程序集组成，所以在分片间移动程序集会改变其键。PR-A 移动了 64 个后端程序集中的 17 个；这 17 行变为 `lane-assembly-not-in-baseline` 且差值为 null，但每个执行通道仍输出 `Baseline comparison: available`，因为 `$baselineAvailable` 对每个摘要只要求至少一行可比较。**自 #1507 起，该失败模式已按构造封闭**——键只由程序集组成，因此重新安置程序集仍保留比较；`lane-assembly-not-in-baseline` 原因不再存在，其后继 `assembly-not-in-baseline` 只能由快照确实从未记录的程序集触发。本段保留为键变更原因的记录。**已提交快照来自运行 `31185687984`**（main push、提交 `8f42c5ea4`、第 1 次尝试、成功、全部 11 个作业为绿色），刷新于 2026-08-07：`schemaVersion` 为 2，6019 个测试对应 71 个执行通道+程序集行，键覆盖恢复为后端 64/64 加 `connector-host` 7/7。所有差值仍只用于报告。

该运行的五个执行通道跨越两个运行器镜像（`backend-shard-3` 使用 `ubuntu24@20260804.265.1`，其他四个使用 `ubuntu24@20260720.247.2`），只有依靠下文所述来源证明拆分才被接受；按此前跨执行通道 `runnerImage` 相等规则，此次刷新会被直接拒绝。这不是一次性现象：前一次运行也以同样方式混合，但新镜像落在*另一个*执行通道（`backend-shard-2`）；这就是“GitHub 独立调度每个作业”的实际表现——混合是调度属性，不是运行属性，因此等待同构运行等于等待硬币恰好落在一面。旧版 `-Repository/-GitHubRunId/-GitHubJobId` 控制台导入已无法生成可用基线，因为其目标 `Backend Tests` 作业现在是不运行测试的聚合；所以上述 EvidenceRoot 命令是唯一受支持的刷新路径。刷新需要同一次合格运行的四个分片证据产物加 Connector Host 产物；`Assert-NervEvidenceRootAuthority` 会拒绝不完整的后端分片族，因此基线不能静默只覆盖一个分片。其他有意的测试拓扑变更，只应从最新完成、第 1 次尝试成功、必需作业均成功的 `main` CI push 刷新。若已提交基线不是测试粒度 `trx-elapsed`，每次比较都以 `unavailableReason: incompatible-granularity-or-duration-metric` 表示只报告不可用；Markdown 会输出该原因，绝不渲染空的 `baseline=ms, delta=%` 占位符。契约测试针对显式构造的项目粒度基线断言该不可用渲染，断言可用路径的精确带符号差值，并额外要求已提交快照保持 `test`/`trx-elapsed`，且只携带正耗时与非空程序集键。它们有意不再要求快照覆盖每个已认证执行通道：该断言会把分片重排或新增测试项目变成针对测量值的红色门禁，而这正是 #1507 移除的耦合；覆盖缺口现由 `scripts/report-backend-test-shard-balance.ps1` 作为只报告警告输出。证据根摘要的来源证明按**两类**检查，因为二者背后的事实类型不同（见下文“运行身份与逐作业环境”）。运行身份——运行 ID、尝试次数、头 SHA、受测 SHA、仓库、事件、分支、来源 URL——必须完整、非空，并且在**每个执行通道间相互一致**：同一工作流运行的五个作业按构造共享这些值，任何不相等都表示摘要不属于同一次运行。逐作业环境——运行器操作系统、解析后的运行器镜像、精确 SDK——必须在**每个摘要中**完整、非空且格式正确，但不要求跨执行通道相等。摘要还必须拥有唯一有效的执行通道、符合允许清单的执行通道到作业映射、成功的原生执行、非零执行量，且没有失败或违规。生产生成器不公开 Actions 夹具或其他权威替换参数。两条生成器路径使用相同 checkout 来源证明验证器：它验证 push 的头/受测值相等，同时保留不同的 PR 头/受测值用于验证，随后独立强制仅 `main` push 证据有资格作为基线。EvidenceRoot 还验证运行 URL/工作流/事件/分支/头 SHA/尝试/结论、最新条目的匹配 ID/头 SHA/尝试/结论/事件/分支、每个必需作业，以及每个作业日志的受测 SHA、运行器操作系统/镜像/版本和精确 SDK。测试使用夹具对象执行纯验证器。行仍按执行通道加程序集*存储*，因为执行通道是测量发生位置的真实来源证明；但读取器比较键只由程序集组成（见“耗时数据是缓存，不是受治理资产”），并且只有两侧都使用测试粒度 TRX elapsed 耗时时才输出耗时差值。应提交脚本生成的差异以及逐执行通道解析后的运行器镜像和实际 .NET SDK 来源证明；绝不得手工编辑快照。

### 运行身份与逐作业环境

证据摘要中的 11 个来源证明字段最初作为一组检查：必须完整、非空，并在每个执行通道间逐字节相等。其中 8 个字段确实应当如此。`workflowRunId`、`runAttempt`、`headSha`、`testedSha`、`repository`、`event`、`headBranch` 和 `sourceUrl` 是**运行本身**的属性；同一次运行的每个作业按构造报告相同值，因此其中出现不相等并非运行器特性，而是两次不同运行被混入同一基线。对这 8 个字段的相等检查是真实检查，保持不变。

另外 3 个字段——`runnerOs`、`runnerImage`、`dotnetSdk`——是**某个作业落到的机器**的属性。GitHub 独立调度每个作业，因此“本次运行的运行器镜像”并不存在。相等规则只在托管运行器集群恰好同构时才成立。2026-08-07 镜像滚动升级期间，连续两次合格的 `main` push 运行都跨越两个镜像，但逐执行通道组合*不同*：

| 运行 | `backend-shard-1` | `backend-shard-2` | `backend-shard-3` | `backend-shard-4` | `connector-host` |
| --- | --- | --- | --- | --- | --- |
| `31149427664` (`b490ca8d4`) | `…20260720.247.2` | `…20260804.265.1` | `…20260720.247.2` | `…20260804.265.1` | `…20260804.265.1` |
| `31161226667` (`c74f633d5`) | `…20260720.247.2` | `…20260720.247.2` | `…20260720.247.2` | `…20260804.265.1` | `…20260720.247.2` |

两次运行都合法。按旧规则，两者都会以 `Evidence summaries have mixed provenance field 'runnerImage'` 失败，从而彻底阻止基线刷新：`Assert-NervEvidenceRootAuthority` 还要求来源是*最新*合格运行，因此唯一通过方式是赢得竞赛——等待五个作业恰好全部落到同一镜像的运行，然后在下一次 `main` push 到来前使用它。若运行器集群中比例 `p` 使用新镜像且作业独立调度，则五个执行通道全部匹配的概率是 `p⁵ + (1−p)⁵`：当 `p = 0.5` 时约为 6%，只有滚动升级完成后才趋近 1。这是伪装成验证规则的 schema 局限：`source` 是扁平结构，无法表达“五个执行通道、两个镜像”；相等规则只是阻止扁平结构产生虚假记录的手段。

修复方式是让 schema 表达事实。**基线 `schemaVersion` 现为 2**：移除 `source.runnerOs` / `source.runnerImage` / `source.dotnetSdk`，改用 `source.laneProvenance`——每个证据执行通道一行，携带该通道的 `jobName`、`runnerOs`、`runnerImage` 和 `dotnetSdk`。不再保留可能被读取器误认为整体的运行级运行器字段。**schema-1** 基线的扁平三字段只是*第一个执行通道*的环境，绝不能按运行级字段读取；已提交基线在 2026-08-07 刷新前为 schema 1，此后一直为 schema 2（当前来自运行 `31185687984`）。

该区别由强制机制保证，而非仅记录在文档中。`New-NervTestEvidenceSummary` 只接受 `schemaVersion` 1 或 2，其他值一律报告 `unsupported-baseline-schema-version`；随后比较降级为只报告不可用，而不是针对代码从未见过的布局运行。两个已知版本的比较行为相同，因为比较键是程序集，不涉及执行通道或运行器字段；该门禁用于防止*未来*第三种结构被当作现有两种结构之一读取。**本段是 schema 版本门禁理由的唯一权威。**代码及其契约测试只保留单行结论和指向本文的引用；门禁发生变化时，应在此处编辑。

“其他值”按字面理解，覆盖 JSON 文件实际可容纳的每种结构：缺失 `schemaVersion`、`null`、非数字字符串、布尔值、数组和小数。正因如此，版本使用 `[int]::TryParse` 读取，而不是进行 `[int]` 转换。转换存在三种不同错误：对 `"abc"` 和 `[1, 2]`，它会从纯摘要构建器抛出转换错误（不同于本段承诺的结构化原因）；它会静默把 `1.5` *舍入*进受支持集合；还会把 `true` 变成 `1`，使布尔值像文件声明 schema 1 一样参与比较——最后一种会针对从未读取过的布局产生真实的带符号差值。

契约测试从行为上覆盖 6 种非整数结构：断言报告的原因、原因会到达每个程序集行，而且不会抛错。其中 4 种会在 TryParse 被改回转换时变红：`"abc"`、`[1, 2]`、`1.5`、`true`。另外两种 `null` 和字段缺失在两种写法下都会同样被拒绝（`[int]$null` 原本就是 `0`）；它们只是结构覆盖，不能计作本次修复的证据。

调用 `TryParse` 时没有传入 `NumberStyles`/`IFormatProvider` 参数，这与同一文件其他位置的 `[DateTimeOffset]::TryParse(..., InvariantCulture, ...)` 不同。该不对称是有意的，两种情况并不相同：日期字符串以确实带区域性格式的外部文本到达，而此处的值已经经过 PowerShell `[string]` 转换，该转换以区域性不变方式渲染数字。**对于该门禁实际接收的结构——即通过 `[string]` 转换到达它的每个值——判定都不受区域性影响；没有回归测试对此断言，因为在这些结构上该测试不可能失败。**这是实测结论而非假设：11 个可由 JSON 表示的值 × 5 种区域性（`en-US`、`de-DE`、`fr-FR`、`zh-CN`、`ar-SA`）× 3 种合理门禁写法（当前区域性 `Integer`、不变区域性 `Integer`、允许千位分隔符的当前区域性 `Number`）= 165 种组合；跨区域性判定差异为**零**，跨门禁写法差异为零，受区域性影响的 `[string]` 渲染也为零。

该声明有明确边界，而且边界承载语义：确实*可以*构造区分两种写法的情况。将 `schemaVersion` 设为 JSON **字符串**，其文本为当前区域性自身的 `PositiveSign` 后接 `2`——在 `ar-SA`/`ar-EG` 下是码位 `U+061C U+002B U+0032`，在 `fa`/`fa-AF`/`fa-IR` 下是 `U+200E U+002B U+0032`，在 `ckb-*` 下是 `U+200F U+002B U+0032`（此处有意写作码位，因为字面量包含不可见的双向文本标记）。此时 `[string]` 转换是恒等操作，原始文本不变地到达 `TryParse`；当前区域性会将其**接受**为 `2`，而不变区域性重载会**拒绝**。在本仓库 .NET/ICU 上实测：1063 种区域性中有 63 种携带非 ASCII `PositiveSign`，共有 4 种不同写法；在 `ar-SA` 下断言拒绝的测试会在当前代码上变红、在不变区域性实现上变绿，因此它具备区分能力，不是恒真式。

**该测试仍被有意省略，门禁也仍被有意保持原状。**这类测试锁定的是对罕见符号的偶然容忍，而非门禁契约；并且符号*被*容忍时，得到的值是正确的 `2`——与上述 `true → 1` 结构不同，不会针对从未读取过的布局执行完整比较。还要注意，区域性进入该门禁恰好只有一个*可达*入口，即符号。另一个候选面确实存在于区域性数据中：同样的 1063 种区域性里，**92 种声明了**非 ASCII `NumberFormat.NativeDigits[0]`（阿拉伯-印度数字 `U+0660`、扩展阿拉伯-印度数字 `U+06F0`、天城文 `U+0966`、孟加拉文 `U+09E6`、N'Ko、藏文、缅甸文、Adlam 及另外三种，共 11 套不同数字）；但 `[int]::TryParse` 在任何区域性下都不接受它们，包括数字自身所属区域性：`U+0662` 的解析结果为 `False`；依次把这 92 种区域性设为当前区域性，并向其提供自身原生数字 `2`，接受次数仍为零。原生数字面存在于数据中，但通过该门禁不可达，因此符号仍是唯一入口；而符号只对绕过 `[string]` 转换的文本可达。

**上述两个计数都使用 ordinal 比较**（`[string]::Equals(…, StringComparison::Ordinal)`），正是该限定使其可信：本段每个数字最初都曾因改用区域性感知比较而算错。PowerShell 字符串比较默认感知区域性，`c` 前缀也**不会**使其成为 ordinal——`-eq`/`-ne` 与 `-ceq`/`-cne` 都会查询排序表，而排序表恰好会把此处涉及的字符报告为“相同”。符号中的双向文本标记可被排序忽略（`"$([char]0x061C)+" -eq '+'` 返回 `$true`，`-cne` 同样返回 `$false`）；每套原生数字也与对应 ASCII 数字排序相等（`"$([char]0x0660)" -ne '0'` 返回 `$false`，尽管两者是不同字符）。对相同数据运行相同扫描会得到：

| 写法 | 非 ASCII `PositiveSign` | 非 `'0'` `NativeDigits[0]` |
| --- | --- | --- |
| ordinal 比较 | **63** | **92** |
| `-ne` | 0 | 0 |
| `-cne` | 0 | 0 |

**`Group-Object` 是同一陷阱的更糟形式，因为它隐式且静默。**它的默认键比较同样感知区域性，因此按原始 `NativeDigits[0]` 字符串对这 92 种区域性分组，只会得到**一组** 92 项——全部 11 套数字折叠在一起，既无错误也无警告。改用码位作键（`[Text.Rune]::GetRuneAt(…).Value`）后，得到上述 **11** 组；符号侧也以相同方式把 4 种写法折叠为 1 组。因此，即使筛选全程使用 ordinal 比较并正确得到 92，也仍可能在最终分组步骤丢失整个分布。任何重新运行该矩阵的人都必须以 ordinal 方式进行比较**并生成键**，否则测量的是排序表而不是区域性数据。

该测量是一次性的，应将其视为问题可以关闭的论据，而非实时门禁：若未来编辑为该门禁提供受区域性影响的 `NumberStyles`（`AllowThousands`、`AllowDecimalPoint`），或让值以原始文本而非经 `[string]` 转换进入，则应重新运行该矩阵，而不是相信本段。上述反例恰好落在第二个触发条件上，这证明触发条件监视的是正确接缝。

`source.laneProvenance` 还必须**完整**。其执行通道集合必须与基线程序集行记录的执行通道集合双向精确相等：不得缺失、不得多出、不得重复。部分 `laneProvenance` 会比其替换的扁平三字段更糟——扁平字段至少明确自称运行级字段，而五个执行通道耗时旁只有一行来源证明，会形成看似完整的静默部分记录；该拆分正是为了防止这种失败。每行的 `jobName` 也是来源证明，并使用绑定摘要的同一个 `Get-NervTestEvidenceLaneJobs` 允许清单检查：对允许清单中的执行通道，作业名称必须精确匹配，因此行不能空白、臆造或借用同级执行通道。（允许清单外的执行通道——只有旧版控制台导入中被允许清单有意省略的未分片 `backend`——仍必须给出某个作业名称。）

**精确让渡的内容：**`runnerOs` 跨执行通道相等。现在同一基线的两个执行通道可以在摘要门禁中报告不同操作系统，而以前不允许。除此以外没有让渡任何内容——`runnerImage` 和 `dotnetSdk` 从未有可失去的逐执行通道补偿检查；所有逐摘要结构规则都得到保留，并新增一条。以下三点覆盖 `runnerOs` 情况：该值现在必须匹配 `Linux`/`Windows`/`macOS` 枚举（以前只要求非空）；`Assert-NervEvidenceRootAuthority` 从**各执行通道自身的作业日志**推导操作系统并拒绝不匹配；当前接入 CI 的五个执行通道全部运行在 `ubuntu-24.04`，因此真正的跨操作系统基线会是有意的拓扑变更，而非漂移。

除此以外，逐摘要结构验证保持不变：镜像必须是解析后的 `<image>@<version>`（绝不能是 `ubuntu-latest` 等选择器），SDK 必须是精确的三段版本。更重要的是，真正保护环境记录的检查从来不是跨执行通道比较，而是 `Assert-NervEvidenceRootAuthority`：它下载**每个执行通道自身的作业日志**，并要求该通道摘要匹配从该日志独立解析的镜像、操作系统和 SDK。这严格强于跨执行通道相等：它能拒绝借用同级执行通道真实存在镜像的执行通道，而相等检查按定义无法做到。契约测试覆盖已接受的混合镜像运行、逐字段拒绝混合运行身份、基线中的逐执行通道记录、执行通道缺失/多出/重复及 `jobName` 空白/臆造/借用情况、schema 版本门禁，以及单执行通道权威被调换/伪造的情况。

Script Governance CI 作业和 `check-script-compatibility.ps1 -FastOnly` 都直接执行 `scripts/tests/test-evidence.Tests.ps1`，因此语义契约失败会传播实际进程退出码，而不是依赖源码扫描。`summary.json` 和作业摘要公开相同的选中执行通道选择器与逻辑结果行。

成功保留的产物会有意用有界、隐私安全的占位符替换失败测试的原始消息，并保持 `diagnostics.log` 为空；失败根因仍位于受访问控制的 Actions 作业日志中。这是有意设置的隐私边界，并非声称保留产物包含完整失败诊断。

本地夹具结果、本地完整解决方案执行、PR CI 与产物可用性、合并状态、真实依赖执行通道的实际执行，以及合并后的测试粒度基线刷新，都是彼此独立的交付状态，任何一项都不能推出另一项。
