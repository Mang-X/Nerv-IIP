# API 契约与代码生成参考索引

本页只维护稳定路径、机器事实入口和已批准的窄例外；不复制 Governance 的长期规则，也不维护 endpoint/operationId 第二份清单。

## 权威入口

| 对象 | 当前路径 / 生产者 |
| --- | --- |
| API 运行时架构 | `docs/architecture/integration/api-contracts.md` |
| API/codegen 治理 | `docs/governance/api/contracts-and-codegen.md` |
| API/codegen Runbook | `docs/runbooks/api-codegen.md` |
| Platform SDK 能力基线 | `docs/architecture/platform/sdk.md` |
| BusinessGateway surface 治理 | `docs/governance/api/business-gateway-surface.md` |
| BusinessGateway restore manifest | `docs/reference/api/business-gateway-surface-restore.manifest.json` |
| Facade coverage 治理 | `docs/governance/api/facade-coverage.md` |
| Facade coverage 机器事实 | `docs/reference/api/facade-coverage-matrix.json` |
| API/codegen 历史总账 | `docs/reports/audits/api-contract-and-codegen.md` |
| BusinessGateway surface 迁移前快照 | `docs/reports/audits/business-gateway-api-surface-canonicalization.md` |
| Facade coverage 历史渲染/决策记录 | `docs/reports/audits/facade-coverage-matrix.md` |

## 当前 OpenAPI 与生成路径

| 对象 | 当前路径 |
| --- | --- |
| PlatformGateway OpenAPI snapshot | `frontend/packages/api-client/openapi/platform-gateway.v1.json` |
| BusinessGateway Console OpenAPI snapshot | `frontend/packages/api-client/openapi/business-gateway-console.v1.json` |
| Hey API 配置 | `frontend/packages/api-client/openapi-ts.config.ts` |
| PlatformGateway generated output | `frontend/packages/api-client/src/generated/` |
| BusinessGateway Console generated output | `frontend/packages/api-client/src/generated/business-console/` |
| Business Console 稳定导出 | `frontend/packages/api-client/src/business-console.ts` |
| api-client 总入口 | `frontend/packages/api-client/src/index.ts` |
| OpenAPI 导出 producer | `scripts/export-gateway-openapi.ps1` |
| OpenAPI/api-client drift verifier | `scripts/verify-openapi-client-drift.ps1` |

当前 Hey API 配置只有 PlatformGateway 和 BusinessGateway Console 两个输入。`business-gateway-mobile.v1.json`、`src/generated/mobile/` 与 `mobile.ts` 属于历史总账中记录的后续演进设想，不是当前已交付机器事实；若未来落地，以届时代码/OpenAPI 配置为准。

工具精确版本以 `frontend/package.json`、受影响 package、.NET build 配置和脚本头声明为准，本页不复制版本号。

## Endpoint / operationId 清单的权威来源

当前 endpoint、route、operationId、DTO、权限和生成类型以这些生产者为准：

1. 后端 endpoint/contract 与相应测试；
2. 两个受控 Gateway OpenAPI snapshots；
3. `frontend/packages/api-client/src/generated/**` 机械生成结果；
4. facade 暴露状态则额外以 `facade-coverage-matrix.json` 为机器事实。

迁移前 `docs/reports/audits/api-contract-and-codegen.md` 中的大型 endpoint/operationId 表只用于历史调查，**不是当前清单**。不得从该 audit 复制、恢复或校正当前 endpoint；发现差异时必须回到上述生产者确认。

## 受控兼容例外

### FileStorage `scanStatus` v1 删除（#1604）

2026-08-17 已批准一个窄范围版本例外：在 FileStorage v1 尚未形成受支持客户发布基线、且 `scanStatus` 没有受支持外部消费方的前提下，#1604 允许从当前 `/v1` DTO、PlatformGateway v1 OpenAPI snapshot 和同批生成客户端直接删除病毒扫描字段，而不为该废弃字段单独建立 v2 路由。

