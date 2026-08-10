# AGENTS.md — Nerv-IIP 平台

> 规范的代理指令文件（所有代理/模型共享）。查看当前项目状态前，始终先读取 `docs/architecture/implementation-readiness.md`。

## 开始前

**进行变更前必须先读取 `docs/architecture/implementation-readiness.md`。**该文件记录当前阶段、已交付服务、数据库 schema 与环境前置条件。不得根据既有知识假定服务、schema 或端口已就绪，必须在那里核实。

其余资料的位置如下：

- **架构决策**：`docs/adr/`，按编号顺序排列。
- **服务边界**：`docs/architecture/context-map.md`。
- **目录规则**：`docs/architecture/repo-layout.md`（规范仓库布局）。
- **本地开发 / Aspire 排障**：`docs/architecture/local-dev-troubleshooting.md`；排查启动、基础设施容器或部署产物前先阅读。
- **其他主题**：`docs/architecture/` 的文件按主题命名（例如 `database-schema-conventions.md`、`frontend-structure.md`），按关键词搜索。

容易忽略的边界：`backend/` 中每个目录是一个 CleanDDD 服务（`services/`、位于 `services/Business/` 的业务服务），另有 `gateway/`（面向 Console 的 PlatformGateway、面向 Business Console/PDA facade 的 BusinessGateway）和位于 `common/` 的窄共享库。`connector-hosts/` 是独立的 .sln，绝不得合并进 `backend/`，也不得与 `backend/` 或 `frontend/` 建立引用（反向亦然）。

## 命令

### 本地开发启动

```powershell
.\nerv.ps1 bootstrap        # 已连接空白机器的预检、还原与本地密钥
.\nerv.ps1 bootstrap -InstallMissing -Start
.\nerv.ps1 dev              # 通过 Aspire CLI/AppHost 启动完整平台
.\nerv.ps1 stop             # 通过 Aspire CLI 停止当前 AppHost
.\nerv.ps1 status           # 显示运行中的 Aspire AppHost/资源
.\nerv.ps1 logs apphub      # 持续输出 Aspire 资源日志
.\nerv.ps1 wait gateway -Status up -TimeoutSeconds 600
.\nerv.ps1 dev -InfraOnly   # 仅启动基础设施（PostgreSQL、Redis、RabbitMQ、MinIO、OTel）
.\nerv.ps1 publish-compose  # 生成 Aspire Docker Compose 产物
.\nerv.ps1 ports            # 规范端口矩阵
.\nerv.ps1 fullstack run -Scenario smoke  # 代理自有的真实全栈验证
.\nerv.ps1 fullstack start               # 仅用于交互式诊断
.\nerv.ps1 fullstack stop                # 停止精确的诊断会话
```

代理自有的真实全栈验证必须使用 `fullstack run`。交互式 `fullstack start` 仅供诊断，交接前必须停止。

### 后端（.NET 10）

```powershell
dotnet build backend/Nerv.IIP.sln
dotnet test  backend/Nerv.IIP.sln
dotnet test  connector-hosts/Nerv.IIP.ConnectorHost.sln

# EF migration：显式设置 PostgreSQL profile：
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name> `
  --project backend/services/<Svc>/src/Nerv.IIP.<Svc>.Infrastructure `
  --startup-project backend/services/<Svc>/src/Nerv.IIP.<Svc>.Web
```

### 前端（Node.js >=22.18.0，pnpm 11.13.1）

```powershell
pnpm -C frontend check         # 格式 + lint
pnpm -C frontend typecheck     # 最快的单项检查
pnpm -C frontend test           # vitest
pnpm -C frontend build          # 生产构建
pnpm -C frontend generate:api   # 从 Gateway OpenAPI snapshot 生成 Hey API 代码
```

### 脚本（受治理）

```powershell
scripts/check-script-governance.ps1   # 所有脚本的门禁
scripts/verify-*.ps1                  # 验证脚本
```

## 变更决策表

