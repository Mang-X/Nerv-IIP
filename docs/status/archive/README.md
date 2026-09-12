# 状态历史快照

本目录保存已经冻结的时点记录。文件用于追溯当时判断，不构成当前实现、项目进度或发布裁决。

本 README 是可维护的目录索引；新增日期化快照或历史类别时可以更新本页。下面列出的快照文件本身禁止原地修改：

- [`vertical-slices/`](vertical-slices/)：第一至第四阶段纵切形成过程与历史验收口径。
- [`implementation-readiness-2026-08-26.md`](implementation-readiness-2026-08-26.md)：与迁移前原文件使用同一 Git blob。
- [`backend-bootstrap-plan.md`](backend-bootstrap-plan.md)：M2-J 迁移前的后端启动与首批实施阶段计划，与旧 Architecture 文件使用同一 Git blob。
- `project-status-dashboard-2026-05-26.html`：2026 年 5 月生成并更新的非实时看板。

调查、实验、审计与修复记录不放在状态目录，统一从 [`../../reports/README.md`](../../reports/README.md) 进入。

需要纠正历史结论时，在当前文档、Issue 或新的日期化报告中说明，不改写旧快照。

## 退役 readiness 入口的追溯

`docs/architecture/implementation-readiness.md` 的兼容路由已退出工作树。当前状态使用 [`../current.md`](../current.md)，迁移前总账使用上方冻结快照；旧兼容路由本身可从 [M4 前固定提交](https://github.com/Mang-X/Nerv-IIP/blob/ed492f406ae6d9fa72c4d5c63205f69c490ab2a1/docs/architecture/implementation-readiness.md) 追溯。

冻结 ADR、Report、Status archive 和 Superpowers 记录中的旧路径描述属于当时上下文，不重新解释为当前事实。回看旧记录时使用其记录时点的 Git 树；需要完整 readiness 正文时使用本目录快照，而不是重新建立一个当前兼容状态页。
