# ADR 0025：现场采集、控制下发与 historian 转为平台自有能力

- 状态：已接受
- 日期：2026-07-03
- 关联：Linear MAN-419 / GitHub #737
- 修订对象：[ADR 0014：APS 与设备 IIoT 排程边界](0014-aps-and-iiot-scheduling-boundary.md) 的决策第 8、10、23 条与后果第 6 条

## 背景

ADR 0014 在 2026-05-27 冻结 APS 与设备 IIoT 边界时，把 PLC/DCS/SCADA、OPC UA、MQTT 划为
外部系统或 Connector 来源，并明确「平台只接收受控事实，不保存控制凭据，不下发控制命令」
（决策第 8 条）、「高频 historian 后置」（决策第 10 条）、「P0 不保存高频原始 historian，
不向 PLC/DCS/SCADA/OPC UA/MQTT 下发控制命令」（决策第 23 条）、「现场控制闭环仍是后续专题」
（后果第 6 条）。

这些表述在当时成立：它们是 P0 的**交付边界**，不是对能力归属的永久裁决。
2026-07-03 MAN-419 / #737 把这条边界前移——现场采集、控制下发与 historian 从「后置或外置」
转为平台自有的主动交付路线。ADR 0014 其余条款不受影响，原条款文本保留为当时事实。

## 决策

1. **现场采集进入平台自有路线。** #683 先以 OPC UA Connector 打通真实设备到
   IndustrialTelemetry HTTP 采样入库的第一条通道，#684 在该框架上补 Modbus TCP 与 MQTT。
   采集连接、节点/寄存器/topic 映射、断线重连、bucket 聚合、source sequence 幂等和状态快照
   都属于 Connector Host + IndustrialTelemetry 的能力边界。
2. **设备控制进入分阶段路线。** #687 复用 Ops operation task、approval gate 和 Connector Host
   claim/result 机制，下发 write-tag、start-stop 和 parameter-set 等命令；值域校验、审计、审批
   和回执必须可追踪。主平台控制面只提供通用任务/审批/审计骨架，控制语义仍属于
   IndustrialTelemetry/Connector 业务边界。
3. **Historian 与报警深化进入后续能力路线。** #689 负责 raw/hourly/daily 分层存储、降采样和
   保留策略；#685、#686、#690 分别补报警通知联动、ack/shelve/escalation 和
   DeviceStateChanged 下游消费。

## 理由

ADR 0014 的原表述是按 P0 交付顺序写的边界，读者容易将其读成对能力归属的永久裁决。把转向
记录为独立决策而不是在 0014 上追加段落，可以让两件事都保持可读：0014 的原判断仍是 2026-05-27
的当时事实，本 ADR 是 2026-07-03 之后的现行判断。

## 已考虑的替代方案

**维持 ADR 0014 的原判断：现场采集、控制下发与 historian 永久外置或永久后置。** 本 ADR 明确
否决该方案——它会让平台在真实设备接入、控制下发审计与时序保留上长期依赖外部系统，而这三项
正是 #683/#684、#687、#689 承接的能力。

该转向的原始记录（MAN-419 / #737 追加于 ADR 0014 的补遗）只声明了结果与承接路线，**未保留
当时对其它备选的权衡**；此处只登记可从记录中识别的被否决方案，不追加当时未记录的理由。

## 后果

1. 不得把完整行业套件一次性标为已交付。README 与就绪性文档必须继续按「已交付 / 进行中 /
   规划中」标注现场能力，避免把未完成的 Issue、设计方向或路线图写成当前代码事实。
2. 截至本 ADR 记录时，已交付代码仍只证明 tag mapping、bucket summary、device state snapshot、
   alarm raise/clear、runtime availability 以及 Maintenance/MES/Scheduling 的现有消费者。
   其余能力由上述票承接，交付状态以 `docs/architecture/implementation-readiness.md` 与票面为准。
3. 主平台不因本转向获得控制语义：通用任务/审批/审计骨架与业务控制语义的边界维持不变。

## 范围之外

1. 本 ADR 不改变 ADR 0014 除决策第 8、10、23 条与后果第 6 条之外的任何条款。
2. 本 ADR 不把 APS 高级优化器、仿真与自动重排纳入范围；那些仍按 ADR 0014 与 ADR 0022 后置。
3. 本 ADR 不声称上述任何路线已交付，也不承载各票的实施状态。
