# 真实依赖测试操作 Runbook

规则见 [`../../governance/testing/real-dependency-lanes.md`](../../governance/testing/real-dependency-lanes.md)。当前 lane/member/scenario 参数以 `scripts/*-test-lane.json`、acceptance matrix、runner 脚本帮助与 workflow 为准。

## 本地前置

- PostgreSQL 基础变量指向受治理的管理入口；每个测试/runner 只创建并删除自己拥有的临时数据库/schema。
- Redis/CAP 使用当前 session/run 唯一 namespace/version；不要 `FLUSHALL`、删除未知 key 或抢占其它 worktree consumer group。
- 多 worktree 共享本地依赖时，不停止共享服务来“清理”一个会话；只回收本次 invocation 明确拥有的资源。

## 执行

1. 从 manifest/runner help 确认目标 lane/member/scenario 与所需环境变量。
2. 先做 protocol/readiness probe；依赖不可达时停止并修复环境，不把被选中真实依赖测试改成 skip。
3. 使用现有 runner 执行，保留自然退出码。
4. 失败时先采集脱敏的数据库/Redis/CAP/业务状态和 cleanup 证据，再精确回收当前 run 的数据库、namespace 和子进程。
5. runner 中断后的残留只用当前受治理 cleanup 脚本预览/清理；具体命名、最小年龄和安全条件以脚本帮助为准。

## 判读

只有实际 job/lane 的 passed/failed 运行时结果能证明该真实依赖执行过；合同测试、planning、stable aggregate 或另一个 provider 的成功都不能替代。