| 变更区域 | 必读文档 | 必需检查 |
|---|---|---|
| 后端服务 / endpoint | implementation-readiness、api-contract-and-codegen、facade-coverage-matrix | `dotnet test backend/Nerv.IIP.sln`（包含 facade-coverage 门禁）；在 `facade-coverage-matrix.json` 中将每个新增/变更的业务 endpoint 声明为 `exposed`/`deferred`/`internal`；若契约变更则导出 OpenAPI |
| Gateway route / contract | api-contract-and-codegen | 后端测试；导出 OpenAPI；`pnpm -C frontend generate:api` |
| DB schema / migration | database-schema-conventions、database-schema-catalog | migration + schema 约定测试；更新 catalog + comments |
| 前端页面 / 功能 | frontend-structure | `pnpm -C frontend check && pnpm -C frontend typecheck && pnpm -C frontend test && pnpm -C frontend build` |
| 脚本 | script-automation-governance | `scripts/check-script-governance.ps1` |
| Connector Host | connector 边界文档 | `dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln`；不得引用后端服务实现 |
| 基础设施 / Aspire 部署 | deployment-baseline | `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj` |

PDA 变更还须运行 `pnpm -C frontend --filter @nerv-iip/business-pda` 的 `typecheck`/`test`/`build`；影响原生 Capacitor 产物时运行 `cap:sync`。

## Scope Gate：任务定级门（建票时与实施前强制）

完整规则见 `skills/scope-gate/SKILL.md`（判级触发器、合理化对照、红旗清单以该文件为准；安装：`npx skills add ./skills/scope-gate --copy`）。最低要求：

1. 每张 issue 建票时定级并打 `scope:S/M/L/XL/spike` 标签，票面首段写一行定级理由；一张票只装一个问题，多发现清单按域拆成多张票。
2. 任一升级触发器命中即 L（跨业务域、DB+契约+UI 连动或两跳链、架构决策、兼容/迁移、验收不清、须先读大量代码、测试策略不明、账本/清单型任务、触及 workflows/门禁/共享测试基建）——判级不看代码量。
3. **L 级不得直接进入实施**：先在票面写拆解清单（每个子项 ≤M、各自独立 PR 可审可绿），母票只跟踪；XL 先出 Spec/ADR 独立评审再拆票；`?` 开 timebox 的 Spike 票，只产出报告与重新定级的新票，不合并生产代码。
4. 实施会话开工的第一个动作是复述级别与交付形态（几个 PR、顺序、各自验收），与票面不符先改票面；实施或审核中改动滚出票面范围，立即停下把膨胀部分开新票，本 PR 守住原范围。

## 已知基线注意事项

- GitHub CI 为前端运行 `check` + `typecheck` + `build`；变更决策表中的完整本地门禁仍包含 `test`。
- 依赖 Docker 的 `verify-*.ps1` 脚本需要 Docker daemon 运行；若不可用，应报告并跳过，而不是视为代码失败。

## 文档与协作语言

所有新增或修改的人工文档默认使用简体中文，包括标题、章节、说明段落、表格中的业务含义、步骤、注意事项、验收结论，以及 AGENTS、CLAUDE、Copilot 和 Skill 指令中的自然语言要求。子目录 `AGENTS.md` 自动继承此全局要求，且不得削弱它。

代码、命令、标识符、路径、文件名、URL、版本号、正则表达式、API/类型/方法/变量/配置键/协议字段/数据库标识符，以及必要的产品名、库名、标准名、缩写和没有稳定中文译名的专业术语可以保留原文。为保持可搜索性而保留英文术语时，所在句子必须给出中文解释。翻译不得改变需求强度、状态、数字、日期、边界条件、命令语义、代码围栏、链接目标或表格结构；`MUST`、`SHOULD` 等规范性词汇可以译为“必须”“应当”，不得弱化语义。

Linear 中的项目名称与说明、Issue 标题与正文、评论、审核意见、状态说明、验收记录、复盘结论及其他面向团队的协作文本默认使用中文。引用外部英文原文时可保留短引文，但必须提供中文上下文或中文结论。不得翻译机器输入、生成文件，或必须保持原样的测试夹具、日志、运行证据、指纹、快照和协议样本；具体范围与分类规则见 `docs/architecture/document-language-governance.md`。

## 后端测试确定性

