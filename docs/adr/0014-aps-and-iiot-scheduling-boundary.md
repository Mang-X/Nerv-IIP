# ADR 0014: APS 与设备 IIoT 排程边界

- Status: Accepted
- Date: 2026-05-27

## Context

ADR 0012 将 APS 作为后置能力处理，首批 MES 只保留规则排产。这在业务平台 MVP 阶段是合理的，因为当时主数据、工程资料、MRP、ERP、WMS、MES、IndustrialTelemetry 和 Maintenance 都还需要先落地事实源。

2026-05-27 的 MES PC 交付复盘改变了这个前提。Business Console 页面已经暴露 MES 工作台，但真实使用需要排程能够理解工艺路线、工序顺序、设备能力、班次日历、物料齐套、质量阻断、报警、停机和维护窗口。如果继续把 APS 完全后置，甘特图会变成展示壳，MES 派工也会继续依赖手工判断或静态规则。

同时，APS 不能被误放到前端甘特图、MES 执行域或 IndustrialTelemetry 中。甘特图只负责展示与交互；MES 只拥有工单、工序任务、执行和报工事实；IndustrialTelemetry 只拥有采集后的设备状态、报警和摘要事实；Maintenance 只拥有维修、保养、点检和停机事实。需要一个明确的业务排程边界来消费这些事实并输出可解释的排程结果。

#207 进一步暴露了设备运行事实的第二个边界风险：如果 APS 和 MES 各自从报警列表、维修工单或页面状态中拼接可用性，系统会出现同一台设备在排程、派工和开工检查中给出不同结论的问题。设备 IIoT P0 必须把外部采集事实、报警生命周期、维护窗口、停机占用和 readiness reason code 先标准化，再供 APS/MES 消费。

## Decision

