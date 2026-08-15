# BarcodeLabel MVP 设计

## 目标

将 BarcodeLabel 建设为条码规则、标签模板、标签打印批次和扫描记录的业务事实来源。

BarcodeLabel 为 WMS、MES、Inventory 和移动端/PDA 工作流提供支持，但不持有仓库任务状态、库存数量或业务单据生命周期。

## 当前状态

BarcodeLabel 当前没有服务目录。MasterData 持有 SKU/条码策略输入，FileStorage 持有文件/模板附件，Inventory 持有库存数量和移动事实。

## 持有的事实

BarcodeLabel 持有：

1. BarcodeRule：条码类型、前缀、长度、校验和规则、允许的来源单据类型及状态。
2. LabelTemplate：模板元数据、模板文件引用、变量 schema 及状态。
3. LabelPrintBatch：打印请求、目标单据引用、标签值、请求数量、状态及幂等键。
4. LabelPrintItem：生成的标签值和可选的文件引用。
5. ScanRecord：来源设备、扫描值、来源工作流、目标单据引用、扫描结果及幂等键。

BarcodeLabel 不持有：

1. Inventory 的在手、可用、预留或冻结数量。
2. WMS 的入库、出库或任务状态。
3. ERP 的采购、销售或发票状态。
4. MES 的工单或工序状态。
5. FileStorage 对象键、二进制字节或下载授权。
6. SKU 主数据事实、UOM 或条码策略所有权。

## API 范围

| API | 用途 | 权限 |
| --- | --- | --- |
| `POST /api/business/v1/barcodes/rules` | 创建或更新条码规则。 | `business.barcodes.templates.manage` |
| `POST /api/business/v1/barcodes/templates` | 创建或更新标签模板。 | `business.barcodes.templates.manage` |
| `GET /api/business/v1/barcodes/templates` | 列出有效模板。 | `business.barcodes.templates.manage` |
| `POST /api/business/v1/barcodes/print-batches` | 创建标签打印批次及生成的标签项。 | `business.barcodes.print` |
| `GET /api/business/v1/barcodes/print-batches/{printBatchId}` | 读取打印批次详情。 | `business.barcodes.print` |
| `POST /api/business/v1/barcodes/scans` | 记录扫描结果。 | `business.barcodes.scans.write` |
| `GET /api/business/v1/barcodes/scans` | 按设备、值或来源单据列出扫描记录。 | `business.barcodes.scans.write` |

## 规则

1. 创建打印批次需要幂等键和来源单据引用。
2. 使用相同载荷重复提交同一打印幂等键时，返回现有批次。
3. 使用不同载荷重复提交同一打印幂等键时，拒绝请求。
4. 创建扫描记录需要来源设备、扫描值、来源工作流和幂等键。
5. 使用相同载荷重复提交同一扫描幂等键时，返回现有扫描记录。
6. 扫描记录是仅追加的业务事实，不改变 WMS、MES 或 Inventory 状态。
7. 模板文件 ID 只能作为 FileStorage 引用；禁止使用对象键和长期有效的 URL。
8. 当条码值由服务生成时，必须根据规则、来源单据和序列输入确定性地产生。

## 事件

BarcodeLabel 发布采用 ADR 0011 信封格式的事件：

1. `barcode.LabelPrintBatchCreated`
2. `barcode.LabelPrintBatchCompleted`
3. `barcode.LabelScanned`
4. `barcode.ScanRejected`

事件携带公开单据引用、来源设备、扫描值、模板/规则 ID 以及幂等/关联 ID。事件不得携带 FileStorage 对象键。

## 权限

初始权限代码：

1. `business.barcodes.templates.manage`
2. `business.barcodes.print`
3. `business.barcodes.scans.write`

## 持久化

默认 schema：`barcode`。

必需的表：

1. `barcode_rules`
2. `label_templates`
3. `label_print_batches`
4. `label_print_items`
5. `scan_records`

每张表和每个业务列都需要 schema 注释。PostgreSQL migration 历史记录必须使用 `barcode.__EFMigrationsHistory`。

## 测试

验收要求：

1. 针对确定性标签生成、打印幂等性和扫描幂等性的 Domain 测试。
2. 验证拒绝空白扫描值、缺失设备和冲突幂等载荷的 Domain 测试。
3. 针对路由形态、授权、校验和 operation ID 的 Web 测试。
4. 使用 `Nerv.IIP.Testing` 的 schema 约定测试。
5. 针对打印和扫描事实的集成事件转换器/序列化测试。
6. 证明所有公开响应均不包含 `objectKey` 或 `object_key` 的测试。