该例外要求后端与重新生成的客户端同批升级，并由数据库迁移先对历史非 `clean` 文件 fail closed 后再删除扫描列。它**只适用于 #1604 / `scanStatus`**；FileStorage 形成受支持客户发布后，字段删除仍遵循 Governance 的一般破坏性变更/主版本规则。

### Gateway download-grant 两跳路由 v1 删除（#3314）

2026-09-20 已批准一个窄范围版本例外：在 PlatformGateway / BusinessGateway v1 尚未形成受支持客户发布基线、且被删的四条路由没有受支持外部消费方的前提下，#3314 允许从当前 v1 OpenAPI snapshot 与同批生成客户端直接删除下列路由，而不为它们建立 v2 或保留过渡期：

| 删除的路由 | 原 operationId | 原权限口径 |
| --- | --- | --- |
| `POST /api/business-console/v1/files/{fileId}/download-grants` | `createBusinessConsoleSopFileDownloadGrant` | `business.engineering.documents.read` |
| `GET /api/business-console/v1/files/download-grants/{downloadGrantId}/content` | `downloadBusinessConsoleSopFileContent` | `business.engineering.documents.read` |
| `POST /api/console/v1/files/{fileId}/download-grants` | `createConsoleFileDownloadGrant` | `files.download-grants.create` |
| `GET /api/console/v1/files/download-grants/{downloadGrantId}/content` | `downloadConsoleFileGrantContent` | `files.read` |

**为什么不能走一般的主版本/迁移窗口程序**：这四条路由本身就是缺陷载体。它们把 FileStorage 的 download grant id 交给调用方，而该 id 是全服务共用命名空间、其兑换面既不校验用途也不记录签发门面——#3314 实测两条权限口径不同的路由可以互相兑换对方签发的 grant，三层没有一层拒绝。保留过渡期等于按窗口长度延长一个已确证的越权面。

替代面按 ADR 0030 决策 3 提供，形状不同（以 `fileId` 为入参的单跳字节路由），因此不是重命名而是契约变更：

- `GET /api/business-console/v1/files/sop-documents/{fileId}/content`（operationId 沿用 `downloadBusinessConsoleSopFileContent`）
- `GET /api/console/v1/files/{fileId}/content`（`downloadConsoleFileContent`，要求 `files.download-grants.create` + `files.read` 两个码，与被删两跳合计所需一致、不收窄）

该例外要求后端与重新生成的客户端同批升级（本仓前端调用方已在同一 PR 内迁移，`verify-openapi-client-drift.ps1` 为零漂移的机器判据）。它**只适用于 #3314 与上表四条路由**；两个 Gateway 形成受支持客户发布后，路由删除仍遵循 Governance 的一般破坏性变更/主版本规则。

### MES 列表 `status` 枚举收窄到聚合真实值域（#3912）

2026-09-28 已批准一个窄范围版本例外：#3912 允许把 BusinessGateway v1 OpenAPI 中
**MES 列表**的状态枚举，从「10 个 schema 与 11 条列表路径共用的同一份 29 值小写并集」
收窄为各聚合的真实值域，而不为被移除的码值建立 v2 路由或保留过渡期。

**收窄前**：每个 MES 列表的 `status`（行属性与 `status` 查询参数）都是同一份
`accepted/active/blocked/cancelled/closed/completed/...` 共 29 值的并集。

**收窄后**，按路径背后聚合的真实值域：

