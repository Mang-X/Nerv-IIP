# NERV-183 定时性能回归门禁设计

## 目标与范围

为现有业务性能基线增加独立的 GitHub Actions 定时通道。该通道每天运行一次，也允许人工触发；它使用 disposable PostgreSQL 18 service container，调用现有受治理入口 `scripts/verify-business-performance-baseline.ps1`，超过阈值时自然失败，并保留 JSONL 指标与 summary JSON 供下载和后续趋势比较。

本任务为 Scope M、单 PR。它不进入每个 PR 的 required CI，不修改业务实现、数据库 schema、业务 HTTP endpoint、OpenAPI、generated client 或前端，也不替代 NERV-423/NERV-688 管理的全量 `*PostgresProfileTests` 真实依赖通道。

## 方案选择

采用“单次 nightly + 多次人工校准 + 可配置失败探针”。scheduled run 使用仓库中已校准的固定分场景阈值；`workflow_dispatch` 暴露仅用于验证的全局阈值输入，使维护者可用 `1 ms` 明确证明超阈值会让 job 失败，而不需要提交故意变慢的业务代码。

首版不在单个 job 内重复运行并计算中位数。现有脚本一次运行即可为 Inventory、MES、ERP 各输出一条指标；先用同一 GitHub hosted runner 通道进行至少三次人工运行，取观测最大值并保留足够裕度，能够以较小复杂度建立初始门禁。若后续实际 nightly 数据仍显示频繁误报，再由独立任务引入统计窗口或中位数聚合。

## Workflow 结构

新增 `.github/workflows/nightly-business-performance.yml`：

- 触发器只有每天一次的 `schedule` 与 `workflow_dispatch`，不响应 `pull_request` 或普通 `push`；
- 权限保持 `contents: read`；
- job 和每个显式 step 均设置 `timeout-minutes`；
- `services.postgres` 使用 `postgres:18`，通过 `POSTGRES_USER`、`POSTGRES_PASSWORD`、`POSTGRES_DB` 初始化 disposable 数据库，并配置 `pg_isready` health check；
- 使用 `actions/checkout@v4`、`actions/setup-dotnet@v4`、`actions/cache@v4` 和 `actions/upload-artifact@v4`；
- job 级 `NERV_IIP_PERF_POSTGRES` 指向 service container 暴露的本机端口；
- PowerShell step 调用 `scripts/verify-business-performance-baseline.ps1`，场景为 `all`、profile 为 `nightly`、输出路径固定在 `artifacts/business-performance/nightly/`；
- 正常 scheduled run 使用分场景阈值。人工触发若提供全局阈值，则用它覆盖分场景阈值，专门服务于失败探针；
- artifact step 使用 `if: always()`，仅上传 `metrics.jsonl` 与 `summary.json`，缺文件时失败，不上传 TRX、stdout、stderr、数据库内容或连接串，保留 30 天；
- 不使用 `continue-on-error`、`|| true` 或其他失败吞噬手段。

## 可执行合同与 TDD

新增 `scripts/tests/nightly-business-performance-workflow.Tests.ps1`，从真实 workflow 读取结构并用临时副本执行削弱变异。测试先于 workflow 编写并先观察到缺文件/缺合同的预期红灯，再实现最小 workflow 使其转绿。

合同覆盖：

1. 仅存在 `schedule` 与 `workflow_dispatch`，cron、只读权限、job/step timeout 固定；
2. PostgreSQL 18 service、health check 与 `NERV_IIP_PERF_POSTGRES` 必须存在；
3. 必须调用受治理性能脚本，使用 `Scenario all`、`Profile nightly`、显式 JSONL/summary 路径与非零阈值；
4. artifact 必须 `if: always()`、`if-no-files-found: error`、唯一 run/attempt 名称、30 天留存，且仅上传两份脱敏文件；
5. 禁止 `continue-on-error`、`|| true`、直接 `dotnet`/`docker` 业务执行；
6. 对移除连接串、把阈值改为 0、删除 `if: always()`、放宽缺文件策略和吞掉失败等削弱分别做变异，确保合同会红。

该测试接入现有 `.github/workflows/ci.yml` 的 Script Governance job，使以后修改 nightly workflow 的 PR 会执行合同，但不会运行慢性能场景。

## 阈值校准与真实验收

workflow 首次推送后，从该分支连续人工触发至少三次正常运行。记录每个 run 的 Inventory、MES、ERP `elapsedMilliseconds`，以同一通道的观测最大值为基线，并设置足以覆盖 hosted runner 抖动的整数阈值；阈值不得为 0。校准值和 run URL 写入 PR 描述与 Linear 验收评论，但运行 artifact 不提交仓库。

随后人工触发一次 `1 ms` 全局阈值失败探针，要求：

- job conclusion 为 failure；
- summary 的 `passed` 为 `false` 且 `violations` 非空；
- `metrics.jsonl` 与 `summary.json` artifact 仍可下载。

最后以正式阈值再触发一次成功运行，确认 PostgreSQL 已真实执行、三个场景指标齐全、summary `passed=true`、artifact 可下载。只有该最终正常运行成功后才把 PR 标为可审核；远端运行状态与本地合同测试分别报告，不互相替代。

## 文档与失败处理

更新 `docs/architecture/implementation-readiness.md` 第 47 条附近，记录 nightly 通道、artifact、阈值来源和 NERV-423/NERV-688 边界。产品文档 `frontend/apps/docs` 无影响。

若 Docker、本地 PostgreSQL 或 GitHub Actions 暂时不可用，应明确报告环境阻塞；不得把未执行写成通过。若三次 hosted-runner 指标差异过大而无法设置合理固定阈值，停止收紧阈值并回到设计阶段评估多次中位数，不以 0 阈值制造假绿。