1. APS 不再完全后置。Nerv-IIP 将 `BusinessScheduling` / APS lite 纳入 P0 业务平台主线，先冻结排程输入输出契约和可测试的有限产能调度内核。
2. `BusinessScheduling` 是业务平台行业能力，不属于主平台控制面。主平台仍只提供 IAM、AppHub、Ops、FileStorage、Notification、Connector Host、PlatformGateway 和通用 SDK 能力。
3. `BusinessScheduling` 拥有排程问题、排程方案、资源负载、冲突项、不可排原因、锁定任务和排程版本事实。它不拥有 MRP 需求、正式工单、执行报工、库存余额、设备主数据、设备报警或维修工单，也不直接创建或更新 MES OperationTask。排程方案发布后，由 MES 命令按方案落地执行域变化。
4. DemandPlanning 继续拥有 MPS/MRP、计划建议和 pegging。APS 消费已接受或待评估的计划工单建议、MES 工单和需求约束，但不替代 MRP。
5. MES 继续拥有工单、工序任务、派工执行、报工、停机记录和完工入库请求。MES 可以消费 APS 排程方案，不能在执行域内部隐藏独立调度算法。
6. BusinessMasterData 和 ProductEngineering 提供静态资源事实：工厂、产线、工作中心、设备、班次、日历、技能、MBOM、工艺路线、工序和 ProductionVersion。
7. IndustrialTelemetry 和 Maintenance 提供运行可用性事实：设备状态、活动报警、停机、点检、保养和维护窗口。APS 只消费可用性查询或事件投影，不直接读取 PLC/DCS/SCADA。
8. PLC/DCS/SCADA、OPC UA、MQTT 和现场控制系统仍是外部系统或 Connector 来源。平台只接收受控事实，不保存控制凭据，不下发控制命令。
9. 甘特图和排产图属于前端展示与交互能力。它们消费 APS 输出 DTO，提交用户调整意图，不自行计算正式排程。
10. P0 算法范围是可解释、可复现、可测试的启发式有限产能排程：工序前后置、设备容量、班次日历、锁定任务、维护窗口、活动报警、急单插入和交期优先级。全局最优求解器、复杂仿真、自动重排策略和高频 historian 后置。
11. `SchedulingProblem` 与 `SchedulePlan` 是 #206 的稳定业务契约。契约必须包含版本号、组织/环境、排程窗口、工单/工序、资源、日历、锁定任务、物料齐套、质量阻断、设备可用性、排程算法版本、资源负载、冲突项、不可排原因和变更说明。需要跨服务编译期共享时，契约放入 `backend/common/Contracts/Nerv.IIP.Contracts.Scheduling`；仅页面消费的 HTTP request/response 仍以 Scheduling Web OpenAPI 和 generated api-client 为准。
12. P0 调度内核必须是纯计算单元：输入一个已经标准化的 `SchedulingProblem`，输出一个 `SchedulePlan`，不在算法内部访问数据库、HTTP 服务、系统时间、随机数或本地时区。服务层负责组装事实、持久化版本、发布事件和调用 MES 后续命令。
13. P0 启发式排序规则固定为确定性规则：锁定/已开工任务先占用产能；其余任务按急单/优先级、交期、工单号、工序顺序和工序号排序；每道工序选择最早可行资源，同一时间下优先主资源，再按资源编码排序。任何无法满足的约束必须进入冲突项或不可排原因，不能静默丢弃。
14. 排程方案状态至少区分 preview、generated 和 released。Preview 不写入 MES 执行事实；generated 可以持久化并供甘特或方案对比读取；released 才能通过 MES 命令把方案落到工单/工序执行域。
15. `BusinessScheduling` 的所有 plan 级读取、甘特读取和 release 操作必须同时携带并校验 `organizationId`、`environmentId` 和 `planId`。Gateway 授权上下文不能只保护入口请求，Scheduling 服务本身也必须用同一上下文过滤计划。
16. APS HTTP/OpenAPI 契约中的 enum 使用 camel-case string 值，与 `SchedulingJson.Options` 运行时序列化保持一致；不得让 OpenAPI/codegen 将排程 enum 暴露为整数联合类型。
17. P0 排程内核必须先标准化输入再计算指纹。语义相同但集合顺序不同的 `SchedulingProblem` 必须得到同一 `problemFingerprint`；重复资源、重复日历、非正工序时长、非法时间窗或其它结构性坏数据必须作为明确的输入错误处理，不能在算法内部变成未分类异常。
18. 锁定任务虽然必须原样保留到输出，但仍参与有限产能校验。锁定任务之间或锁定任务与资源容量、日历、停机窗口冲突时，方案必须保留 assignment 并输出 `InvalidLockedAssignment` 或更具体的冲突说明，不能把不可行的锁定状态标成无冲突。
19. P0 冲突 reason code 是可解释性契约的一部分。容量不足、日历不可用和排程窗口外必须尽量分别返回 `Capacity`、`Calendar` 和 `OutsideHorizon`，不得把所有无可行 slot 的情况都折叠成窗口外。
20. #207 以查询优先、事件辅助的设备运行事实模型落地。IndustrialTelemetry 和 Maintenance 继续拥有各自事实；Scheduling 与 MES 消费标准化可用性/readiness 结果，不直接解释原始报警、外部 tag 或维修单状态。
21. `EquipmentRuntimeAvailability` 是 #207 的跨边界语义，不是新的服务所有权。它由设备资产、工作中心、采集来源、状态快照、活动报警、停机、点检、保养和维护窗口共同归一化，输出为时间窗内的 available/unavailable/unknown、reason code、severity、source reference 和可选替代设备建议。
22. #207 的标准 reason code 必须能同时映射到 APS `SchedulingUnavailabilityWindowContract.ReasonCode`、`ScheduleConflictReasonCodeContract.Equipment` 的 detail/message，以及 MES release/dispatch/start readiness 的阻断原因。APS 和 MES 不得维护两套互不兼容的设备原因字典。
23. IndustrialTelemetry P0 继续只保存受控事实和摘要：tag/source mapping、device state snapshot、active/cleared alarm lifecycle、telemetry summary 和 runtime availability projection。P0 不保存高频原始 historian，不保存现场控制凭据，不向 PLC/DCS/SCADA/OPC UA/MQTT 下发控制命令。
24. 设备状态、样本和报警接入必须以 `organizationId + environmentId + sourceSystem/sourceConnector + deviceAssetId + sourceSequence` 或等价外部报警键做幂等边界。重复上报同 payload 返回同一事实；同 key 不同 payload 是冲突；迟到状态不能覆盖更新的设备状态，只能作为历史事实或被明确拒绝。
25. 报警生命周期以同一组织、环境、设备、报警码和外部报警 ID 合并。重复 raise 不创建新活动报警；clear 必须关闭对应活动报警；乱序 clear 必须以可诊断错误或 pending-clear 策略处理，不能静默让 APS/MES 继续认为设备可用。
26. Maintenance P0 输出维护/点检/保养/停机占用窗口，并继续发布 `maintenance.AssetUnavailable` 和 `maintenance.AssetRestored`。维护事实可以来自人工工单、保养计划、点检结果或 IndustrialTelemetry 报警消费，但不把设备主数据或报警事实复制为自己的主数据。
27. BusinessGateway 可以聚合设备/IIoT 页面 facade，展示设备状态、活动报警、停机/维护占用、tag 映射和工单/排程影响入口；Gateway 不持久化运行事实，不计算可用性，不实现报警合并规则。
28. Business Console 设备/IIoT 页面必须使用中文业务文案表达状态、阻断原因和下一步处理，不展示 source sequence、内部 event envelope、算法版本、组织/环境上下文或 connector 调试元数据。

