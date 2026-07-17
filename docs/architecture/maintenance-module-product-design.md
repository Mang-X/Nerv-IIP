# 设备维护 模块产品/业务设计

> 业务域:设备维护(/maintenance) + 设备运行详情维护上下文(/equipment/{deviceAssetId}) · 前端落点:`frontend/apps/business-console/src/pages/maintenance` + `frontend/apps/business-console/src/pages/equipment/[deviceAssetId].vue`
> 后端:BusinessMaintenance(`backend/services/Business/Maintenance`)· facade:BusinessGateway `/api/business-console/v1/maintenance/**`
> 关联:#794(运行小时指标 + 保养计划触发模式)、#688/#884(IIoT 运行小时聚合读面)、#416(设备可靠性闭环)

## 1. 这页给谁用、解决什么

- **设备保全班 / 维修工**:登记周期保养计划、处理维护工单、记录点检、领用备件。
- **设备工程师 / 主管**:看设备可靠性(MTBF/MTTR)、可用窗口、累计运行小时与到期趋势,决定保养节奏。

主操作:**登记保养计划(选触发模式)→ 到期批量开单 → 维修/点检/备件闭环 → 看可靠性与运行小时**。

## 2. 信息架构(IA)

- `/maintenance/plans` 保养计划:列表(触发模式 / 保养周期 / 下次到期)+ 新建计划 + 生成到期工单。
- `/maintenance/work-orders`、`/inspections`、`/spare-parts`、`/reliability`、`/availability`:维护闭环各正式页面。
- `/equipment/{deviceAssetId}` 设备详情「维护与可靠性上下文」:按当前设备收敛的工单/计划/点检/备件/可用窗口 + **运行小时指标卡**(累计运行小时、距下次保养还需 X 小时)。

导航不因本次变更改动(同页面),故 `frontend-navigation-map.md` 无需更新。

## 3. UX 要点

### 3.1 保养计划触发模式(#794)
新建保养计划表单用**分段控件**(NvTabs)选触发模式,三档语义与后端领域模型一一对应:

| 模式 | 语义 | 提交字段 | 到期口径 |
| --- | --- | --- | --- |
| 日历周期 | 按日期到期 | `interval`=保养周期,无 `runtimeHourInterval` | 到期日 |
| 运行小时 | 按累计运行小时到期,**不受日历影响** | `interval` 留空,`runtimeHourInterval`=触发小时数 | 运行小时剩余 |
| 两者组合 | **两条独立触发线**(日历 + 运行小时),各自维护独立到期游标、各自到期时开单 | `interval` + `runtimeHourInterval` 都填 | 前端展示以运行小时剩余为主,读取失败/暂无样本时回落日历到期日 |

- 运行小时数提供常用值快捷按钮(500 / 1000 / 2000)+ 手动数字输入;**必填**校验为字段级(红框 + 字段级错误)+ 底部汇总,校验不过不发请求。
- 界面文案用业务语言(如「每运行满 1000 小时保养一次」),不暴露 ISO duration(P30D)或「后端」等实现术语。

### 3.2 列表展示
- **触发模式列**:由存储事实区分三态——有日历 `interval` 无运行小时 = 日历周期;无 `interval` 有运行小时 = 运行小时;两者皆有 = 两者组合。
- **下次到期列**:日历型显下次到期日;运行小时型显**剩余小时**(前端按各计划自己的起算窗口经 `queryBusinessConsoleTelemetryRuntimeHours` 派生;遥测**读取失败**显「读取失败」、真实**无运行样本**显「暂无样本」——两者分开不混淆)。

### 3.3 设备详情运行小时指标(#794 消费 #884)
- **累计运行小时**卡:窗口聚合(锚定运行小时计划起算日,无则近 90 天),明确是「窗口内累计运行事实」非终身表底;无运行样本时诚实显「无样本」。
- **距下次保养还需 X 小时**卡:前端逐计划派生剩余小时;设备有多个运行小时计划时取**剩余最小**(最紧迫)者,不按创建顺序;剩余未知(无样本/读取失败)的计划排在有剩余之后,读取失败与无样本在卡片上分别显示。

## 4. 角色与权限
- 读:`business.maintenance.plans.read` / `business.maintenance.work-orders.read` / `business.iiot.telemetry.read`(运行小时卡)。
- 写:`business.maintenance.plans.manage`(建计划 / 生成到期工单)/ `business.maintenance.work-orders.manage`。
- facade 层按 operation 强制权限;运行小时读面(`telemetry/runtime-hours`)按 IIoT telemetry read 门控。

