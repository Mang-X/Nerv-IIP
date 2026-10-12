# 最小权限连续旅程准备核验（2026-10-12）

本报告对应 [#4365](https://github.com/Mang-X/Nerv-IIP/issues/4365) 与 [#4288 spec r1](https://github.com/Mang-X/Nerv-IIP/issues/4288)。只交付前提准备及公开能力缺口；生产、排程和父级组合验收由后续切片执行。

## 版本与环境

- 真栈代码：`fb1f0da6432e7fa0aff46bcc977c655bb180c087`，从当时最新 `origin/main` 起票分支；本 PR 仅增加报告，不修改运行代码、seed、权限模型或契约。
- 有效会话：`nerv-38e3-b4c543`，`org-001/env-dev`、`SITE-001`、`Asia/Shanghai`；独立 PostgreSQL 18 / Redis 8、session 专属卷，非生产环境。
- macOS `27.0.1`（`26A434`），.NET `10.0.302`，PowerShell `7.6.4`，Aspire `13.4.6`，Node `v24.18.0`，本机 pnpm `12.11.2`（仓库声明 `11.22.0`，本次未改 lockfile）。Docker Server `29.8.2`。
- 受治理入口：`pwsh -NoProfile -File nerv.ps1 fullstack start -Scenario issue-1912-real-machine-walkthrough`；保留 launcher 至交互完成。显式关闭 `NERV_IIP_LEADER_DEMO`、`NERV_IIP_LEADER_DEMO_WORLD`、`NERV_IIP_LEADER_DEMO_HISTORY`，使用 walkthrough 最小投影。
- 首次会话 `nerv-38e3-72cb19` 在 launcher 返回后 AppHost 已退出，页面 `ERR_CONNECTION_REFUSED`；它不算运行证据，已由 `fullstack stop` 清理至 `Stopped remaining=0`。第二次使用 `-NoBuild` 与保持存活的自有 launcher，经实际登录/公开请求核实有效。

## 可重复准备顺序

使用现有公开入口，依序执行。以下账号、文件和费率仅为隔离验收数据；不向共享开发库或生产库写入。命令的口令由本地秘密输入，登录响应的 access/refresh token 不留存。

1. 从上述 SHA 启动 walkthrough 隔离栈；用 `fullstack url` 获取本次 gateway/business-gateway/business-console URL，端口不可复用旧记录。准备身份通过 PlatformGateway `POST /api/console/v1/auth/login` 登录，检查 `organizationId/environmentId`。其 admin 身份只维护前提，不执行业务验收。
2. `GET /api/console/v1/iam/roles` 读取既有 `role-production-planner` 权限；用 `POST /api/console/v1/iam/roles` 创建下表角色（`roleName`、`permissionCodes`），`POST /api/console/v1/iam/users` 创建对应 `journey4365-<岗位>`（`loginName`、合成 `.invalid` 邮箱、临时 `password`）。保存返回 `roleId/userId`，不可重用本报告的生成 ID。
3. `PUT /api/console/v1/iam/users/{userId}/membership` 传 `{ "roleIds": ["本次 roleId"] }`。范围使用既有 IAM 公共维护 API `PATCH /api/iam/v1/roles/{roleId}/data-scopes`，请求为 `{ "dataScopes": [{"scopeType":"organization","scopeCode":"org-001"}] }`，现场/交接改为下面八个 `work-center` scope。PlatformGateway 当前无该 facade，使用本次 IAM URL 的公开 `/api/iam`，不使用 internal token、数据库写入或新授权系统。
4. 每个账号本人登录 PlatformGateway，完成 `POST /api/console/v1/auth/change-password`（`currentPassword/newPassword`）；再次登录须 `passwordChangeRequired=false`。`GET /api/console/v1/auth/me` 对比完整 principal、roleIds、permissionCodes、org/env；不能只看登录 HTTP 成功。
5. 以下 BusinessGateway JSON 写请求均带 `organizationId=org-001`、`environmentId=env-dev`。`POST /master-data/workers` 建立 `EMP-J4365-<岗位大写>`，`userId` 必须为本次账号 ID，`employmentStatus=active`；计划/现场/物料/质检/财务/交接分别使用 `DEPT-PLAN/DEPT-PROD/DEPT-WH/DEPT-QA/DEPT-BIZ/DEPT-PROD`。这里及后文缩写路径均以 `/api/business-console/v1` 为前缀。
6. `POST /master-data/skills` 建 `SKILL-J4365-ASSEMBLY`（`skillName=旅程验收总装操作`、`groupName=production`、`requiresCertification=false`）；另用现有 `POST /master-data/reference-data` 添加相同 `code` 的 `codeSet=skill` 工厂自定义码。人员关系的校验读取该码表，不只读取技能目录。`POST /master-data/personnel-skills` 绑定现场账号，`level=intermediate`、`effectiveFrom=2026-10-01`、`effectiveTo=2026-12-31`。以 `/master-data/personnel-skills/matrix` 和 `/master-data/workers` 公开读回，不能以创建响应默认 `active` 字段代替。
7. `POST /master-data/teams` 建 `TEAM-J4365`（`WS-02`）、`TEAM-J4365-WS-01`、`TEAM-J4365-WS-03`，都使用 `departmentCode=DEPT-PROD`、既有 `shiftCode=EARLY`。`POST /master-data/teams/{teamCode}/members` 绑定现场/交接账号，`effectiveFrom=2026-10-01`，交接为 `isLeader=true`。这样现场主体目录覆盖三车间，授权仍只有路线的八个工作中心。`/me/work-context?permissionCode=...&scopeKind=work-center&scopeId=WC-FA-01` 读回真实 grant/selectedScope；`WC-FA-02` 请求应 403。不要把目录候选范围当授权。
8. 公开读取 SKU、已发布路线及生产版本，用 `/engineering/production-versions/resolve` 输入 `skuCode=FG-QJ-P1-L`、`effectiveDate=2026-10-12`、`lotSize=10` 核实当前 V2。本票不创建工单、operationTask 或物料/产出批次终态。后续旅程可采用批次号 `J4365-MAIN`，但 materialLotId/producedLotId/workOrderId/operationTaskId 必须由真实业务返回后登记，当前没有这些强 ID。
9. 每道工序准备独立 UTF-8 `.txt` 验收指引，含产品、路线、工序和工作中心、前序/质量/设备/物料门禁、正常 UI 开始/报工与同 ID 读回规则，并声明非现场生产规程。`POST /files/sop-documents/upload-sessions` 传 `fileName/contentType=text/plain/expectedSizeBytes/checksum(SHA256)`；向返回 uploadUrl 发送 tus PATCH（`Tus-Resumable:1.0.0`、`Upload-Offset:0`、`Content-Type:application/offset+octet-stream`、org/env query），确认 offset 等于字节数，再 `POST .../{uploadSessionId}/complete`。随后 `/engineering/sops/publish` 传 `documentNumber=SOP-J4365-<operationCode>`、`revision=1`、工序、工作中心、路线/修订、`effectiveDate=2026-10-01` 和真实 fileId。文件不能跨多个 document 共用同 fileId+revision：现有工程文档唯一键限制该组合。`/engineering/sops/current` 按相同工序/工作中心/路线和 `asOfDate=2026-10-12` 读回 Published，再以现场账号 `GET /files/sop-documents/{fileId}/content` 核实字节和 SHA256。
10. 质检账号创建并启用首件方案 `IP-J4365-FIRST`：`POST /quality/inspection-plans`，`category=first-article`、`skuCode=FG-QJ-P1-L`、`workCenterId=WC-FA-01`，单个 characteristic 为 `VISUAL-J4365`，`method=visual`、`severity=major`、`required=true`、`samplingRule=all`、`characteristicType=attribute`；再 `POST /quality/inspection-plans/{inspectionPlanId}/activate`。不要给首件方案配置巡检 assignee/interval。读取方案须为 active；不创建检验结果或 NCR。
11. `/erp/finance/accounting-periods` POST 建 `2026-10-J4365`、`startDate=2026-10-01/endDate=2026-10-31`。每个路线工作中心调用 `/erp/finance/work-center-cost-rates` POST，`hourlyRate=60`、`currencyCode=CNY`、`effectiveFromUtc=2026-10-01T00:00:00Z`，结束为空；原时点 `2026-10-12T12:00:00Z` GET 应选 revision 1。机时费率 POST `/erp/finance/work-center-machine-overhead-rates`，同 periodCode，`applicability=applicable`、`fixedOverheadBudget=1000`、`variableOverheadBudget=200`、`normalCapacityMachineHours=100`、CNY。由财务账号 GET 回读 revision 1；这些数值仅为可复算验收输入（机时费率 12 CNY/h），不是外部报价或既有业务成本。期间 Open/日期公开读回缺口见后文，不能由费率 GET 代证。
12. 每个账号经真实 Business Console 登录页面进入下表页面。计划员打开“新建 MPS”可读取成品、工厂和单位候选，但取消而不保存；其余读取职责内页面与 API。退出本人会话；交接前用 `fullstack stop -SessionId <本次 session>` 停止自有资源，保留清理回执，不删除其他会话。

上述非幂等创建只运行一次；发现错误先公开读回已提交状态，再按既有入口补正，不盲重放。示例 `idempotencyKey` 可按 `j4365-<操作>-<实体编码>` 区分；新运行要在其隔离环境重新创建并保存新 ID，不把旧 evidence 自动升格。

## 本次身份与最小授权

组织级 scope 用于计划、物料、质量和财务的现有组织读写行为；现场和交接为八个工作中心的显式 grant。账号均无 IAM 管理权限、无平台管理员角色、无 internal 权限。准备阶段合成账号不代表真实员工的职业认证。

| 岗位 / 登录名后缀 | principalId | roleId | scope | 实际页面 |
| --- | --- | --- | --- | --- |
| `planner` | `user-01a12fee9fdc7360aee0349a16881218` | `role-01a12fee9fa270ab8623dc1e62875739` | org-001 | `/planning` |
| `operator` | `user-01a12feea0ee725d947d61c233c788ff` | `role-01a12feea0d77ff2b1658acaf5ccdc60` | 路线八工作中心 | `/mes/operation-tasks` |
| `material` | `user-01a12feea1c67bd1aeacb042da64345c` | `role-01a12feea1a47ecb9f64c54779cf0e97` | org-001 | `/mes/materials` |
| `quality` | `user-01a12feea28e78f1a6519d07b8a3ff8e` | `role-01a12feea27d703a8bb37ed9663ce883` | org-001 | `/quality/inspections` |
| `finance` | `user-01a12feea3387bf79c4d3ed7d8f41e1b` | `role-01a12feea3287d6a97296e237b064f3a` | org-001 | `/erp/finance/work-center-cost-rates` |
| `handover` | `user-01a12feea3f074789fb515ff9675af53` | `role-01a12feea3de726b86fc0f307522f6ad` | 路线八工作中心 | `/mes/handovers` |

完整 permissionCodes（prefix 保持真实机器标识）：

- `planner`：`business.masterdata.products.read`, `business.masterdata.resources.read`, `business.mes.work-orders.read`, `business.planning.demands.manage`, `business.planning.demands.read`, `business.planning.mps.manage`, `business.planning.mps.read`, `business.planning.mps.release`, `business.planning.mrp.read`, `business.planning.mrp.run`, `business.planning.suggestions.manage`, `business.scheduling.plans.manage`, `business.scheduling.plans.read`, `business.scheduling.plans.release`, `notifications.messages.read`, `notifications.tasks.read`。
- `operator`：`business.engineering.documents.read`, `business.engineering.standard-operations.read`, `business.masterdata.resources.read`, `business.mes.downtime.manage`, `business.mes.downtime.read`, `business.mes.foundation.read`, `business.mes.materials.read`, `business.mes.operations.manage`, `business.mes.operations.read`, `business.mes.quality.read`, `business.mes.reporting.read`, `business.mes.reporting.write`, `business.mes.work-orders.read`。
- `material`：`business.inventory.ledger.read`, `business.inventory.movements.create`, `business.masterdata.products.read`, `business.masterdata.resources.read`, `business.mes.materials.manage`, `business.mes.materials.read`, `business.mes.work-orders.read`。
- `quality`：`business.masterdata.products.read`, `business.masterdata.resources.read`, `business.mes.operations.read`, `business.mes.quality.read`, `business.mes.quality.write`, `business.mes.work-orders.read`, `business.quality.inspection-plans.manage`, `business.quality.inspection-records.create`, `business.quality.inspection-records.read`, `business.quality.ncr.manage`, `business.quality.ncr.read`。
- `finance`：`business.erp.finance.manage`, `business.erp.finance.read`, `business.inventory.ledger.read`, `business.masterdata.resources.read`, `business.mes.receipts.manage`, `business.mes.receipts.read`, `business.mes.work-orders.read`。
- `handover`：`business.masterdata.resources.read`, `business.mes.downtime.read`, `business.mes.handovers.manage`, `business.mes.handovers.read`, `business.mes.operations.read`, `business.mes.reporting.read`, `business.mes.work-orders.read`。

## 本次产品、工序与前提读回

当前生产版本 `01a12fed-3f5f-7428-8220-7f941a60e802`，MBOM `MBOM-FG-QJ-P1-L:2`，路线 `ROUTING-FG-QJ-P1-L:1`；按上述日期/批量公开 resolve 为 active。SKU `FG-QJ-P1-L` 公开读回 active。

| sequence | operationCode | workCenterCode | 本次 SOP fileId | 字节数 | SHA256 |
| --- | --- | --- | --- | ---: | --- |
| 10 | `OP-WB-CUT` | `WC-TUB-01` | `file_01a12ff26aa174808c1a4a39792c0848` | 2407 | `4f3f645883b34f462ded461124d070c07f738f19bf116b3f480b79ab17b586b6` |
| 20 | `OP-WB-CNC` | `WC-ROD-01` | `file_01a12ff2ccde74c593517d18e410afd4` | 346 | `3cdf19ed9f62e5aa5a00023a75f7037ad5425dda39a5016884fe59af9abea7d8` |
| 30 | `OP-WB-GRD` | `WC-GRD-01` | `file_01a12ff2cd3e7cf38bfa81241b6eab6a` | 342 | `60724e4a448fcbf83d8fa3c8bd170ac4c6851367fa7c0b0a2ee1047878ae8cb4` |
| 40 | `OP-WB-VLV` | `WC-VA-01` | `file_01a12ff2cd84781a9579ec18fcdc4ce2` | 347 | `a8e5b21904fe1ced83039848ded90d5b23fa2a88add6c1a90a64d47974491183` |
| 50 | `OP-WB-ASM` | `WC-FA-01` | `file_01a12ff2cdce7cc984ca5c9dea137f13` | 341 | `d51afb785fb047bdf53f5f5743ea882f9873891afdd396b2785aab97975f32b4` |
| 60 | `OP-WB-CTG` | `WC-CT-01` | `file_01a12ff2ce2d7e118ea27ced8df60cb1` | 347 | `613c92b7360abbeef49b689419bc1118c55228218f0f7c51ac6e488a8ccfe714` |
| 70 | `OP-WB-TST` | `WC-TS-01` | `file_01a12ff2ce747d8ea5a702223b052b7e` | 347 | `4d4b40e944b7961986b3b70731fba46bfc4200483ff0d2ffb1af46d665947d62` |
| 80 | `OP-WB-PKG` | `WC-PK-01` | `file_01a12ff2cebe74c3a6391007b445fdf8` | 341 | `418767a7d21463d5d08fd7476c4f1f8b1a08b0c44d31479fc49ec2784ce3ac9e` |

六名员工 active；三个早班组各含现场/交接账号（交接为组长），人员技能矩阵已读回有效期间。八份 SOP 均 Published 且匹配路线修订 1；现场账号可读全部文件。人工费率 revision 1 在选定原时点有效，机时费率在选定期间 revision 1 可读。首件方案 `IP-J4365-FIRST` 公开读回 active；没有检验结果。

## 准备度与缺口

| #4365 标准 | 本次结果 | 边界 |
| --- | --- | --- |
| 版本、可重复操作、org/env、产品和工序强 ID | 已记录 | 真实批次/工单/operationTask 尚未产生，交给连续旅程通过正常 UI 产生 |
| 各岗位真实登录及公开 principal/permission/scope | 已执行 | 六账号本人改密、API login/me 和真实登录页；现场/交接精确范围以及范围外 403 |
| 必要主数据公开核实有效，不预造终态 | 大部分成立，期间状态未核实 | SKU、生产版本、员工/技能/班组、SOP、费率、检验方案已公开读回；期间只有创建 ID 与存在性间接校验 |
| 缺公开能力记录精确缺口与 owner | 已记录 | 下述缺口不在本 PR 新造能力 |

1. **会计期间状态缺口**：BusinessGateway `/erp/finance/accounting-periods` 仅 POST，GET 返回 **405**；ERP `ErpSalesFinanceEndpoints.cs` 同样只有 open/close/reopen。Open 回执 `accountingPeriodId` 为 `01a12ff1-45ce-74e2-b603-c5c4e0946247`。配置机时费率只检查期间存在，不核实 Open 或日期。因此本报告不声称期间 Open/日期已公开读回；该必要前提保持未验收。owner 为 `Business.Erp` 的 `AccountingPeriodAggregate`、finance endpoints 与其 Gateway facade。单独 Scope Gate 时，若新增 Gateway/OpenAPI/codegen 合同，触发契约生成链升级规则，应先批准窄拆分；本票不扩为财务模型或全端角色改造。
2. **岗位目录未建模**：所有 `/me/work-context` 的 `issues` 保留 `position-master-not-modeled`，`resolutionStatus=incomplete`。现场/交接已取得八工作中心的真实 `authorizedScopes/selectedScope`；这不是完整岗位模型验证。owner 为 MasterData `GetPrincipalWorkContextQuery`；本旅程沿现有授权模型，未增加岗位体系。
3. **准备数据的适用期**：原时点、技能与期间用于 2026-10-12 十月旅程。后续组合版本或执行日期变化时，重新准备并读回受影响项；本票费率不能冒充旧工单原时点费率。当前一个总装技能关系不代表所有工序/真实硬件均已具备职业技能认证。

## 实际证据与未跑项

- 脱敏 evidence 位于本工作树 `artifacts/issue-4365/`：`roles.json`、六份 `context-*.json`、`workers-final.json`、`skill-final.json`、三个 TEAM 成员读面、`production-version-resolve.json`、`sops-verified.json`、`read-rate-*.json`、`read-machine-rate-*.json`、`quality-plans-final.json`、`verification.log/verification-summary.json` 和岗位 PNG。口令、token 与连接串不入包。
- 实际请求工具为本次本地一次性 Python HTTP 调用；准备操作序列见上文，不添加永久 seed/测试 runner。正式门禁及最终 head、PR CI 在 PR 正文记录，不将 baseline 真栈结果当新 SHA 的重新运行。本 PR diff 仅报告，运行代码、配置与依赖文件和 baseline 相同；后续父验收仍须固定最终组合 SHA。
- 本次不执行 MPS/MRP/工单生成、物料收发与消耗/冲销、开工报工、NCR 返工/报废、完工成本恢复、库存入账或交接终态；没有用 admin 代行这些业务动作。设备、打印、PDA、跨班次/午夜、父级组合验收均未运行。
- 公开读面的空计划/工序/领料/成本/交接列表只是未预造终态的背景，不作为连续业务通过证据。八份 SOP 与 active 检验方案是前提，不是质量/生产终态。
- 初始化遇到的错误已按真实公开合同纠正：班组使用 `EARLY`（不是 `SHIFT-EARLY`）；技能关系先补对应码表；SOP 使用独立 fileId；首件方案不配置巡检派发字段。没有修改实现来放宽这些约束。浏览器等待使用的文案不匹配超时、短时 preview reconnect 不作业务失败或通过证据；后续实际页面/公开读回分别记录。

资源停止回执与 evidence hash 在交接前归档；数据随 session 专属卷清理，下一席位按本报告在自身固定版本重建，不复用本次已停止的 runtime。