## Rationale

1. 没有 APS lite，MES PC 很难解释为什么某个工单可以派、应该派到哪台设备、何时开工、急单会影响哪些任务。
2. 把 APS 放进 MES 会让执行事实和计划推算耦合，后续难以支持独立排程工作台、方案对比和模拟。
3. 把 APS 放进甘特图会导致页面承担领域决策，后端无法复现、审计或给接口调用者提供一致结果。
4. 把设备 IIoT 运行事实纳入 APS 输入，可以让报警、维护窗口、停机和设备恢复真实影响排程，而不是只在页面显示红点。
5. 先做 APS lite 而不是完整优化器，可以支持 P0 交付，同时控制算法、数据量和运维复杂度。
6. 对设备可用性采用统一 reason code，可以让 APS 的不可排解释、MES 的开工阻断和 Console 的设备异常页面使用同一事实语言。

## Consequences

1. `docs/architecture/business-platform-domain-architecture.md` 中 “APS 后置” 的旧判断必须改为 “APS lite 进入 P0，高级优化后置”。
2. P0 issue roadmap 增加 #206 APS 调度内核与排程数据契约，以及 #207 设备 IIoT/IndustrialTelemetry 运行事实与 APS/MES 联动基线。
3. 后续实现若新增 `BusinessScheduling` 服务或 schema，必须补 schema catalog、authorization matrix、OpenAPI contract tests、IAM seed、BusinessGateway facade、api-client 生成和验证脚本。
4. #78 甘特图/RFC 仍是前端展示与交互路线，不承担 APS 算法。甘特图完成不代表 APS 完成。
5. #195 MES 质量与设备 readiness 只消费 reason code 和可用性结果，不实现采集、维修或调度内核。
6. 高级优化、仿真、自动重排、高频时序存储和现场控制闭环仍是后续专题，不能混入 P0。
7. #206 的 worker 实现必须优先写算法回归测试，再写内核实现；若先搭建服务骨架，也不能把未验证的排程规则藏在 endpoint、Gateway 或前端里。
8. 调度内核若输出延期或不可排结果，仍应返回可解释 `SchedulePlan`，而不是以 500 或空方案掩盖业务冲突。系统错误和业务不可排必须分开建模。
9. `schedule_problems` 的 idempotency key 是 `organizationId + environmentId + problemId`，不是全局 `problemId`；不同组织/环境可以复用同一自然 problem id 而不会共享快照或方案。
10. #206 P0 不做 previous-plan diff 输入；`ScheduleChangeType.Moved` 作为后续方案对比能力保留值。当前 change summary 只表达锁定保留、新增排程、交期延误和不可排阻断。
11. #207 落地后，MES readiness 和 BusinessScheduling availability adapter 必须回归到同一设备 reason code catalog。任何新设备阻断原因都要同时覆盖 contracts、服务测试、Gateway facade 和前端文案。
12. 设备运行事实不成为主平台能力。主平台 Console、PlatformGateway、AppHub、Ops 或 IAM 不得承载 IndustrialTelemetry、Maintenance、APS 或 MES 的行业规则。

## Addendum 2026-07-03: 现场能力范围转向

