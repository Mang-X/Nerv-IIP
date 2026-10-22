# 数据库发布能力阶段形成历史

> Frozen by M2-D / #2396 on 2026-08-28.
> Source: `docs/architecture/database-release-runbook.md` at `main@15814789ae0ca36bf435e6c70c3ade22d6ffc2a4`.
> 本页只保存旧 Runbook 中的阶段形成叙事，不是当前发布步骤、支持矩阵或项目状态。

旧文档曾用“第五/六/七阶段”描述数据库发布基础逐步形成：

- **第五阶段**：验证 AppHub/Ops 可通过 migrations 从空 PostgreSQL 数据库建表；当时其 `__EFMigrationsHistory` 仍使用 provider 默认 schema。
- **第六阶段**：把 AppHub/Ops 的 schema 治理元数据和服务 schema 迁移历史配置固化为门禁；旧库升级需要把历史记录从 `public.__EFMigrationsHistory` 复制到 `apphub.__EFMigrationsHistory` / `ops.__EFMigrationsHistory`。
- **第七阶段**：补齐 IAM `iam` schema、初始 migration、seed/auth profile 验证和持久化登录基线。

M2-D 将仍有效的旧库升级条件重写为 [`../../runbooks/database-release.md`](../../runbooks/database-release.md) 中的**条件式前置检查**，不再要求操作者理解阶段编号。当前支持边界、命令参数和发布资格以当前 Runbook、脚本、migration manifest、代码与 GitHub/Linear 为准。
