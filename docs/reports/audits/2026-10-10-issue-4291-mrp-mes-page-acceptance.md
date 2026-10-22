# #4291 MPS → MRP → MES 真实页面闭环（2026-10-10）

本记录仅证明 [#4291](https://github.com/Mang-X/Nerv-IIP/issues/4291) 在下述版本、输入和隔离栈中成立。父 [#4273 唯一获批 spec r2](https://github.com/Mang-X/Nerv-IIP/issues/4273) 的最终组合验收，以及 #4272 最小权限多角色验收，均未在此完成。既有路径首次业务复验通过，本 PR 仅提交必要证据，未修改产品代码、契约、Schema、forecast 政策或测试框架。

## 固定版本与运行边界

- 实际运行 SHA：`2851a4c5079974ab9b684fc6036dcb171ab4e9df`，包含 #4289 的正式 migration。证据提交后的 PR head 由 PR 正文记录；本记录不将文档提交 SHA 冒充已运行产品版本。
- 工作树：`/Users/mang/.t3/worktrees/Nerv-IIP/issue-4291-mrp-mes-acceptance`。
- 成功业务会话：`nerv-4291-261011`，真实 PostgreSQL 18、Redis 8/CAP，各会话专属动态端口与卷；admin / `org-001` / `env-dev`，不是生产或最小权限账号。
- Business Console：`http://localhost:58000`；Gateway：`http://localhost:57996`；BusinessGateway：`http://localhost:58002`。
- 启动沿当前 `fullstack start` 和 `issue-1912-real-machine-walkthrough` 最小种子入口；world/history/scale 关闭。首次会话完成构建；成功会话使用同 SHA 的 `-NoBuild`，由持续存活的 PTY 保持启动器与 guardian 生命周期。

```powershell
./nerv.ps1 fullstack start -SessionId nerv-4291-261011 -Scenario issue-1912-real-machine-walkthrough -NoBuild
./nerv.ps1 fullstack url business-console -SessionId nerv-4291-261011
```

T3 `preview_status` 返回无 tab，`preview_open` 连续三次返回 `The calling provider no longer owns an active thread run.`，因此使用仓库已存在的 `@playwright/test` Chromium 机制操作真实 DOM。没有路由拦截、响应 mock、API fixture 写入或预先建立 MES 工单。登录口令仅从本机 `0600` 文件读入，不留存在证据中；本次临时操作 driver 不作为新增框架提交。

## 页面动作与权威终态

输入：`SF-ROD-01`（活塞杆 φ20×380）、`SITE-001`、`2 pcs`、计划周期 `2026-10-20`，MRP 窗口 `2026-10-10 ~ 2026-11-09`。

| 步骤 | 真实页面动作 | 权威结果 |
| --- | --- | --- |
| 创建 MPS | `/planning` → 新建 MPS → 目录选 SKU、工厂、单位 → 数量 2 → 日历选 10 月 20 日 → 保存主计划行 | `01a1263e-03f4-70f1-9355-5ca51e404078`，`Draft` |
| 评审 / 发布 | MPS 主计划 tab → 评审 → 发布 | 同一 MPS `Reviewed → Released`，公开命令及刷新读面均返回原 SKU、工厂、数量、单位、日期 |
| 运行 MRP | 页面运行 MRP → 使用上述窗口 → 提交 → 等待页面计算完成 | `01a1263e-78b8-73b4-ade7-05a2fc646327`，`Created → Running → Completed`；1 个 MPS 输入、2 条建议；无 failureReason、无输入降级 |
| 查看建议 / 来源 | MRP 运行 → 查看追溯 → 计划建议 | 生产建议 `01a1263e-7bad-76c3-8f9c-812428a94efe` 为 `Open`，`SF-ROD-01 / 2 pcs / 2026-10-20`；pegging 的 `sourceType=mps`，来源为上述 MPS |
| 采纳生产建议 | 仅该生产建议行 → 接受 | 公开命令返回 `BusinessMes / WorkOrder / WO-20261010-000001`；建议刷新为 `Accepted`。采购建议 `RM-BAR-01 / 2.8 kg` 未采纳 |
| MES 权威读回 | `/mes/work-orders` → 点击 `WO-20261010-000001` → 工单详情 | 工单已创建；产品与数量相同；来源建议 ID 与 `MPS:<mpsId>` 完整一致；真实工序 10/20/30 均已生成 |

MES 数据库中的真实聚合 ID 为 `01a1263e-d45b-7571-9f4b-35d08142d064`，工单号为 `WO-20261010-000001`；`sku_id=SF-ROD-01`、`quantity=2`、`uom_code=pcs`、`status=created`。`source_document_id` 是本次生产建议强 ID，`source_demand_reference` 和 `source_demand_references` 均包含 `MPS:01a1263e-03f4-70f1-9355-5ca51e404078`。MES 与 pegging 的生产版本引用相同：`01a1263b-7ae8-72d5-a64f-efb5e2313c3d`。

CAP received 的 `PlanningSuggestionAcceptedIntegrationEvent` 为 `Succeeded`、重试 0；MES inbox 的 `business-mes.demand-planning-suggestion-accepted` 记录关联同一建议的 idempotency key。上述 transport 证据只辅助归因，业务完成依据仍是 MES 公开详情和持久化工单读回。

MRP 公开查询与 PostgreSQL 均完整读回 141 字符来源，超过历史 128 字符限制，未截断：

```text
inventory-http:2;erp-purchase-orders:0;accepted-purchases:0;mes-work-orders:0;master-data-planning-parameters:2;master-data-uom-conversions:0
```

ProductEngineering 来源为 `product-engineering-http:2;product-engineering-http:0`；`inputSources=[mps]`、输入覆盖 `2026-10-20 ~ 2026-10-20`、`hasInputDegradation=false`、`inputDegradationSources=[]`。来源计数 0 表示本轮对应适配器没有输入，不代表适配器未调用。

## 可审证据

[结构化证据](assets/2026-10-10-issue-4291/evidence.json)作为冻结报告的精选附件，保留固定输入、页面产生的公开 HTTP method/path/status 与闭合业务字段投影、数据库白名单字段、CAP/inbox 关联及会话 ownership。不提交运行 manifest/log/原始 artifact，不留存授权头、token、口令、连接串或任意原始请求/响应正文。相同读面重复结果已去重，保留状态变化。

- [MPS 已发布](assets/2026-10-10-issue-4291/screenshots/02-mps-released.png)
- [MRP 成功与来源追溯](assets/2026-10-10-issue-4291/screenshots/03-mrp-completed.png)
- [采纳前生产建议](assets/2026-10-10-issue-4291/screenshots/04-production-suggestion-open.png)
- [已接受建议及 MES 工单状态](assets/2026-10-10-issue-4291/screenshots/05-production-suggestion-accepted.png)
- [MES 工单来源、数量和三道工序](assets/2026-10-10-issue-4291/screenshots/07-mes-source-detail.png)

数据库读回只做 SELECT：`demand_planning.mrp_runs` 的固定 run ID/状态/完整来源/长度；`mes.work_orders` 的聚合 ID/工单号/产品/数量/单位/来源；`mes.processed_integration_events` 与 `cap.received` 的目标事件 identity/status。命令通过本会话 PostgreSQL 容器内既有环境取得认证，未输出凭据。

## 清理与未运行项

首次启动会话 `nerv-4291-261010` 健康后，启动命令所在 exec 生命周期结束，后续页面访问 `ERR_CONNECTION_REFUSED`，Aspire 已无 AppHost、会话容器已消失。该会话未写业务数据；执行 `./nerv.ps1 fullstack stop -SessionId nerv-4291-261010` 得到 `state=Stopped remaining=0`，只回收其四个专属卷。

成功会话 `nerv-4291-261011` 的栈、单据和卷按派单要求保留供独立审核与父验收复用，最终 stop/清理尚未执行，不能声称清理通过。ownership 的容器 ID 与卷名见结构化证据；退出审核/验收后由本实施席位或明确接手者执行 `./nerv.ps1 fullstack stop -SessionId nerv-4291-261011` 并回读 `Stopped / remaining=0`。不停止未知资源或其它任务容器。

本票没有修复产品代码，所以未重跑后端单测、PostgreSQL 回归、独立 Redis/CAP lane 或正式 FullChain v1 Authority；本次真实页面运行不是这些自动化 lane 的通过证明。证据文档受影响门禁与 PR exact-head CI 结果分别留在 PR。未运行专项已有库升级、Down、旧代码回退（父 spec r2 明确取消），未执行生产环境、多角色最小权限、物理领料/报工/排产和父 #4273 固定组合验收。

MES 页面已有紧急度“读取失败”和 MPS 来源没有销售关联提示，不影响本票已读回的工单来源、产品与数量；未把这些提示修成额外范围。工单仍为 `created`、原料缺料 2.8 kg、未释放/排程/开工，是本次采纳终点之后的真实状态，不声称完整制造执行完成。
