# route、permission 与 shape registry 权威盘点

本文是 NERV-872 的 Spike 调查报告。快照基于 2026-08-18 的
`c67bb99ed28e2c476f29d01decf0ee7bfe6eb1d9`。本次只盘点 producer、consumer、重复形式、
漂移风险、现有验证与建议 seam，并拆分后续票；不修改 production、test、contract、
generation、workflow 或 script。

相关治理边界见[仓库布局](./repo-layout.md)、[上下文地图](./context-map.md)、
[API 契约与代码生成](./api-contract-and-codegen.md)、
[Facade coverage 矩阵](./facade-coverage-matrix.md)与
[文档语言治理](./document-language-governance.md)。

## Scope-Gate 与证明边界

- Gate 2 定级为 `scope:spike`：一个工程日、只交付本报告和一个 ready PR。
- 本次不实施 registry 迁移，不批量刷新快照或摘要，不把发现夹入 production/test 修复。
- 边界固定且可独立审查、独立变绿的后续项拆成 `scope:M`；跨层权威选择进入
  `scope:XL / Spec`，裁决后再按 producer/consumer 分批。
- `rg`、集合差和静态解析只证明当前源码/文件存在及相互关系；它们不证明端点真实启动、
  鉴权成功、路由可达、完整生成链执行或跨服务数据一致。除“复核命令”明确记录的门禁外，
  本报告不把任何未运行路径标为通过。

## 结论

1. 当前没有一个跨 backend service、Gateway、IAM、frontend 的全局 registry 可以直接宣布为
   单一权威。应先按事实所有权分 seam，而不是把所有字符串移动到一个巨型公共包。
2. 服务侧 `*EndpointContracts.All` 实际驱动 FastEndpoints 的 method、route 与 authorization；
   `facade-coverage-matrix.json` 则是 facade 分类权威。两者复制 endpoint identity，但现有双向
   测试已能发现 13 个已接线服务内部的行漂移，真正盲区是“新服务在两份手工 assembly 清单中
   同时遗漏”。
3. permission 已出现真实漂移：业务服务使用的 100 个 unique permission code 中，5 个不在
   IAM 的 95 个 business seed code 中。Inventory 占 2 个，Quality 占 3 个；Gateway 还使用了
   其中 1 个。各层现有测试只对本层常量自证，没有 closed-world consumer→IAM 门禁。
4. Business Console 当前静态比对的 89 个可比较 route 没有发现 navigation/page-meta 权限差，
   但没有全树 fail-closed 门禁；PDA 的缺项风险更直接，permission map 漏 key 时当前表达式会
   默认显示任务。
5. Gateway OpenAPI 快照与 generated client 是受权威生成入口和 drift gate 约束的派生物，
   不应当作需要手工合并的重复 registry。Gateway 手写 downstream/facade DTO 则需按 BFF 语义
   区分 intentional mapping 与纯 passthrough，不能用字段相似度批量去重。
6. `WorldHistory*` 跨服务 shape/spec 副本是服务隔离下的有意复制，但变体和护栏不完整。
   直接抽公共实现会引入反向耦合；该族已由 NERV-751、NERV-765 承接，先做架构/护栏裁决。

## 总体盘点

