# 工程资料到生产版本

这条路径帮助工程、工艺和生产准备角色把可生产的产品版本准备出来。它不承诺完整 PLM/PDM 能力，只覆盖当前 Business Console 已暴露的工程资料页面和生产版本窄化工作台。

## 适用角色

- 工程资料维护员：维护工程物料、文档、EBOM 和 ECO/ECN。
- 工艺工程师：维护 MBOM、标准工序和工艺路线。
- 生产准备员：确认生产版本是否可供计划和 MES 使用。

## 前置资料

- 已有组织、环境和登录权限。
- MasterData 中已有 SKU、UOM、工厂、工作中心、设备和必要参考数据。
- 产品图纸、物料清单和工艺资料已经完成业务审核，准备录入系统。

## 页面入口

| 目标 | Business Console 路由 |
| --- | --- |
| 工程资料工作台 | `/engineering` |
| 工程物料 | `/engineering/items` |
| 工程文档 | `/engineering/documents` |
| EBOM | `/engineering/ebom` |
| MBOM | `/engineering/mbom` |
| 标准工序 | `/engineering/standard-operations` |
| 工艺路线 | `/engineering/routings` |
| 生产版本 | `/engineering/production-versions` |
| ECO/ECN | `/engineering/eco` |

## 操作步骤

1. 在 `/master-data/skus` 确认成品和关键物料 SKU 存在，并检查 UOM、分类和生命周期状态。
2. 在 `/engineering/items` 建立或检查工程物料。当前 `ItemCode` 语义冻结为 MasterData SKU code。
3. 在 `/engineering/documents` 按 `itemCode` 和文档类型关联图纸、作业指导或质量证据。
4. 在 `/engineering/ebom` 维护设计结构，确认替代料、虚拟件、位号、得率和损耗。
5. 在 `/engineering/mbom` 将 EBOM 转为制造用料，确保非 phantom EBOM child SKU 被 MBOM material line 覆盖。
6. 在 `/engineering/standard-operations` 准备启用的标准工序，再到 `/engineering/routings` 发布工艺路线。
7. 在 `/engineering/production-versions` 绑定 SKU、MBOM、Routing 和有效日期，形成计划和 MES 可引用的版本。
8. 如需变更，使用 `/engineering/eco` 释放 ECO/ECN；当前归档为即时执行。

## 业务对象/单据流

SKU -> EngineeringItem -> EngineeringDocument -> EBOM -> MBOM -> StandardOperation -> Routing -> ProductionVersion -> ECO/ECN。

## 状态变化

- EBOM、MBOM、Routing 和 ProductionVersion 从 `Draft` 经校验发布为 `Published`，被 ECO/ECN 影响后可归档为 `Archived`。
- ProductionVersion 需要真实 MBOM、Routing、SKU 和有效日期校验；同一 SKU 的 active 有效窗不能重叠。
- ECO/ECN release 会在同一事务内归档受影响版本。

## 成功结果

- 计划、MES 和工程工作台能按 SKU 找到已发布生产版本。
- Routing 中的工序保存标准工序快照，后续执行不会被标准工序名称或工时漂移直接改写。
- MBOM 与 EBOM 的关键物料关系可解释，不把设计结构和制造领料结构混为一谈。

## 常见失败/空态

- SKU 或 UOM 未准备：先回到 `/master-data/skus`、`/master-data/units` 补齐。
- 标准工序未启用：Routing release 会拒绝引用。
- 生产版本有效期重叠：调整旧版本归档或修改新版本有效窗。
- 页面列表为空：通常是组织/环境下尚未录入数据，不代表服务不可用。

## 当前限制

- 当前文档只覆盖 Business Console 已暴露的工程资料页面，不描述完整 PLM/PDM。
- ECO release 当前按即时归档处理；future effectiveDate 延迟切换和更细粒度下游失效事件属于后续 hardening。
- 工程资料页面是窄化工作台，不承诺完整图纸审批、复杂变更影响分析或高级版本矩阵。

[内部缺口记录](/internal/gaps/engineering-to-production)
