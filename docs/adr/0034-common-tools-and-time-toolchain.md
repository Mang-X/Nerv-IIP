# ADR 0034：平台通用工具与时间处理工具链

- 状态：已接受（转录既有裁决，不代表实现已完成）
- 日期：2026-08-24
- 原始来源：[Linear 平台通用工具与时间处理工具链裁决](https://linear.app/mangax/document/平台通用工具与时间处理工具链裁决-14c0fc2db966)；原文更新于 2026-08-25，本记录保留原裁决日期。

## 背景与理由

通用数据处理、日期时间、数字和字符串操作需要明确工具链，以减少重复手写，同时避免万能 `Utils`、Request 继承树和无语义公共包装层。语言与平台标准库优先；第三方库只解决成熟、稳定、跨业务重复的问题。

本记录转录原文已接受的边界；仓内长期决策以本 ADR 为住所，原始来源用于追溯。版本与实际使用情况回到 [技术栈资料索引](../reference/technology-stack.md) 指向的 manifest 和代码，不从决策状态推导实现状态。

## 决策

### 后端

| 能力 | 已接受边界 |
| --- | --- |
| 当前时间与定时器 | .NET `TimeProvider` |
| 时间点 | `DateTimeOffset`；事件与持久化默认 UTC |
| 业务日期 | `DateOnly` |
| 当地时刻 | `TimeOnly`，与显式业务时区配对 |
| 持续时间 | `TimeSpan` |
| 数字与字符串 | .NET BCL、`CultureInfo`、`NumberStyles`、`string` / span 接口优先 |
| 查询规范化 | 组合式值对象、规则扩展或委托 validator |

不自建万能日期库或 `DateTimeUtils`、`StringUtils`、`NumberUtils`。

### 前端

| 能力 | 已接受边界 |
| --- | --- |
| 通用数组、对象与集合处理 | `es-toolkit` |
| 日期计算与显式解析 | `date-fns` |
| 日期选择器与日历值 | 现有 `@internationalized/date` |
| 本地化日期与数字展示 | `Intl.DateTimeFormat`、`Intl.NumberFormat` |

不使用 Moment.js、Day.js、lodash/lodash-es 或全量二次导出 `utils/index.ts`。本记录保留原裁决的前端边界，不实施前端迁移。

### 业务与公共模块所有权

领域金额、状态、权限、编码和业务日期规则仍归所属模块。公共模块提供小接口与高价值复用；供应商类型不得无必要地穿透 seam。每次以「公共能力 + 一个首篇消费者」落地，其余迁移拆为独立 S/M 票。

## 已考虑的替代方案与后果

原文明确排除万能 Utils、Request 继承树、无语义包装层及上列前端替代库，但未逐项记录落选比较；本次不补造理由。标准库与成熟第三方库承担通用机制，模块保留业务语义，代价是不能通过一层万能 wrapper 抹平所有差异。

## 范围与复评边界

Office/PDF、报表、发票、标签和音视频是独立能力，见 [ADR 0035](0035-business-documents-reports-and-media-toolchain.md)。本记录不复制任务映射、迁移进度或验收状态。原裁决未单列复评触发条件；需要改变上述选择或所有权时，按 [决策记录 Governance](../governance/decisions/records.md) 新建取代记录，不从当前实现反推历史裁决。
