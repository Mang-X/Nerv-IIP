# Scheduling 订单紧急度快照保留与恢复 Runbook

本页承载受控配置、演练、恢复、迁移与发布操作。架构所有权与安全不变量见 [`../architecture/business/scheduling-order-urgency-retention.md`](../architecture/business/scheduling-order-urgency-retention.md)。精确参数、route、migration 名称和脚本行为发生变化时，以当前配置、代码、迁移和脚本帮助为准并同步修正文档。

## 启用前检查

1. 保留 worker 默认保持禁用；只启用明确的 `organizationId + environmentId` scope，禁止通配范围。
2. FileStorage 合规 bucket 必须预先存在并启用版本控制；使用对象存储法律保全时还必须具备对象锁能力。服务不得为了通过启动而创建或降级合规 bucket。
3. 先部署所需数据库 migration 和 FileStorage/Scheduling 代码，再启用 scope。
4. 首次演练不要注入任何删除授权：只验证归档、精确 versionId、SHA-256、长度及数据库批次证据。

## 配置纪律

当前配置入口为 `OrderUrgencyRetention`。在线/总保留窗口、批次大小、归档载荷上限和法律保全等值以部署环境配置为准；任何示例值都不能替代实际配置审查。

删除授权必须短时、可审计且只用于一次受控运行。不得保存常设 `SourceDeletionAuthorization`；只有精确归档版本已超过总保留期且销毁明确获批时才允许提供独立 `ArchiveDeletionAuthorization`。运行完成并核对审计后移除临时授权。

## 首次启用/扩容顺序

1. 在功能仍禁用时应用当前 Scheduling retention 相关 migration。
2. 验证合规 bucket、版本控制/对象锁要求以及 FileStorage 连接。
3. 启用一个小 scope，不提供删除授权；确认归档对象的 key、versionId、hash、size 与 Scheduling evidence 一致。
4. 注入短时源删除授权，对一个小批次执行；确认仍在窗口内以及每个订单最新快照均未删除。
5. 观察数据库延迟、对象存储延迟、eligible backlog 与 oldest eligible age，再逐步调整 batch/size；不得把调大 batch 当作错误恢复手段。

## 恢复

恢复必须经当前内部 Scheduling restore 契约执行，不得手工复制对象或直接插入数据库记录。请求使用当前认证主体或受信任的内部 actor 传递方式；actor 不能由任意请求体伪造。

恢复完成前核验：批次 ID、精确对象版本、哈希/长度、scope 信封、恢复数量、恢复审计，以及应用读取路径确实可见新水合快照。幂等重放也应留下可解释审计。

## 容量与并发验证

PostgreSQL lane 的 `scheduling-postgres-profile` 成员包含 `OrderUrgencyRetentionPostgresCapacityTests`。按[真实依赖测试操作](testing/real-dependencies.md#postgresql-入口)准备管理连接，并为每次运行提供唯一数据库后缀、结果目录及摘要路径。

```powershell
$run = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$output = "artifacts/scheduling-postgres/$run"
pwsh scripts/ci/run-postgres-test-lane.ps1 -MemberId scheduling-postgres-profile -DatabaseSuffix $run -ResultsDirectory "$output/results" -SummaryPath "$output/summary.json"
```

该命令执行成员登记的全部 Scheduling PostgreSQL 测试。核对摘要中的用例身份、执行结果与清理状态。
如需保留容量详情，可在本次调用的进程环境中将 `NERV_IIP_URGENCY_RETENTION_EVIDENCE` 设为独立 JSON 文件路径。

## 停止条件

出现任一条件立即停止破坏性步骤并保留源快照：scope 配置无效、法律保全生效、删除授权缺失/过期、FileStorage/MinIO 不可用、bucket 版本控制不满足、对象证据不完整或 hash/length 不一致、租约争用/丢失、数据库 fencing/revision 冲突。

## 回滚

先禁用全部受影响 scope 并移除删除授权，再评估代码回滚。已经存在归档/恢复审计或对象版本时默认保留这些证据；未经记录治理明确授权，不运行会销毁审计证据的 migration `Down`。需要恢复数据时走正式 restore 契约，而不是回写原批次终态。
