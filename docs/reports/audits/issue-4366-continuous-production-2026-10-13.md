# #4366 连续生产旅程证据候选（2026-10-13）

本报告提交真实连续 UI 与公开终态的证据候选，充分性由独立审核裁定；不自称父验收通过。生产代码零修改，没有新契约、会计政策或恢复机制。成本“差额即漏账”的误判及等待机器费用门控建议已撤回，不开生产修复票。

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

未捕获公开voucher明细/GL余额、独立DB SELECT/提交审计；SQL不是公共readback，subscriber不独自代证金额。累计减聚合wipCleared为0不是GL1405余额0。旧session已删除，不能回填；没有新栈/fixture拼链，不把公共inventory数量响应冒充计价响应。

## 未跑与清理

material lot消耗/冲销未跑：materialLotId=null、consumedMaterialLots=[]、materialCost0。NCR本轮仅sort-and-screen，返工/报废/退供应商/让步未跑；QA矩阵无审批read/start不证明全局无模板。新复检、跨午夜、硬件/打印/PDA未跑。标准人工差异numeric_scale_out_of_range/unavailable不算完整标准差异成功。原时点缺费率死信补费率replay本轮未跑，停服恢复不代该专门支线；获批票面要求成本等待恢复但未指定必须故障缺费率，充分性由独立审核裁定。

启动实际 `python3 /tmp/nerv4366-r3-start.py`→`pwsh -NoProfile -File nerv.ps1 fullstack start -Scenario issue-1912-real-machine-walkthrough`，exit0；ERP `aspire resource business-erp stop/start`及`aspire wait business-erp --status healthy --timeout 120`自然0。原脚本自然exit1/25成立1失败；修正归档命令见README，非仓库gate/新runtime。

官方 `pwsh -NoProfile -File nerv.ps1 fullstack stop -SessionId nerv-0d43-e0363f`自然0，UTC05:11:18.262225/Stopped/remaining=[]/errors=[]；精确容器/卷/网络空。自有preview关闭，launcher exit0、observer Ctrl-C exit130且58822不监听，三个口令精确扫描0 hit并删除私有文件（不称全面秘密审计）。本轮没有新资源；原树/分支及旧两轮归档保留。候选影响计划、本地门禁、PR CI和独立审核在PR分列，不改写runtime身份或父验收状态。
