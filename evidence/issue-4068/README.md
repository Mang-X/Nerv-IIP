# #4068 草案工作区视觉验收

本证据仅归属 `7c567e5da706b6d329c4515243794eb799fb23f2`（2026-09-30 开工时最新 `origin/main`）。本分支只保存视觉证据，不并入 main，不承载产品改动。

结论：发现 **2 项直接布局问题，均未修复**；#4068 保持 OPEN。截图均逐张人工查看。没有用 DOM 断言或测试通过替代视觉判断。

## 环境与证明边界

- 最新 SHA 的真实 Business Console `/scheduling` 页面、真实 DHTMLX trial 引擎；临时 HTTP fixture 提供冻结排程与登录/工作范围响应。不是真实 fullstack，不证明真实服务端、数据库或发布结果。
- 第一段使用 T3 preview：先 `preview_status` 返回 `available:false`，随后 `preview_open` 成功并实际运行。浏览器 UA 是 T3Code Nightly / Electron 44 / Chrome 152。后段 T3 返回 `No preview automation host is available`、`Do not retry`，才切换本机 Google Chrome headless / Playwright 补原生 resize 与未知/只读详情。未伪造拖动事件。
- T3 CSS viewport 为 1440×900 与 1024×900；PNG 由工具等比输出到 1280 宽，因此文件像素尺寸与 CSS viewport 不相同。`pw-` 文件是 Chrome 的原始 viewport 像素。
- 暗色主题；本机时区 Asia/Shanghai。fixture 输入为 UTC，图面/详情转换为 UTC+8。
- DHTMLX evaluation 通知会遮住局部卡片；部分截图保留通知。原生 resize 前、反馈资源图及 Chrome 详情通过通知右上角的正常 UI 点击关闭。切换图面/更新模型后通知可能重新出现；未通过删 DOM 或 CSS 隐藏。
- 首次 Vite 优化依赖出现 `504 (Outdated Optimize Dep)`，刷新后正常渲染；这不是产品缺陷。
- 没有运行 CI、后端门禁或真实 fullstack；本次无产品修改，不将这些边界冒称通过。

## 可复现输入与步骤

最新工作树运行 `pnpm install --frozen-lockfile`；使用当前 `frontend/apps/business-console/package.json` 的 `vp dev` 入口，独立端口 54168，Gateway 临时 fixture 端口 54169。与现有 `frontend/apps/business-console/e2e/issue4043-draft-feedback.spec.ts` 的冻结方案 fixture 相同，历史方案 `/scheduling/plans/history` 另返回同一个方案的 summary，供只读对照。

1. 计划员拥有 `business.scheduling.plans.read`、`business.scheduling.plans.manage`、`business.mes.work-orders.read`；进入 `/scheduling`，点击“全部加入”与“生成首版”。方案 `APS-260930-001`，资源 `CNC-01`、工作中心 `WC-TURN`。
2. 2026-09-30 UTC assignment：A-10 粗车 `08:00–10:00`；A-20 同一工单精车 `10:00–12:00`；A-30 第二张工单粗车 `14:00–16:00`。图面分别为当地 16:00–18:00、18:00–20:00、22:00–次日00:00。
3. 日历 UTC `08:00–12:00`、`12:00–18:00`；horizon `00:00–24:00`，资源/中心容量 1、利用率1；精车前序为粗车，精车 dueUtc `12:00`。没有外部固定占用。
4. 工单甘特切“小时”，点击 A-20；搜索“精车”命中1条，“磨削”无匹配；由1440缩到1024。
5. 表格编辑 A-20 起止为 `2026-09-30T07:00:00.000Z`、`2026-09-30T19:00:00.000Z`：同时出现前序倒置、日历外、占用冲突、延期420分钟。切资源板、查看“需核对”列表和详情。恢复 `10:00Z–12:00Z` 后问题清除。
6. Chrome 原生 resize：资源板小时刻度，正常 UI 关闭 evaluation 通知，鼠标命中右端 `.gantt_task_drag.task_right`（8×55 CSS px），`mouse.down/move/up` 拉伸 A-20，结果为 `10:00Z–16:00Z`，详情6小时，冲突区间 `14:00Z–16:00Z`，延期240分钟。
7. 未知依据：同一响应仅省略可空 `validationContext`，模拟真实可达的旧历史快照；重新生成后打开 A-20，明确显示“方案未记录校验依据，日历、占用、前序和交期暂无法核对”与“未记录可核对的工序交期”。没有伪装按期或校验通过。
8. 历史“甘特图”以同一个冻结方案，在小时/日/周/月切换并与草案资源板对照；1024下打开 A-20 详情。

## 逐项结论与截图