- Scheduler、lease、expiry 等时间语义必须注入 `TimeProvider`；只有真实 transport/process 才使用 wall clock。
- 异步可见性断言使用有界 `Eventually`，并报告脱敏 condition、elapsed、attempts 与 last observation；禁止 fixed sleep-before-assert。
- 推进假时钟前必须先等待被测计时器**已注册**的显式边沿信号；`await Task.Yield()` 与“`BackgroundService.StartAsync` 已返回”都不是屏障。晚注册的计时器会以推进后的 now 重新定期，tick 永久丢失、等待方永不返回（MAN-799 与 MAN-663 各踩一次）。
- 网络测试必须显式区分 connection/request budget、caller cancellation 与业务 HTTP response；不得输出 headers/body/凭据。
- FluentValidation、culture、`TZ`/env 等可变全局值使用 scoped capture/restore 并序列化 mutator；FastEndpoints 静态变异只能使用 collection serialization、sacrificial process isolation，或对“写该静态状态的动作”与“读它的动作”使用显式互斥门（MAN-663 的 `BusinessGatewayTestHostGate`：宿主构建独占全部 permit，请求各持一个 permit 且必须**服务端**持有，覆盖整条服务端管线）。三者均不得宣称 restore；互斥门必须有在门被削弱时失败的回归测试，否则退回前两种。
- 网络失败分类必须显式接收 caller 的 `CancellationToken`：caller 取消原样传播，只有 helper 自己的超时才算 `RequestTimeout`；对端返回的 408/504 也是 timeout，不并入业务错误。生产默认超时按真实依赖的正常抖动取秒级，毫秒级预算仅由测试通过配置覆盖。
- 测试体中不得手写 `Environment.SetEnvironmentVariable` / `CultureInfo.Current*` / `ValidatorOptions.Global.*` 赋值；应使用 `GlobalTestStateScope` 的 mutator（`UseCulture`、`SetEnvironmentVariable`…）。它串行化全部 mutator、按行捕获旧值、精确恢复“不存在/空串/有值”三态，并在 dispose 后拒绝再写。自建的 `PreserveEnvironment`/`RestoreEnvironment` 只恢复而不串行化，是弱化复制品。
- `backend/test-determinism-baseline.json`（schema 3）每行必须声明 `classification`：`expiring-debt` 是已承认债务而不是豁免，除 `ownerIssue`/`exitCondition`/`expiresOn` 外，还必须以 `registeredByIssue` 记录登记变更、以 `registeredOn` 记录登记日期；owner 必须是登记变更之外仍存在的 issue。`ownerIssue` 与 `registeredByIssue` 只接受 `MAN-\d+` 或 `#\d+`，按命名空间及去前导零后的数字比较 canonical identity，相同即为自担保并失败。`registeredOn`/`expiresOn` 都按 UTC、invariant `yyyy-MM-dd` 解析：登记日不得晚于 UTC 今日，expiry 必须在登记日到其后 45 天的含边界区间内，且早于 UTC 今日仍会失败；到期日按类别错开，`reason` 按行而非按文件书写。`permanent` 仅用于“被扫描构造本身就是受审计原语”的位点（原语实现与自身自测），必填 `rationale`，禁带 `ownerIssue`/`registeredByIssue`/`exitCondition`/`registeredOn`/`expiresOn`，且仅在 checker 持有的 `路径=pattern=maxRows` 白名单容量内生效。当前三条容量为 `GlobalTestStateScopeTests.cs=StaticSetter=12`、`GlobalTestStateScope.cs=StaticSetter=9`、`BoundedObservationWindow.cs=Task.Delay=1`。白名单按 path/pattern ordinal 精确匹配，容量统计通过行级校验的 permanent baseline 行数，而非 source occurrence 或 `occurrenceCount`；同一文件新增其他 pattern 不会被旧理由连带放行，baseline 也不能自行扩容。新增常设例外或提高容量必须修改 `scripts/check-backend-test-determinism.ps1` 并走脚本治理。`rationale` 按同一条常设理由书写（scope 外前置/teardown、scope 内被测变异、原语实现、轮询原语各一条）。当前到期债务为 0 行。
- 扫描范围包含 `backend/common/Testing/**`：静态写入集中到 `GlobalTestStateScope` 后，该边界是受依赖的设计前提而非附带事实，因此共享测试基础设施目录必须处于门禁中（checker 找不到该目录下的项目会直接失败）。
- 必需/按需启用的执行通道、隔离登记表及其强制执行仅由 MAN-661 管理；普通测试变更不得自行建立隔离规则。

