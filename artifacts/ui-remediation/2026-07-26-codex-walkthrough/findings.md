# 2026-07-26 领导演示逐页走查与修复清单

## 验收口径与证据

- 范围：`business-console/src/pages` 的 96 个路由页面，以及 `apps/screen/src/pages` 的 10 个大屏路由。
- 视口：1920 × 1080；账号：`admin`；数据窗口：本机 2026-07-26 演示栈。
- 修复前证据：`artifacts/ui-remediation/2026-07-26-codex-walkthrough/full/`。
- 修复后 PC 证据：`artifacts/ui-remediation/2026-07-26-codex-walkthrough/verified-final2/`。
- 修复后大屏证据：`artifacts/ui-remediation/2026-07-26-codex-walkthrough/verified-screen-final2/`。
- 每页采集：全页截图、console/page error、HTTP 4xx/5xx、空状态、可见 GUID/技术文案、横向溢出。
- 边界：仅改前端布局、文案和组件使用；未改后端、OpenAPI、生成客户端或种子数据。

## 已修

| 端   | 路由                                                                                                                    | Severity | 发现                                                                                                 | 修法                                                                                                       |
| ---- | ----------------------------------------------------------------------------------------------------------------------- | -------: | ---------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| PC   | `/barcode/print-batches`                                                                                                |       P1 | 批次、模板、文件 GUID 直接暴露；`purchase-receipt`、`printed` 等枚举未本地化；业务编号不是主操作入口 | 表格改以采购收货单等业务编号为首列并提供可点击链接；业务来源、状态全部本地化；隐藏内部批次、模板、文件标识 |
| PC   | `/quality/inspection-tasks`                                                                                             |       P1 | 检验任务 ID、检验方案 ID 两列为 GUID，挤占领导演示表格宽度                                           | 隐藏内部标识列，保留工单、来源、物料、时间、超期与处置操作                                                 |
| PC   | `/engineering/bom-analysis`                                                                                             |       P1 | 单张 KPI 卡孤悬，错误与警告混在一个结果中                                                            | 用 `NvMetricCard` 变体组成结果、错误、警告三卡指标组                                                       |
| PC   | `/engineering/ebom`、`/engineering/items`、`/engineering/mbom`、`/engineering/routings`                                 |       P1 | 各页仅一张 KPI 卡，层级松散                                                                          | 统一为 `NvMetricCard` breakdown + alert 双卡组，拆分总量与草稿风险                                         |
| PC   | `/erp`、`/mes/capacity`、`/mes/dispatch`、`/mes/downtime`                                                               |       P1 | 各页仅一张 KPI 卡，异常/待处理量缺少独立视觉层级                                                     | 统一为 `NvMetricCard` breakdown + alert 双卡组                                                             |
| PC   | `/mes/materials`                                                                                                        |       P1 | 物料需求仅一张 KPI 卡                                                                                | 改为总需求 icon 卡 + 待齐套 alert 卡                                                                       |
| PC   | `/mes/schedules`                                                                                                        |       P2 | “过渡入口”“正式 APS”等实现阶段文案；空状态未说明结果来源                                             | 删除用途说明段落与阶段性措辞；空状态明确来自规则排程结果                                                   |
| PC   | `/equipment/[deviceAssetId]`                                                                                            |       P2 | “正式页面”、英文 Maintenance 与用途说明段落属于实现/说明书文案                                       | 改为业务名称和数据来源式空状态，删除说明书段落                                                             |
| PC   | `/master-data/facilities`                                                                                               |       P2 | “组织树高级搜索即将上线”属于研发进度文案                                                             | 改为当前可执行的搜索提示                                                                                   |
| PC   | `/maintenance/spare-parts`                                                                                              |       P1 | 列表接口 500 时仍渲染“暂无备件需求”，把失败误报为空数据，并直接显示技术错误                          | 请求失败时只显示业务化重试提示并隐藏空表；后端 500 另列阻塞                                                |
| PC   | `/login`                                                                                                                |       P2 | 登录背景使用渐变装饰                                                                                 | 改为纯色与透明叠层                                                                                         |
| 大屏 | `/`、`/login`、`/factory`、`/equipment`、`/line`、`/line/[id]`、`/quality`、`/warehouse`、`/workshop`、`/workshop/[id]` |       P1 | 页面、卡片、进度条、状态灯大量使用 CSS 渐变背景，与 owner 的无渐变裁决冲突                           | 全部改为纯色、透明色、描边和字重层级；保留非背景用途的遮罩                                                 |
| 大屏 | 同上                                                                                                                    |       P2 | 可见文案残留 `#570`、`#738`、historian、mock、后端接入、聚合端点等研发术语                           | 改为数据窗口、数据接入状态、业务口径与导航指引                                                             |
| 大屏 | `/quality`                                                                                                              |       P1 | 右侧待处理队列的第三组越过内容容器底部并与页脚区域重叠                                               | 压缩分组间距、元信息留白与进度条高度；最终 DOM 测量末组底部 990.75，小于容器底部 1007.75                   |