| 票面范围 | 结论 | 证据 |
| --- | --- | --- |
| 工具栏布局、对齐、字体层级、间距 | 通过正常1440；1024只读工具栏能换行访问；详情/动态缩窄发现问题 | [草案小时](draft-小时.png)、[只读1024](readnarrow.png)、D1/D2 |
| 日/周/月及现有细刻度对照 | 通过：轴、颜色语义、非工作时段一致；班次边界仅在小时出现；粗刻度2小时任务收缩为细条，无需在条内读全名，可用搜索/详情 | [草案小时](draft-小时.png) / [只读小时](readonly-小时.png)、[草案日](draft-日.png) / [只读日](readonly-日.png)、[草案周](draft-周.png) / [只读周](readonly-周.png)、[草案月](draft-月.png) / [只读月](readonly-月.png) |
| 图例、颜色语义 | 通过：当前输入无缺料/设备风险，图例仅列当前实际语义；小时含班次，日周月移除班次 | 同上；[反馈详情](feedbackdetail.png) |
| 搜索命中/无结果 | 反馈可辨认；详情位置发现D1 | [命中](hit.png)、[无匹配](miss.png) |
| 页内详情 | 1024资源板垂直堆叠时正常；草案1440及动态缩窄发现D1；只读1024发现D2 | [正常窄窗详情](narrowdetail.png)、[草案详情横溢](s2.png)、[只读详情横溢](pw-readonly-detail-1024.png) |
| 资源工序卡与控件遮挡 | 通过基本呈现：1440小时工单号省略、工序可读，hover与详情可读全名；1024短时卡更窄，完整内容依靠详情；evaluation通知影响已注明 | [卡片及hover](pw-resource-endpoints-1440.png)、[正常窄窗详情](narrowdetail.png) |
| 原生右端resize及图面/详情一致 | 操作完成且时间/冲突/交期一致；操作后详情横溢属D1 | [resize后1440](pw-native-resize-1440.png)、[缩窄1024](pw-resize-detail-1024.png) |
| 日历外/占用/前序/交期差同时出现 | 通过反馈内容可辨认，窄窗详情纵向滚动可查阅；未遮挡列表/图面工具栏 | [需核对列表](feedbacklist2.png)、[四类反馈详情](feedbackdetail.png) |
| 冲突消除/正常状态 | 通过：恢复输入后详情正常按期、不再出现需核对；原生resize另触发冲突/延期 | [正常窄窗详情](narrowdetail.png)、[resize后](pw-native-resize-1440.png) |
| 未知校验依据 | 通过：明确未知及无工序交期，未冒称通过 | [工作区提示1440](unknown.png)、[未知详情1024](pw-unknown-detail-1024.png) |
| 1440/1024主操作访问 | 发现D1/D2：不满足详情/动态宽度的访问要求 | 下方直接问题 |

## 直接问题及最小修正建议

### D1：草案图面把工作区撑宽，详情与缩窄窗口越出可见区域

- 输入：1440×900、小时、A-20 点击打开详情。工作区从正常宽度被撑成1476 CSS px，left280/right1756；详情 left1403/right1739，CSS viewport仅1440。[s2.png](s2.png)。搜索仍会保留该越界详情，[hit.png](hit.png)、[miss.png](miss.png)。
- 1440缩至1024时，工单甘特仍保留1128宽、right1408，[narrow.png](narrow.png)。Chrome 真鼠标资源resize之后同样出现1440右侧详情越界和1024横向位移，[pw-native-resize-1440.png](pw-native-resize-1440.png)、[pw-resize-detail-1024.png](pw-resize-detail-1024.png)。
- 最小相关owner：`frontend/apps/business-console/src/components/scheduling/SchedulingDraftBoard.vue` 的图面/详情 flex 布局，与 `frontend/packages/scheduling/src/engine/dhtmlx/DhtmlxEngine.ts` 的容器尺寸观察。建议仅修图面尺寸收缩与祖先最小宽度约束，保证打开详情和窗口缩窄时图面随容器重算；不重做设计系统。
- 状态：未修复、未复验。

### D2：只读甘特在1024下打开详情，同样把详情推到右边之外

- 输入：1024×900、历史“甘特图”、小时、A-20。打开前工具栏正常换行可用，[readnarrow.png](readnarrow.png)；打开后右侧详情在viewport之外，只剩边缘，[pw-readonly-detail-1024.png](pw-readonly-detail-1024.png)。截图已关闭evaluation通知，不能归因于通知遮挡。
- 最小相关owner：`frontend/apps/business-console/src/components/scheduling/SchedulingPlanGantt.vue` 的并排甘特/详情 flex 布局。建议窄窗采用可收缩图面或与草案一致的下方堆叠，详情关闭和字段保持viewport内可访问。
- 状态：未修复、未复验。

## 未验证与限制

- 未验证真实 fullstack / 真实持久化 / 发布；本票用HTTP fixture视觉验收，不能冒称真实环境业务闭环。
- 原生右端resize实际验证；左端resize的独立截图/鼠标过程未执行。没有把右端成功冒称两端全部通过。
- 普通整条移动/跨资源改派未在本轮独立重做；图面与详情一致性实际证据来自资源右端resize和表格编辑。
- 1024完整四刻度组合未穷举；1024实际检查正常资源详情、四反馈/未知详情、只读工具栏/详情及1440动态缩窄；1440四刻度同数据对照已齐。
- 仅当前工单/资源/反馈状态范围，未扩全站、移动端或设计系统。全部直接发现未处置，因此不关闭本票。
