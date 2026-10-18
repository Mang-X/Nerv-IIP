# 新报工日报“历史站点时区缺失”调查

调查日期：2026-10-18；范围：[#4466](https://github.com/Mang-X/Nerv-IIP/issues/4466)，母跟踪 [#4454](https://github.com/Mang-X/Nerv-IIP/issues/4454) 的 A12。Scope：spike，高难度；时间盒最多 1 工作日，本次当日完成。只交付调查报告，不修改生产代码，不将调查关闭作为旅程修复验收。

**裁决：新报工可以在当前快照采集失败后被日报误称为“历史站点时区缺失”。已定位缺项产生处和误分类处；这不是日报事件投影漏写。原八条报工具体触发的采集失败原因仍未决。** 本次用当前真实类的受控实验确认：即使站点已返回 `Asia/Shanghai`，后续班次查询失败也会清空整份维度快照并触发该标签。因此不能从标签倒推站点主数据缺时区，更不能据此补猜测值。

## 基线和证明范围

- 本次开工 `git fetch origin main` 后，HEAD、`origin/main` 和 `git ls-remote origin refs/heads/main` 均为 `c968944b0b49555ab81c697c71ecb66fb92c8fed`；隔离工作树 `/Users/mang/.t3/worktrees/Nerv-IIP/t3-0c1b6b49`，分支 `t3/investigate-task-4466-delivery`，开工 tracked 工作区干净。
- 历史真实页面运行于 `a9b625b297e7797c058e40b428c24bf611750daf`，2026-10-16，隔离会话 `nerv-a016-1016ab`，非管理员岗位；冻结资产提交 `a154cf38a774c5addc9090f17d86300c930cdd16`。[原报告 A12][history-report]、[原操作指南][history-guide]、[规范化证据][history-evidence]、[原截图][history-image]证明当时新会话八次报工合计 16、报工数 8、合格率 100%，页面显示历史时区缺失。本次没有重跑这条真实旅程。
- 当前静态链核对以上新基线；下文源码链接全部固定到该 SHA。对 provider、`ProductionReport`、日报 query 和事件 converter 四文件执行 `git diff --name-only a9b625b297e7797c058e40b428c24bf611750daf c968944b0b49555ab81c697c71ecb66fb92c8fed -- <上述四文件路径>`，输出为空。这支持用同一链解释历史可达路径，不证明部署配置、主数据和 HTTP 响应相同。
- 当前实际执行仅含合成 HTTP fixture、EF Core InMemory 和既有定向测试；没有真实 MasterData HTTP、PostgreSQL、CAP、Gateway 或浏览器运行。实验不是 formal provider lane，也不是生产修复验收。

## 从输入到页面的实际来源

1. 排程发布消费者将 `AffectedOperations.ResourceId` 存入 MES 任务 `DeviceAssetId`；报工命令读取同一任务的 `DeviceAssetId`、`WorkCenterId`、`ShiftId`，将其作为设备引用、工作中心、班次码交给 `CaptureAsync`。不是从日报请求、浏览器时区或当前登录人的站点补值。[任务赋值][assignment]、[报工命令][command]。
2. MES 的 `HttpProductionReportOeeDimensionSnapshotProvider` 先用组织/环境及精确 `deviceAssetId` 查 MasterData；成功且返回规范 GUID、工作中心和 `device.SiteCode` 后，再用精确 `siteCode` 查站点的 `Timezone`；有班次时再按精确 `shiftCode` 查五个班次定义字段。调用使用内部服务 token；设备引用支持 GUID／code 别名由 MasterData resolver 负责，不能假定所有失败都是别名不支持。[provider][provider]、[MasterData producer][masterdata]。
3. 缺设备立即得到 `degraded/device-missing`。设备解析、站点或班次任一后续失败都返回 `ProductionReportOeeDimensionSnapshot.Degraded(...)`：保留原设备引用和任务工作中心，但站点、时区、班次字段全部为 NULL。尤其班次失败时，即使此前已读到站点时区，也不保留部分维度。这是当前实现的整份快照处理；本票不改变它。[快照结构][snapshot]。
4. `ProductionReport.Record` 原样保存 `OeeDimensionResolutionStatus`、`OeeDimensionDegradedReason`、`OeeSiteCode`、`OeeSiteTimezone` 等字段；EF 映射到 `mes.production_reports` 的 `oee_*` 列。NULL resolution status 的列注释明确表示快照合同之前的 legacy 行。快照缺失在采集返回时已经发生，当前赋值和映射没有另丢时区。[领域持久化赋值][report]、[列映射][mapping]。
5. 报工集成事件 converter 从保存的维度快照复制 `SiteTimezone`、站点、班次等到 `ProductionReportRecordedPayload`；事件是下游 OEE 的输入。**生产日报则由 MES `QueryProductionStatisticsQueryHandler` 直接 join `ProductionReports` 与 `WorkOrders`，读取持久化 `Oee*` 字段，不等待遥测消费者或日报事件投影。** 本次未运行 CAP，不能认证事件投递或遥测终态；也没有证据要求修复 CAP。[converter][converter]、[日报查询][query]。
6. 日报 `Resolve` 的顺序是：status NULL → `historicalDimensionLegacyUnresolved`；reason 恰为 `device-missing` → `deviceMissing`；其它记录若 `SiteTimezone` NULL → `historicalTimezoneMissing`。这里不检查记录创建年代，也不按已保存的其它采集原因区分。按工作中心聚合时，非 resolved 快照改给 `historicalDimensionSnapshotDegraded`；按 SKU 聚合不要求这些时间维度，不能以 SKU 显示 resolved 证明快照完整。[分类和聚合][query]。
7. BusinessGateway 透传 MES 统计查询。前端表格与 CSV 共享映射，将 `historicalTimezoneMissing` 直接翻成“历史站点时区缺失”；页面不再判断新旧。[Gateway][gateway]、[前端标签][labels]、[表格][table]。

## 三种缺项应如何判读

- **缺少当前权威事实或其读取失败：** 需检查当次精确设备／站点／班次响应与 snapshot reason。`not-found` 不等同于物理设备不存在；还可能是当前查询范围内没有唯一可用资源。`master-data-http-*`、不可用和 timeout 是读取失败，不是站点本身没有时区。
- **采集后没有保存时区：** 当前 `Degraded` 整份清空维度，已读到站点时区但班次失败也会发生；这是本次合成反例的实际结果。不能单凭 NULL 列判断数据库映射漏写。若 provider 输出非空而同次保存／事件变空，才有进一步查赋值／持久化／converter 的证据；本次未观察到这种差异。
- **真实历史无快照：** 应以合同前来源／记录 provenance 以及 NULL resolution status 共同核对；NULL status 是当前实现的 legacy 标记，不是任意新行的年代证明。旧记录中已捕获的历史时区无效／缺失，与本次新记录采集失败也不能混同。仅 `SiteTimezone == null` 或 `ReportedAtUtc` 的日期不足以证明“历史”；不能用当前主数据重算旧记录来伪造报工时点快照。

现有日报在缺时区时按 `Asia/Shanghai` 日历日降级归日，缺班次时按站点时区日历日降级，均保留降级原因。这是现状，不代表已取得权威业务日。本票不修改兜底、归日算法或历史数据，不把合计可见当作维度完整。

## 当前受控实验

实验代码在 [Program.cs.txt](assets/issue-4466/Program.cs.txt) 和 [Probe.csproj.txt](assets/issue-4466/Probe.csproj.txt)，输出投影在 [probe-summary.json](assets/issue-4466/probe-summary.json)。它直接调用当前 provider → `ProductionReport.Record` → converter → EF InMemory 保存、清 tracker 后读取 → 实际日报 query。命令路径与完整质量门禁分开；合成数据不来自旧现场，不经过报工 command、HTTP endpoint、认证、消息总线或真实数据库。`NoopMediator` 不执行事件消费者。

| 输入场景 | 持久化 status / reason | 持久化／事件时区 | 日维度原因 | 工作中心维度原因 |
| --- | --- | --- | --- | --- |
| 设备、站点、班次均成功 | resolved / NULL | Asia/Shanghai | 无 | 无 |
| 设备引用为空 | degraded / device-missing | NULL | deviceMissing | deviceMissing |
| 设备精确查询零行 | degraded / not-found | NULL | historicalTimezoneMissing | historicalDimensionSnapshotDegraded |
| 站点返回但 timezone NULL | degraded / site-timezone-missing | NULL | historicalTimezoneMissing | historicalDimensionSnapshotDegraded |
| 站点已返回 Asia/Shanghai，随后班次零行 | degraded / shift-not-found | NULL | historicalTimezoneMissing | historicalDimensionSnapshotDegraded |
| 构造无快照的 legacy 对照 | NULL / NULL | NULL | historicalDimensionLegacyUnresolved | historicalDimensionLegacyUnresolved |

每例仅一条合成报工、良品 2／报废 0／返修 0，三种已查询聚合（day、workCenter、sku）均合计 2／报工 1。实验记录当前行为，不将误分类输出写成期望业务合同，也没有新增永久测试或反向批准该标签。最有区分力的是“站点已成功、班次失败”对照：它排除了“标签必然意味着权威站点没有时区”的推论。

复现时从固定源码 SHA 的仓库根将两份 `.txt` 复制回 `artifacts/issue-4466/probe/Program.cs`、`Probe.csproj`，保留项目引用相对路径，再执行：

```sh
dotnet run --project artifacts/issue-4466/probe/Probe.csproj
```

本次 .NET SDK 为 `10.0.302`；EF InMemory `10.0.8`，其余依赖由固定仓库的 Central Package Management 解析。原始 stdout／TRX 仅本地保留；提交的是不含请求 URL、认证头、token、连接串和真实业务主体的规范化投影及合成诊断源码。源码哈希在 JSON 中，可核对复制内容。

## 原八条报工的未决项与停止点

原证据保存 `PRPT-20261016-000001..000008`、各自工序和良品 2，未保存它们的 `OeeDimensionResolutionStatus`、`OeeDimensionDegradedReason`、设备／班次实际引用、MasterData 当次响应、CAP payload 或关联日志。旧报告也明确本地请求日志和运行连接不进入冻结资产。

因此不能在这八条上裁定是 `not-found`、`site-timezone-missing`、`shift-not-found`、服务调用失败或其它具体原因；也不能说八条都有同一 reason。若历史部署确与固定源码相同，则页面路径更支持“非 legacy 且非 device-missing 的 NULL时区”而非真实历史行，但这仍是有前提的静态推断。

要进一步认证真实 producer 根因，下一轮只需在新隔离栈重现一条报工：保存同任务的设备／班次引用与分配来源、当次精确 MasterData 结果及关联 MES 日志、保存后的 status/reason/时区列、同报工事件 payload、公开日报 day/workCenter/CSV 读回。现有 API 不暴露快照诊断字段时，可按已有授权的只读诊断路径取证；不能因此写库、重放报工或扩大生产 API。跨午夜／跨时区真实归日仍未运行，保持未验收。此步骤是后续候选的验证准备，不是本票已完成的真实运行。

16 是八道工序各 2 的产出和，最终成品数量为 2；本次没有证据认定数量计算错误，也不提数量修复。

## 复用、重新定级与最小后续候选

2026-10-18 读取 #4466 完整正文、标签和评论，GitHub 原生 Parent 为 #4454、`blocked_by=[]`、`sub_issues=[]`；无额外获批 plan。完整读取母票及复用来源 [#4284/T12](https://github.com/Mang-X/Nerv-IIP/issues/4284) 与 [#4405](https://github.com/Mang-X/Nerv-IIP/issues/4405) 正文、评论与状态。

- #4284 CLOSED，[PR #4364 结项](https://github.com/Mang-X/Nerv-IIP/issues/4284#issuecomment-6122847564) 已覆盖新报工 `device-missing` 与 legacy 分离；本次该分支仍可达且输出区分正确，不重开其原范围，不继承旧测试／真实栈结果为当前通过。
- #4405 CLOSED，批准范围是 APS 的工厂时区编辑与有限投影，明确排除 MES 和历史 CSV；不承接 MES 报工快照，也不把其 `workCenter → Site.Timezone` 的授权读面直接移植到本采集器。
- 当前全量开放 Issue 列表 586 项，按日报／报工时区／快照 producer 检索正文和标题仅命中本票；再核 9 张开放 PR、当前分支全部状态 PR、#4466 的原生 closing PR 引用，未发现重复交付。此为查询时点的 owner 核查，批准／建票前仍需刷新。

**调查本身维持 scope:spike；后续最小候选建议 scope:M、难度中，尚未批准、未建票、未实施。** 候选仅让班组长在日报和 CSV 分辨“当前采集未完成”与“合同前无快照”，保持已有合计与降级状态：

1. 在已有 MES 日报分类处，将明确 `degraded` 且非 `device-missing` 的新式快照归为既有通用快照不完整原因；复用现有 `historicalDimensionSnapshotDegraded` 枚举，页面／CSV 用不暗示年代的“报工维度快照不完整”，保持 `deviceMissing` 和 legacy 两支。保留 resolved 但数据异常的原诊断，不凭日期猜新旧。
2. 只补三种可观察反例：新记录设备解析失败、站点成功而班次失败、真实 legacy 来源对照；页面和 CSV 不再把前两种称作历史缺时区，原数量、原快照 NULL 和 legacy 标记不变。复用 #4284 的 command/provider 和 PostgreSQL query seam，不扩测试框架。真实验证以一条新报工的公开日报／CSV 与权威快照只读诊断闭合；历史对照明确 seed 或自然历史 provenance。

范围仅 MES 读分类、共享标签及受影响既有测试；不新增枚举／DTO、迁移或 OpenAPI 生成链，不改采集身份规则、不保留半份快照、不补默认时区、不刷新历史、不修 CAP。若批准范围要求展示原始 producer reason 或独立保留站点时间维度，应先给出真实失败样本和产品要求再重新定级，不能把它们混入上述 M 候选。本次不为这些假设另开旁支票。

## 调查结项与未完成验收

已交付：当前来源链、缺项位置、错误历史标签的受控反例、历史证据缺口、重新定级和唯一直接后续候选。独立审核确认这些调查产物后可以结项 spike；“定位原八条具体失败原因”、当前真实岗位日报修复、跨午夜／跨时区、CAP／遥测终态与母票连续旅程均未验收。报告 PR 合并不代表这些事项通过；#4454 的生产验收继续独立保留。

实际命令／门禁、精确候选 head 与输出随 PR 证据发布，避免把后来 CI 状态写成报告基线的历史事实。本地工作材料位于本工作树 `artifacts/issue-4466/`；未启动 fullstack、Aspire、容器或浏览器。诊断 console 与定向测试退出后只保留 SDK 的常规构建服务，不持有业务 session；不停止共享或未知进程。

[history-report]: https://github.com/Mang-X/Nerv-IIP/blob/a154cf38a774c5addc9090f17d86300c930cdd16/docs/reports/audits/2026-10-16-mes-aps-fresh-walkthrough.md
[history-guide]: https://github.com/Mang-X/Nerv-IIP/blob/a154cf38a774c5addc9090f17d86300c930cdd16/docs/reports/audits/2026-10-16-mes-aps-operations.md
[history-evidence]: https://github.com/Mang-X/Nerv-IIP/blob/a154cf38a774c5addc9090f17d86300c930cdd16/docs/reports/audits/assets/2026-10-16-mes-aps/evidence.json
[history-image]: https://github.com/Mang-X/Nerv-IIP/blob/a154cf38a774c5addc9090f17d86300c930cdd16/docs/reports/audits/assets/2026-10-16-mes-aps/screenshots/23-daily-report.png
[assignment]: https://github.com/Mang-X/Nerv-IIP/blob/c968944b0b49555ab81c697c71ecb66fb92c8fed/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/SchedulingPlanReleasedIntegrationEventHandler.cs#L222
[command]: https://github.com/Mang-X/Nerv-IIP/blob/c968944b0b49555ab81c697c71ecb66fb92c8fed/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/MesProductionCommands.cs#L310
[provider]: https://github.com/Mang-X/Nerv-IIP/blob/c968944b0b49555ab81c697c71ecb66fb92c8fed/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/ProductionReportOeeDimensionSnapshotProvider.cs#L30
[masterdata]: https://github.com/Mang-X/Nerv-IIP/blob/c968944b0b49555ab81c697c71ecb66fb92c8fed/backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListMasterDataResourcesQuery.cs#L49
[snapshot]: https://github.com/Mang-X/Nerv-IIP/blob/c968944b0b49555ab81c697c71ecb66fb92c8fed/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/ProductionReportAggregate/ProductionReportOeeDimensionSnapshot.cs#L53
[report]: https://github.com/Mang-X/Nerv-IIP/blob/c968944b0b49555ab81c697c71ecb66fb92c8fed/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/ProductionReportAggregate/ProductionReport.cs#L75
[mapping]: https://github.com/Mang-X/Nerv-IIP/blob/c968944b0b49555ab81c697c71ecb66fb92c8fed/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/ProductionReportEntityTypeConfiguration.cs#L36
[converter]: https://github.com/Mang-X/Nerv-IIP/blob/c968944b0b49555ab81c697c71ecb66fb92c8fed/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventConverters/MesIntegrationEventConverters.cs#L17
[query]: https://github.com/Mang-X/Nerv-IIP/blob/c968944b0b49555ab81c697c71ecb66fb92c8fed/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Production/QueryProductionStatisticsQuery.cs#L215
[gateway]: https://github.com/Mang-X/Nerv-IIP/blob/c968944b0b49555ab81c697c71ecb66fb92c8fed/backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs#L1858
[labels]: https://github.com/Mang-X/Nerv-IIP/blob/c968944b0b49555ab81c697c71ecb66fb92c8fed/frontend/apps/business-console/src/features/mes-production-report/productionStatisticsCsv.ts#L34
[table]: https://github.com/Mang-X/Nerv-IIP/blob/c968944b0b49555ab81c697c71ecb66fb92c8fed/frontend/apps/business-console/src/components/mes/production-report/MesProductionStatisticsTable.vue#L67