| 路径 / schema 组 | 聚合 | 值域 |
| --- | --- | --- |
| `/mes/work-orders`、`/mes/production-plans`、`BusinessConsoleMesWorkOrderItem` | `WorkOrder` | 10（小写） |
| `/mes/operation-tasks`、`/mes/dispatch-tasks`、`/mes/wip`，4 个 schema（`OperationTaskItem` / `DispatchTaskRow` / `OperationTaskRow` / `WipSummaryRow`） | `OperationTask` | 6（PascalCase） |
| `/mes/material-issue-requests`、`BusinessConsoleMesMaterialIssueRequestRow` | `MaterialIssueRequest` | 7（PascalCase） |
| `/mes/finished-goods-receipt-requests`、`BusinessConsoleMesReceiptRequestRow.receiptStatus` | `FinishedGoodsReceiptRequest` | 5（PascalCase） |
| `/mes/related-quality-items`、`BusinessConsoleMesRelatedQualityItemRow` | `DefectRecord` | 5（PascalCase） |
| `/mes/shift-handovers` | `ShiftHandover` | 2（PascalCase） |
| `/mes/downtime-events`、`/mes/capacity-impacts`、2 个停机/产能 schema | `WorkCenterUnavailability`（读面派生） | 2（PascalCase） |

**为什么不能走一般的主版本/迁移窗口程序**：那份 29 值并集本身就是缺陷载体，不是既有能力。
它把 7 个互不相干的读面聚合的状态混成一个集合，并且**用小写拼写冒充 PascalCase 域的运行时值** ——
工序、领料单、完工入库单、不良记录、交接班、停机事件、产能影响这 7 个读面在服务端实际会产生的值，
**一个都不在**该枚举内。保留过渡期等于让 29 个并集码继续作为合法过滤值被接受，而真正的运行时值
反而被契约判为非法。契约此时不是在描述能力，而是在描述漂移。

（「停机事件」与「产能影响」是同一个 `WorkCenterUnavailability` 聚合派生出的两套读面
—— `DowntimeEventRow` 与 `CapacityImpactRow`，见上表合并的那一行。）
**「几个」要看按什么数，三种数法别混**：按**读面**是 7 个（工单、工序、领料单、完工入库单、
不良记录、交接班、停机/产能）；按**聚合**是 6 个（停机与产能同属 `WorkCenterUnavailability`）；
按**域值域来源**是 7 个 —— `OperationTask` 的 6 个值域被 4 个读面共用，但每读面各一行契约，
而 `WorkCenterUnavailability` 的 2 个值被停机、产能两个读面共用，因此「读面数」与
「值域来源数」在工序任务与停机产能这两处方向相反，不能互相换算。

**那 29 个码逐个对回域，结论分三类**（不要笼统说成「27 个没有对应数据」——
只有 4 个是真正无对应数据的，其余各有归属）：

| 类别 | 个数 | 码 |
| --- | --- | --- |
| 与域值域字面相同（工单本就是小写） | 8 | `cancelled` `closed` `completed` `created` `hold` `released` `scrapped` `started` |
| 拼写错误 —— 域里是对应的 PascalCase 码 | 17 | `queued` `inProgress` `paused` `scheduleInvalidated` `open` `recovered` `requested` `partiallyReceived` `received` `posted` `partiallyPosted` `inventoryPostingFailed` `reworkPending` `scrapAccepted` `returnAccepted` `dispositionAccepted` `accepted` |
| **域里完全没有对应数据** | **4** | `active` `blocked` `ready` `warning` |

**本次变更是「收窄 + 新增」，不是纯收窄。** 相对旧的 29 值并集，处理器为
`MaterialIssueRequest` 补回了 `ReceiptPosting` / `ReturnRequested` / `ReservationExpired`，
为 `WorkOrder` 补回了 `split` / `merged` —— 这 5 个码**旧并集里根本不存在**，是本次新增的
合法过滤值（它们在域里都有常量与可达赋值）。把它们连同 PascalCase 正名一起记为「收窄」，
会低报本次变更，故在此更正。同一批 PascalCase 正名也把 17 个拼写变体纠正为真实值。

特别地，`/mes/downtime-events` 与 `/mes/capacity-impacts` 从 29 值收到 2 值（`Open` /
`Recovered`）的准确性质是**两件事叠加**：