## 核心原则

1. 平台优先于业务。行业语义（工厂、产线、设备模型）位于领域扩展中，绝不放入主平台、PlatformGateway、IAM、AppHub、Ops 或主控制台。
2. 先冻结逻辑边界，物理部署保持灵活。
3. 前端仅使用显式 Vue 结构（不使用伪 Nuxt runtime）；后端按服务边界组织（不得退化为单体）。
4. 应用集成通过 `connector-hosts/` 中的 Connector Host 模式完成。
5. AI 能力遵循治理 → 查询 → 低风险操作；主平台中不托管模型。
6. Platform SDK 是模块化且仅客户端使用的；外部单元不得引用主平台内部实现。
7. File Storage 与 Notification 是通用能力；业务服务仅按 ID 表达意图。
8. 平台、应用、Connector Host、扩展之间保持主版本对齐。
9. 文档、契约与目录是稳定性的基础；结构变更前先更新文档。
10. Aspire 是唯一部署模型；Compose/installer/package 从其适配生成。
11. 自动化脚本是受治理、可审计的工程资产。

## “禁止”约束

1. 不得创建无归属的 SharedKernel/Common/Utils 巨型目录。
2. 不得将 Connector Host 合并进 `backend/Nerv.IIP.sln`，也不得在 `connector-hosts/` 与 `backend/`/`frontend/` 间建立交叉引用。
3. 不得将行业领域规则写入 PlatformGateway、IAM、AppHub、Ops 或主控制台。
4. 不得创建跨 schema 外键。
5. 不得在非一次性环境使用 `EnsureCreated()`。
6. 脚本中不得直接调用 `dotnet`、`docker`、`pnpm`、`pwsh`；应 dot-source `scripts/lib/ScriptAutomation.ps1`（`Invoke-DotNet`、`Invoke-Pnpm`…）。不得定义名为 `Write-Error` 的函数（会遮蔽内置 cmdlet）。
7. 不得在 startup 文件中编写 Minimal API route mapping；仅使用 FastEndpoints。
8. 不得手工编辑 OpenAPI snapshot 或 generated client code。
9. 应用代码不得使用非 `Nv*` 的 component name，也不得 deep-import `components/ui/`（参见“NvUI 组件库”）。
10. 不得在 Domain/Application/Endpoint/SDK 层引用 provider-specific API 或编写 raw SQL。
11. 不得在 `infra/` 或仓库中存储 credentials、secrets 或 customer keys。
12. 不得以 `dotnet run` 启动 AppHost；始终通过 `.\nerv.ps1 dev`/`stop`/`wait`/`logs` 使用 Aspire CLI（参见 local-dev-troubleshooting）。

## NvUI 组件库：命名与导入边界（前端）

NvUI 是 `@nerv-iip/ui` / `@nerv-iip/ui-mobile` 中 Nerv-IIP 的品牌组件层。权威规范为 ADR 0020（`docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md`；**Appendix A = 冻结的逐组件映射表**）。

1. **应用/业务代码使用 `Nv*` 品牌组件**（`NvButton`、`NvDataTable`、`NvPageHeader`、`NvOeeHero`、`NvMobileBadge`，…）。没有 `Nv` 前缀的名称是 shadcn 原版 base primitive：当存在对应 `Nv*`（Appendix A）时，原版名称不得在应用代码中使用；尚无 `Nv` 版本的少数 primitive（`Alert`、`Empty`、`Toaster`、`Skeleton`，…）仍从同一 bare barrel 使用原版名称。
2. **仅通过稳定边界导入：**裸 `@nerv-iip/ui` 与 `@nerv-iip/ui-mobile`（唯一允许的 sub-entry 是 `@nerv-iip/ui/file-preview`）。禁止 deep path、直接 `reka-ui` 或直接 `shadcn-vue`。
3. **通过 contract test 而非 ESLint 强制执行**（ADR 0006）：每个 app 的 `nvui-imports.contract.test.ts`，以及每个 package 的 `nvui-naming.contract.test.ts`。
4. **package 名称永不变更**（ADR 0020 Decision 2）：品牌体现在 `Nv*` 前缀，绝不得顺带重命名 `@nerv-iip/ui` / `@nerv-iip/ui-mobile`。
5. `components/ui/` 中的 shadcn 原版保持 byte-for-byte 不变（没有 `Nv`，没有 `--nv-`）。定制通过品牌层中的重建副本实现。

