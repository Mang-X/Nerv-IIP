# BusinessMasterData 所有权变更治理

本页只规定 BusinessMasterData 稳定主数据的**当前变更审批与最低审计要求**。事实由谁拥有、跨域如何消费见 [`../../architecture/business/master-data-field-ownership.md`](../../architecture/business/master-data-field-ownership.md)；字段和行为最终以当前代码、契约和迁移为准。

## 规则

1. 改变稳定事实 owner、资源层级归属、UOM/跟踪/保质期语义、合作伙伴身份/合规、设备关键属性或跨域 ReferenceData 语义前，必须由对应业务责任人审批。
2. 审批与审计不能通过直接修改下游缓存、历史单据或其它服务数据库来替代；owner 服务仍是最终写入边界。
3. 历史业务单据依赖的引用不能因主数据禁用、替换或合并而失去可解释性。
4. 删除代码、改变语义或跨 owner 移动事实属于高风险变更；需要明确原因、生效时间和操作者，并评估消费者/契约/迁移影响。

## 最低审批与审计

| 范围 | 管理责任 | 变更前最低审批 | 最低审计 |
| --- | --- | --- | --- |
| SKU / UOM | 业务管理员 + 计划/物料负责人 | UOM、可追溯、保质期或业务启用角色变化 | 变更前后值、原因、生效日期、操作者 |
| 合作伙伴身份 | 销售/采购负责人 | 角色、税务/合规或资质变化 | 变更前后值、原因、操作者 |
| 资源层级 | 生产/维护负责人 | 工厂/产线/工作中心/设备重新归属 | 原/新父级、生效日期、操作者 |
| 设备资产 | 维护负责人 | 资产类别、关键性、可维护性或遥测启用变化 | 变更前后值、操作者 |
| ReferenceData | CodeSet 的领域责任人 | 删除代码或改变稳定语义 | CodeSet、code、原/新含义、操作者 |

M2-L 拆分前同时包含架构、规则和开放问题的原文冻结在 [`../../reports/m2-l-business-master-data-field-matrix-pre-split-2026-09-07.md`](../../reports/m2-l-business-master-data-field-matrix-pre-split-2026-09-07.md)，仅用于追溯。
