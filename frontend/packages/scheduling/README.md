# @nerv-iip/scheduling

统一接口的两个排程可视化组件：**工单甘特图**（`GanttChart`）与**资源甘特图**（`ResourceSchedulerBoard`）。
通过一层「引擎无关」适配，使用 DHTMLX Gantt Professional Evaluation 试用版渲染。本仓库无商业部署、不涉及商业行为，永久保留现有试用版，不更换、不移除，也不承诺后续采购或自研；裁决见 [ADR 0036](../../../docs/adr/0036-dhtmlx-evaluation-permanent-retention.md)。

## 三层架构

```
Vue 组件层      GanttChart · ResourceSchedulerBoard（稳定 props/emits）
   │ 通过 provide/inject 选择引擎
SchedulingEngine 适配器接口   mount/setData/applyCommand/on/destroy(engine/engine.ts)
   └─ DhtmlxEngine   封装 DHTMLX 9.x 试用版原生核心（唯一产品引擎）
数据契约层      ScheduleModel + aps-mapper(model/) —— 引擎只消费它，不触及 APS 契约细节
```

DHTMLX 供应商包缺失时，`readOnly=true` 的 `SchedulingCanvas` 使用本包的只读 DOM 时间轴，仍展示真实任务、资源泳道、班次/日刻度、冲突与锁定状态；编辑态继续显示「排程引擎未加载」。该适配层不提供拖拽/改派，也没有引入第二套第三方甘特库。

**可替换性由 `engine/conformance.ts` 保证**：任何 `SchedulingEngine` 实现传入 `runEngineConformance(makeEngine)` 后，必须通过同一套契约测试。单元测试使用内联 `FakeEngine` 测试替身（仅存在于 `engine/conformance.selfcheck.test.ts`，不进入产品源码目录，也不导出）自校验；试用包存在时也会测试 `DhtmlxEngine`（见下）。

## DHTMLX Evaluation 供应商包

仓库已跟踪 `vendor/dhtmlx/dhtmlxgantt.es.js`、`dhtmlxgantt.css` 与 `dhtmlxgantt.d.ts`，保留厂商 Evaluation 许可声明、提示与水印。干净检出已有这些文件，无需手工拷贝供应商包或商业登录。

business-console 的 `vite.config.ts` 检测到供应商包后，会自动将 `@dhx/trial-gantt`（es.js）和
`@dhx/trial-gantt/codebase/dhtmlxgantt.css` 别名到供应商包；DHTMLX 布局/网格 CSS 在 `main.ts` 与
预览入口中以副作用方式导入。**CSS 子路径别名必须排在 `@dhx/trial-gantt` 之前**（Vite 字符串别名采用前缀匹配）。

适配器通过 `engine/dhtmlx/loader.ts` 动态加载；`engine-kind="auto"` 在检测到 DHTMLX 时使用它。
供应商包缺失时沿用上述只读时间轴或编辑态未加载提示，不回退至 NativeEngine。
条件别名让 `@dhx/trial-gantt` 可解析到供应商包或桩实现。

> 无供应商包时，只读组件渲染本包内置时间轴（`data-testid="readonly-schedule-timeline"`）；供应商包可用时，预览与页面使用 DHTMLX（`data-engine="dhtmlx"`）。

## 公开导出

`GanttChart` · `ResourceSchedulerBoard` · `useSchedulingPlan` · `useSchedulingEdits` ·
`toModel` · `toLockedAssignments` · `runEngineConformance` · `isDhtmlxAvailable` · 全部模型与引擎类型。

`toModel` 保留方案冻结的 `validationContext`，工序交期来自该上下文。纯函数 `evaluateDraft(model)` 输出逐工序浏览器建议：合并连续班次后的日历反馈、半开区间资源/工作中心分段并发容量、真实前序状态及交期差。占用计入 setup、利用率保留、固定/外部区间及不可用窗口，分段工序只计实际生产段；缺上下文如实返回无法核对。调用方随共享草案重算，不依赖引擎适配器或重预览响应，也不替代后端规则。`runEngineConformance` 仅证明引擎接口，APS 业务口径由 `draft-feedback.test.ts` 的直接断言证明。

公开 `command` 在引擎初始化期间由组件暂存，待引擎 mount 并接收当前模型后按序消费；因此加载期间的搜索、定位及选中也会在图面就绪后成立。供应商模块不可用时沿用既有不可用展示。

## 资源板端点调整

可编辑资源板的工序条左右端由 DHTMLX 原生 resize 处理，按小时吸附；整条移动继续使用自定义虚影、跨泳道改派和取消区。端点调整通过既有 `TaskDragPayload.kind='resize'` 延后上报，保留工序与资源身份，由消费方写回共享草案。锁定、只读、资源时间块和多段工序不能拉伸；零或负时长不上报。

## 编辑语义:锁定—重预览

后端（#206 BusinessScheduling）是确定性有限产能启发式算法，不做自动重排。前端「完整可编辑」具体为：
拖动 → 将分配标记为 `locked` 并移至新位置 → 调用 `preview` 围绕锁定项重算 → 差异/冲突高亮 → `release` 提交。
撤销/重做由前端计划状态栈实现。`preview`/`release` 由 `useSchedulingEdits` 以注入函数提供；本包不绑定后端问题定义的形状。

## 命令

```bash
pnpm -C frontend/packages/scheduling test       # vitest(内联 FakeEngine 跑契约,DHTMLX skip 除非装了试用包)
pnpm -C frontend/packages/scheduling typecheck   # vue-tsc
```
