# 内部缺口记录：需求计划到完工入库

该页面是内部缺口记录，不作为官网 Docs 对外营销文案。

## 证据页面

- `/getting-started/planning-to-finished-goods`
- `frontend/apps/business-console/src/pages/planning/index.vue`
- `frontend/apps/business-console/src/pages/mes`
- `docs/architecture/implementation-readiness.md` Scheduling、MES、Inventory/WMS 段落

## 建议 issue 标题

- `[Planning/MES] 计划到完工入库路径补齐甘特入口、上下文选择和过账失败诊断导航`

## 缺口记录

### 能力缺失

- 高级 APS 优化器、正式甘特、跨域高级报表和移动端独立 API 仍未作为当前上手闭环交付。

### 操作不连贯

- 计划建议、排产结果、MES 生产计划和工单之间仍需要更清晰的上下文穿透，用户容易在 `/planning`、`/scheduling` 和 `/mes/plans` 之间手动切换。

### 手填 ID

- 工单详情、生产版本、工作中心和设备范围仍可能需要用户从列表复制对象号或参数进入诊断页。

### 术语不清

- `/mes/schedules` 的规则排程和 `/scheduling` 的 APS lite 容易被误解为完整 APS；文档和页面文案需要持续区分。

### 反馈不足

- `foundation-readiness` 缺上下文时应提示选择 SKU、生产版本、工作中心或设备；完工入库失败后也需要更直达的库存过账失败诊断入口。