| registry 族 | producer | consumer | 当前权威来源 | 重复形式与规模 | 主要漂移风险 | 当前验证 | 建议 seam | 分流 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 服务 endpoint | 13 个 Business Web 的 `*EndpointContracts.All` | FastEndpoints `Configure*Contract`、OpenAPI operationId、FacadeCoverage | service-local endpoint contract | matrix 复制 415 行 service/method/route/operationId | 新服务可在测试源码和 csproj 两份接线清单中同时遗漏 | 已接线服务做 live↔matrix 双向比较，另核对 Gateway OpenAPI operationId；仅要求 live >=300 | 保留 service contract 为 endpoint shape 权威；matrix 只维护分类，服务集合做 closed-world discovery | NERV-882；可机械收口 |
| acceptance endpoint catalog | 部分 service contract | `PublicBusinessEndpointCatalog.All` 的 #77 chain 测试 | 场景所需 endpoint 清单 | 名称称 `All`，实际只组合部分服务/合同 | 容易把存在性清单误报为全平台或真实 full-chain 覆盖 | 只断言场景所需合同存在 | 改成封闭场景 catalog/名称，显式列出未覆盖域 | NERV-887；可机械收口 |
| IAM / service / Gateway permission | service endpoint metadata、Gateway auth 常量、IAM seed | runtime authorize、IAM catalog/role、frontend visibility | 当前分裂；按上下文地图 IAM 拥有 permission facts | service 100、IAM business seed 95、Gateway 93、Business Console 67 个 unique code | 已有 5 个 consumer code 无 IAM seed；描述、角色迁移也可独立漂移 | 各层局部测试；无全仓 consumer→seed 闭合测试 | 先修真实语义，再加 closed-world gate；长期权威由 Spec 裁决 | NERV-883、885、888、886 |
| Business Console route/permission | 99 个 page 文件及 `definePage.meta` | router guard、`navigation.ts` 102 项/90 unique route | page 文件是路由与 guard metadata 的运行来源；navigation 是产品 IA | route 与 permission 在 page/navigation 两写 | 新页面/菜单只改一侧；手工测试覆盖点状 | 静态盘点 89 个可比较 route 权限相同；无 whole-tree 门禁 | navigation 保留 label/icon/order，只引用 typed route；权限派生或全树等价断言 | NERV-889；边界固定 |
| PDA task kind | `PDA_TASK_KINDS` 13 项 | PDA 首页 permission/icon map 与文件路由 | business-core task kind 是任务目录 | ID 在 task、permission、icon、page route 多写 | permission map 漏项时 `!permission` 默认显示 | 当前 13 项均有映射/页面；测试未穷举所有 key | literal-union ID + exhaustive map，缺项 fail closed | NERV-884；可机械收口 |
| OpenAPI / generated client | Gateway runtime Swagger | 快照、Hey API generated client、stable barrel、apps | Gateway runtime OpenAPI | 两份 JSON 快照与 generated 目录 | 手改派生物或漏跑 codegen | export→generate→git diff 的现行 drift gate | 保持现有生成 seam；不手工合并 | 已有 NERV-658/NERV-491；非本次新票 |
| Gateway DTO shape | downstream HTTP response 与 facade endpoint DTO | BusinessGateway adapter、Swagger、frontend client | 每个 facade 的公开 DTO 语义 | `Application/BusinessServices` 有大量 record；存在显式 passthrough 副本 | downstream wire 与 facade shape 独立变化 | focused mapping/test，OpenAPI 只覆盖 facade 输出 | 逐 capability 判断“映射”还是“纯透传”；生成 transport 边界由 NERV-658 裁决 | 需架构裁决 |
| WorldHistory shape/spec | 各服务 seed 实现 | 各服务本地 seed、golden vector、full-chain | 当前为多服务本地副本，没有全族权威 | 多个 `WorldHistory*Spec/Calendar/Configuration/Random/Timeline` 族有同型副本和显式变体 | 公共部分静默漂移；当前实现快照可能被误当规格 | 局部 golden vector 与可选真实 full-chain；覆盖不完整 | 中立 manifest/codegen、共享 fixture package、或保留副本+结构 digest 三案裁决 | 复用 NERV-751、765 |

## route registry 细目

### 服务 endpoint 与 facade classification

`docs/architecture/facade-coverage-matrix.json` 当前有 415 个 endpoint，分布如下：

| service | endpoint 数 |
| --- | ---: |
| Approval | 16 |
| BarcodeLabel | 12 |
| DemandPlanning | 16 |
| Erp | 55 |
| IndustrialTelemetry | 27 |
| Inventory | 17 |
| Maintenance | 26 |
| MasterData | 49 |
| Mes | 55 |
| ProductEngineering | 39 |
| Quality | 41 |
| Scheduling | 15 |
| Wms | 47 |

