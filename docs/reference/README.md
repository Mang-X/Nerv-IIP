# Reference 文档入口

本目录维护**当前可查事实的人工索引与解释层**：Schema 目录、受控码表、契约/页面矩阵、事件消费矩阵、术语和技术资料链接。Reference 不拥有运行时实现，也不承担项目状态、历史审计或长期工程规则。

读取 Reference 前先确认真正的 producer。代码、配置、公开契约、生成物、migration、seed、脚本帮助和测试与本文冲突时，以 producer 为准并修正文档。

## 当前 Reference

| Reference | Producer / 权威来源 | 更新方式 | 主要消费者 | 生成属性 |
| --- | --- | --- | --- | --- |
| [`data/database-schema-catalog.md`](data/database-schema-catalog.md) | EF Core migrations、EntityConfigurations、DbContext | Schema/迁移变更时同步人工解释；物理结构以代码为准 | 后端开发、数据库治理、发布排障 | 人工维护 |
| [`demo/factory-world-bible.md`](demo/factory-world-bible.md) | `WalkthroughSeedSpec`、对应 seed 与行为测试 | 最小走查设定/价格/工时调整时与代码投影一起修改 | Product、Demo、真实走查 | 人工维护，与 seed/spec 共同受控 |
| [`frontline/contract-page-scope-action.md`](frontline/contract-page-scope-action.md) | BusinessGateway OpenAPI、Gateway 授权/代理实现、generated client、业务服务与页面代码 | 公开 operation、scope、action 或页面消费变化时复核 | 一线页面、PDA/Console 产品与验收 | 人工维护 |
| [`frontline/acceptance-evidence.md`](frontline/acceptance-evidence.md) | 当前公开读写面与真实验收输入 | 真实验收证据口径变化时更新 | PDA/Console 真实账号验收 | 人工模板 |
| [`glossary.md`](glossary.md) | 当前领域边界、公开契约与产品语义 | 术语边界变化时同步 | 全仓文档与命名评审 | 人工维护 |
| [`integration/event-consumption-matrix.md`](integration/event-consumption-matrix.md) | `backend/common/Contracts/**`、业务本地事件、活动 `IntegrationEventConsumer` / `CapSubscribe`、事件测试 | 新增/删除事件或消费方时按治理规则复核 | 跨域架构、事件设计、业务闭环审查 | 人工维护 |
| [`master-data/dictionary.md`](master-data/dictionary.md) | MasterData seed、ReferenceData API、独立目录 API、当前前端消费代码 | CodeSet/字段映射或目录切换时同步 | MasterData、Product、Business Console | 人工维护 |
| [`technology-stack.md`](technology-stack.md) | `.node-version`、package manifest/lockfile、`Directory.Packages.props`、项目文件、AppHost/部署配置 | 技术角色或官方资料入口变化时更新；精确版本不复制到本文 | README、Architecture、部署与依赖评审 | 人工索引 |

## 使用规则

1. Reference 回答“当前有哪些、从哪里查、如何解释”，不回答“为什么选择”（ADR）、“系统边界是什么”（Architecture）、“必须遵守什么规则”（Governance）、“如何执行”（Runbook）或“现在做到哪一步”（Status）。
2. 表格中的状态、枚举、版本、路由、Schema、producer/consumer 关系必须能回到明确 producer；不得把旧 Issue、历史报告或一次性扫描结果升级为当前事实源。
3. 机器可读输入或生成伴随物必须与其生产者原子迁移；不得为了 Reference 目录再造第二份 JSON registry、哈希清单或自然语言同步器。
4. 当前 Reference 发生结构变化时同步活跃 README/AGENTS/代码注释；冻结报告、ADR 与 `docs/superpowers/**` 不为清理旧 URL 批量改写。
5. M2 迁移期旧 `docs/architecture/*` 兼容页只负责导航；最终删除条件由 M2-M/M4 统一收口。