- **取消本就不生效的码**。读面 `MesProductionQueries` / `MesWorkbenchQueries` 的过滤是
  `request.Status.Trim().ToLowerInvariant()` 之后 switch，只认 `open` / `recovered`，
  其余一律 `Where(_ => false)`。被收窄掉的那些码本就在这条 switch 的 `_` 分支里，
  传进去只会得到空结果集 —— 取消它们不损失任何能返回行的过滤能力。
  **注意这是单条路径的口径，不是全局口径**：全局 29 值里只有 4 个（`active` / `blocked` /
  `ready` / `warning`）在任何聚合上都没有对应数据；其余 25 个都对得上某个聚合的真实值域
  （8 个字面相同 + 17 个拼写变体）。
- **对仍生效的两个码改拼写**。`open` / `recovered` 在收窄前本来就在 29 值枚举内且**真能过滤到行**，
  收窄后契约写 PascalCase。这不是「取消」而是「改拼写」，但因为读面比较前先 lower，
  `Open` 与 `open` 行为等价（都归一化成 `open`），故运行时无差异。

所以对这两条路径而言结论是「无可观测功能损失」；但理由不是「取消的码本就不生效」这一句 ——
生效的那两个是被改拼写而非被取消。

**替代面**：无。新增的 5 个码是域里本就存在的过滤值（此前被统一并集挤掉），
其余是把契约校正回生产者的事实。

该例外要求后端与重新生成的客户端同批升级（本仓前端消费方已在同一 PR 内迁移到真实值域，
`verify-openapi-client-drift.ps1` 为零漂移的机器判据；后端契约测试以 MES 域常量比对
OpenAPI 枚举，域里增删状态而契约没跟上时断言必然红）。它**只适用于 #3912 与上表所列
MES 列表路径/schema**；两个 Gateway 形成受支持客户发布后，任何状态枚举的收窄仍遵循
Governance 的一般破坏性变更/主版本规则。

### #3912 的范围登记（消费端迁移，以及一次被打破的票面约定）

**票面原文是「不得夹带进本 PR」，这一条被打破了**，事实与代价记在这里，使票内可审计
（issue 评论是过程记录，本节是长期事实）：

1. **本 PR 超出了票面字面范围**：票面要求只改契约生产端（`MesListDisplayOpenApiDocumentProcessor`）
   及其直接守门，但实际改动**包含前端消费端迁移**（见下条第 2 点）。**代价**：评审需要额外花
   一轮确认前端那些改动没夹带别的意图 —— 它们与后端收窄不是同一件事，票面也未授权。
   **这不是本来就在范围内**，是把范围显式扩了并留痕。
2. **值域收窄随契约同批落地**：前端各处状态字面量、筛选下拉、词表键集一律改用上表所列
   真实值域；`useMesReferenceLabels.ts` 的 `toLowerCase()` 归一化适配层**删除**而非保留 ——
   保留它会让后端哪天改发另一种拼写时的契约漂移被静默吸收。`Open` 是跨聚合重码
   （不良记录=待处理、交接=待接班、停机/产能=未恢复），共享词表只放停机语境的读法，
   其余语境由 `statusOptions` 的 `overrides` 显式覆盖。
3. **console 与 `business-core` 的入库词表本次不合并**：`console` 的
   `RECEIPT_STATUS_LABELS` / `RECEIPT_STATUS_TONES` 与
   `frontend/packages/business-core/src/labels/mesLabels.ts` 保持两份，本次仅登记为已知重复。
   合并需另票，届时一并决定键集归属。**这一条是按协调方指示登记、不合并**，不是被忽略。

守卫现状：后端 `Nerv.IIP.ContractBoundary.Tests/MesListStatusContractTests` 以 MES 域常量
比对导出 snapshot 的枚举；前端 `useMesReferenceLabels.test.ts` 逐语境钉住
`overrides` 与词表键集。

后端守门共 25 条，分五层：

1. **逐条值域比对**（21 条）：10 个行 schema 的 `status` / `receiptStatus` 与 11 条列表
   `status` 查询参数，各与对应聚合的域常量比。