`FacadeCoverageMatrixTests` 反射这些 assembly 的 `*EndpointContracts.All`，比较
service/method/route/operationId、classification 和 Gateway OpenAPI operationId。这个验证对
**已列入** `BusinessWebAssemblyNames` 且被 csproj 引用的服务是双向的，能拦截未登记和 stale 行；
但两份列表都由人维护，`live.Count >= 300` 不能证明候选服务集合闭合。NERV-882 只收口这个
discovery seam，不更改 415 行分类。

`PublicBusinessEndpointCatalog.All` 只组合 MasterData、ProductEngineering、DemandPlanning、Mes、
Erp、Quality 的部分合同、Wms、Inventory、IndustrialTelemetry 与 Maintenance；Approval、
BarcodeLabel、Scheduling 以及 Quality reason-code 等不在其中。它服务于若干场景的“合同存在”
断言，不是全平台 route registry，更不是 HTTP/full-chain 实际执行证据。NERV-887 负责限定名称和
证明范围，而不是把整个仓库塞入 acceptance 项目。

### Business Console 路由

静态扫描得到 99 个 `.vue` page、`navigation.ts` 102 个条目和 90 个 unique route。临时解析脚本
对 89 个可以直接映射的 route 比较 `requiredPermissions`，未发现集合不一致；导航目标也都能映射
到页面。未进入导航的非动态页面包括 `/design-system/blocks`、`/design-system/shell`、
`/engineering`、`/forbidden`、`/login`、`/wms`，其中包含登录/错误页和领域 index，不能简单要求
所有页面都有菜单。

因此建议的 seam 不是从页面生成整棵导航：page meta 继续拥有 route guard 权限，navigation
继续拥有 label、icon、order 与产品 IA，只把 route identity 类型化并让 permission 从 page meta
派生，或由 closed-world test 比较。NERV-889 固定在这个 seam；NERV-491 继续负责更广的
operationId→barrel→composable→route→menu 治理。

### PDA 任务入口

`pdaTaskKinds.ts` 当前声明 13 个 task kind；PDA 首页另有
`KIND_PERMISSIONS: Record<string, string>` 与 `KIND_ICONS: Record<string, Component>`。
当前 key 和页面均齐全，但 `visibleKinds` 使用
`!permission || identity.can(permission)`：新增 kind 漏 permission key 时会默认显示。后端仍是授权
权威，所以这不是后端提权证明，却是可复现的 fail-open 可见性风险。NERV-884 将 ID 收窄为
literal union，并让 permission/icon/routeReady route 穷举失败关闭。

## permission registry 细目

集合扫描以 exact code 比较，当前 unique 计数为：

| 层 | 数量 | 角色 |
| --- | ---: | --- |
| Business service `*PermissionCodes.cs` | 100 | endpoint/runtime consumer |
| IAM business seed | 95 | 当前可授予目录与 seed producer |
| BusinessGateway auth constants | 93 | facade/runtime consumer |
| Business Console `permissions.ts` | 67 | 前端可见性 consumer |

Business service 中存在、IAM business seed 中不存在的 5 项为：

- `business.inventory.expired-stock.override`
- `business.inventory.reservations.manage`
- `business.quality.measuring-devices.manage`
- `business.quality.measuring-devices.read`
- `business.quality.spc.manage`

Gateway 也使用 `business.inventory.expired-stock.override`；Business Console 的 67 项均在 IAM
business seed 中。另有 20 个已 seed 权限没有专属中文描述，当前 `IamPermissionCatalog` 会回退为
code；这不影响 code 集合闭合结论，但说明“code、描述、默认角色”是三个不同迁移维度。

