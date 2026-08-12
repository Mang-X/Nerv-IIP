# Superpowers 规划治理

- **新 spec/plan：** 以对应 GitHub Issue 为唯一权威。
- **spec：** 使用唯一受管标记区块；批准状态、修订号和日期保存在区块内。
- **本地新 spec/plan 文件：** 各自只含一个永久 Markdown 链接。
- **plan：** 使用一个索引评论和逐 Task 独立评论。
- **实施后修订：** 不覆盖历史 Task；新增修订与替代评论，并更新索引。
- **执行前：** 用 `gh issue view` 回读；Issue、标记、索引或链接异常时停止。
- **证据：** 测试、CI、审核、PR 与合并证据和计划正文分离。
- **同步边界：** 不直接创建 Linear 同步票，不修改 `.agents/skills/` 或 `skills-lock.json`。
