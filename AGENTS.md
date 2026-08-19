# AGENTS.md — Nerv-IIP 平台

本文件是共享代理入口；详细规则按目标路径最近的代理指令和本仓库权威文档按需加载。

## 开工

1. 任何变更前读取 `docs/architecture/implementation-readiness.md`，核实当前阶段、
   已交付能力与环境前置条件。
2. 修改文件前读取从仓库根到目标路径的全部 `AGENTS.md` 和
   `AGENTS.override.md`；最近文件扩展或覆盖父级指令。
3. 命令、版本、目录与生成入口以当前仓库文件、配置、脚本和帮助输出为事实源。

## 跨域触发

- 服务边界、目录或跨域调整：读取 `docs/architecture/repo-layout.md` 与
  `docs/architecture/context-map.md`。
- 本地启动、Aspire/fullstack 运行或排障：读取
  `docs/architecture/local-dev-troubleshooting.md`。
- 人工文档或协作文本：读取 `docs/architecture/document-language-governance.md`；该规则覆盖仓库文档，以及 GitHub/Linear 的 Issue、PR、评论和审核文本。
- 新增、修改或审核测试断言、golden/snapshot/digest、provider/lane 命名或测试删除：读取 `docs/architecture/test-validity-governance.md`。
- GitHub/Linear 发布前：除非用户明确指定其他语言，先检查标题、正文、评论、Review、状态说明和验收结论中的自然语言是否为简体中文；代码、命令、路径、标识符、检查名、SHA、URL 和必要的英文引文按语言治理规则保留。
- 新建、修订或执行 Superpowers spec/plan：先读取 `docs/superpowers/AGENTS.md`。
- 用户可见页面或流程变更：按
  `docs/adr/0021-product-docs-information-architecture.md` 评估产品文档影响。

## 交付

1. 按最近指令和权威来源运行受影响门禁。
2. 只报告实际执行证据，并明确未运行项。
3. 区分本地测试、CI、真实运行、PR 合并和 tracker 完成。