不能在本 Spike 里机械补 5 个字符串：seed 会改变哪些权限可分配，默认角色是否获得权限还涉及
兼容和最小权限语义。因此先以 NERV-883、NERV-885 分域裁决和迁移，再由 NERV-888 建立
consumer→IAM 的 closed-world gate。长期是把 IAM 当编译依赖、建立中立 manifest 多目标生成，
还是保持分布式常量只做验证，交给 NERV-886 的 Spec；禁止业务服务反向引用 IAM Web/Domain。

## shape registry 与生成边界

### 已有权威生成链

现行链路为：

```text
Gateway FastEndpoints runtime
  -> /swagger/v1/swagger.json
  -> scripts/export-gateway-openapi.ps1
  -> frontend/packages/api-client/openapi/*.v1.json
  -> frontend/packages/api-client/openapi-ts.config.ts
  -> pnpm -C frontend generate:api
  -> frontend/packages/api-client/src/generated/**
  -> stable barrel
  -> frontend apps
```

`scripts/verify-openapi-client-drift.ps1` 会真实构建并启动两个 Gateway、导出 Swagger、安装前端
依赖、重新生成客户端，再对快照和 generated 目录执行 git status/diff。快照和 generated code
因此是派生物；存在扫描不能替代这条运行门禁，本 Spike 也没有因为只改文档而运行该重型链路。

### Gateway 手写 DTO

BusinessGateway 的 BusinessServices 层包含大量 downstream/facade record。多数 record 承担 BFF
裁剪、聚合、强类型 ID 或 envelope 解包，shape 相似不等于重复事实源。一个明确例外是
`BusinessConsoleTelemetryModels.cs` 注释承认
`BusinessConsoleTelemetryOperationApprovalSummary` 镜像
`Nerv.IIP.Contracts.Ops.OperationApprovalSummary`，因为 Gateway 没有引用 `Contracts.Ops`；同一链路
还存在 downstream record 与 facade record 两次映射。

是否让 Gateway 引用 public contract、生成 downstream transport client，或保持 adapter 隔离，
会改变服务边界和公开序列化合同，不是机械替换。NERV-658 已是这一问题的先行 ADR/Spike，
本次不另开重复票。

### WorldHistory 跨服务副本

归一化 namespace 后，本次静态分组得到：

- `WorldHistoryCalendar.cs`：12 份、5 个变体，最大同型组 9 份。
- `WorldHistoryConfiguration.cs`：12 份、5 个变体，最大同型组 8 份。
- `WorldHistoryRandom.cs`：12 份、4 个变体，最大同型组 9 份。
- `WorldHistoryTimeline.cs`：8 份、2 个变体，最大同型组 7 份。
- `WorldHistorySpec.cs`：8 份、4 个归一化变体；已有 NERV-765 记录其业务差异和护栏缺口。
- `WorldHistoryPhase2Spec.cs`：4 份同型；另有 Device/Count/Procurement/Mes/Quality 等 spec 族。

这些副本让各服务能独立 seed，不通过共享数据库或跨服务查询；直接搬入公共 runtime 包会用编译
耦合换掉文本重复。建议由 NERV-751、NERV-765 比较三条路线：中立且版本化的 seed manifest
做 build-time codegen、独立 demo/fixture package、或保留服务本地副本并加跨仓 structural digest
与真实跨服务闭环。裁决前不创建逐服务迁移票。

## 后续票与顺序

### 可机械或边界固定

| 票 | Scope | producer/consumer 层 | 依赖 |
| --- | --- | --- | --- |
| NERV-882 | M | Business Web discovery → FacadeCoverage | 独立 |
| NERV-884 | M | task kind → PDA permission/icon/route | 独立 |
| NERV-887 | M | service contracts → #77 acceptance catalog | 独立 |
| NERV-889 | M | page route meta → navigation permission | related to NERV-491；可独立审查 |

### 需领域语义但范围固定

| 票 | Scope | producer/consumer 层 | 依赖 |
| --- | --- | --- | --- |
| NERV-883 | M | Inventory endpoint permission → IAM seed/role | 独立，先于 NERV-888 |
| NERV-885 | M | Quality endpoint permission → IAM seed/role | 独立，先于 NERV-888 |
| NERV-888 | M | endpoint/Gateway permission → IAM closed-world gate | blocked by NERV-883、NERV-885 |

