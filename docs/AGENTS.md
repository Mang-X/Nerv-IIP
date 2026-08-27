# 文档目录路由

修改 `docs/` 下的人工文档前，先从 `docs/README.md` 确认内容类型与权威住所，并读取 `docs/architecture/document-language-governance.md`。

- **当前状态：** 只在 `docs/status/current.md` 维护全仓级重点、阻塞、统一入口和少量跨域注意事项；完成项直接删除，不增加历史章节。
- **历史状态：** `docs/status/archive/README.md` 是可维护的目录索引；目录内日期化快照为冻结资产，禁止原地修订。发现错误时在当前文档或新的日期化报告中说明。
- **旧兼容入口：** `docs/architecture/implementation-readiness.md` 只负责导航，不得重新添加项目状态、实现说明、事故过程或裁决。
- **机器输入或生成物：** 按 `docs/architecture/document-language-governance.md` 的权威分类表处理，不因所在目录宽泛而跳过分类。
- **ADR：** 新增或修订前读取 `docs/adr/README.md` 与 `docs/architecture/decision-record-governance.md`。
- **当前架构：** 先读 `docs/architecture/README.md`，只描述当前组件、边界、事实所有权、依赖方向和交互，不混入项目进度或事故过程。
- **任务进度与证据：** 留在 GitHub/Linear 和对应 PR，不复制为仓库长期总账。

不得为状态页新增生成脚本、永久 manifest、自然语言 checker 或独立 CI step。
