# Quality 检验 MVP 设计

## 目标

将现有 Quality 服务从仅包含 NCR 的范围扩展到检验计划和检验记录，但不赋予 Quality 对库存、仓库、ERP 或 MES 状态的所有权。

## 当前状态

Quality 已具备 NonconformanceReport 的 Domain、Infrastructure、Web、PostgreSQL 迁移和测试。InspectionPlan 和 InspectionRecord 尚不存在。新范围必须保留现有 NCR 路由、权限、事件和测试。

## 所有权事实

Quality 拥有：

1. InspectionPlan：面向 SKU、供应商、客户、工序步骤、工作中心、设备资产或单据类型的检验规则集。
2. InspectionCharacteristic：具有目标值、公差、方法、严重程度和抽样规则的测量或检查特性。
3. InspectionRecord：来源单据或工序的执行结果。
4. InspectionResultLine：观测值、通过/失败结果和缺陷分类。
5. QualityDispositionReference：检验失败后创建 NCR 时，从失败检验到 NCR 的链接。

Quality 不拥有：

1. Inventory 库存余额、库存移动或位置状态。
2. WMS 入库/出库任务状态。
3. ERP 采购收货、销售退货或供应商单据状态。
4. MES 工单、工序任务或报工状态。
5. MasterData 的 SKU、伙伴、工作中心、设备资产或可复用参考值。

## MVP 命令与查询

| API | 用途 | 说明 |
| --- | --- | --- |
| `POST /api/business/v1/quality/inspection-plans` | 创建检验计划。 | 支持收货、工序、终检和维护检验类别。 |
| `POST /api/business/v1/quality/inspection-plans/{inspectionPlanId}/activate` | 激活草稿计划。 | 已激活计划具有版本，且执行字段不可变。 |
| `GET /api/business/v1/quality/inspection-plans` | 列出已激活和草稿计划。 | 按类别、SKU、伙伴、工作中心和状态筛选。 |
| `POST /api/business/v1/quality/inspection-records` | 记录一次检验执行。 | 引用来源服务和来源单据 ID。 |
| `POST /api/business/v1/quality/inspection-records/{inspectionRecordId}/failures/ncr` | 根据失败记录创建 NCR。 | 复用现有 NCR 聚合并保留来源检验链接。 |
| `GET /api/business/v1/quality/inspection-records` | 列出检验记录。 | 按来源单据、SKU、类别和结果筛选。 |

## 计划规则

1. 草稿计划在激活之前可以编辑。
2. 已激活计划不得更改会影响历史记录的特性。
3. 计划可由新版本取代。
4. 计划适用性基于公开引用 ID 和编码，绝不使用跨 schema 外键。

## 记录规则

1. 一条检验记录引用一份来源单据或一道工序。
2. 记录结果为 `passed`、`rejected` 或 `requiresDisposition`。
3. 失败记录可以创建 NCR，但 Quality 不直接创建库存移动、仓库任务、采购退货或返工单。
4. 测量项存储观测值、单位、结果和可选附件文件 ID。
5. 附件文件 ID 仅引用 FileStorage 公开 ID。

## 事件

Quality 发布符合 ADR 0011 信封格式的事件：

1. `quality.InspectionPassed`
2. `quality.InspectionRejected`
3. `quality.NcrOpened`
4. `quality.DispositionDecided`
5. `quality.NcrClosed`

检验事件携带来源引用、受检项引用、结果摘要和检验记录 ID。事件不命令下游服务变更状态。

## 权限

初始权限编码：

1. `business.quality.inspection-plans.manage`
2. `business.quality.inspection-records.create`
3. `business.quality.inspection-records.read`
4. `business.quality.ncr.manage`

## 持久化

默认 schema 仍为 `quality`。

新增数据表：

1. `inspection_plans`
2. `inspection_plan_characteristics`
3. `inspection_records`
4. `inspection_result_lines`

现有 NCR 表保持不变。迁移必须扩展当前 Quality schema，而不是替换现有迁移历史记录。

## 测试

验收要求：

1. 覆盖计划激活、计划不可变性、记录通过/失败计算和从检验创建 NCR 的领域测试。
2. 覆盖内部授权、路由形状、校验和 operation ID 的 Web 测试。
3. 证明现有 NCR endpoint 仍正常工作的回归测试。
4. 覆盖新表和新列的 schema 约定测试。
5. 覆盖检验结果事件和 NCR 链接事件的事件转换器测试。
