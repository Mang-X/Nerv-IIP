# #4068 两项布局修复视觉复验

当前运行基线 `7959d3428ade61705c879dea98e96107a787e998`；产品 head `392a1eee2d4825e4f40f6ed0a2a02b06cae3a9f9`。20 张 after PNG 已在 rebase 后重新执行并逐张亲看。before-D1/D2 是原反例基线 `3e1bde5e51db18c222afcfc1e65c3f06e28aa7fe`，未冒充当前基线 before。after PNG 由本次工作树当前代码重新采集，逐张人工打开审视；旧验收记录仍在上级目录，不能作为本 head 新验证。

运行：Business Console Vite +真实 DHTMLX，HTTP fixture 沿用仓库 `e2e/issue4043-draft-feedback.spec.ts` 的同一 APS-260930-001 三工序/单资源数据。非真实后端数据或端到端发布证据。T3 preview_status/open 曾成功，但实际操作返回 `No preview automation host is available ... use a headless browser ... such as Playwright` 后，改本机 Google Chrome + Playwright；未伪造拖拽事件。

|项|输入与结果|截图|
|---|---|---|
|D1 草案详情|1440 小时刻度点 A-20；原 board 被 inline min-content 撑宽，修复后 board/right 1408、detail/right 1391，页面1440|before-D1 / 01|
|D1 动态窄窗|保持详情 1440→1024；board/right992、detail/right975，下置并可滚动完整查阅|02 / 03|
|D2 只读详情|原1024详情right1340，修复后right992、page1024、下置；1440侧栏right1408|before-D2 / 07 / 08 / 09|
|资源卡|1440与1024泳道完整，窄窗卡片省略标题，正常hover能读完整内容|04 / 05 / pw-resource-endpoints|
|同数据刻度|草案资源与只读资源：小时/日/周/月，网格日期与卡片位置一致；长刻度卡片变窄是现有刻度语义|draft-scale-* / readonly-scale-*|
|搜索|输入不存在，显示无匹配，工具条仍完整|06|
|原生resize|真实鼠标命中A-20右端拖柄，10–12Z变10–16Z；6小时、占用冲突14–16Z、延期240分钟；图面缩窄、详情仍完整|pw-native-resize / pw-resize-detail|

限定：DHTMLX trial 通知会在换刻度/重绘后出现；通过正常UI尝试关闭，截图如仍存在保留事实，不改厂商通知DOM。D1动态图面缩窄后旧悬浮tooltip节点仍产生scrollWidth1283，但board/detail均在1024内；03实际详情完整，不声称全页所有悬浮层均无溢出。本次修复不扩展到厂商tooltip生命周期。初始四类反馈/未知依据完整验收在上级旧基线记录；本次只复验布局相关的正常、资源resize占用/延期反馈，不把旧截图声明成本head新证据。未复验真实后端/发布及日历/前序/未知反馈。

本地门禁（相同产品内容）：fmt check通过；2文件lint通过（SchedulingPlanGantt.vue131原有unicorn warning未改）；Console build含vue-tsc通过；旧base Console全包246files/2893tests通过；当前base Console全包正在重跑，最终结果以PR更新为准；新base Scheduling108passed/4existing skipped；git diff --check通过。无新增CSS形状测试或产品功能。

文件清单（每张均人工打开）：

- [01-draft-wide-detail.png](01-draft-wide-detail.png)
- [02-draft-narrow-chart.png](02-draft-narrow-chart.png)
- [03-draft-narrow-detail.png](03-draft-narrow-detail.png)
- [04-resource-wide.png](04-resource-wide.png)
- [05-resource-narrow.png](05-resource-narrow.png)
- [06-search-empty.png](06-search-empty.png)
- [07-readonly-wide-detail.png](07-readonly-wide-detail.png)
- [08-readonly-narrow-chart.png](08-readonly-narrow-chart.png)
- [09-readonly-narrow-detail.png](09-readonly-narrow-detail.png)
- [before-D1.png](before-D1.png)
- [before-D2.png](before-D2.png)
- [draft-scale-周.png](draft-scale-周.png)
- [draft-scale-小时.png](draft-scale-小时.png)
- [draft-scale-日.png](draft-scale-日.png)
- [draft-scale-月.png](draft-scale-月.png)
- [pw-native-resize-1440.png](pw-native-resize-1440.png)
- [pw-resize-detail-1024.png](pw-resize-detail-1024.png)
- [pw-resource-endpoints-1440.png](pw-resource-endpoints-1440.png)
- [readonly-scale-周.png](readonly-scale-周.png)
- [readonly-scale-小时.png](readonly-scale-小时.png)
- [readonly-scale-日.png](readonly-scale-日.png)
- [readonly-scale-月.png](readonly-scale-月.png)