2. **值域归属穷举**（`Every_mes_enum_value_domain_has_an_owner`）：扫出 `BusinessConsoleMes*`
   前缀下的**全部 134 个 schema**（响应包装、请求体、详情、联合体、以及 6 个非 object 的
   string 型枚举 schema 都在内），**跟随 `$ref` 解析到实际值域**（藏在包装层、藏在 `items[]`
   元素上、经几层间接引用都算同一处），逐个问「说得出属于谁吗」。新增一个枚举值域而
   既不属本票聚合、也没在白名单写明理由时必然红 —— 只做第 1 层的话，那条值域根本进不来，
   门禁会照绿。

   **6 个非 object 的 string 型 schema 里，只有 1 个值域的采集真的依赖它**：
   采集跟随 `$ref`，而这 6 个里有 5 个（`AndonCategory` / `AndonStatus` /
   `ProductionStatisticsDimension` / `ProductionStatisticsResolutionStatus` /
   `ProductionStatisticsDegradedReason`）都被 object 型 schema 引用，值域照样被收进来。
   只有 `AndonQueue` **被 0 个 schema 引用**，它的值域
   （`all` / `awaitingResponse` / `unclosed`）只由它自己承载。
   **所以把扫描面收窄到只扫 object 型的真实后果是：该值域不再被采集、其豁免变成多余项，**
   **而不是「无人认领」** —— 实测收窄后 25/25 仍全绿（未认领的红是「值域在面上、又没有归属」，
   值域不在面上时第 2 层压根看不见它）。也因此收窄+删掉那条豁免叠加仍全绿：9 项豁免降到 8 项，
   没有一条断言察觉。这与本节其余段落「新增值域必红」不矛盾 —— 那说的是**在面上**新增。
3. **声明者穷举**（`Aggregated_status_domains_are_declared_only_by_registered_schemas`）：
   **本票聚合的状态值域只允许由登记的 10 个行 schema 就地声明**（不跟随 `$ref`）。
   NSwag 的 `XxxListResponse` / `XxxResponse` 包装层只经 `items[].$ref` 传递地含有该值域、
   自身不声明，所以不判「携带」只判「声明」—— 判携带等于要求把 10 个生成物包装层也登记一遍。
4. **豁免表自证**（`Every_exempt_value_domain_is_genuinely_non_aggregated`）：
   `NonAggregatedDomains` 的任何一条键都不得等于本票任一聚合的真实状态值域。
   前三层都只问「值域有没有归属」，豁免表把值域判成「不归本票管」时不与之矛盾 ——
   于是一条真实漂移可以「给域加新状态 + 契约不跟进 + 豁免键改成新值域 + 把该读面从
   `RowStatusProperties` 摘掉」四步绕过，让该聚合的逐条比对整体消失。
   **本条判据取「域常量 ∪ 登记派生值」的并集**：只取域常量能防上面那条（掏空登记表），
   只取派生值则在「新增聚合状态源时忘了加域常量」时静默放过 —— 两侧各防一种，缺任一侧都留旁路。
5. **路径侧反向穷举**（`Every_mes_list_status_query_is_registered`）：扫出
   `/api/business-console/v1/mes/**` 下所有带 `status` 查询参数的 GET 路径，逐个问
   「登记了吗」，与第 2、3 层对称。

第 1 层的 `RowStatusProperties` 是本票状态登记的**唯一来源**：第 2、3 层的归属集合
由它派生，不再另抄一份。早先第 3 层有一张手抄的 `StatusDeclarers`，两份可各改各的 ——
只从手抄那份摘掉一项而 `RowStatusProperties` 不动，两条穷举的归属集合就少一个值域，
而该值域同时还在 `NonAggregatedDomains` 里（工序任务与停机/产能各被多个读面共用），
于是唯一一处约束消失、守门照绿。派生而非拷贝，这条旁路不再存在。

