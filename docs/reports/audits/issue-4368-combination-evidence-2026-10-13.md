# #4368 最终组合证据候选（2026-10-13）

本报告整合 [#4288 正式 spec r1](https://github.com/Mang-X/Nerv-IIP/issues/4288)及[批准四票方案](https://github.com/Mang-X/Nerv-IIP/issues/4288#issuecomment-6123446945)的三个已合并切片，交付 [#4368](https://github.com/Mang-X/Nerv-IIP/issues/4368) 的组合候选。Scope Gate 复核为 M：一树、一分支、一 PR，仅新增本时点报告；无生产能力、永久账本、证据框架或新验收政策。**本报告不是独立父验收；#4288 保持 OPEN，父规格的必需组合标准尚不勾选。**

## 固定组合与来源

候选组合 SHA 为 **`4a3b4a044e57ac9150c628fcc7b36ec506d4a123`**，也是本树起点和本轮 fetch 后的 origin/main。它包含全部前三切片和交班窄修复；本票合并前不能把它称为“全部四票已合并后的最终 SHA”。#4368 合并后独立父席位须另钉实际 merge/main SHA，核对本报告增量及后续变化，再作父验收；不追改旧 runtime 的 SHA。

| 子票 / PR | API 回读 | 合并 SHA | 职责与原报告 |
| --- | --- | --- | --- |
| #4365 / [#4369](https://github.com/Mang-X/Nerv-IIP/pull/4369) | CLOSED / MERGED | `14f873264cfa452bcf10747ec21a33ffabc4c4a2` | [最小角色和前提准备](minimum-role-journey-prerequisites-2026-10-12.md) |
| #4367 / [#4370](https://github.com/Mang-X/Nerv-IIP/pull/4370) | CLOSED / MERGED | `f402b31d9e1bd3a5786f22054e381f1dbfae4fbe` | [非空排程旅程](scheduling-nonempty-journey-2026-10-12.md) |
| #4391 / [#4392](https://github.com/Mang-X/Nerv-IIP/pull/4392) | CLOSED / MERGED | `9aa493c14028ba5984f4f61da7b47525406d5c63` | Console 交班真实对象、人工在制和未完明细；不代连续生产 |
| #4366 / [#4397](https://github.com/Mang-X/Nerv-IIP/pull/4397) | CLOSED / MERGED | `4a3b4a044e57ac9150c628fcc7b36ec506d4a123` | [连续生产与公开库存计价补证](issue-4366-continuous-production-2026-10-13.md) |

本轮实际通过 `git merge-base --is-ancestor <各 merge SHA> <组合 SHA>` 核验上述包含关系；关系证据不代替业务终态。原生 API `issues/4368/parent` 为 #4288；`dependencies/blocked_by` 为 #4365/#4366/#4367，均 closed；#4288 原生 children 为 #4365/#4366/#4367/#4368/#4391。完整 body/comments/labels、依赖、PR/CI 回执与等价 diff 保存在本轮归档，不以父摘要代规格。

[#4288 主线接续裁定](https://github.com/Mang-X/Nerv-IIP/issues/4288#issuecomment-6136053460)明确 #2691 新父范围另行交付、#3743 只取本旅程所需 ERP 成果。本票不代关闭 #2691、不将其它服务再扩为主线前置。

## 原运行身份及复用边界

所有真实业务证据仍只归各自原 SHA/session；数字相同的 WO/lot 名称在隔离数据库内不是跨会话同一实体。

| 来源 | 固定 runtime / session | 实际证明与限制 |
| --- | --- | --- |
| 准备 R1 | `fb1f0da6432e7fa0aff46bcc977c655bb180c087` / `nerv-38e3-b4c543` | 六主体真实登录、principal/权限/scope、员工/技能班组、八 SOP 等；不产生生产终态 |
| 准备 R2 | `e25aa18c100201ac5fb9e67fdf7168a36cca7391` / `nerv-38e3-e09306` | 财务/现场公开期间 Open、UTC 当日八工作中心 ERP Ready；只差报告的运行代码等价 |
| 准备同步补验 | `e62db3d8cbc4a64ea3a6a1d1b6f5ce6a9139f46a` / `nerv-38e3-9f6baa` | MES 注册变化后的新栈期间/准备度 15 passed；不是六岗位 UI 重跑，也不证明任意日期 |
| 排程 | `14f873264cfa452bcf10747ec21a33ffabc4c4a2` / `nerv-bcf2-e9ad2f` | 两真实工单、插单16工序、非空停机2 OP60、manualLock1/1、发布/撤销、跨 EARLY/MIDDLE 和八 MES 投影，公开核验14 passed |
| 连续生产原链 | `9aa493c14028ba5984f4f61da7b47525406d5c63` / `nerv-0d43-e0363f` | 空 MPS→MRP/采纳→11项补料/齐套→8工序→检验/NCR sort-and-screen→双主体未完交接→OP80 lot→ERP停服等待/恢复；原同成品 Inventory 最终公开计价 GET 缺失 |
| 独立计价补验 | `f2d66acbcba3b735f5cee14f48c7b02f92a4f743` / `nerv-0d43-b8f062` | 新空 MPS 真实 UI 链至同 OP80/lot/FGR/Inventory 的公开 qty2、unitCost7.415347、movementAmount14.830694；本会话未跑 NCR 处置/双主体交接 |

共同声明环境为 macOS27.0.1、.NET10.0.302、PowerShell7.6.4、Aspire13.4.6、Nodev24.18.0，隔离 PostgreSQL18/Redis8，org-001/env-dev、SITE-001、Asia/Shanghai；pnpm 版本差异见各报告（12.0.0/12.11.2），不宣称完全相同工具链。运行数据按公开入口各自重建，秘密不留存。原准备可重复顺序见 #4365 报告；生产原/新 roles/workers、权限、scope、SOP/费率/期间及强 ID 在各原选集内，不重用已删 session 的身份。

### 本轮实际等价检查与受影响项

本轮使用 `git diff --name-only <runtime> <组合>`、真实完整 diff 和 `git rev-parse <SHA>:<path>`；外部 `intake/*-equivalence.json` 保存路径及原/候选 tree/blob OID，`*-full.diff` 保存真实差异。该静态检查支持以下有限复用，不等于组合 runtime 执行：

- **f2d→组合：**共8路径，仅 #4366 报告、选集、截图和单次核验脚本；运行代码、配置、依赖无改动。公开计价补验保持 f2d 身份，无受影响业务路径需重复。
- **9aa→组合：**共43路径；生产增量仅 ERP、OEE、设备/技能、maintenance 日期展示及相关测试，另有报告资产。MES、ERP、Inventory、Quality 服务、两 Gateway、Contracts、infra、依赖锁、交接页和质量页 tree/blob 相同。新日期控件未改变费率提交的本地零点语义；原 60/r1 有效费率和新会话公开读回分别保留。NCR/双主体交接复用原链，不拼入新会话。[#4397 R2](https://github.com/Mang-X/Nerv-IIP/pull/4397#pullrequestreview-5500390783)已独立裁定子票补证充分，不能代本票或父验收。
- **排程14f→组合：**全仓131路径有变化，不能宣称整个仓库等价。Scheduling/MES/IAM/MasterData/ProductEngineering、PlatformGateway、Contracts、infra、backend依赖、scheduling/business-core/auth包、排程页、停机页、工单页和作业页保持相同 tree/blob。BusinessGateway 变化限报工/追溯 IAM 显示名，不改本旅程排程/停机/基础准备度 facade。api-client仅新增报工 `ReportedByDisplayName` 类型，不改排程合同或请求。UI仅 NvDatePicker 新增可选 locale 展示，未传 locale 仍返回原 ISO；停机使用既有 datetime-local 输入；排程页面与 scheduling 包未变。CAP helper 为新增显式死信重放入口，Scheduling/MES 注册未变，原正常 SchedulePlanReleased/Revoked 消费路径不调用新增入口。其它服务 replay、报工姓名与交班写入不改变本排程断言，因此复用14项原事实；不称新组合实际求解或硬件实跑。
- **准备到组合：**不宣称旧完整准备树与组合全等。新增质量/重放/交班/日期增量已经由后续 #4366 的固定新会话实际重建六岗位与所需前提，排程所需准备在其原栈独立重建。R1 `ready-with-gaps/incomplete` 和 `position-master-not-modeled` 不被覆盖为全岗位模型完成；组合执行日期改变时必须重新公开核有效期。

本票未启动新栈。现有缺项补验是 #4366 的新 f2d UI/Inventory 链；本轮没有发现再重复这些业务路径能增加信息的运行代码变更。组合证据复用的充分性仍交独立审核和父验收裁定，不自审。

## 母 spec 全部 User Stories 映射

“已取证”仅表示原运行有该范围证据，不勾选父验收或未跑分支。

| r1 标准 | 实际证据来源 / 结果 | 固定组合边界 |
| --- | --- | --- |
| US1 计划员 MPS/MRP/采纳 | #4366 两次独立空 MPS UI 开场，MPS Released、MRP Completed/inputSources=mps、13建议、采纳强 ID→WO | 原链9aa / 补链f2d分别成立 |
| US2 principal/角色/权限/org-env | #4365六岗位公开身份与范围；#4366两会话角色重新准备，本人业务写入 | 管理员只准备，旧主体不改属组合 |
| US3 员工/技能班组/工序/SOP | #4365前提公开核验；#4366本人八工序开工报工，实际准备独立重建 | 软件身份/工时，不证明职业认证或硬件采集 |
| US4 缺料/补料/齐套 | #4366原链首MIR源仓不足400后仍Requested/received0，正常UI入库11笔/领收11项→Ready；补链独立11/11 | 不代 material lot消耗/冲销 |
| US5 真实 SKU/WO/OP 的检验/NCR | 原链OP70 inspection passed/2；DEF/NCR同SKU/OP70/qty1，closed→MES DispositionAccepted | sort-and-screen；ncrId=null原样保留 |
| US6 返工/报废分别回读 | 本组未执行返工/报废，不能用原sort-and-screen或#4277旧父证明代证 | **未跑，不勾选必需失败/缺证项** |
| US7 原时点费率/期间、成本恢复/完工/库存 | 原链等待恢复及会计校准；补链finance UI FGR000002 Requested→Posted，同WO/OP80 lot/movement公开量价及ERP资本化 | 旧库存计价GET缺失不回填；新链未重跑NCR/交接 |
| US8 非空插单/停机/冻结/跨班 | #4367 insert16、impact2、manualLock1/1、显式撤销与跨EARLY/MIDDLE八投影 | 停机是WC-CT-01资源事实，WO/OP=null；不是物理停机或现场换班 |
| US9 产品/批次/工单/工序交接 | 原9aa交班时WO started/2/0、OP80 InProgress、WIP2；另一员工Accepted集合保持，随后OP80产lot | 交接不直接存批次；通过同WO/OP公开回链，不修改历史快照 |
| US10 material lot/午夜/设备/打印/PDA | 下表逐项保留未跑原因 | 无mock/模拟器/邻近场景替代 |
| US11 证据层次分列 | 下节区分本地、PR CI、merge、runtime、硬件、父验收 | 子票Closed/CI聚合绿不等于整组通过 |
| US12 请求/事件/截图/资源清理 | 各原报告和选集保存请求/响应/correlation/强ID、截图及stop；归档保持各session原属 | 缺TraceId不造关联，早期tail不足不声称全日志完整 |

## 未跑、失败与不可证明

| 分支 | 实际边界与原因 |
| --- | --- |
| material lot 消耗 / 冲销 | 未执行批次消耗及冲销动作；原报工 materialLotId=null/consumedMaterialLots=[]/materialCost0，补料齐套不代消耗政策。 |
| NCR 返工 / 报废 / 退供应商 / 让步 / 新复检 | 本组选择sort-and-screen，其余处置未执行、没有对应MES回传终态；QA矩阵缺审批read/start不证明全局无模板。本项按票面记录未跑，不把US6改成通过。 |
| 跨午夜 / 跨班现场生产 | 排程为未来计划求解/发布；没有等待未来午夜或执行缺料工单。原生产同日连续链不代午夜/现场换班。 |
| 设备 / 打印 / PDA / 真机 | 使用逻辑工作中心和软件UI，没有实体设备采集、物理停机、打印机或PDA操作；没有真机环境/回执。浏览器和软件工时不代硬件。 |
| 专门缺费率 dead-letter replay | 本轮使用既有 ERP 停服/恢复，未制造原时点缺费率再补费率replay；#4350旧证据不是本轮连续链。 |
| 标准差异 / 会计扩展 | numeric_scale_out_of_range/unavailable保留，不报完整标准差异通过。公开voucher详情、GL余额、独立DB SELECT未取证；聚合未清WIP0不代表GL余额0。 |
| 失败前提 fixture | 补链早期 PG WO/lot fixture 的FGR000001仍Requested/无成品movement、erp-capitalization-provenance-not-observed；排除主UI链，未伪造processed。 |
| 原错误零差额谓词 | 原25成立/1失败及脚本hash保留；会计校准33/0只核归档关系。差额0.859137由既有最终收货差异分支解释，不新增零差额/晚到机器费用门控政策。 |

[#4397 E2回应](https://github.com/Mang-X/Nerv-IIP/pull/4397#issuecomment-6140999048)限定早期14:31 Inventory subscriber只证物料handler；新FGR的15:05 SELECT/INSERT和07:05:57Z公开GET才证终态。没有TraceId时按实际强ID/时间关联，不以早期成功记录代证最终量价。

## 本地门禁、PR CI、合并、runtime、硬件与父验收

| 证据层 | 实際结果及身份 | 不能证明 |
| --- | --- | --- |
| 既有子票本地 | #4365 exact e62：期间补验15、tracked文档检查/docs build/diff；#4367 exact a203：文档检查/docs build/diff，runtime14f另有14事实；#4366 exact e53：影响计划/diff和脚本合同2/2，归档33/0与51/0；#4391 exact2d：页面35、check0errors63warnings、build含typecheck | 各旧head输出不作为#4368新head门禁；归档consumer不是新runtime或仓库业务gate |
| 既有PR CI | #4369 run38250534792/e62、#4370 run38261355336/a203、#4397 run38363690038/e53均completed/success；实际Impact/Script Governance，20jobs为6success/14policy-skipped。#4392 run38344617440/2d：32jobs，21success/11policy-skipped，前端实际分片执行 | backend/frontend成功aggregate不认证未选lane；provider/Redis-CAP/FullChain等跳过不报实跑 |
| 合并 | 上表四个merge API/ancestry已核 | merge不证明merge-SHA main CI或业务重跑；本票未watch/poll main CI |
| 真实栈/UI | 上表各原session；生产/NCR/交接和公开计价分开归属 | 不称同一个最终组合session，更不称生产环境 |
| 硬件 | 未跑 | 软件UI、mock或模拟器不代真机 |
| #4368本地/PR | 本票新增报告先git add，再当前正式影响计划及受影响文档门禁；精确head、命令、自然输出在PR与本轮归档 | 本文不预报未完成CI，未派子代理/自审/合并 |
| #4288独立父验收 | **未运行，保持OPEN**；待本票正常保护合并后由独立席位另钉最终SHA | 本候选、子票闭合、33/51事实核验不能代父验收；US6等缺证不勾选 |

## 归档、完整性与资源停止

原完整来源保持不变：

- #4365：`/Users/mang/.local/state/nerv-iip/evidence-archives/issue-4365-e25aa18c1-r1`（116）、`issue-4365-5837ea5d4-r2`（24）、`issue-4365-e62db3d8c-sync`（35）；closeout为`issue-4365-14f873264-closeout`。
- #4367：同目录`issue-4367-2cdcab9d9-candidate`（82）、`issue-4367-a20323c3a-r2`及`issue-4367-a20323c3a-merge`。这些数量/hash核验为原票证据，本报告不伪称本轮全包重核。
- #4366 原链：`/Users/mang/.t3/archives/Nerv-IIP/issue-4366-20261013/run-nerv-0d43-e0363f-continuous-r3/`，188文件/38,618,434字节，tar SHA256 `ec4d61d18f510ff01837ae20813419818e5e2d6361f11220c1553f28c32ed5b4`。
- #4366 补链：同目录`run-nerv-0d43-b8f062-inventory-r4/`，135文件/34,660,468字节，tar SHA256 `e933379c9496b687b817207a0ff5177c9207cefb925ba4a5540998ea4fe23fa3`。最终合并/CI/清理回执在`pr-4397-r2-final/FINAL-MERGE-EVIDENCE.md`。
- 本轮：`/Users/mang/.t3/archives/Nerv-IIP/issue-4368-20261013/` 保存API、原始diff/tree比较、命令输出、head/base及SHA256清单；本票不复制原秘密或建立永久registry。

原准备及排程stop均Stopped/remaining0；原/新生产stop均Stopped/remaining=[]/errors=[]，精确容器/卷/网络空、preview/observer/launcher退出，口令值窄扫描0hit后删除私有文件。以上是各原session回执。本轮未启动runtime、浏览器或常驻辅助进程，文档门禁结束后无自有运行资源；保留本worktree/分支供父独立审核及全部后续修复。
