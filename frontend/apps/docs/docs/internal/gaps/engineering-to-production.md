# 内部缺口记录：工程资料到生产版本

该页面是内部缺口记录，不作为官网 Docs 对外营销文案。

## 证据页面

- `/getting-started/engineering-to-production`
- `frontend/apps/business-console/src/pages/engineering`
- `docs/architecture/implementation-readiness.md` ProductEngineering 段落

## 建议 issue 标题

- `[ProductEngineering] 生产版本上手链路补齐图纸审批、变更影响和延迟生效说明`

## 缺口记录

### 能力缺失

- 完整 PLM/PDM、图纸审批、复杂变更影响分析和 future effectiveDate 延迟切换未形成当前可上手闭环。

### 操作不连贯

- 基础资料到工程资料的跨页跳转仍依赖用户知道 SKU、UOM、工作中心和设备之间的先后关系，缺少向导或上下文穿透。

### 手填 ID

- 文档仍需要用 SKU code、itemCode、MBOM/Routing 编号说明对象关系；页面侧需要更多从列表选择对象的入口来减少手工录入。

### 术语不清

- EngineeringItem 的 `ItemCode` 与 MasterData SKU code 的关系容易被误读为两套编码，需要在页面文案和帮助文本中继续澄清。

### 反馈不足

- 生产版本发布失败、有效期重叠和标准工序未启用时，需要更直接的错误定位和下一步跳转。
