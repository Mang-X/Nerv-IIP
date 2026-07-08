# 设备工程师：第一周路径

设备工程师让设备事实可见、报警可处置、维修可闭环。第一周先走通下面 8 条路径，每条路径都以
一个业务结果收尾。状态口径见[按角色入门](/roles/)。

## 第一周路径

| #   | 路径（页面操作串）                                                                                                            | 业务结果                   | 状态                                                                                                             |
| --- | ----------------------------------------------------------------------------------------------------------------------------- | -------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| 1   | 在 `/master-data/devices` 确认台账，再到 `/equipment/telemetry/tags` 和 `/equipment/telemetry/alarm-rules` 配置采集与报警规则 | 设备具备可监控的数据基础   | ✅ 可用                                                                                                          |
| 2   | 在 `/equipment/telemetry/history` 看运行历史，在 `/equipment/telemetry/oee` 看 OEE                                            | 设备运行事实与效率可解释   | 🟡 部分可用：OEE 性能率与质量率真实计算未交付（[#738](https://github.com/Mang-X/Nerv-IIP/issues/738)）           |
| 3   | 在 `/equipment/alarms` 确认、搁置或跟踪升级报警                                                                               | 报警生命周期受控           | 🟡 部分可用：自定义搁置时长、批量确认与升级链展示未交付（[#794](https://github.com/Mang-X/Nerv-IIP/issues/794)） |
| 4   | 在 `/maintenance/work-orders` 接报警开维修工单并确认恢复                                                                      | 故障到恢复形成闭环记录     | 🟡 部分可用：维修工时、人员与费用记录未交付（[#793](https://github.com/Mang-X/Nerv-IIP/issues/793)）             |
| 5   | 在 `/maintenance/spare-parts` 申领备件并跟踪出库                                                                              | 维修备件从库存出库有据可查 | ✅ 可用                                                                                                          |
| 6   | 在 `/maintenance/plans` 维护保养计划，在 `/maintenance/inspections` 执行点检                                                  | 预防性维护按计划触发并留档 | 🟡 部分可用：点检测量值动态行未交付（[#793](https://github.com/Mang-X/Nerv-IIP/issues/793)）                     |
| 7   | 在 `/maintenance/reliability` 与 `/maintenance/availability` 查看 MTBF/MTTR 与可用率                                          | 可靠性指标可用于改进决策   | ✅ 可用                                                                                                          |
| 8   | 从平台向设备下发控制命令并留审计                                                                                              | 远程控制受审批与审计约束   | ⛔ 缺口：设备控制下发 UI 未交付（[#792](https://github.com/Mang-X/Nerv-IIP/issues/792)）                         |

## 从哪里学

- 概念解释：[核心流程图 · 设备维护](/processes/#设备维护)
- 设备维护主线的专属教程尚未提供，随后续功能批次补齐。