## 待裁决 / 超范围阻塞

| 端  | 路由                                                     | Severity | 发现                                                                        | 阻塞原因 / 建议                                                                                           |
| --- | -------------------------------------------------------- | -------: | --------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| PC  | `/equipment/[deviceAssetId]`、`/maintenance/spare-parts` |       P1 | `GET /api/business-console/v1/maintenance/spare-parts` 在当前演示栈返回 500 | 后端问题，按任务边界未修改；前端已避免误报“暂无数据”。建议由 Maintenance/BusinessGateway 责任线复现并修复 |
| PC  | `/engineering/documents`                                 |       P1 | 新建工程文档要求用户手工填写“文件引用 ID”，属于技术型交互                   | 当前契约没有上传通道，纯前端替换会伪造能力；建议裁决为接入文件选择器，或建立后端/契约跟进项               |
| PC  | `/quality/inspection-tasks`                              |       P2 | 默认每页 200 条，当前实测长表无横向溢出，但领导演示需要较长纵向滚动         | 改成 50/100 条还是引入虚拟化会改变产品使用习惯；建议产品确认默认密度后另行调整                            |

## 等数据（不修）

| 端   | 路由                                                                                                                                                                                        | Severity | “暂无数据”位置                                                   | 数据来源 / 等待原因                                                   |
| ---- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------: | ---------------------------------------------------------------- | --------------------------------------------------------------------- |
| PC   | `/equipment`、`/equipment/[deviceAssetId]`、`/equipment/alarms`、`/equipment/telemetry/connectors`、`/equipment/telemetry/history`、`/equipment/telemetry/oee`、`/equipment/telemetry/tags` |       P2 | 设备实时值、单机详情、报警、采集连接、历史趋势、OEE、标签        | 设备遥测、维修历史与常驻模拟尚未落地；符合任务说明，仅登记            |
| PC   | `/maintenance/availability`、`/maintenance/inspections`、`/maintenance/reliability`、`/maintenance/work-orders`                                                                             |       P2 | 设备维护窗口、点检、可靠性、待执行维护                           | 依赖设备档案、维修工单与完工历史；当前演示数据未覆盖                  |
| PC   | `/approval`                                                                                                                                                                                 |       P2 | 待决策、待处理审批                                               | 当前账号没有待办审批实例                                              |
| PC   | `/erp`、`/erp/finance/ar-ap`、`/erp/finance/cost-candidates`、`/erp/procurement/rfqs`、`/erp/procurement/supplier-quotations`、`/erp/sales`                                                 |       P2 | 采购申请、应付、成本候选、询价、供应商回价、销售机会             | 相应业务流程实例未灌入；页面空状态已说明形成来源                      |
| PC   | `/inventory/counts`、`/inventory/movements`                                                                                                                                                 |       P2 | 盘点任务、待确认库存移动                                         | 当前数据包含历史流水，但没有待处理盘点/移动；页面查询口径是待处理集合 |
| PC   | `/mes/capacity`、`/mes/downtime`、`/mes/handovers`、`/mes/production-reports`、`/mes/quality`、`/mes/schedules`、`/mes/traceability`                                                        |       P2 | 产能影响、停机、交接、遥测报工候选、质量处置、规则排程、追溯结果 | 对应执行事件、排程动作或查询条件尚未形成；空状态已说明触发来源        |
| PC   | `/planning`、`/scheduling`                                                                                                                                                                  |       P2 | 规划样本、未排工序                                               | 当前演示栈没有规划样本/待排操作                                       |
| PC   | `/wms`、`/wms/counts`、`/wms/inbound`、`/wms/outbound`、`/wms/wcs`                                                                                                                          |       P2 | 条件化库存明细、盘点单、出入库明细、WCS 任务                     | 当前筛选范围或作业实例为空；不以历史库存流水替代待处理作业            |
| 大屏 | `/equipment`、`/line`、`/line/[id]`、`/workshop`、`/workshop/[id]`                                                                                                                          |       P2 | 真实设备参数、趋势、设备驱动产出/节拍                            | 当前大屏演示数据 seam 可渲染布局；真实遥测与常驻模拟仍等待设备数据线  |

## 页面覆盖矩阵

以下每个路由均由目录树自动发现，并以真实浏览器访问；设备详情使用页面实际返回的设备 `DEV-ASM-01` 解析动态路由，不伪造设备 ID。

