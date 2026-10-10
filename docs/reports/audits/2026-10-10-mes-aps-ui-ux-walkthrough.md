# MES / APS 真实页面 UI/UX 与业务流程走查（2026-10-10）

## 结论与证明范围

当前具备多项可演示能力，但**尚不能流畅演示一条正常生产闭环，也未达到产品级异常恢复体验**。最优先处理的是 MRP→建议的阻断、急单路由不完整、撤销发布未撤回 MES 排程、完工成本无法恢复，以及质量/停机上下文接力。布局问题另列，不能用样式修整代替业务闭环。

本轮完成了可访问 MES 17 个菜单和 APS 总览/编辑/历史视图的页面走查，并连续尝试计划员、现场操作员、物料员、质检与班组长视角的主要旅程。**使用的是隔离环境 admin 身份，不是各角色的真实最小权限账号**；MRP 主路径在失败处停止，后续三工序工单由公开 API 构造审计 fixture，仅用于独立验证 APS/MES 下游。不存在“全流程均通过”的结论。

- 基线：`f7573754ed98bdb0583e8465057de625b535ca49`；未修改产品代码、契约、测试或数据库结构。
- 真实依赖：隔离 Aspire fullstack `nerv-195b-a3a1a9`，PostgreSQL、Redis/CAP；Business Console `http://localhost:51681`，Gateway `51672`，BusinessGateway `51683`。
- 启动：`pwsh -NoProfile -File ./nerv.ps1 fullstack start -NoBuild -Scenario issue-1912-real-machine-walkthrough -EnableWmsDemoWorker`。场景只有最小走查数据；world/history/scale 关闭，不能用历史演示数据指标代替本轮结果。
- 初始亮色布局检查：1024/1280；有数据后的暗色检查：1440×900及浏览器原宽度。临时视口已恢复；未做手机/PDA、全浏览器或数值可访问性审计。
- 构建实际执行：`pnpm -C frontend --filter @nerv-iip/business-console build`，vue-tsc/Vite通过；有纯注释与大chunk警告。未执行自动化测试、正式FullChain Authority、CI/main验证或真实设备采集。
- 审计业务操作只写隔离测试数据，详见[证据与活动记录](2026-10-10-mes-aps-ui-ux-evidence.md)。未修复代码，未开issue，未发布GitHub/Linear记录，未创建PR。

## 已有流程和完成项的复用

复用[当前MES设计](../../product/mes/design.md)、[APS设计](../../product/scheduling/design.md)、[角色旅程](../../product/frontline/role-journeys.md)，不另造平行业务规则。既有图形流程为[历史MES最小闭环图](../../superpowers/plans/2026-05-27-mes-operational-foundation-reset.html)中的 `MES minimum operating loop`（约257行）；这是历史规划，不证明当前完成状态。当前Product的APS主操作序列、MES导航树和角色旅程矩阵作为操作文档的结构依据。

tracker 已实际查询 GitHub open/closed；每项注明直接owner或邻近完成项。没有查询Linear全量，不声称全tracker绝对无重复；“未找到owner”仅是本轮定向检索结果。确认实施后仍应在发布前做最终去重。

已跑通并保留的能力：MPS评审/发布、领料申请→CAP生成WMS出库/拣选、MES收料及刷新齐套、三工序APS首版/锁定/撤销重做/保存恢复/重预览/发布、候选预览并保存草稿而不自动发布、MES前序开工拒绝、开工/暂停、良品报工、产出批次、入库登记等待成本、缺陷→CAP自动NCR。这些能力不重复列为缺失。

## 覆盖矩阵

| 旅程/角色 | 实际结果 | 不能扩大为通过的范围 |
| --- | --- | --- |
| 计划员：需求→MPS→MRP→建议→转单 |需求与MPS录入/发布通过；MRP失败，UI建议与转单阻断 | API fixture不证明MRP转单成功；预测池语义需澄清 |
| 计划员：急单→齐套→释放→单单排产 | 创建/收料/释放通过；单单排产缺工序20失败 | 该单报工完工不能证明完整工艺执行 |
| 排产员：三工序首版→编辑锁定→重预览→发布→MES读回 | 三工序已排，发布真实落到MES；保存恢复/撤销重做通过 |设备未知、无员工，不能证明实际人员设备可生产 |
| 排产员：局部重排候选→选定草稿 | 一候选、0移动、锁定1/1；草稿保存，不自动发布 | 非空停机/多工单传播、CTP插单与性能未实测 |
| 排产员：撤销→MES失效 | APS已撤销、CAP消费成功；MES未开工仍排程 | 下游撤回失败，不用受理成功代替结果 |
| 物料员：仓库→领料→拣选→线边→耗料 | 审计原料入账10kg，MES收料1.4kg；WMS待处理也能收；报工未耗料 | 物理拣选交付、批次消耗/退料/补料完整对账未通过 |
| 操作员：任务→开工→报工→完工 | 急单admin无派工可开工/报1良品/完成；三工序OP10开工暂停、OP20前序拒绝 | 无员工/班组/现场角色账号；三工序完整生产和身份归属未验证 |
| 质检：工序检验/缺陷→NCR→处置 | 质检预填SKU错误取消；缺陷/NCR自动创建，身份和互链异常 | MRB审批模板缺失，返工/报废/关闭未提交 |
| 设备：停机→产能→APS重排→恢复 | 停机登记403/CORS失败 |无停机实体，后续恢复/影响传播不能判通过 |
| 班组长：交班→接班 |表单打开，但无班组候选，未创建 |交班内容/接班回执与角色scope未验证 |
| 财务/主管：成本→入库→追溯→报表 |费率维护成功，但ERP replay无handler、入库等待；谱系与日报有数据 |最终库存/成本过账、会计期间、跨时区生产日未通过 |