**四类界面映射**（仅为示例；冻结表见 ADR 0020 Appendix A）：

| 界面 | package · layer | 命名规则 | 示例 |
|---|---|---|---|
| PC（console / business-console） | `@nerv-iip/ui` · `pc/` `blocks/` `layout/` | 素名优先 → `Nv` + plain name | `NvButton` `NvDataTable` `NvPageHeader` |
| Mobile（business-pda） | `@nerv-iip/ui-mobile` | 与原版/PC 冲突 → `NvMobile*`；移动原生专名 → `Nv*` | `NvMobileBadge` `NvMobileDialog` · `NvScanBar` `NvCell` |
| Touch（工位看板 / 车间一体机） | `@nerv-iip/ui` · `touch/` | 冲突 → `NvTouch*`，否则 `Nv*` | `NvTouchButton` `NvQtyStepper` |
| Screen（大屏 / 挂墙） | `@nerv-iip/ui` · `screen/` | 通用词 → `NvScreen*`；工业专名 → `Nv*` | `NvScreenButton` · `NvOeeHero` `NvTaktGantt` |

跨两个界面的组件必须分别构建（每个 layer 一份），绝不使用“一个组件、两种模式”。新名称遵循 ADR 0020 §1.2（R1–R5）。

## 常见错误

以下是曾造成真实回归的仓库专属陷阱。运行/启动经验（Aspire、infra pinning、部署产物）见 `docs/architecture/local-dev-troubleshooting.md`。

1. **Endpoint 默认带 `[AllowAnonymous]`。**内部服务 API 需要 `[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]`；Gateway Console endpoint 使用 `GatewayPolicies.ConsoleAuthenticated`；仅 health endpoint 保持匿名。
2. **Scaffold 残留。**从 NetCorePal template 生成后，删除所有 demo endpoint/aggregate/test；核实 `ServiceName`；将 `UseAuthentication()` 置于 `UseAuthorization()` 前。
3. **用 `Guid.NewGuid()` 而非 `Guid.CreateVersion7()`。**EF 的 v7 generator 仅在默认 ID 时触发；constructor 中赋予 v4 GUID 会破坏时序索引局部性。
4. **同步 EF Core 调用。**Repository/service/query-handler 方法必须异步并带 `CancellationToken`。
5. **并行测试中使用 `Environment.SetEnvironmentVariable()`。**使用 `builder.UseSetting()` 或 `DisableParallelization` collection。
6. **PostgreSQL DbContext 缺少 `MigrationsHistoryTable("__EFMigrationsHistory", "<schema>")`。**否则 history 会落在 `public`。
7. **用字符串比较布尔配置。**使用 `builder.Configuration.GetValue<bool>(...)`。
8. **Query filter 与 DB unique index 不匹配。**若 unique index 有 N 列，dedup/lookup query 必须过滤全部 N 列。
9. **在 Domain aggregate 中存放 secrets。**Secret name/API key/credential reference 仅可位于 Infrastructure 或 configuration。
10. **InMemory auth store 签发假 token。**它们必须经服务的 token issuer 产生真实 JWT，否则 Gateway JWT validation 会拒绝所有请求。
11. **测试断言通过 source-file path traversal。**使用 DI、`DbContext` reflection 或 `Nerv.IIP.Testing` helper；绝不使用 `Path.Combine(AppContext.BaseDirectory, "..", ...)`。
12. **无上下文的 readiness endpoint 报告上下文相关阻塞。**未提供 SKU/work-center/device scope 时，全局 readiness 不得报告该类阻塞；所属前端显示选择提示或 empty state。
13. **前端 facade 在 business scope 为空时发起调用。**Composable 应规范化 ID 并抑制所需 scope 为空的 query；不请求或给出明确 empty state，绝不重复失败调用。
14. **demo/default identifier 导致 500。**`WO-001` 之类默认值仅是 UI 便利项，不保证 seed 存在；handler/facade 对缺失记录返回领域恰当的 empty/`Unknown` 结果。

