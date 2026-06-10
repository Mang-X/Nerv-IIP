# 排产工作台 模块产品/业务设计

> 业务域:排产计划(/scheduling) · 前端落点:`frontend/apps/business-console/src/pages/scheduling` + `@nerv-iip/scheduling`
> 关联:#78(甘特/排产前端)、#206(BusinessScheduling / APS lite)、#207(设备运行事实)、ADR 0014(APS 排程边界)
> 设计/实施依据:`docs/superpowers/specs/2026-06-10-unified-scheduling-gantt-design.md`、`docs/superpowers/plans/2026-06-10-unified-scheduling-gantt.md`

## 1. 这页给谁用、解决什么

- **计划员/排产员**:把已生成的排程方案看清楚(工单进度、资源负载),发现并处理冲突与未排产工序,微调后重新排程并发布。
- **车间主管/跟单**:从工单视角看进度与瓶颈;从资源视角看机台/工作中心的负载与过载。

主操作:**看计划 → 看冲突/未排产 → 调整(锁定/改派)→ 重新排程 → 发布**。

## 2. 信息架构(IA)

- 顶层域「排产计划」`/scheduling`,单页**排产工作台**。页内两种视图(布局切换,**不进菜单树**):
  - **工单甘特**:工单 → 工序 WBS 时间线(依赖链、关键路径、里程碑、进度)。
  - **资源排产板**:工作中心/资源为行,工序块按资源时间轴排布 + 资源负载/利用率。
- 右侧面板(页内 Tabs):**冲突 / 未排产 / 变更摘要**;点条出**检视 Sheet**。
- 与「制造执行 › 规则排程」的关系:规则排程是 #206 之前的过渡触发入口,保留触发能力,展示统一导流到本工作台。

## 3. UX 要点

- **锁定—重预览闭环**:拖动工序(改期/改时长/改派)→ 该工序标记锁定 → 「重新排程」围绕锁定项重算 → 变更摘要/冲突高亮 → 「发布计划」提交。撤销/重做为前端快照栈。
- **冲突**用业务语言芯片(产能不足/交期风险/物料未齐套/设备不可用…),点击选中并定位到对应条。
- **未排产**每项给原因 + 「去处理」出路。
- 空状态:无计划时指引「前往规则排程」或在需求与计划中生成方案。
- 不暴露工程语言(reasonCode/operationId/source/demo/seed 等)。

## 4. 角色与权限

- 可见域:计划员、排产员、车间主管;只读旁观:跟单、管理。
- 当前 `navigation.ts` 的 `requiredPermissions` 为宽松默认;BusinessGateway 的 `scheduling.*` 读/发布权限为权威。新增「调整/发布」操作时,前端可见性与网关授权**一起改**,补 `scheduling.plan.read` / `scheduling.plan.release` 等权限码映射(随权限拆分 PR 同步)。

## 5. 数据来源(facade 代码事实)

BusinessGateway `/api/business-console/v1/scheduling/plans`:`list` / `{planId}`(detail)/ `{planId}/gantt` / `{planId}/release` / `preview` / `create`(POST 问题定义)。
读取经 `@nerv-iip/api-client` 生成 SDK + curated barrel(`SchedulePlanContract` 等)→ `@nerv-iip/scheduling` 的 `toModel` 归一化。

## 6. 技术落点(引擎可替换)

详见 `frontend/packages/scheduling/README.md`。三层:Vue 组件层(稳定契约)→ `SchedulingEngine` 适配器(DhtmlxEngine / NativeEngine,共同通过引擎契约测试)→ `ScheduleModel` + `aps-mapper`。
MVP 引擎:DHTMLX Gantt 9.x 专业版**试用**(评估许可,禁分发,不入 git);无许可环境回落 NativeEngine。换自研版本只换适配器。

## 7. 分期

- **P0(已落地)**:可视化只读 + 视图切换 + 冲突/未排产/变更面板 + 检视;NativeEngine 渲染;APS facade 对接(读)+ 发布。
- **P1**:DHTMLX 试用渲染接入(资源面板/关键路径/自动排程辅助)、拖拽改派/改期落地到重预览。
- **后续**:真正的后端重预览(见 §8 缺口)、依赖编辑、产能日历可视化、与 #207 设备 availability 联动着色。

## 8. 后端缺口(整批 consolidated issue,落地后回填 issue 号)

1. **从计划 + 锁定分配重预览**:现有 `preview` 需要完整排程问题定义(`SchedulingProblemContract`),前端仅持有计划。需提供「按 planId + 锁定分配重算」端点,或让计划可回溯其问题。当前前端「重新排程」在缺该端点时仅在客户端保持锁定项,不做后端重算。
2. **工序依赖编辑**:契约无独立 link 端点;MVP 依赖链由 `operationSequence` 派生展示,不可编辑。
3. **资源产能日历**:MVP 以 `resourceLoads.availableMinutes` 近似;缺资源工作日历端点用于精确产能带。

> Issue:_待发后回填_(`scheduling.前端MVP后端缺口`)。

## 9. 验收

- 工作台在亮/暗 + 动态色下可视化两视图;冲突/未排产/变更面板与检视可用;发布闭环可用。
- `@nerv-iip/scheduling` 引擎契约测试通过(Native 始终,DHTMLX 装试用包时);business-console typecheck/test/build 全绿。
- E2E 渲染与视图切换、视觉基线、性能门禁(~2000 工序)就绪。
- UI 无工程语言、无假数据/假分页;文档与代码同步。