## 问题清单与优先级

共30项：P1 8、P2 20、P3 2；另列数据前提/静态风险。截图有即时状态和流程上下文两种：08/21保留失败后的表单输入，未捕获已消失的toast；07是顶层刷新后的Ready；15是最终发布结果，未保留对比弹窗专图。相应失败或瞬时差异以操作观察、DOM和日志为证，不把上下文截图冒充画面内直接错误证明。

P1：关键旅程阻断、跨域状态失真或业务对象错误，优先于演示；P2：严重理解/接力/恢复成本或需确认的业务策略；P3：导航与信息密度收敛。本轮未发现有足够证据列P0的数据损坏事故。优先级是审计建议，仍待用户确认。

| 编号 | 优先级 | 验证等级 | 问题 |
| --- | --- | --- | --- |
| [L01](#l01) | P2 | 实际·布局 | 甘特评估版提示覆盖任务条 |
| [L02](#l02) | P2 | 实际·布局 | 暗色甘特任务条和文字难辨 |
| [L03](#l03) | P2 | 实际·布局 | 异常恢复表格长标识重叠，成本状态侵入时间列 |
| [L04](#l04) | P2 | 实际·布局/UX | APS 编辑区埋在长页面下，动作与编辑分离 |
| [B01](#b01) | P1 | 实际+日志+静态根因 | 已发布 MPS 后 MRP 失败，主生产路径中断 |
| [B02](#b02) | P1 | 实际+静态根因 | 急单只建一工序，生产版本三工序无法排程 |
| [B03](#b03) | P1 | 实际+日志+静态根因 | 补费率后 ERP 成本事件仍无重放能力 |
| [B04](#b04) | P1 | 实际+日志+静态根因 | APS 撤销成功但 MES 工序仍已排程 |
| [B05](#b05) | P1 | 实际+静态根因 | 缺陷来源回跳错误，已创建 NCR 在 MES 显示为无 |
| [B06](#b06) | P1 | 实际+静态根因 | 真实工单的 NCR 使用占位物料 MES-SKU-UNRESOLVED |
| [B07](#b07) | P1 | 实际 | 从工序呼叫质检带出错误物料 |
| [B08](#b08) | P1 | 实际+日志，根因范围有界 | 工序停机登记被拒绝且提示成服务不可用 |
| [U01](#u01) | P2 | 实际+静态解释 | 需求池 forecast 与预测管理两套入口语义不清 |
| [U02](#u02) | P2 | 实际+静态根因 | 需求与工单紧急度读取失败 |
| [U03](#u03) | P2 | 实际，既有功能待收敛 | 可执行/开工菜单与服务端前序阻塞矛盾 |
| [U04](#u04) | P2 | 实际，业务策略待确认 | 有用料需求但零批次消耗仍可完成，库存未扣 |
| [U05](#u05) | P2 | 实际，业务策略待确认 | MES 能在 WMS 尚未拣选时直接确认线边收料 |
| [U06](#u06) | P2 | 实际+静态解释 | WMS 拣选显示数量0，计划量不可见 |
| [U07](#u07) | P2 | 实际 | 收料后齐套需另一次顶层刷新 |
| [U08](#u08) | P2 | 实际，根因未定 | 页面切换后作业范围不可选，整页空队列需重载 |
| [U09](#u09) | P2 | 实际 | 详情加载前完工入库按钮可点击，跳到无上下文死路 |
| [U10](#u10) | P2 | 实际，原因未定 | 入库提交后自动重开无批次表单 |
| [U11](#u11) | P2 | 实际 | 重预览对比把相同原新时间的工序标成新增 |
| [U12](#u12) | P2 | 实际 | 设备未知同时显示设备不可用冲突 |
| [U13](#u13) | P2 | 实际 | 新报工日报显示历史站点时区缺失 |
| [U14](#u14) | P2 | 实际·UX判断 | 日期和时间编辑口径混用 |
| [U15](#u15) | P2 | 实际·UX判断 | 成本异常恢复链接不保留工作中心或工序 |
| [U16](#u16) | P2 | 实际·UX判断 | 追溯能展示谱系但难接力处理 |
| [U17](#u17) | P3 | 实际·UX判断 | MES菜单17个入口仍多次跨页接力 |
| [U18](#u18) | P3 | 实际·UX判断 | 生产准备检查重复长清单，修复入口是文字 |

## 逐项复现与影响

<a id="l01"></a>

### L01 — 甘特评估版提示覆盖任务条

- 优先级/证据：**P2 · 实际·布局**。
- 复现：打开已有三工序方案→排程总览→向下滚到工单甘特或资源排产板。
- 实际结果/影响：dhtmlxGantt evaluation 提示遮挡时间轴/任务；演示需解释第三方提示。
- 截图：[38-aps-gantt.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/38-aps-gantt.jpg)。
- 已有任务：复用 OPEN [#1270](https://github.com/Mang-X/Nerv-IIP/issues/1270)，许可治理关联 [#1757](https://github.com/Mang-X/Nerv-IIP/issues/1757)；不新开同类票。
- 改进方向：正式包接入后复验；不能靠隐藏通知冒充正式包。

<a id="l02"></a>

### L02 — 暗色甘特任务条和文字难辨

- 优先级/证据：**P2 · 实际·布局**。
- 复现：同 L01，在暗色、1440×900 下查看三道短工序。
- 实际结果/影响：任务条、标签与背景对比很低；长工序编号折行，短工序时间很难直观看清。未做数值对比度测量。
- 截图：[38-aps-gantt.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/38-aps-gantt.jpg)。
- 已有任务：未找到直接 owner；[#4068](https://github.com/Mang-X/Nerv-IIP/issues/4068) CLOSED 只覆盖详情越界等旧范围。
- 改进方向：先恢复条/文字/依赖线可读性，再调编号与缩放；亮色有数据视图仍需复验。

<a id="l03"></a>

### L03 — 异常恢复表格长标识重叠，成本状态侵入时间列

- 优先级/证据：**P2 · 实际·布局**。
- 复现：打开集成运维死信队列的三条 ERP 事件；另打开完工入库列表。
- 实际结果/影响：1440 下事件类型、消费者、失败码、时间互相重叠；较宽视口入库等待说明也跨列，最需要诊断时信息最难读。
- 截图：[28-replay-unsupported.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/28-replay-unsupported.jpg)。
- 已有任务：未找到直接 owner；恢复能力复用 [#3772](https://github.com/Mang-X/Nerv-IIP/issues/3772)，不能重复建整套死信页面。
- 改进方向：列宽/换行/省略与详情阅读组合处理；同时复验入库等待说明。

<a id="l04"></a>

### L04 — APS 编辑区埋在长页面下，动作与编辑分离

- 优先级/证据：**P2 · 实际·布局/UX**。
- 复现：排程总览有工单、候选、物料/设备提示时→下滚到表格编辑→修改/锁定→回顶层重预览。
- 实际结果/影响：编辑需要反复纵向滚动；1440 下锁定、待排、反馈列还在横向可视区外。截图40只显示左侧，完整列见 DOM。
- 截图：[40-aps-table.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/40-aps-table.jpg)。
- 已有任务：沿现有统一草稿工作区改进；[#3595](https://github.com/Mang-X/Nerv-IIP/issues/3595)/[#3623](https://github.com/Mang-X/Nerv-IIP/issues/3623)–3626/[#3633](https://github.com/Mang-X/Nerv-IIP/issues/3633) 已完成能力不重开。
- 改进方向：给主动作固定可达位置，压缩重复摘要；保持三视图同一草稿语义。

<a id="b01"></a>

### B01 — 已发布 MPS 后 MRP 失败，主生产路径中断

- 优先级/证据：**P1 · 实际+日志+静态根因**。
- 复现：需求计划→新建 SF-ROD-01/2pcs/SITE-001 MPS→评审→发布→运行10-10至11-09 MRP。
- 实际结果/影响：第二次运行失败，无法评审生产建议和从 UI 转单。日志 PostgreSQL22001：InventorySnapshotSource 长141，列限128；不是缺 BOM。
- 截图：[02-mrp-failed.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/02-mrp-failed.jpg)。
- 已有任务：未找到直接 owner；[#1378](https://github.com/Mang-X/Nerv-IIP/issues/1378) CLOSED 来源身份、[#1933](https://github.com/Mang-X/Nerv-IIP/issues/1933) OPEN 混合范围仅邻近。
- 改进方向：修复来源持久化合同与迁移；用同一输入完成建议→采纳→MES工单，不以求解内存成功代替。

<a id="b02"></a>

### B02 — 急单只建一工序，生产版本三工序无法排程

- 优先级/证据：**P1 · 实际+静态根因**。
- 复现：工单→新建急单，SF-ROD-01有效版本、WC-ROD-01、序号10、60分钟→补料齐套→释放→对该单排产。
- 实际结果/影响：只得到 OP10；APS 报缺工序20，页面无修复工艺任务路径。标准工序未绑定，SOP也不可用；该单却能报工完工。
- 截图：[08-rush-scheduling-failed.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/08-rush-scheduling-failed.jpg)。
- 已有任务：未找到直接 owner；[#203](https://github.com/Mang-X/Nerv-IIP/issues/203)/[#3696](https://github.com/Mang-X/Nerv-IIP/issues/3696)/[#4185](https://github.com/Mang-X/Nerv-IIP/issues/4185) CLOSED 为邻近能力。
- 改进方向：统一急单与常规转单的路由冻结合同；不要用更多手填工序按钮掩盖。

<a id="b03"></a>

### B03 — 补费率后 ERP 成本事件仍无重放能力

- 优先级/证据：**P1 · 实际+日志+静态根因**。
- 复现：报工1良品→登记入库→等待成本→死信查看missing-work-center-cost-rate→新增WC-ROD费率10CNY/h有效10-10→重放报工事件。
- 实际结果/影响：重放结果“该服务无重放能力”；三条待处理仍在，FGR成本0/1，无法走到库存入库终态。缺费率本身是数据前提，无法恢复是产品缺口。
- 截图：[28-replay-unsupported.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/28-replay-unsupported.jpg)。
- 已有任务：[#1070](https://github.com/Mang-X/Nerv-IIP/issues/1070)/[#3769](https://github.com/Mang-X/Nerv-IIP/issues/3769)/[#3771](https://github.com/Mang-X/Nerv-IIP/issues/3771)/[#3772](https://github.com/Mang-X/Nerv-IIP/issues/3772)/[#3728](https://github.com/Mang-X/Nerv-IIP/issues/3728) CLOSED 的费率、检查、引导能力复用；本次 ERP NoHandler 未找到直接 open owner。
- 改进方向：补对应 ERP replay handler，并验证成本回写→FGR→Inventory实际库存；另区分机器实绩缺失，不能只重放人工成本。

<a id="b04"></a>

### B04 — APS 撤销成功但 MES 工序仍已排程

- 优先级/证据：**P1 · 实际+日志+静态根因**。
- 复现：发布plan-01a124ecbb7072ea9f8077e320abfa54→历史表格→撤销发布→确认→MES工序执行→刷新。
- 实际结果/影响：APS已撤销；OP20/30仍Scheduled。CAP100ms成功消费且watermark正确，不能解释成延迟。Revoke未加载Assignments，撤销AffectedOperations为空。
- 截图：[43-revoke-mes-readback.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/43-revoke-mes-readback.jpg)。
- 已有任务：Refs [#701](https://github.com/Mang-X/Nerv-IIP/issues/701)/[#1237](https://github.com/Mang-X/Nerv-IIP/issues/1237) CLOSED；[#4241](https://github.com/Mang-X/Nerv-IIP/issues/4241) CLOSED 修复Release同族问题，范围不含独立Revoke。未找到本缺陷 owner。
- 改进方向：补完整撤销事件，验证未开工失效、已执行不被错误清空；状态反馈需等权威结果而非只显示发出。

<a id="b05"></a>

### B05 — 缺陷来源回跳错误，已创建 NCR 在 MES 显示为无

- 优先级/证据：**P1 · 实际+静态根因**。
- 复现：MES质量→登记缺陷→WO2 OP10/尺寸超差1→点来源工序链接；再从质量管理进入NCR。
- 实际结果/影响：来源链接拼到/mes/work-orders/WO…-OP-10，工单不存在。Quality已有1条NCR，MES却显示0/无，没有接力处置入口；后端投影ncrId写死null。
- 截图：[35-quality-broken-link.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/35-quality-broken-link.jpg)。
- 已有任务：[#3319](https://github.com/Mang-X/Nerv-IIP/issues/3319) CLOSED 身份投影背景，[#980](https://github.com/Mang-X/Nerv-IIP/issues/980) CLOSED NCR处置已存在；[#2982](https://github.com/Mang-X/Nerv-IIP/issues/2982)/[#1427](https://github.com/Mang-X/Nerv-IIP/issues/1427) OPEN 仅母/全域票。未找到精确链接/互链 owner。
- 改进方向：保留工单与工序两个身份，投影真实NCR关联；复用NCR处置界面，别另建MES处置按钮体系。

<a id="b06"></a>

### B06 — 真实工单的 NCR 使用占位物料 MES-SKU-UNRESOLVED

- 优先级/证据：**P1 · 实际+静态根因**。
- 复现：沿 B05 登记缺陷→质量管理→不合格品处理→打开处置。
- 实际结果/影响：NCR物料占位，无法可靠按真实SKU分析和跨域处置；事件不传SKU，Quality直接用占位。返工可回查源工单，但报废等未实测，不能推断全部失败。
- 截图：[45-ncr-unresolved.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/45-ncr-unresolved.jpg)。
- 已有任务：未找到直接 owner；[#3319](https://github.com/Mang-X/Nerv-IIP/issues/3319)邻近，旧[#461](https://github.com/Mang-X/Nerv-IIP/issues/461) CLOSED仅历史残留背景。
- 改进方向：在权威边界补足真实物料身份；分别验证返工/报废结果，避免占位继续外传。

<a id="b07"></a>

### B07 — 从工序呼叫质检带出错误物料

- 优先级/证据：**P1 · 实际**。
- 复现：WO2 OP10→操作→呼叫质检→查看自动打开的检验登记。
- 实际结果/影响：工单是SF-ROD-01，表单默认SKU-001；未携带正确skuId。用户如果照默认提交会把检验对象登记错。本轮取消，未产生错检验。
- 截图：[20-quality-wrong-sku.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/20-quality-wrong-sku.jpg)。
- 已有任务：未找到直接 owner；[#3773](https://github.com/Mang-X/Nerv-IIP/issues/3773) CLOSED选择器、[#3112](https://github.com/Mang-X/Nerv-IIP/issues/3112) CLOSED事件能力不可冒充上下文正确。
- 改进方向：从真实工单/工序读出锁定物料，不依赖全局第一选项。

<a id="b08"></a>

### B08 — 工序停机登记被拒绝且提示成服务不可用

- 优先级/证据：**P1 · 实际+日志，根因范围有界**。
- 复现：WO2 OP10→记录异常→设备与停机→登记停机→选机械故障DT-MECH和当前有效时间→确认。
- 实际结果/影响：登记失败、列表仍0。BG日志403作业范围，CORS OriginNotAllowed使前端不能读取真实拒绝原因；并未到MES写入。WC设备身份是否相关尚未证实。
- 截图：[21-downtime-failed.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/21-downtime-failed.jpg)。
- 已有任务：复用[#1174](https://github.com/Mang-X/Nerv-IIP/issues/1174)/[#1164](https://github.com/Mang-X/Nerv-IIP/issues/1164)/[#1168](https://github.com/Mang-X/Nerv-IIP/issues/1168)/[#1177](https://github.com/Mang-X/Nerv-IIP/issues/1177) CLOSED权威门禁，不取消门禁；未找到直接 owner。
- 改进方向：在同一授权上下文修正可选任务与写面范围，允许前端读到错误；验证登记→产能→APS候选→恢复。

<a id="u01"></a>

### U01 — 需求池 forecast 与预测管理两套入口语义不清

- 优先级/证据：**P2 · 实际+静态解释**。
- 复现：需求池新建默认forecast的SF-ROD-01/2pcs需求→运行MRP。
- 实际结果/影响：首轮已完成但0建议、覆盖0、输入未记录。producer明确排除demand_sources.forecast，另读forecast_inputs；页面没有把两种“预测”说明清楚。不能判所有预测MRP失效。
- 截图：[01-demand-mrp-zero.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/01-demand-mrp-zero.jpg)。
- 已有任务：未找到直接 owner；[#1759](https://github.com/Mang-X/Nerv-IIP/issues/1759) CLOSED预测管理既有能力复用，[#805](https://github.com/Mang-X/Nerv-IIP/issues/805)/[#2926](https://github.com/Mang-X/Nerv-IIP/issues/2926)邻近。
- 改进方向：明确录入对象和进入MRP条件；需求、MPS独立计划语义需产品确认，不直接合并两模型。

<a id="u02"></a>

### U02 — 需求与工单紧急度读取失败

- 优先级/证据：**P2 · 实际+静态根因**。
- 复现：需求池录入后或MES工单列表查看紧迫度。
- 实际结果/影响：出现读取失败；Scheduling provider对未包裹MES响应读GetProperty(data)抛KeyNotFound。APS池自己的紧急度仍显示关注，不能报成全模块失效。
- 截图：[48-workorder-urgency.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/48-workorder-urgency.jpg)。
- 已有任务：直接复用 OPEN [#3920](https://github.com/Mang-X/Nerv-IIP/issues/3920)。
- 改进方向：修正响应适配，维持统一解释；将失败与无紧急度分开。

<a id="u03"></a>

### U03 — 可执行/开工菜单与服务端前序阻塞矛盾

- 优先级/证据：**P2 · 实际，既有功能待收敛**。
- 复现：发布三工序WO2→OP20显示可执行→菜单开工。
- 实际结果/影响：点击被拒绝“前序工序尚未完成（工序10）”；WIP三个任务仍无卡点。正确后端门禁已有，前端要在行动前解释。
- 截图：[18-predecessor-block.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/18-predecessor-block.jpg)。
- 已有任务：直接复用 OPEN [#3154](https://github.com/Mang-X/Nerv-IIP/issues/3154)；WIP能力[#3885](https://github.com/Mang-X/Nerv-IIP/issues/3885) CLOSED已排除终态，不重开。
- 改进方向：消费权威blockReasons，按钮前说明；WIP共享同一阻塞事实。

<a id="u04"></a>

### U04 — 有用料需求但零批次消耗仍可完成，库存未扣

- 优先级/证据：**P2 · 实际，业务策略待确认**。
- 复现：急单收料1.4kg（无批次）→开工→报良品1/完成，不选物料批次→查看线边库存。
- 实际结果/影响：报工成功且WO完成；线边RM-BAR-01仍可用1.4kg，入库物料过账0/0。已观察到追溯/成本不完整，良品是否强制消耗必须确认；后端目前仅报废强制。
- 截图：[30-line-side-unconsumed.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/30-line-side-unconsumed.jpg)。
- 已有任务：未找到直接 owner；[#1944](https://github.com/Mang-X/Nerv-IIP/issues/1944) CLOSED仅捕获能力，[#2720](https://github.com/Mang-X/Nerv-IIP/issues/2720) OPEN架构票邻近。
- 改进方向：先明确批次策略和正常良品耗料合同，再设计默认耗量/校验；不简单禁止所有无消耗场景。

<a id="u05"></a>

### U05 — MES 能在 WMS 尚未拣选时直接确认线边收料

- 优先级/证据：**P2 · 实际，业务策略待确认**。
- 复现：领料MIR生成WMS出库MI-MIR及P1拣选，保持待处理→返回MES确认收料。
- 实际结果/影响：直接收料成功1.4kg，WMS未完成。页面看似仓库已交付，物理执行关系未闭合；可能是允许线边独立确认的产品政策，不贸然判后端绕权。
- 截图：[05-wms-pending.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/05-wms-pending.jpg)。
- 已有任务：复用既有领料/收料能力；[#1176](https://github.com/Mang-X/Nerv-IIP/issues/1176) OPEN共享执行邻近，未找到直接政策 owner。
- 改进方向：明确谁证明出库/实物到线、如何对账；保持仓储与MES回执可追。

<a id="u06"></a>

### U06 — WMS 拣选显示数量0，计划量不可见

- 优先级/证据：**P2 · 实际+静态解释**。
- 复现：领料申请1.4kg→WMS对应拣选任务列表。
- 实际结果/影响：任务行数量0，但数据库plannedQuantity1.4；前端用非空executedQuantity0优先。未丢任务或数量，列语义和计划/实拣关系不清。
- 截图：[06-picking-zero.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/06-picking-zero.jpg)。
- 已有任务：未找到直接 owner；[#1176](https://github.com/Mang-X/Nerv-IIP/issues/1176)邻近。
- 改进方向：并列计划/已执行数量和执行状态，避免把未拣的0当需求量。

<a id="u07"></a>

### U07 — 收料后齐套需另一次顶层刷新

- 优先级/证据：**P2 · 实际**。
- 复现：工单详情→收料确认成功→查看上方齐套→点详情顶层刷新。
- 实际结果/影响：下方领料状态更新，上方齐套仍旧；顶层刷新后才Ready。截图07是刷新后的Ready，只证明最终读回；瞬时旧态以操作观察记录。
- 截图：[07-received-kit-stale.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/07-received-kit-stale.jpg)。
- 已有任务：复用既有[#3858](https://github.com/Mang-X/Nerv-IIP/issues/3858)齐套口径能力；未找到直接刷新 owner。
- 改进方向：收料成功后同步刷新受影响读面/明确刷新中，不让用户猜另一刷新按钮。

<a id="u08"></a>

### U08 — 页面切换后作业范围不可选，整页空队列需重载

- 优先级/证据：**P2 · 实际，根因未定**。
- 复现：APS发布→从MES导航进入工序执行。
- 实际结果/影响：出现无可选择授权范围/0任务；整页重新加载后恢复4任务。仅复现一次，未捕获该次完整失败网络，不能声称权限数据丢失。
- 截图：[16-scope-unavailable.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/16-scope-unavailable.jpg)。
- 已有任务：[#4256](https://github.com/Mang-X/Nerv-IIP/issues/4256) CLOSED只覆盖列表OR读取，不覆盖此次客户端恢复；未找到直接 owner。
- 改进方向：先最小复现切换时范围状态，再提供范围读取失败/重试；别用空队列提示代替失败。

<a id="u09"></a>

### U09 — 详情加载前完工入库按钮可点击，跳到无上下文死路

- 优先级/证据：**P2 · 实际**。
- 复现：进入已完工WO详情，数据尚未加载时立即点完工入库。
- 实际结果/影响：跳转仅带workOrderId；入库页只有禁用“从工单详情发起”，无法继续。等详情完全加载后再点可带SKU/数量并打开有效表单。
- 截图：[11-receipt-dead-end.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/11-receipt-dead-end.jpg)。
- 已有任务：未找到直接 owner；[#3419](https://github.com/Mang-X/Nerv-IIP/issues/3419)/[#3420](https://github.com/Mang-X/Nerv-IIP/issues/3420) CLOSED入库能力复用。
- 改进方向：上下文未就绪禁用/说明；目标页能按工单权威加载上下文。

<a id="u10"></a>

### U10 — 入库提交后自动重开无批次表单

- 优先级/证据：**P2 · 实际，原因未定**。
- 复现：从加载完成详情→登记产出LOT批次1pcs→提交。
- 实际结果/影响：登记成功后表单又出现，批次为空/暂无可入库产出，但登记按钮仍可用；容易以为没提交成功或再次登记。未再次提交。
- 截图：[12-receipt-awaiting-cost.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/12-receipt-awaiting-cost.jpg)。
- 已有任务：未找到直接 owner。
- 改进方向：成功回执关闭表单、显示单号及待成本状态；检查query/watch与剩余可入库量。

<a id="u11"></a>

### U11 — 重预览对比把相同原新时间的工序标成新增

- 优先级/证据：**P2 · 实际**。
- 复现：首版WO2三工序→锁OP30→重预览→方案对比。
- 实际结果/影响：OP10/20原新时间相同却标新增，OP30保持；移动0。计划员难理解真正改变的内容。根因未定位。
- 截图：[15-aps-published.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/15-aps-published.jpg)。
- 已有任务：复用既有对比与锁定能力，不重建。
- 改进方向：按稳定工单/工序身份对齐，区分新增/移动/未变。截图15侧重最终发布，详细变化来自实际对比观察。

<a id="u12"></a>

### U12 — 设备未知同时显示设备不可用冲突

- 优先级/证据：**P2 · 实际**。
- 复现：查看排程设备提示、甘特工序详情与冲突摘要。
- 实际结果/影响：明确提示未知且可按计划排入，详情冲突却叫设备不可用，历史列表计3项冲突；发布允许。未知不能直接当已知故障。
- 截图：[38-aps-gantt.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/38-aps-gantt.jpg)。
- 已有任务：复用[#4253](https://github.com/Mang-X/Nerv-IIP/issues/4253)/[#3629](https://github.com/Mang-X/Nerv-IIP/issues/3629)等已完成Unknown语义背景，未证明同一producer缺陷已有人承接。
- 改进方向：统一Warning/Blocking名称、摘要与发布说明；不凭未知强行拦全部方案。

<a id="u13"></a>

### U13 — 新报工日报显示历史站点时区缺失

- 优先级/证据：**P2 · 实际**。
- 复现：当天创建并报工WO1→生产日报。
- 实际结果/影响：新记录显示数据不完整/历史站点时区缺失。无DeviceAssetId时snapshot provider直接device-missing，无法取得站点时区；不是实际旧记录。
- 截图：[33-daily-report.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/33-daily-report.jpg)。
- 已有任务：[#2855](https://github.com/Mang-X/Nerv-IIP/issues/2855)/[#3859](https://github.com/Mang-X/Nerv-IIP/issues/3859) CLOSED已有快照/生产日能力；未找到本路径直接 owner。
- 改进方向：补新报工站点维度权威来源或准确降级原因；未实测跨午夜/跨时区归日。

<a id="u14"></a>

### U14 — 日期和时间编辑口径混用

- 优先级/证据：**P2 · 实际·UX判断**。
- 复现：查看APS窗口/工单时间→表格编辑。
- 实际结果/影响：浏览器美式日期AM/PM、中文本地时间、UTC ISO输入并存；同一工序甘特16:00而编辑08:00+00:00，容易误改8小时。
- 截图：[40-aps-table.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/40-aps-table.jpg)。
- 已有任务：复用 OPEN [#3905](https://github.com/Mang-X/Nerv-IIP/issues/3905)。
- 改进方向：统一显示本地生产时间并显式时区，编辑用可理解控件；UTC保留传输合同。

<a id="u15"></a>

### U15 — 成本异常恢复链接不保留工作中心或工序

- 优先级/证据：**P2 · 实际·UX判断**。
- 复现：死信missing费率→去补充工作中心费率；machine事实缺失→去派工看板核实。
- 实际结果/影响：到目的页仍需手查WC/OP；已完工WO派工不能直接补机器实绩，链接不是完整恢复操作。
- 截图：[27-cost-deadletters.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/27-cost-deadletters.jpg)。
- 已有任务：复用[#3772](https://github.com/Mang-X/Nerv-IIP/issues/3772) CLOSED引导，补现有入口而非另建恢复台。
- 改进方向：带对象上下文及正确可执行恢复路径，并明确恢复后重放/读回。

<a id="u16"></a>

### U16 — 追溯能展示谱系但难接力处理

- 优先级/证据：**P2 · 实际·UX判断**。
- 复现：查询WO1→查看5节点4边。
- 实际结果/影响：有工单/工序/报工/user-admin/产出批次，无已登记入库/材料消耗节点，状态有Reported/Produced；节点不支持业务详情接力。耗料缺失与U04同源，不报成全部追溯没实现。
- 截图：[24-traceability.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/24-traceability.jpg)。
- 已有任务：复用 OPEN [#2691](https://github.com/Mang-X/Nerv-IIP/issues/2691)报工人；[#2688](https://github.com/Mang-X/Nerv-IIP/issues/2688)重复节点未复现，不新报重复。
- 改进方向：围绕物料→工序→产出→入库连接详情，保留真实缺失标记。

<a id="u17"></a>

### U17 — MES菜单17个入口仍多次跨页接力

- 优先级/证据：**P3 · 实际·UX判断**。
- 复现：现场任务→报工/质检/停机→回工单；查看左侧导航。
- 实际结果/影响：派工看板独立菜单、异常分三页、诊断独立；同一任务连续工作需要反复切换。已有角色驾驶舱，不能说完全无角色入口。
- 截图：[37-overview.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/37-overview.jpg)。
- 已有任务：复用docs/product/mes/design.md §2/§3现有设计；按已完成任务逐项核实，暂不新开导航重构总票。
- 改进方向：先让工序上下文内处理主动作，再按既有目标导航收敛，避免先全模块重画。

<a id="u18"></a>

### U18 — 生产准备检查重复长清单，修复入口是文字

- 优先级/证据：**P3 · 实际·UX判断**。
- 复现：准备检查不筛范围→查看18阻塞列表及后续区域表。
- 实际结果/影响：17WC缺费率+会计期间，提醒本身正确；大段逐条重复，关键处理入口无可点击带上下文链接。无员工仍其它区域Ready，不能据此证明派工就绪。
- 截图：[25-foundation-blocked.jpg](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/25-foundation-blocked.jpg)。
- 已有任务：复用[#3771](https://github.com/Mang-X/Nerv-IIP/issues/3771) CLOSED检查能力，数据补齐与UI收敛分开。
- 改进方向：按当前工单/范围汇总，给可点击维护入口，区分基础准备与现场任务readiness。

## 数据前提、静态风险和未验证分支（不混入已复现缺陷）

- **D01 / 演示前提**：无员工、技能、班组与真实角色账号；派工筛“本工作中心班组”及“全部在岗员工”都无候选。班次交接缺班组；确认按钮的空状态解释可改善，但不因空种子判派工算法失败。截图[19](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/19-dispatch-no-worker.jpg)、[23](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/23-handover-no-team.jpg)。复用[#3063](https://github.com/Mang-X/Nerv-IIP/issues/3063)等已有数据任务，先确认当前范围而非重开全部seed。
- **D02 / 演示前提**：无已发布有效SOP、无会计期间、初始无费率、无接入设备实绩。SOP点击显示无有效SOP，未复现[#3660](https://github.com/Mang-X/Nerv-IIP/issues/3660)旧401；费率检查[#3771](https://github.com/Mang-X/Nerv-IIP/issues/3771)已工作。需补真实演示数据，但不能用补数据掩盖B02/B03/B06。
- **S01 / P2静态风险**：Paused恢复路径绕过新readiness，见`MesOperationTaskActionReadinessEvaluator.cs:164-166`、`MesWorkbenchCommands.cs:1654-1655`。没有实测“暂停后新增质量/设备阻塞再恢复”，无对应运行截图；不作为已复现安全事故。
- **S02 / 尚未确认**：资源板当前DOM只看到两条泳道，而甘特有三道工序。代码没有按locked过滤；可能是虚拟渲染/模型字段。截图39不能证明第三泳道真正丢失，暂不新增缺陷条目。
- 未实测：多工单急单CTP/复杂插单传播、非空停机影响/恢复、拆分/合并/并行/装配依赖、换型/跨班次/跨午夜、报废带批次消费与退料、NCR返工/报废/关闭、遥测确认、报工冲销提交、序列号真实打印、PDA/真机、员工/班组最小权限。冲销仅查看预览并取消，保留原产出/入库证据。[#1052](https://github.com/Mang-X/Nerv-IIP/issues/1052)/[#1058](https://github.com/Mang-X/Nerv-IIP/issues/1058)/[#816](https://github.com/Mang-X/Nerv-IIP/issues/816)等已有开放范围不重复开票。

## 下一步

见[分批改进计划](2026-10-10-mes-aps-ui-ux-plan.md)和[逐步操作文档](2026-10-10-mes-aps-ui-ux-operations.md)。本报告只冻结本次走查，用户确认前不实施修复。数据补齐、产品策略确认、代码修复与角色验收分别处理。