| 分组             | 已对照路由                                                                                                                                                                                                                                                                                                                                              |
| ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| PC · 工作台/权限 | `/`、`/approval`、`/forbidden`、`/login`                                                                                                                                                                                                                                                                                                                |
| PC · 条码        | `/barcode/print-batches`、`/barcode/rules`、`/barcode/scans`、`/barcode/templates`                                                                                                                                                                                                                                                                      |
| PC · 设计系统    | `/design-system/blocks`、`/design-system/shell`                                                                                                                                                                                                                                                                                                         |
| PC · 产品工程    | `/engineering`、`/engineering/bom-analysis`、`/engineering/documents`、`/engineering/ebom`、`/engineering/eco`、`/engineering/items`、`/engineering/mbom`、`/engineering/production-versions`、`/engineering/routings`、`/engineering/standard-operations`                                                                                              |
| PC · 设备        | `/equipment`、`/equipment/[deviceAssetId]`、`/equipment/alarms`、`/equipment/telemetry/alarm-rules`、`/equipment/telemetry/connectors`、`/equipment/telemetry/control-bindings`、`/equipment/telemetry/history`、`/equipment/telemetry/oee`、`/equipment/telemetry/tags`                                                                                |
| PC · ERP         | `/erp`、`/erp/finance`、`/erp/finance/ar-ap`、`/erp/finance/cost-candidates`、`/erp/finance/vouchers`、`/erp/procurement/purchase-orders`、`/erp/procurement/receipts`、`/erp/procurement/rfqs`、`/erp/procurement/supplier-quotations`、`/erp/sales`、`/erp/sales/deliveries`、`/erp/sales/orders`、`/erp/sales/quotations`                            |
| PC · 库存        | `/inventory/availability`、`/inventory/counts`、`/inventory/lots`、`/inventory/movements`                                                                                                                                                                                                                                                               |
| PC · 维护        | `/maintenance/availability`、`/maintenance/inspections`、`/maintenance/plans`、`/maintenance/reliability`、`/maintenance/spare-parts`、`/maintenance/work-orders`                                                                                                                                                                                       |
| PC · 主数据      | `/master-data/code-rules`、`/master-data/devices`、`/master-data/facilities`、`/master-data/organization`、`/master-data/partners`、`/master-data/product-categories`、`/master-data/reference-data`、`/master-data/scheduling`、`/master-data/skill-catalog`、`/master-data/skills`、`/master-data/skus`、`/master-data/units`、`/master-data/workers` |
| PC · MES         | `/mes`、`/mes/capacity`、`/mes/dispatch`、`/mes/downtime`、`/mes/foundation`、`/mes/handovers`、`/mes/materials`、`/mes/operation-tasks`、`/mes/plans`、`/mes/production-reports`、`/mes/quality`、`/mes/receipts`、`/mes/schedules`、`/mes/traceability`、`/mes/wip`、`/mes/work-orders`、`/mes/work-orders/[workOrderId]`                             |
| PC · 计划/质量   | `/planning`、`/scheduling`、`/quality/analysis`、`/quality/inspection-tasks`、`/quality/inspections`、`/quality/ncrs`、`/quality/reason-codes`                                                                                                                                                                                                          |
| PC · WMS         | `/wms`、`/wms/counts`、`/wms/inbound`、`/wms/outbound`、`/wms/picking`、`/wms/putaway`、`/wms/wcs`                                                                                                                                                                                                                                                      |
| 大屏             | `/`、`/equipment`、`/factory`、`/line`、`/line/[id]`、`/login`、`/quality`、`/warehouse`、`/workshop`、`/workshop/[id]`                                                                                                                                                                                                                                 |

## 验证结果

最终结果以提交前最后一次命令为准：

- 浏览器：PC 96/96 路由完成；除上述两个页面共享的备件接口 500 外，无 console/page error、4xx/5xx、横向溢出或可见 GUID/技术文案信号。大屏 10/10 路由完成，最终复核无运行时或布局信号。
- `pnpm -C frontend typecheck`：通过。
- `pnpm -C frontend test`：通过；business-console 108 个测试文件、1296 个测试通过，全 workspace 通过。运行输出仍含既有 jsdom CSS 解析、localStorage 实验提示和 Vue warning，无测试失败。
- `pnpm -C frontend build`：通过；保留依赖内 `/* #__PURE__ */` 位置和 chunk size 的既有构建告警。
- PDA `typecheck` / `test` / `build`：全部通过；34 个测试文件、340 个测试通过。运行输出仍含既有 jsdom CSS 解析和 localStorage 实验提示。
- touched 文件格式：逐文件 `vp fmt --check` 通过。