## “完成”定义

按变更决策表验证，最低要求如下：

1. ✅ 受影响区域的 targeted test 通过。
2. ✅ 无新增 warning（后端将 warning 视为 error）。
3. ✅ 契约变更时已刷新 generated artifact（OpenAPI → api-client）。
4. ✅ schema 变更时已更新 DB migration/catalog/comment。
5. ✅ 脚本变更时脚本治理通过。
6. ✅ 受影响的 `verify-*.ps1` 脚本仍通过。
7. ✅ 已更新 `docs/architecture/` 与 `docs/adr/` 中相关文档。
8. ✅ 新增/变更业务服务 HTTP endpoint 已在 `facade-coverage-matrix.json` 声明为 `exposed`/`deferred`/`internal`，且 facade-coverage 门禁通过（如下）。

## Facade 覆盖治理：业务 endpoint 的两跳 DoD

业务能力仅在同时交付服务 HTTP endpoint 和 Gateway facade 这两跳时才可端到端使用（OpenAPI snapshot → `pnpm -C frontend generate:api` → `types.gen.ts` → stable barrel）。任何新增或变更业务服务 HTTP endpoint 的 issue/PR 都必须逐 endpoint 声明：

1. **`exposed`**：同一 PR 交付 facade + OpenAPI export + codegen + stable-barrel re-export。
2. **`deferred`**：显式延后；在 `facade-coverage-matrix.json` 以 `followUp` 登记。它是受跟踪缺口，绝不静默。
3. **`internal`**：按设计永不暴露（service-to-service、background scheduler、connector/WCS callback）；以 `rationale` 登记。

**强制机制：**`backend/tests/Nerv.IIP.FacadeCoverage.Tests`（位于 CI 的 `dotnet test backend/Nerv.IIP.sln` 中）反射每个服务的 `*EndpointContracts.All` registry；未登记的 live endpoint、Gateway snapshot 缺少的 `exposed` 行，或被静默给出 facade 的 `deferred`/`internal` 行都会失败。Registry 与说明位于 `docs/architecture/facade-coverage-matrix.{json,md}`。新业务服务也必须在 gate project 中登记其 `.Web` assembly。

## GitHub 工作流

- 创建 PR 时直接使用 `gh` CLI（本仓库的 GitHub connector 曾多次返回 404）。若 `gh` 操作失败，明确报告命令和错误。
- PR 描述必须回答文档影响检查：变更是否影响产品文档（`frontend/apps/docs`）？新增页面、变更业务流程或用户可见行为均为“是”；在同一 PR 更新文档或引用后续 issue；否则写“文档：无影响”。IA 规则见 ADR 0021。
- 若 PR 新增/变更业务服务 HTTP endpoint，逐项说明 facade 声明（`exposed`/`deferred`/`internal`），并确认已更新 `facade-coverage-matrix.json`。这是硬门禁。

## 子目录覆盖

子树中最近的 `AGENTS.md` 会扩展并覆盖本文件。当前包括：

- `frontend/apps/business-console/AGENTS.md`：业务前端三大支柱（产品/业务/UX）。
- `frontend/apps/business-pda/AGENTS.md`：PDA 命令、测试四层定义、移动端硬规则。
- `frontend/apps/screen/AGENTS.md`：大屏设计硬门禁、数据 seam 三段式。
- `frontend/packages/ui/AGENTS.md` 与 `frontend/packages/ui-mobile/AGENTS.md`：NvUI 库内侧规则（原版零改动、R1–R5 命名、token/layer）。

使用 `AGENTS.override.md` 作为临时覆盖；删除它即可恢复基础治理。
