# 测试 provider、lane 与 trait 盘点及迁移模板

本文是 NERV-871 的 M 级治理盘点。快照基于 2026-08-18 的
`625ca49b5bef50a340657f333149bc58cc057a02`。它只记录当前事实、漂移和后续批次，
不修改测试、provider、trait、manifest、workflow 或共享脚本。

本盘点采用 PR #1662 的 provider 证明范围术语作为讨论参考，但不依赖该未合并 PR
中的文件。仓库当前证据格式、失败条件和可接受结论仍以
[测试证据治理](./test-evidence-governance.md)及现行 manifest、policy、runner 为准。

## 判定方法

每个测试身份必须分别核对以下四层。后一层不能由前一层推断：

1. **名称与 trait**：类名、方法名、注释和 `Trait` 只表达意图。
2. **policy 与 manifest**：`test-evidence-policy.json` 约束证据，lane manifest 选择身份；
   二者只证明路由合同，不证明本次已执行。
3. **实际 lane**：TRX/JSONL 中必须出现精确测试身份，并满足期望、发现、执行和结果计数。
4. **provider 与边界**：必须有本次 provider 身份、readiness、版本、进程边界和 cleanup
   证据。零执行、静默 `return`、skip 或依赖缺失均不能标为通过。

因此，`Postgres`、`Redis`、`CAP`、`Docker` 或 `FullChain` 出现在名称、trait、项目路径、
过滤器、manifest 或 lane 名中，都不能单独作为实际执行证明。

## 五类证明范围

| 类别 | 实际身份要求 | 可以证明 | 不允许宣称 |
| --- | --- | --- | --- |
| InMemory/fake | 明示内存 provider、fake/stub/no-op 和进程内边界 | 纯逻辑、状态转换、应用编排、替身分支 | SQL、migration、schema、数据库约束/事务/锁、真实 transport、跨进程恢复 |
| PostgreSQL | 实际 Npgsql 连接、数据库所有权、readiness/版本、精确测试结果与 cleanup | 本次覆盖的 migration、SQL、schema、约束、事务/锁/并发或重启行为 | 未执行身份、Redis/CAP、真实服务拓扑或未覆盖场景 |
| Redis | 实际 Redis endpoint、readiness/版本、精确身份和 cleanup | 本次覆盖的 Redis 数据结构、锁、幂等或重启行为 | CAP transport、PostgreSQL 隔离、HTTP/full chain |
| CAP | 明示实际 transport；InMemory 与 RabbitMQ/Redis 等 transport 分开记录 | 本次 transport 下的 publish/consume/outbox/inbox/retry/DLQ 行为 | 由 `cap-*` trait 推断真实 broker，或把进程内消息替身称为跨进程链路 |
| FullChain | manifest 精确身份、public entrypoint、实际服务拓扑与全部依赖的 readiness/result/cleanup | 该入口、拓扑和依赖下的端到端场景 | 仅 dotnet + 单 provider、进程内 handler/context，或 manifest 外场景 |

## 当前可复核运行基线