**判据的根基是「值域归属」，不是属性命名。** 前五轮依次用过「属性名以 `Status` 结尾」
「(schema, 属性) 登记表」「跟随 `$ref`」「不跟随 `$ref` + (schema, 属性) 登记表」，
每轮都被找出新旁路：属性改名 `state` / `phase` 即绕过（`$ref` 形态放过、内联形态仍被抓）；
`items[]` 元素 `$ref` 到白名单 enum 即绕过；污染白名单引用目标的值域即绕过。共同点是
**都在认「这个位置像不像状态」**，而位置可以随便改。现在只认一件事：这个枚举的值域
等于本票哪个聚合的域值域 —— 值域改不动。

### 覆盖边界与已实测的放行项（不要把「注入全红」读成无边界）

改动 snapshot 的 7 条注入实测全部转红（含前几轮曾放行的三种），基线 25/25 绿。
下表两类形态**经实测为绿**。登记时**写机制而不是只写颜色** —— 两者是绿的机制相同
（都在扫描面之外），但含义完全不同：不是「扫到了但放行」，而是「扫不到」。

| 形态 | 实测 | 机制 |
| --- | --- | --- |
| 状态 enum 藏进 `*ResponseDataOf*` 包装层（`NetCorePalExtensions…` 前缀） | 绿 | 第 2、3 层的扫描面是 `CollectEnumValueDomains` / `MesSchemaNames`，两者**只按 `MesSchemaPrefix` 过滤**、无 object 类型筛选。该层前缀是 `NetCorePalExtensions…`，不在面上 —— 判据**扫不到它**。**有意（范围取舍）**：实测本票 7 个聚合的值域在 MES 前缀外**就地声明数为 0**（若计入经 `$ref` 的传递携带则非 0：WorkOrder 1、OperationTask 5、MaterialIssue 2、FinishedGoods 1、DefectRecord 1、WorkCenterUnavailability 2、ShiftHandover 0 —— 判据只判声明不判携带，故不构成漏网）。把包装层纳入判定面只会让 10 个 NSwag 生成物误红。 |
| 在 MES 前缀外新增一个与本票无关的只读 schema、其值域无人认领 | 绿 | 同一机制：值域无人认领本该由第 2 层判红，但该 schema 在 `MesSchemaPrefix` 之外，**两层都扫不到**，所以红不起来。**这不是「值域有人认领所以绿」** —— 把同一形态放进 MES 前缀内，值域未认领即如期转红。 |

**本票边界就是 `BusinessConsoleMes` 前缀**（两条放行项的共同机制）。
**将来若某个包装层就地内联了本票状态值域，需重评本条边界** ——
判据只认值域不认传递，则「包装层含该值域」在定义上不可能独立于行 schema 存在；
但「独立声明」是可以发生的，那时本票边界就该重新划。

白名单只剩两张，粒度都落在**值域**上（不是位置），新增值域却忘了登记时断言会红并列出未认领项：

| 白名单 | 粒度 | 内容 | 缺了会怎样 |
| --- | --- | --- | --- |
| `NonAggregatedDomains` | **值域**（9 项） | 安灯 3 个 + 生产统计 3 个 + 回执字面量 3 个，值域不由本票 7 个聚合的域常量定义 | 漏判成「无人认领」而误红；谎报本票值域则由第 4 层拦下 |
| `NonListStatusQueryPaths` | **路径**（2 项） | 见下 | 同上 |

`NonAggregatedDomains` 不是通用逃生口 —— 往里加一项值域，那一整个值域（可能正是本票某个状态的
旧拼写）就不再受任何约束。本仓只允许加「值域确实不由本票聚合的域常量定义」的项。

`NonListStatusQueryPaths` 里的两条（`reportable-operation-tasks` /
`telemetry-production-report-candidates`）是 `status` **无 enum** 的自由 string，
属「缺值域」而非「值域被覆盖」，方向与本票相反，独立票处理。

## 历史材料边界

`docs/reports/audits/**` 保存迁移前总账、历史漂移、修复批次、调查和曾经的端点渲染，目的是可追溯，不承担当前规范或机器事实。若 audit 与 Current Architecture / Governance / Runbook / Reference 或代码生产者冲突，以当前权威来源为准，并把 audit 视为当时状态快照。