## 5. 数据来源(facade 代码事实)
- 保养计划:`listBusinessConsoleMaintenancePlans`(纯 DB 投影,支持 `deviceAssetId` 过滤;返回 `interval`/`nextDueOn`/`runtimeHourInterval`/`nextDueRuntimeHours`/`lastGeneratedRuntimeHours`,**不含**剩余小时——剩余由前端派生)、`createBusinessConsoleMaintenancePlan`(`interval` 可空、`runtimeHourInterval` 可选,二选一或都填)、`generateDueBusinessConsoleMaintenanceWorkOrders`。
- 运行小时:`queryBusinessConsoleTelemetryRuntimeHours`(窗口聚合 `totalRuntimeHours` / `hasRuntimeSamples` / 日粒度)。
- 可靠性 / 工单 / 点检 / 备件 / 可用窗口:各自 facade,设备详情按返回设备字段客户端收敛。**仅保养计划**列表支持 `deviceAssetId` 服务端过滤(设备范围查询);工单列表当前仍是全局 `skip/take` 分页 + 页面客户端按设备过滤,目标设备工单落在全局前 N 条之外时可能遗漏,工单读面的服务端设备过滤为后续增强。

## 6. 领域口径(代码事实)
- `MaintenancePlan.Interval` **可空**:运行小时型计划无日历触发、无 `NextDueOn`,只在累计运行小时越过阈值时开单(PM 调度器 `generate-due`)。一个计划必须至少有一个触发(日历 / 运行小时 / 两者)。
- **组合计划的两条触发线相互独立**(沿用 #416 设计):`generate-due` 对每个计划先按日历游标 `ConsumeDueDates` 生成 `date:*` 工单,再按运行小时游标 `ConsumeRuntimeDue` 生成 `runtime:*` 工单,两个游标各自推进、各自幂等键。因此组合计划若日历线与运行小时线在同一轮同时到期,会分别开出日历型与运行小时型工单(不是合并成一单);这是「日历 PM + 用量 PM 两类保养各自成立」的口径,若业务需要「同轮只开一单」的单 occurrence 语义,属 #416 域模型调整,不在本 PR 范围。
- 运行小时累计口径:前端按各计划 `[plan.StartsOn, now]` 窗口调 `queryBusinessConsoleTelemetryRuntimeHours` 得 `totalRuntimeHours`,`剩余 = nextDueRuntimeHours − totalRuntimeHours`(负数截 0);无真实样本时为空。**列表查询本身是纯 DB 投影,不逐计划 fan-out 到 IndustrialTelemetry**——曾把该派生放服务端 list 查询逐计划调 provider,导致 DbContext 并发/超时使 list 500,故改前端派生(每页至多 6 路并发、非阻塞)。
- 剩余小时按**各计划各自起算窗口**计算,不同计划不可共用一个窗口直接比较。阈值推进后(生成到期工单)前端按新 `nextDueRuntimeHours` 重算,不显旧值。

## 7. 分期
- 本期(#794):触发模式三档 + 运行小时数快捷值 + 字段级校验;列表三态 + 剩余小时;设备详情两卡。
- 后续:保养计划**编辑**(见后端缺口)、运行小时趋势/预测、按到期紧迫度排序的计划工作台。

## 8. 后端缺口(整批 consolidated issue,落地后回填 issue 号)
- **保养计划编辑**:当前 facade 只有 create/list/generate-due,无 update 端点;#794 要求「创建/编辑表单」,编辑链路(update 命令/facade/前端预填与 mutation)拆为独立 follow-up **#945**,本 PR 不 close #794。
- 运行小时阈值触发的真机验收需连接器持续上报设备运行状态样本(冷启动 dev 无历史样本时运行小时=0);阈值→工单逻辑由后端域测试(`FixedAssetRuntimeHoursProvider`)覆盖。

## 9. 验收
- 三档触发模式可切换,运行小时模式字段/快捷值/字段级校验齐;运行小时型计划提交 `interval` 为空(真纯运行小时,起始日不产生日历工单)。
- 列表三态可辨、运行小时型显剩余小时;设备详情累计运行小时 + 距下次保养(多计划取最紧迫)正确,按设备范围查询不受全局分页影响。
- 门禁:前端 typecheck/test/build + touched fmt;后端 Maintenance + BusinessGateway + FacadeCoverage 全绿。
