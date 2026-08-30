# AGENTS.md — Nerv-IIP 平台

本文件是共享代理入口。普通局部任务不以全局项目状态或历史总账作为开工前置。

## 开工

1. 读取从仓库根到目标路径由工具实际选中的各级 Agent 指令；同目录优先级文件会覆盖普通 `AGENTS.md`，不要假定两份文件自动合并。
2. 当前 Issue、spec 或用户要求存在时先读取；随后以目标代码、配置、公开契约、测试和命令帮助核实当前事实。
3. 从 `docs/README.md` 按任务类型选择必要文档；不要默认读取历史状态快照或兼容入口。
4. 命令、版本、目录、生成入口与运行行为以当前仓库事实为准。

## 按任务加载

- 服务边界、目录、数据所有权、跨域调用或公开契约调整：先读 `docs/architecture/README.md`，再读其中路由到的当前架构文档；仓库级基础边界至少核对 `docs/architecture/repo-layout.md` 与 `docs/architecture/context-map.md`。
- 查询当前 Schema 目录、码表、事件消费关系、术语、页面/契约矩阵、权限目录或技术资料：从 `docs/reference/README.md` 路由；Reference 是人工索引，精确实现事实仍回到其 producer。
- 修改当前工程规则（授权、Schema、后端结构、设计系统、错误传输、持久化启动、文档规则、测试治理等）：先从 `docs/governance/README.md` 选择直接相关 Governance；规则页不替代代码事实。
- 用户、角色、业务流程、IA 或 UX 变更：先从 `docs/product/README.md` 读取对应当前 Product，再按 `docs/adr/0021-product-docs-information-architecture.md` 评估产品文档影响。
- 引入、推翻或复评会在当前任务结束后继续约束实现的长期决策：先读 `docs/adr/README.md`、相关 ADR 和 `docs/governance/decisions/records.md`。
- 本地启动、Aspire/fullstack 运行或排障：读取 `docs/runbooks/local-development.md`；其它操作型任务从 `docs/runbooks/README.md` 路由。
- 发布、里程碑规划或跨域能力盘点：读取 `docs/status/current.md`；任务细节、负责人和验收证据回到 GitHub/Linear。
- 核对历史时点判断：从 `docs/status/archive/` 或 `docs/reports/` 读取对应冻结记录；历史不得覆盖当前代码、配置、契约和测试。
- 人工文档或协作文本：读取 `docs/AGENTS.md` 与 `docs/governance/docs/language.md`。
- 新增、修改或审核测试断言、golden/snapshot/digest、provider/lane 命名、确定性等待、测试 evidence 或测试删除：从 `docs/governance/testing/README.md` 读取直接相关规则；执行与排障从 `docs/runbooks/testing/README.md` 路由。
- 新建、修订或执行 Superpowers spec/plan：先读取 `docs/superpowers/AGENTS.md`。
- GitHub/Linear 发布前：除非用户明确指定其他语言，标题、正文、评论、Review、状态说明和验收结论使用简体中文；代码、命令、路径、标识符、检查名、SHA、URL 和必要英文引文按语言 Governance 保留。

## 交付

1. 按最近指令和权威来源运行受影响门禁。
2. 只报告实际执行证据，并明确未运行或 policy-skipped 项。
3. 区分本地测试、CI、真实运行、PR 合并和 tracker 完成。