同一主干 SHA 的 GitHub Actions run
[`32104975404`](https://github.com/Mang-X/Nerv-IIP/actions/runs/32104975404) 已完成且 CI Summary
为 success。下表只把对应 artifact 的结果归给实际被选中的身份。

| lane | manifest 选中 | 实际结果 | provider/readiness | cleanup | 允许结论 |
| --- | ---: | --- | --- | --- | --- |
| `postgres` | 14 个 member | expected=discovered=passed=73，failed=skipped=0 | PostgreSQL 18.6，readiness passed | passed | 只证明这 73 个精确身份的 PostgreSQL 行为 |
| `redis-cap` | 1 个 member、2 个 identity | expected=discovered=passed=2，failed=skipped=0 | PostgreSQL 18.6、Redis 8.10.0，二者 readiness passed | passed | 只证明 DemandPlanning 的 2 个精确 Redis/CAP 身份 |
| `full-chain` | 5 个 member | expected=discovered=passed=5，failed=skipped=0 | PostgreSQL 18.6、Redis 8.10.0，二者 readiness passed | passed | 按各 member 的 entrypoint/dependency 边界分别解释，不把项目其余测试算入 |

`backend-shard-*` 的绿色结果只证明测试方法返回了 passed。若方法在缺依赖时直接
`return`，该结果是需要治理的假阳性，不是 provider 证据。

## 漂移清单

| ID | 固定项目/身份 | 名称、trait 与 routing | 实际 provider、进程与 lane 证据 | 缺口与禁止结论 | 后续票 |
| --- | --- | --- | --- | --- | --- |
| INV-001 | `Nerv.IIP.Iam.Web.Tests`；`IamPostgresProfileTests.cs` 中读取 `NERV_IIP_TEST_POSTGRES` 的 10 个 `[Fact]` | 方法名含 `Postgres`；无 provider trait；PostgreSQL manifest/policy 无 IAM member | 有连接时为进程内 Npgsql/EF PostgreSQL；无变量时直接 `return`。run `32104975404` 的 `backend-shard-2` 仍以约 1.6–4.3 ms 记为 passed | fast lane passed 不能证明 PostgreSQL；也不能把同文件的纯 automigrate 治理测试算作 provider 测试 | NERV-876 |
| INV-002 | `Nerv.IIP.Notification.Web.Tests`；1 个 PostgreSQL profile 与 3 个 CAP 测试 | CAP traits 为 `cap-inmemory`、`cap-rabbitmq`、`cap-rabbitmq-dlq`；独立脚本有过滤意图；hosted PostgreSQL/Redis-CAP manifest/policy 均无这 4 个 identity | profile 使用进程内 Npgsql/EF；CAP 分别使用 PostgreSQL+CAP InMemory 或 PostgreSQL+RabbitMQ。依赖不可达时直接 `return`；`backend-shard-2` 仍以约 226/3.9/4.5/7.2 ms 记为 passed | trait/脚本过滤不能证明实际 broker；不能宣称 PostgreSQL、RabbitMQ transport、retry 或 DLQ 已执行 | NERV-877 |
| INV-003 | `Nerv.IIP.Ops.Web.Tests`；`Postgres_store_persists_task_attempt_and_audit_records` | 名称含 `Postgres`；无 provider trait；PostgreSQL manifest/policy 无 Ops member | 有变量时为进程内 Npgsql/EF PostgreSQL；无变量时直接 `return`；`backend-shard-2` 以 3.25 ms 记为 passed | 不能由方法名或 fast passed 宣称 PostgreSQL 持久化已执行 | NERV-878 |
| INV-004 | `Nerv.IIP.Business.Scheduling.Web.Tests`；`SchedulingListPlansPostgresProfileTests.cs` 的 2 个 `[Fact]` | 名称/注释称 PostgreSQL，注释把 `return` 称为 skipped；项目已有 PostgreSQL member，但 expected identities 不含这 2 项 | 有变量时为进程内 Npgsql/EF PostgreSQL；无变量时直接 `return`；`backend-shard-1` 仍以约 4.4/10.4 ms 记为 passed | 不能借同项目其他 PostgreSQL identity 的 artifact；当前结果也不是可观测 skip | NERV-879 |
| INV-005 | `Nerv.IIP.FileStorage.Web.Tests`；`FileStoragePostgreSqlServiceTests.cs` 的 22 个 `[Fact]` | 类名及部分方法名含 `PostgreSql`；无 provider trait；fast shard 执行。另有 `FileStorageRestartPersistenceTests` 由 PostgreSQL lane 归责 | 被测类型为 `PostgreSqlFileStorageService`，但 fixture 的 `CreateDbContext` 使用 `UseInMemoryDatabase`；进程内执行 | 可证明实现级逻辑/状态，不可证明 SQL、migration、constraint、transaction 或真实 PostgreSQL。不得借 restart identity 的证据扩张 | NERV-880 |
| INV-006 | `Nerv.IIP.Business.FullChain.Tests`；manifest 外 10 个 `[Fact]`，以及 `erp-return-closure` 1 项 | 整个项目从 fast shard 排除；full-chain manifest/policy 只选择 5 个 identity。`erp-return-closure` 被路由到 full-chain | manifest 外 10 项没有 hosted 实际 lane 结果。`erp-return-closure` 本次确实在 full-chain lane 执行，但 entrypoint=`dotnet`、PostgreSQL=true、Redis=false、externalProcesses=false；测试使用进程内 EF contexts、InMemory dead-letter store 与 no-op mediator | 项目名/排除规则不能证明 10 项执行；`erp-return-closure` 只能证明其 PostgreSQL context 行为，不能宣称真实服务拓扑、public entrypoint 或 Redis full chain | NERV-881 |

## 固定测试身份

INV-001 的 10 项为：

- `Fresh_Postgres_has_case_insensitive_user_unique_indexes`
- `Postgres_profile_seeds_admin_and_persists_login_refresh_replay_logout_and_connector_validation`
- `Postgres_refresh_token_rotation_consumes_token_once_and_replay_revokes_token_family`
- `Postgres_refresh_replay_after_consumption_commit_revokes_rotated_session`
- `Postgres_refresh_token_rotation_rolls_back_when_rotated_session_insert_fails`
- `Postgres_login_lockout_blocks_attempts_and_success_resets_failed_state`
- `Postgres_profile_persists_user_create_update_and_disable_commands`
- `Postgres_profile_persists_role_mutation_permission_catalog_and_password_reset`
- `Postgres_user_lifecycle_and_password_policy_use_ef_persistence`
- `Postgres_profile_issues_external_client_token_and_authorizes_with_grants`

INV-002 的 4 项为：

- `PostgreSQL_profile_places_migrations_history_in_notification_schema_when_database_is_available`
- `PostgreSQL_cap_outbox_with_inmemory_messaging_delivers_operation_failed_event_to_notification_consumer`
- `PostgreSQL_cap_outbox_with_rabbitmq_messaging_delivers_operation_failed_event_to_notification_consumer`
- `Rabbitmq_handler_exception_dead_letters_after_retry_threshold_and_continues_consuming`

INV-003 为 `Postgres_store_persists_task_attempt_and_audit_records`。INV-004 为：

- `Postgres_list_marks_invalidated_plan_with_latest_reason`
- `Postgres_list_breaks_exact_timestamp_ties_deterministically_by_id`

INV-005 固定为 `FileStoragePostgreSqlServiceTests.cs` 当前 22 个 `[Fact]`。后续实施必须在票面
保留基线方法清单；新增身份不自动进入该批次。

INV-006 中未被 manifest 选择的 10 项为：

- `MaintenanceLifecycleDockerAcceptanceTests.cs` 当前 7 个 `[Fact]`
- `MaintenancePublicHttpLifecycleAcceptanceTests.Alarm_report_walks_the_public_gateway_and_real_maintenance_http_chain_to_closed_readback`
- `MaintenanceLifecycleDockerCleanupTests.Run_identity_remains_unique_when_created_concurrently`
- `MaintenanceLifecycleDockerCleanupTests.Cleanup_attempts_every_owned_resource_and_residue_scan_when_failures_occur`

另含已选择但边界漂移的
`ErpReturnClosurePostgresAcceptanceTests.Purchase_return_and_sales_rma_close_through_real_postgres_contexts_with_replay_safety`。

## 分批迁移模板

每个后续票必须在 NERV-679 下建立，并填写以下字段；一个服务或一个测试项目一票、
scope 为 S/M、单 PR。不得用一票实施全仓迁移。

```markdown
## Scope-Gate

Scope：S/M｜固定服务或测试项目、文件和测试身份；单 PR。
难度：说明 provider、进程边界和 evidence lane 的判断难点。

## 固定范围

- 项目、文件、精确测试 identity 或封闭的基线清单：
- 当前名称/trait/policy/manifest：
- 实际 provider、进程边界和运行证据：
- 允许与禁止宣称：

## 依赖

- blocked by NERV-866；related to NERV-871 的 INV-xxx。
- 如需 workflow、共享 manifest/policy/runner/脚本，先另过 Scope-Gate，不夹入本票。

## 验收

- [ ] 缺身份/readiness/结果/cleanup、零执行或 skip 时 fail closed。
- [ ] 名称、trait、manifest、实际 lane 与 provider 身份逐项一致。
- [ ] 保存可观察失败证据，再验证修复通过。
- [ ] 只改固定项目内必要文件；单 PR；中文协作文本；准确列出未运行项。
```

本次已创建 NERV-876、NERV-877、NERV-878、NERV-879、NERV-880、NERV-881。
六票均为 `scope:M`、父票 NERV-679、blocked by NERV-866、related to NERV-871；
NERV-679 保持未关闭。任何共享 CI/治理改动仍须独立 Scope-Gate。

## 复核命令

以下命令从名称/trait、manifest/policy 和实际 artifact 三层分别取证：

```bash
rg -n 'Trait\(|FactAttribute|NERV_IIP_TEST_POSTGRES|UseInMemoryDatabase|return;' backend --glob '*Tests.cs'
jq -r '.members[] | [.id,.project,.filter,.status] | @tsv' scripts/postgres-test-lane.json scripts/redis-cap-test-lane.json scripts/full-chain-test-lane.json
rg -n 'IamPostgresProfile|NotificationCapOutbox|NotificationPostgresProfile|OpsPostgresProfile|SchedulingListPlansPostgresProfile|FileStoragePostgreSqlService|MaintenanceLifecycleDocker|MaintenancePublicHttpLifecycle|ErpReturnClosure' scripts/test-evidence-policy.json scripts/*-test-lane.json scripts/backend-test-shards.json
gh run view 32104975404 --json status,conclusion,headSha,url,jobs
gh run download 32104975404 -n postgres-dependency-summary-32104975404-1
gh run download 32104975404 -n redis-cap-dependency-summary-32104975404-1
gh run download 32104975404 -n full-chain-dependency-summary-32104975404-1
```

下载后的 summary 必须核对 `selectedMemberIds`、`expected`、`discovered`、`passed`、
`failed`、`skipped`、`readiness`、provider 版本与 `cleanup`；不能只看 workflow 绿色状态。
