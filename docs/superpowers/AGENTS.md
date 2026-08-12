# Superpowers 规划治理

- **新 spec/plan：** 以对应 GitHub Issue 为唯一权威。
- **开工票面：** 持久化 spec 前创建或复用 GitHub Issue，并记录 Scope Gate 级别、交付形态、难度与主要难点。
- **spec：** 保留 Issue 既有正文，只写入唯一 `<!-- superpowers-spec:start -->` / `<!-- superpowers-spec:end -->` 受管区块；批准状态、修订号和日期保存在区块内。
- **本地新 spec/plan 文件：** 各自只含一个永久 Markdown 链接。
- **历史文件：** 既有 `docs/superpowers/specs/` 与 `docs/superpowers/plans/` 文件不迁移、不改写。
- **plan：** 使用一个索引评论和逐 Task 独立评论；索引包含目标、全局约束、Task 顺序、完成复选框和各 Task 永久链接。
- **实施后修订：** 不覆盖历史 Task；新增修订与替代评论，并更新索引。
- **大范围：** Scope L/XL 按 Scope Gate 拆成子 Issue；一个 Task 不承载多个独立 PR。
- **执行前：** 用 `gh issue view` 回读；Issue、标记、索引或链接任一异常时立即停止，且不得把完整正文回填本地链接文件。
- **证据：** 测试、CI、审核、PR 与合并证据和计划正文分离。
- **同步边界：** 不直接创建 Linear 同步票，不修改 `.agents/skills/` 或 `skills-lock.json`。
- **同步异常：** GitHub → Linear 同步延迟或失败只报告，不阻塞 GitHub spec/plan 流程。
