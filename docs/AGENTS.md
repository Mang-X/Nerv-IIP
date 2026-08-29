# 文档目录路由

修改 `docs/` 下的人工文档前，先从 `docs/README.md` 确认内容类型与权威住所，并读取 `docs/governance/docs/language.md`。

- **当前状态：** 只在 `docs/status/current.md` 维护全仓级重点、阻塞、统一入口和少量跨域注意事项；完成项直接删除，不增加历史章节。
- **Architecture：** 当前组件、边界、事实所有权、依赖方向和交互从 `docs/architecture/README.md` 路由；不混入项目进度、Product/Runbook/Reference/Governance 正文或历史报告。
- **Governance：** 当前必须遵守的工程规则从 `docs/governance/README.md` 路由；只写现态约束、适用范围、例外原则和 producer，不保存事故过程、阶段形成史或时点计数。
- **Product：** 当前产品、角色、IA、UX 与产品验收语义从 `docs/product/README.md` 路由；Product 不复制项目状态、CI run 或一次性调查证据。
- **Runbook：** 当前启动、部署、迁移、恢复和排障操作从 `docs/runbooks/README.md` 路由；命令事实以脚本、CLI help、配置与代码为权威，Runbook 不保存阶段历史。
- **Reference：** 当前 Schema 目录、码表、矩阵、权限导航、术语和技术资料从 `docs/reference/README.md` 路由；Reference 是 producer 的人工索引/解释层，不得成为独立运行时事实源。
- **历史状态：** `docs/status/archive/README.md` 是可维护的目录索引；目录内日期化快照和纵切历史正文为冻结资产，禁止原地修订。
- **冻结报告：** 调查、实验、审计与修复记录从 `docs/reports/README.md` 路由。报告正文只证明声明的时点和范围，完成后冻结；不得把其中的计数、命令或通过结果升级为当前事实。
- **旧兼容入口：** `docs/architecture/implementation-readiness.md` 以及 M2 明确登记的迁移兼容页只负责导航，不得重新添加 Architecture/Product/Runbook/Reference/Governance/Report 正文、项目状态、事故过程或实现总账。
- **机器输入或生成物：** 按 `docs/governance/docs/language.md` 的分类纪律处理，不因所在目录宽泛而跳过 consumer/producers；不得复制平行机器 registry。
- **ADR：** 新增或修订前读取 `docs/adr/README.md` 与 `docs/governance/decisions/records.md`。
- **任务进度与证据：** 留在 GitHub/Linear 和对应 PR，不复制为仓库长期总账。

不得为状态页、Product/Runbook/Reference/Governance 目录、报告目录或迁移分类新增永久 manifest、自然语言 checker、生成脚本或独立 CI step；已有机器契约迁移只做必要路径重定向，不借机改变规则语义。
