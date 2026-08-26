# 文档目录路由

修改 `docs/` 下的人工文档前，先从 `docs/README.md` 确认该内容的类型与权威住所，并读取 `docs/architecture/document-language-governance.md`。

- **机器输入或生成物分类：** 先读 `docs/architecture/document-language-governance.md` 中的权威分类表，不以宽泛目录排除人工文档。
- **新增或修订 ADR、判断决策与当前实现的分工：** 先读 `docs/adr/README.md` 与 `docs/architecture/decision-record-governance.md`。
- **新增或修订当前架构：** 先读 `docs/architecture/README.md`，只描述当前组件、边界、事实所有权、依赖方向和交互，不混入项目进度或事故过程。
- **发布、里程碑或跨域能力盘点：** M0 迁移期间才按需读取 `docs/architecture/implementation-readiness.md`；普通文档和代码任务不读取它。

## implementation-readiness 冻结

自 [GitHub #2288](https://github.com/Mang-X/Nerv-IIP/issues/2288) 的 M0 阶段起，`docs/architecture/implementation-readiness.md` 停止接收新的功能完成日志、Issue/PR 级实施说明、事故过程、focused gate 明细和历史增量段落。

迁移期间只允许修正会直接误导当前发布、迁移或运行入口的严重错误；当前任务进度与验收证据留在 GitHub/Linear 和对应 PR。完整状态总账的归档与兼容入口由 M1 独立处理。
