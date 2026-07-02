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

- `/mes/schedules` 不能被描述为正式甘特或高级 APS；后续需要独立排程工作台消费 APS 输出。
- `foundation-readiness` 缺上下文时应提示选择 SKU、生产版本、工作中心或设备，不能把上下文空态当成全局阻塞。
- 完工入库失败后的诊断入口仍需要更强的跨页导航和用户解释。