MAN-419 / #737 对本 ADR 做追加更正，不删除 2026-05-27 的原始判断。原 Decision 8、Decision 10、Decision 23 和 Consequence 6 中“PLC/DCS/SCADA 仍是外部系统或 Connector 来源”“不下发控制命令”“高频 historian 后置”“现场控制闭环仍是后续专题”等表述，保留为当时 P0 交付边界；从 2026-07-03 起，它们不再表示 Nerv-IIP 永久放弃现场采集、控制或 historian 能力。

1. 现场采集进入平台自有路线：#683 先以 OPC UA Connector 打通真实设备到 IndustrialTelemetry HTTP 采样入库的第一条通道，#684 在该框架上补 Modbus TCP 与 MQTT。采集连接、节点/寄存器/topic 映射、断线重连、bucket 聚合、source sequence 幂等和状态快照都属于 Connector Host + IndustrialTelemetry 的能力边界。
2. 设备控制进入分阶段路线：#687 复用 Ops operation task、approval gate 和 Connector Host claim/result 机制，下发 write-tag、start-stop 和 parameter-set 等命令；值域校验、审计、审批和回执必须可追踪。主平台控制面只提供通用任务/审批/审计骨架，控制语义仍属于 IndustrialTelemetry/Connector 业务边界。
3. Historian 与报警深化进入后续能力路线：#689 负责 raw/hourly/daily 分层存储、降采样和保留策略；#685、#686、#690 分别补报警通知联动、ack/shelve/escalation 和 DeviceStateChanged 下游消费。当前已交付代码仍只证明 tag mapping、bucket summary、device state snapshot、alarm raise/clear、runtime availability 和 Maintenance/MES/Scheduling 的现有消费者。
4. 本 addendum 不把完整行业套件一次性改为已交付。README 和 readiness 必须继续按“已交付 / 进行中 / 规划中”标注现场能力，避免把 open issue、设计方向或路线图写成当前代码事实。

## Implementation Notes

1. #206 是 APS lite 的执行入口，先定义 `SchedulingProblem`、`SchedulePlan`、资源负载、冲突项和不可排原因契约，再实现确定性启发式内核。
2. #207 是设备 IIoT 运行事实的执行入口，负责设备主数据到 tag、状态、报警、停机、维护窗口、APS 可用性查询和 MES readiness 的链路。
3. #206 的第一增量不要求一次性对接所有上游服务。调度内核先消费标准化 `SchedulingProblem`，首个端到端输入可由 MasterData 资源/日历、DemandPlanning 计划工单建议或 MES 工单候选组成；ProductEngineering、Inventory/WMS、Quality、IndustrialTelemetry 和 Maintenance 先以显式快照或 fixture adapter 进入，再随 #189、#190、#193、#195、#207 补真实 adapter。
4. `BusinessScheduling` 的最终落点建议为 `backend/services/Business/Scheduling`，schema 建议为 `scheduling`。如果首个 PR 先以独立 library 或 MES 外部模块承载，也必须保持公开契约与领域边界可迁移。
5. BusinessGateway 可以暴露页面级排程 facade，但不得持久化排程事实或实现调度算法。
6. Business Console 页面必须用中文业务文案表达排程结果、冲突和下一步处理，不展示算法调试、接口契约、组织/环境上下文或样例数据说明。
7. #206 专用设计规格为 `docs/superpowers/specs/2026-05-31-business-scheduling-aps-lite-design.md`，实施计划为 `docs/superpowers/plans/2026-05-31-business-scheduling-aps-lite.md`。后续 worker 必须先按这两个文档执行，不得只参考 issue body 自行扩大范围。
8. 首个算法 fixture 使用减振器制造场景：前减总成、后减总成、管焊接、活塞杆装配、注油封口、阻尼测试/包装，多工作中心、多设备、一个维护窗口和一个急单插入。验收重点是输出稳定、工序顺序正确、维护窗口被避开、急单影响可解释、甘特 DTO 足够渲染。
9. P0 不要求 solver、线性规划、遗传算法、仿真或自动重排。若后续引入求解器，必须新增 ADR 或更新本 ADR，明确依赖、授权、部署、可解释性和失败降级策略。
10. #207 专用设计规格为 `docs/superpowers/specs/2026-06-01-business-iiot-runtime-facts-aps-mes-design.md`。后续 plan 和 worker 分配必须以该规格为准，先落 contracts/reason codes/后端事实链路，再开放 Business Console 页面。