### 需架构裁决

| 票 | Scope | 裁决内容 |
| --- | --- | --- |
| NERV-886 | XL / Spec | permission 单一权威、生成/验证路线、兼容期和分批模板 |
| NERV-658 | 既有 architecture evolution | downstream OpenAPI/generated transport 与 Gateway DTO 边界 |
| NERV-751、NERV-765 | 既有 L/治理母题 | WorldHistory/seed 跨服务权威、护栏与真实闭环 |
| NERV-491 | 既有全链治理 | operationId→barrel→composable→route→menu |

所有新票均为 NERV-872 子票并 related to NERV-679；NERV-679 保持未关闭。

## 复核命令与结果摘要

以下命令均在上述基线 SHA 的独立 worktree 运行：

```bash
# endpoint matrix：415 行、13 个 service；逐 service 数量见上表
jq '.endpoints | length' docs/architecture/facade-coverage-matrix.json
jq -r '.endpoints | group_by(.service)[] | "\(.[0].service)\t\(length)"' \
  docs/architecture/facade-coverage-matrix.json

# FacadeCoverage 的 closed-world 盲区：源码列表 + csproj 引用 + >=300 下限
rg -n 'BusinessWebAssemblyNames|live.Count >= 300|ProjectReference' \
  backend/tests/Nerv.IIP.FacadeCoverage.Tests

# permission 集合：service=100、IAM business seed=95、Gateway=93、frontend=67；
# service - IAM seed 得到上文 5 项，Gateway - IAM seed 得到 1 项，frontend - IAM seed 为空
rg -o 'business\.[a-z0-9.-]+' backend/services/Business --glob '*PermissionCodes.cs'
rg -o 'business\.[a-z0-9.-]+' \
  backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthorization.cs
rg -o 'business\.[a-z0-9.-]+' frontend/apps/business-console/src/permissions.ts

# route/task/shape producer-consumer 取证
rg -n 'requiredPermissions' frontend/apps/business-console/src/navigation.ts \
  frontend/apps/business-console/src/pages --glob '*.vue'
rg -n 'PDA_TASK_KINDS|KIND_PERMISSIONS|KIND_ICONS|!permission' \
  frontend/packages/business-core/src/tasks/pdaTaskKinds.ts \
  frontend/apps/business-pda/src/pages/index.vue
rg -n 'Mirrors Nerv.IIP.Contracts.Ops.OperationApprovalSummary' \
  backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices
```

补充使用临时 Node 静态解析比较 page route/navigation permission，结果为 89 个可比较 route、
0 个 mismatch；使用 namespace-only 归一化脚本对 `WorldHistory*` 文件分组，结果已列于上文。
两者是源码扫描，不是 Vue Router 实际运行或 seed/full-chain 执行。

## 未覆盖边界

- 未启动 13 个业务服务或两个 Gateway，未发真实 HTTP 请求，未证明 415 个 endpoint 全部可达。
- 未运行 OpenAPI export/codegen drift；本 PR 不触及其输入或输出，只核对了权威入口。
- 未执行登录、角色分配或 401/403 场景；5 个 permission 漂移是集合事实，不等于已证明具体用户
  能提权或一定被拒绝。
- 未运行 Business Console/PDA build、typecheck、unit/e2e；静态 route 映射不证明浏览器可达。
- 未运行 WorldHistory seed、真实 PostgreSQL、多服务 full-chain 或 golden vector；副本分组不证明数据
  在运行时一致。
- 未对全部 Gateway record 做字段级语义判定；只记录了源码明确标注的 passthrough 例子，并将系统性
  downstream shape 裁决复用到 NERV-658。
- 本报告未改任何 production/test/contract/generation/workflow/script；后续票的绿灯必须由各自真实
  受影响门禁给出，不能引用本 Spike 的 `rg` 结果代替。
