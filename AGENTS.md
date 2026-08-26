# AGENTS.md — Nerv-IIP 平台

本文件是共享代理入口；详细规则按目标路径最近的代理指令和本仓库权威文档按需加载。普通局部任务不以全局项目状态台账作为开工前置。

## 开工

1. 修改文件前读取从仓库根到目标路径的全部 `AGENTS.md` 和
   `AGENTS.override.md`；最近文件扩展或覆盖父级指令。
2. 当前 Issue、spec 或用户要求存在时先读取；随后以目标代码、配置、公开契约、
   测试和命令帮助核实当前事实。
3. 从 `docs/README.md` 按任务类型选择文档；不要默认读取
   `docs/architecture/implementation-readiness.md`。
4. 命令、版本、目录与生成入口以当前仓库文件、配置、脚本和帮助输出为事实源。

## 按任务加载

- 服务边界、目录、数据所有权、跨域调用或公开契约调整：先读
  `docs/architecture/README.md`，再读其中路由到的当前架构文档；仓库级基础边界至少核对
  `docs/architecture/repo-layout.md` 与 `docs/architecture/context-map.md`。
- 引入、推翻或复评会在当前任务结束后继续约束实现的长期决策：先读
  `docs/adr/README.md`、相关 ADR 和
  `docs/architecture/decision-record-governance.md`。
- 本地启动、Aspire/fullstack 运行或排障：读取
  `docs/architecture/local-dev-troubleshooting.md`。
- 发布、里程碑规划或跨域能力盘点：在 M0 迁移期按需读取
  `docs/architecture/implementation-readiness.md`；普通局部实现、修复、测试、重构和 UI
  调整不读取该文件。后续状态入口迁移以 `docs/README.md` 为准。
- 人工文档或协作文本：读取 `docs/AGENTS.md` 与
  `docs/architecture/document-language-governance.md`；该规则覆盖仓库文档，以及
  GitHub/Linear 的 Issue、PR、评论和审核文本。
- 新增、修改或审核测试断言、golden/snapshot/digest、provider/lane 命名或测试删除：读取
  `docs/architecture/test-validity-governance.md`。
- 新建、修订或执行 Superpowers spec/plan：先读取 `docs/superpowers/AGENTS.md`。
- 用户可见页面或流程变更：按
  `docs/adr/0021-product-docs-information-architecture.md` 评估产品文档影响。
- GitHub/Linear 发布前：除非用户明确指定其他语言，先检查标题、正文、评论、Review、
  状态说明和验收结论中的自然语言是否为简体中文；代码、命令、路径、标识符、检查名、
  SHA、URL 和必要的英文引文按语言治理规则保留。

## 交付

1. 按最近指令和权威来源运行受影响门禁。
2. 只报告实际执行证据，并明确未运行项。
3. 区分本地测试、CI、真实运行、PR 合并和 tracker 完成。
