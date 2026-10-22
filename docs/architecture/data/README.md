# Data Architecture 路由

M2-A 最终分类后，M2-K 没有需要迁入本目录的独立 Data Architecture owner：原候选 `database-schema-conventions.md` 已确认属于 Governance，而当前 Schema catalog 属于 Reference。

因此本目录只负责数据架构任务路由，不新建第二份 Schema 规则或目录：

- 数据库命名、Schema 与迁移规则：[`../../governance/data/database-schema.md`](../../governance/data/database-schema.md)。
- 当前 Schema / 表 / 索引人工目录：[`../../reference/data/database-schema-catalog.md`](../../reference/data/database-schema-catalog.md)。
- 跨服务数据所有权：[`../overview/context-map.md`](../overview/context-map.md)。
- 服务自身持久化事实：从 [`../platform/README.md`](../platform/README.md) 路由到对应 Current Architecture，并以当前 migrations / configuration / code 为最终事实。

若后续出现真正跨服务且长期稳定的 Data Architecture 主题，再通过 ADR / 文档治理决定是否新增 owner；不能因为目录已存在而人为复制 Governance 或 Reference。