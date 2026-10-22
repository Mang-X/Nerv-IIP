# Reference 目录规则

本目录是当前**人工查询索引/解释层**，不是运行时事实数据库。

1. 修改任何 Reference 前先核对其 producer；代码、配置、公开契约、生成物、migration、seed、脚本帮助和测试优先于本文。
2. Reference 只保留当前目录、字段、码表、矩阵、术语与资料链接；项目进度、Issue 完成情况、时间线、事故过程和审计结论进入 GitHub/Linear 或 `docs/reports/`。
3. 稳定的维护/分类规则属于 `docs/governance/`；可执行步骤属于 `docs/runbooks/`；不要把它们重新并回 Reference。
4. 精确依赖版本、镜像 tag、端口和动态能力状态应链接 manifest/lockfile/config/CLI producer，不在人工索引重复抄写。
5. 不为 Reference 建永久 registry、同步脚本、自然语言 checker 或独立 CI step；机器输入/生成物必须跟随原 producer 原子维护。
6. M2 兼容 shim 只负责旧 URL 导航，不复制 Reference 正文；删除条件交 M2-M/M4。
