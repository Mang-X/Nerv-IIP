# Product 文档入口

本目录只承载**当前产品语义**：给谁使用、解决什么问题、业务流程、信息架构、角色边界、UX 规则、分期与产品验收口径。实现事实仍以当前代码、公开契约和测试为准；任务进度与验收证据仍留在 GitHub/Linear。

## 按产品域路由

| 产品域 | 当前文档 |
| --- | --- |
| 基础数据 | [`master-data/design.md`](master-data/design.md) |
| 产品工程 | [`product-engineering/design.md`](product-engineering/design.md) |
| MES | [`mes/design.md`](mes/design.md) |
| 库存与 WMS | [`inventory/design.md`](inventory/design.md) |
| 设备维护 | [`maintenance/design.md`](maintenance/design.md) |
| 排产工作台 | [`scheduling/design.md`](scheduling/design.md) |
| PDA | [`mobile-pda/design.md`](mobile-pda/design.md) |
| 现场多端角色旅程 | [`frontline/role-journeys.md`](frontline/role-journeys.md) |

## 使用纪律

- Product 回答“用户与业务应该怎样工作”，不保存 CI run、一次性调查计数或项目状态总账。
- 服务边界、事实所有权、协议、部署和工程治理仍从 [`../architecture/README.md`](../architecture/README.md) 路由。
- 通用运行验收证据模板不放在 Product；现场角色旅程对应模板位于 [`../reference/frontline/acceptance-evidence.md`](../reference/frontline/acceptance-evidence.md)。
- 工厂设定与人工走查最小数据属于 Reference，当前住所为 [`../reference/demo/factory-world-bible.md`](../reference/demo/factory-world-bible.md)。
- `docs/architecture/*-module-product-design.md` 等旧路径在 M2 迁移期只可能是兼容入口，不能作为 Product 正文继续维护。
