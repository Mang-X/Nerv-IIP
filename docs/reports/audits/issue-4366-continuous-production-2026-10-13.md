# #4366 连续生产旅程证据候选（2026-10-13）

本报告提交真实连续 UI 与公开终态的证据候选，充分性由独立审核裁定；不自称父验收通过。R1 指出原会话缺同成品 Inventory movement 的公开计价 GET，下文追加独立 `f2d66acb` 会话的真实 UI 开场至同批公开计价见证。两次会话及强 ID 各自归属，不合称一次旅程。生产代码零修改，没有新契约、会计政策或恢复机制。成本“差额即漏账”的误判及等待机器费用门控建议已撤回，不开生产修复票。

## 规格、版本与证据身份

来源为 [#4288 正式批准 spec r1](https://github.com/Mang-X/Nerv-IIP/issues/4288)、[获批四票切片](https://github.com/Mang-X/Nerv-IIP/issues/4288#issuecomment-6123446945)及 [#4366 验收](https://github.com/Mang-X/Nerv-IIP/issues/4366)。User Story 7 要求“原计价时点的费率及会计期间有效，以便从完工等待继续恢复成本、完工登记和库存”；切片要求“成本与库存终态实际读回，HTTP/CAP processed 不代证”。Implementation Decisions 要求保留既有政策。本票没有首次单位成本完整或差额为零的要求。[现有产品语义](../../product/erp/design.md)明确资本化差额为累计减资本化金额。

实际 runtime：`9aa493c14028ba5984f4f61da7b47525406d5c63`，session `nerv-0d43-e0363f`；org-001/env-dev、SITE-001、Asia/Shanghai。旧 dda/session f2dbe9 和 15b/session 4a52ac 已停止清理，本轮从空 MPS 新开场，不拼为同一连续链。

候选前 fetch/ff 到 `7118379eac13ea6caf7bbb026d6da318b05b09a4`。9aa→711 的18文件为 OEE/设备/技能及 ERP 日期 UI、对应测试和产品文字；ERP 资本化/机器结算、MES 合同及 handovers 无改动。旧 runtime 仍归属9aa，不把候选门禁、日期 UI 或子票CI当新实跑；merge-SHA main CI 未验证。

环境：macOS27.0.1(26A434)、Nodev24.18.0、PowerShell7.6.4、.NET10.0.302、Aspire13.4.6；隔离 PostgreSQL18/Redis8。官方 walkthrough scenario，leader/world/history=false。T3 原生 preview、1280×800，无 Playwright/mock/路由拦截/假时钟。观察器只 clone 真实 fetch 回执到本机，不发送业务请求或修改输入；秘密不保留。

[单次证据与修正脚本说明](assets/2026-10-13-issue-4366/README.md)和 [原公开状态、895条网络及会计日志选集](assets/2026-10-13-issue-4366/evidence.json)。完整归档为 `/Users/mang/.t3/archives/Nerv-IIP/issue-4366-20261013/run-nerv-0d43-e0363f-continuous-r3/`：188文件、38,618,434字节，bundle SHA256 `ec4d61d18f510ff01837ae20813419818e5e2d6361f11220c1553f28c32ed5b4`。逐文件与tar成员字节核验；完整26截图、六服务日志及准备回执保留。

## 六主体与公开前提

六账号 journey4366-r3-岗位，均公开关联 active 员工 EMP-J4366-R3-岗位。principal、roleIds、permissionCodes、scope 在 evidence.json 原 roles/workers 响应：planner19/org、operator16/八工作中心、material7/org、quality11/org、finance7/org、handover7/八工作中心；不以 admin 执行业务。admin 只准备 IAM、技能班组、SOP、费率期间、既有检验方案/附件。

八工作中心 WC-TUB/ROD/GRD/VA/FA/CT/TS/PK-01；实际 EARLY 派工班组 TEAM-J4366-R3-WS-01。人工费率60CNY/h；机器固定1000、变动200、正常100h，12CNY/h；期间2026-10-J4366-R3公开open。没有预造生产/库存/质量/成本终态。工时是软件生命周期事实，不宣称硬件采集。

## 同一 session 连续 UI 和公开状态

| 阶段 | 主体、实际写入与公开终态 | 原证据成员 |
| --- | --- | --- |
| MPS | planner UI创建2pcs FG-QJ-P1-L；MPS `01a13385-6fbf-74dc-ad08-f2261bb13c75` Draft→Reviewed→Released，评审/发布为同planner | before-planning-*、01～03 |
| MRP/采纳 | MRP `01a13385-a839-72f7-9d0d-75eb6fbf80f1` Completed、inputSources=[mps]、无degradation、13建议；采纳FG建议 `01a13385-ab4a-7690-8755-027ed842c8a6`→WO-20261013-000001，productionVersion `01a13383-0de7-73d2-afcc-96ba4de5d44f`，sourcePlanReference指回同建议/MPS | 04～05 |
| 物料 | 11缺料，首MIR收料400 MATERIAL_SOURCE_LOCATION_UNAVAILABLE；公开仍Requested/received0。material UI正常源仓入库11笔，申请/收MIR...000001～000011，公开Received、请求=实际量、齐套Ready | 07、10 |
| 下达/排程 | planner UI release；下达与质量放行使方案失效，正常锁定重预览/发布。最终 `plan-01a1339d151971d2a7def3aaddfb4b17` released/isInvalidated=false，已完与加工中任务保留 | 完整UI回执/归档 |
| 执行 | operator八工序本人派工/开工/各报工2，PRPT...000001～000008。OP10～70先Completed，OP80先InProgress；最后同WO completedQuantity2、八任务Completed | 18、20、24 |
| 质量 | OP70加工中 QA UI正确SKU/工序。first-article方案与operation来源不匹配400，无检验记录；公开准备operation方案后UI检验 `01a13398-5f77-7f71-8a91-26c37668997b` passed/2 | 14 |
| NCR | QA UI缺陷DEF...000001/OP70/qty1→NCR `01a13399-8e5e-721b-8465-74de82bbd1ca`，同SKU；UI sort-and-screen并关闭。Quality closed、MES DispositionAccepted，ncrId=null按原值保留 | 15 |
| 未完交接 | WO started/计划2/完成0，OP80 InProgress。operator UI选择真实WO/OP、人工WIP2、确认未完2/0/started→SHO...000001；另一主体UI核对并接受，公开仍同WO/OP未完 | 18～19 |
| 完工批次 | 接班后 operator UI完OP80报工2→LOT-PRPT-20261013-000008，同OP80/WO、qty2；交接历史仍2/0/started，不被完工覆盖 | 20、24 |
| 成本恢复/入库 | ERP在六报工后正常停止；财务 UI同lot登记FGR...000001/qty2，先Requested/unitCost=null/posted0、无movement。ERP正常恢复后Posted/unitCost7.853628、库存2/remaining0、同lot与movement | 21～24 |

NCR附件 `file_01a1339b34777dbdb16daf4a3551e4e8` 是本次公开检验/NCR状态1898字节，SHA256 `82b9fe8a710ead59668b8cb7bc5a8a2da0581fb5c31c7de82cb791bd349ce631`，不是实物分选或新复检证明。

交班 outgoing=`user-01a13383d5207c0fbf8cc2e975f5bf8b`、incoming=`user-01a13383dc8b733795f53758036ba838`。创建UTC04:57:13.386/correlation `2ce6ce047f8b4056baf433774a7bad9c`；接班UTC04:59:30.503/correlation `86a097c5ab2f4ff38554a28fa2ca020b`。两个显式集合在Open→Accepted保持；incoming公开WO仍started/OP80 InProgress。后续产出lot创建05:00:13.12Z。

895条真实UI网络：813GET、82写（79×200、2×400、1×202），原请求/响应/correlation完整。两排程发布和接班合法空响应保存为原空字符串，不伪补正文。选错locator/关闭按钮超时仅重新读DOM或重载，不盲重放业务。辅助无resourceType查询500、错误ledger路径404及职责外403保留，不加宽权限；完整原回执仍可复核。

## 会计校准：差额有实际去向

公开ERP三次稳定：actualLaborCost13.805329、machine2.761064、material0、累计16.566393、资本化15.707256、差额0.859137；八项机器结算available。OP80/r1结算 `01a133a4-8b58-75ee-9e2e-a2022554089a`，source `evt-01a133a1f7fd7981a1fe44aee1fd9c7d`，金额恰0.859137。

真实SQL顺序：13:03:04.0031060+08首次资本化标志；13:03:04.0329890+08末机器结算；13:03:05.9746840+08 Inventory过账；13:03:07.0135570+08最终收货WOC凭证。此时用既有最终收货差异分支，不声称跑了已资本化后PostLateAdjustment/WOCADJ。

movement `01a133a4-92f2-7405-a3d5-0254c8db526a` 公共FGR/inventory-link同lot/qty2。Inventory原SQL写moving_average_unit_cost7.853628、inventory_value15.707256、movement_amount15.707256。ERP凭证 JV-20261013-000001/id `01a133a4-96e8-77c1-98e4-ce35499b89e4`，sourceTypeWOC/sourceNo同movement：

| 科目 | 借CNY | 贷CNY |
| --- | ---: | ---: |
| 1406-FINISHED-GOODS | 15.707256 | 0 |
| 5101-PRODUCTION-VARIANCE | 0.859137 | 0 |
| 1405-WIP | 0 | 16.566393 |

同批SQL写capitalizedQuantity2/capitalizedCost15.707256/wipClearedCost16.566393；对应StockMovementPosted subscriber成功结束13:03:07.0219730+08。inbox事件 `evt-01a133a492f9711bb35fa54f0e0d895a`/idempotency含FGR。无TraceId时按事件/强ID/时间关联，不捏造correlation。

借=贷；成品=单位成本×量；5101=公开差额=末工序费用；WIP贷/聚合清理=累计；Inventory同批计价一致。原零差额谓词是无spec来源的实施解释，失败数值真实但不能推出生产缺陷。原脚本/hash/25成立1失败完整日志保持；另存修正版本保留25个有依据事实，以八条金额及强ID关系替代错误谓词。本地实际33成立0失败只证明归档事实，whole4366Accepted=false、independentReviewRequired=true。

未捕获公开voucher明细/GL余额、独立DB SELECT/提交审计；SQL不是公共readback，subscriber不独自代证金额。累计减聚合wipCleared为0不是GL1405余额0。以上均为原 `9aa493c1` 会话的冻结事实；该 session 已删除，不能为它回填新的 GET。

## R1 补证：独立 UI 开场至同批 Inventory 公开计价

[R1 独立审核](https://github.com/Mang-X/Nerv-IIP/pull/4397#pullrequestreview-5499398733) 的 S1/E1 是同一个必需缺口：原 895 条网络中，最后的 Inventory movements GET 早于成品过账；原 SQL 金额和 MES FGR 单位成本不构成 **Inventory 自身的公开量价终态**。[#4288 获批 spec r1](https://github.com/Mang-X/Nerv-IIP/issues/4288)要求按操作文档走既有公开入口；[操作文档 §7 第 6 步](https://github.com/Mang-X/Nerv-IIP/blob/51a408a9c3a323a868d1cd6b31dbd36b239c99e2/docs/reports/audits/2026-10-10-mes-aps-ui-ux-operations.md#L81)要求核目标位置/批次数量和单位成本。现有 [Inventory movement GET producer](../../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Inventory/BusinessConsoleInventoryEndpoints.cs) 返回 `UnitCost`、`MovementAmount`，无需新契约。

本补证的实际运行身份是 `f2d66acbcba3b735f5cee14f48c7b02f92a4f743` / `nerv-0d43-b8f062`，隔离 PostgreSQL 18/Redis 8、org-001/env-dev、SITE-001、同官方 walkthrough scenario。`f2d66acb` 是原 PR 候选安全纳入当时 `origin/main=673293cdd95440e6e1fcc25bfda43f6160c0e646` 的本地 merge SHA；711→673 仅 maintenance 日期表单、测试与产品文档四文件，Inventory/MES/ERP/Gateway 相关 producer 无差异。这是新会话的固定 SHA，不改写 9aa 原证据。T3 原生 browser UI，六个最小岗位登录；admin 仅准备角色、员工/班组、SOP、费率期间和检验方案。72 条本链 UI 写回执为 71×200、1×202，逐条保存请求、响应、correlationId。真实栈读回、原始网络与四服务日志在[独立证据包](assets/2026-10-13-issue-4366/r4-inventory-public-evidence.json)；[核验脚本](assets/2026-10-13-issue-4366/verify-r4-inventory-public.py)单独处理本会话，不改原 25/1 脚本或会计校准。

| 必需路径 | 本会话 UI 写入与公开读回 |
| --- | --- |
| 开场 | MPS/MRP/建议公开空集；planner UI 建 2pcs FG-QJ-P1-L、评审/发布 MPS `01a133ec-ab85-73fb-b49c-187f34848a98`；MRP `01a133ed-a7b6-7103-b436-162206a31fe3` Completed、13 建议、MPS 输入无降级；UI 采纳 `01a133ed-afd9-7285-a395-6c677b3e411c` 生成 WO-20261013-000001，来源回指同 MPS。 |
| 齐套与工序 | material UI 11 笔正常入库、11 张 MIR 领收，公开 11/11 Received、Ready；planner UI 下达/发布，QA 放行引发真实排程失效后 UI 锁定重预览并发布 `plan-01a1340f22b377aab90058751cb46a11`，公开 released/isInvalidated=false。operator UI 本人派工、开工、八道各报工 2；OP70 检验 UI 通过 `01a1340b-d884-7f93-8a2b-0ed5caea12c0`，OP80 `PRPT-20261013-000008`，工单八道 Completed/总量 2。 |
| 同批入库等待 | OP80 的公开报工 GET 与产出批次 GET 均指 `LOT-PRPT-20261013-000008`、同 WO/OP80/qty2。ERP 在第六道之后正常停服；finance UI 以该 lot 登记 `FGR-20261013-000002`（requestId `01a13413-fecd-72df-8dd8-e56918079fdc`）。公开 Requested/unitCost=null/posted movement=null，Inventory GET 仅 33 条物料流水，无该 FGR movement。 |
| 恢复终态 | ERP 正常启动/healthy；公开 FGR Posted/posted2/remaining0/unitCost **7.415347**，inventory-link 同 lot/WO/movement。随后 **`GET /api/business-console/v1/inventory/movements?page=1&pageSize=50&organizationId=org-001&environmentId=env-dev`** 返回第 34 条成品流水 `01a13414-ed2e-7362-acbf-c7582c7c8ee9`：sourceService `business-mes`、sourceDocumentId 同 FGR、lineId 同 WO、sku FG-QJ-P1-L、lot 同 OP80、SITE-001/loc-fg-01、inbound qty **2**、`unitCost` **7.415347**、`movementAmount` **14.830694**。公开 availability 同 lot/location onHand2/available2。 |

原始公开 Inventory GET 在 UTC 07:05:26.773（correlation `4281c6e0e8dd4cbcabbdf29956c78e3f`）仍 33 条且无该 FGR；07:05:57.275（`307d1c86d9c94ec6b42dc31953350abd`）返回上述第 34 条完整量价。FGR 公开 Requested→Posted 的 correlation 为 `0c30b105bf0d48ae95d5049c210ef373`→`8f3ae8b74b0d4edaa8b971aec93e8c35`；OP80 UI 报工 `5130093ac9d04d21b9f929a15878b4e8`，finance UI 登记 `a3f0bc61ca314571b525c4a9f134b4ca`。ERP WorkOrderCompleted 资本化、MES WorkOrderCostCapitalized、InventoryMovementRequested 各 subscriber 成功日志和 Inventory 同 FGR 幂等键/lot/7.415347/14.830694 的实际写入日志归档。公开 ERP 同 WO 累计14.830693、资本化14.830694、差额-0.000001；`14.830693=14.830694-0.000001`，成品金额 `7.415347×2=14.830694`。微额舍入差不被改成零政策。

本会话前段还尝试过仅作前提的直接 PG WO/lot fixture `WO-J4366-R4-COST-001`；它的 FGR-20261013-000001 因缺真实 ERP 资本化来源保持 Requested，Inventory 返回 `erp-capitalization-provenance-not-observed`，没有成品 movement。该失败和 fixture SQL 留在原始归档，**不计主生产链**，没有伪造 ERP processed 状态。上述证据只使用之后从空 MPS 真实 UI 产生的 WO/FGR-20261013-000002。同一新会话没有重跑 NCR 处置和双主体交接；它们仍只由原 `9aa493c1` 全链证明，不能将两次强 ID 说成同一次。充分性留给独立审核；本报告/脚本均保持 `whole4366Accepted=false`。

Finance 角色的公开 `inventory/movements` GET 有 `business.inventory.ledger.read`，而库存移动页面要求 `business.inventory.movements.create`；本次由 finance 公共 GET 核量价、material 角色页面实际展示同 FGR/lot/qty2，不加宽 finance 权限。页面未显示计价列，公开字段由原始 GET 回执证明。`/finished-goods-receipt-requests/{requestNo}` 的辅助 GET 404，改用既有列表 GET；availability 首次漏 `uomCode` 返回 400，按 producer 补 `pcs` 后 200。均保留原回执，没有重放登记。

本次真实核验命令 `python3 docs/reports/audits/assets/2026-10-13-issue-4366/verify-r4-inventory-public.py --output artifacts/issue-4366-r4-inventory-public/r4-verification-result.json` 为 **51 PASS/0 FAIL**（32 项归档源字节 hash、19 项业务/清理关系），五个错误变异（单位成本、金额、lot、WO lineId、数量）各 exit1；这是独立 runtime 的归档事实核验，非新 head 仓库 gate。完整原始归档 `/Users/mang/.t3/archives/Nerv-IIP/issue-4366-20261013/run-nerv-0d43-b8f062-inventory-r4/`，135 文件、34,660,468 字节逐项与 tar 成员字节核验0异常，tar SHA256 `e933379c9496b687b817207a0ff5177c9207cefb925ba4a5540998ea4fe23fa3`。六角色与全部 UI 网络、前后公共回执、事件/服务日志、八张本链截图和失败 fixture 诊断在该归档。原 188 文件/25/1/会计校准字节不修改。

官方 fullstack stop 实际返回 `nerv-0d43-b8f062 state=Stopped remaining=0`；manifest cleanup errors=[]，自有容器/卷/网络读回空，observer 52293 无监听、launcher exit0、preview 关闭；三个私有口令值归档前精确扫描0 hit后删除口令文件。公开 voucher 详情、GL 余额、独立 DB SELECT 未取证；本会话不声称这些为票面新增门槛。新会话未跑的 NCR/交接不计作本会话通过，原 9aa 未跑 material lot、其它 NCR、PDA/硬件等边界保持。

## 未跑与清理

material lot消耗/冲销未跑：materialLotId=null、consumedMaterialLots=[]、materialCost0。NCR本轮仅sort-and-screen，返工/报废/退供应商/让步未跑；QA矩阵无审批read/start不证明全局无模板。新复检、跨午夜、硬件/打印/PDA未跑。标准人工差异numeric_scale_out_of_range/unavailable不算完整标准差异成功。原时点缺费率死信补费率replay本轮未跑，停服恢复不代该专门支线；获批票面要求成本等待恢复但未指定必须故障缺费率，充分性由独立审核裁定。

启动实际 `python3 /tmp/nerv4366-r3-start.py`→`pwsh -NoProfile -File nerv.ps1 fullstack start -Scenario issue-1912-real-machine-walkthrough`，exit0；ERP `aspire resource business-erp stop/start`及`aspire wait business-erp --status healthy --timeout 120`自然0。原脚本自然exit1/25成立1失败；修正归档命令见README，非仓库gate/新runtime。

原 `9aa493c1` 会话的官方 `pwsh -NoProfile -File nerv.ps1 fullstack stop -SessionId nerv-0d43-e0363f`自然0，UTC05:11:18.262225/Stopped/remaining=[]/errors=[]；精确容器/卷/网络空。原会话 preview 关闭，launcher exit0、observer Ctrl-C exit130且58822不监听，三个口令精确扫描0 hit并删除私有文件（不称全面秘密审计）。R1 新会话的独立清理见上节；原树/分支及旧两轮归档保留。候选影响计划、本地门禁、PR CI和独立审核在PR分列，不改写runtime身份或父验收状态。
