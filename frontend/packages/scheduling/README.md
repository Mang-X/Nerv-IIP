# @nerv-iip/scheduling

统一接口的两个排程可视化组件：**工单甘特图**（`GanttChart`）与**资源甘特图**（`ResourceSchedulerBoard`）。
通过一层「引擎无关」适配，最小可行产品（MVP）使用 DHTMLX Gantt 试用专业版渲染；后续切换自研引擎时**只更换适配器**，组件层与业务层零改动。

## 三层架构(换引擎只换中间一层)

```
Vue 组件层      GanttChart · ResourceSchedulerBoard（稳定 props/emits）
   │ 通过 provide/inject 选择引擎
SchedulingEngine 适配器接口   mount/setData/applyCommand/on/destroy(engine/engine.ts)
   └─ DhtmlxEngine   封装 DHTMLX 9.x 试用版原生核心（唯一产品引擎）
数据契约层      ScheduleModel + aps-mapper(model/) —— 引擎只消费它，不触及 APS 契约细节
```

DHTMLX 供应商包缺失时（CI、文档构建或未配置本地试用包），`readOnly=true` 的 `SchedulingCanvas` 使用本包的只读 DOM 时间轴，仍展示真实任务、资源泳道、班次/日刻度、冲突与锁定状态；编辑态继续显示「排程引擎未加载」。该适配层不提供拖拽/改派，也没有引入第二套第三方甘特库。

**可替换性由 `engine/conformance.ts` 保证**：任何 `SchedulingEngine` 实现传入 `runEngineConformance(makeEngine)` 后，必须通过同一套契约测试。单元测试使用内联 `FakeEngine` 测试替身（仅存在于 `engine/conformance.selfcheck.test.ts`，不进入产品源码目录，也不导出）自校验；试用包存在时也会测试 `DhtmlxEngine`（见下）。

## 安装 DHTMLX 试用专业版（生产渲染必需；缺失时显示占位）

> **许可与供应商包移出计划（TODO #1270）**：负责人已确认持有 DHTMLX **商业许可**。下面的试用包流程
> 是过渡状态——正式授权包接入后，`vendor/dhtmlx/` 目录与本节安装步骤一并移除，适配层接口不变
> （可替换性由 `engine/conformance.ts` 保证）。进度跟踪见议题 #1270。

试用版**评估许可禁止分发**，库文件**不提交至 Git**。两种方式任选：

1. 从私有源安装（推荐）：
   ```bash
   npm config set @dhx:registry=https://npm.dhtmlx.com
   pnpm add @dhx/trial-gantt --filter @nerv-iip/scheduling
   ```
2. 本地试用包（已验证）：将 `gantt_trial/codebase/` 拷到 `frontend/packages/scheduling/vendor/dhtmlx/`（已在 gitignore 中排除）：
   ```powershell
   Copy-Item 'C:\…\gantt_trial\codebase\*' 'frontend\packages\scheduling\vendor\dhtmlx\' -Recurse -Force
   ```
   business-console 的 `vite.config.ts` 检测到供应商包后，会自动将 `@dhx/trial-gantt`（es.js）和
   `@dhx/trial-gantt/codebase/dhtmlxgantt.css` 别名到供应商包；DHTMLX 布局/网格 CSS 在 `main.ts` 与
   预览入口中以副作用方式导入。**CSS 子路径别名必须排在 `@dhx/trial-gantt` 之前**（Vite 字符串别名采用前缀匹配）。

无论哪种方式，适配器都通过 `engine/dhtmlx/loader.ts` 动态加载；`engine-kind="auto"` 在检测到 DHTMLX 时使用它，
否则**不挂载引擎、显示占位**（不再回退至 NativeEngine；正式自研引擎见后续 PR）。
条件别名让 `@dhx/trial-gantt` 始终可解析（供应商包或桩实现），保证无许可时 `vite build` 也不会失败。

> CI 无供应商包时，只读组件渲染本包内置时间轴（`data-testid="readonly-schedule-timeline"`）；本地接入供应商包后，预览与页面使用真实 DHTMLX（`data-engine="dhtmlx"`）。

## 换成自研引擎

1. 新建 `engine/<your>/YourEngine.ts`，实现 `SchedulingEngine` 接口（`engine/engine.ts`）。
2. 编写 `YourEngine.test.ts`：`describe('YourEngine conformance', () => runEngineConformance(() => new YourEngine()))`。
3. 在 `components/useEngine.ts` 的 `build()` 选择逻辑中加入它（必要时扩展 `EngineKind`）。
4. 组件层、组合式函数和业务页面**无需改动**。

## 公开导出

`GanttChart` · `ResourceSchedulerBoard` · `useSchedulingPlan` · `useSchedulingEdits` ·
`toModel` · `toLockedAssignments` · `runEngineConformance` · `isDhtmlxAvailable` · 全部模型与引擎类型。

## 编辑语义:锁定—重预览

后端（#206 BusinessScheduling）是确定性有限产能启发式算法，不做自动重排。前端「完整可编辑」具体为：
拖动 → 将分配标记为 `locked` 并移至新位置 → 调用 `preview` 围绕锁定项重算 → 差异/冲突高亮 → `release` 提交。
撤销/重做由前端计划状态栈实现。`preview`/`release` 由 `useSchedulingEdits` 以注入函数提供；本包不绑定后端问题定义的形状。

## 命令

```bash
pnpm -C frontend/packages/scheduling test       # vitest(内联 FakeEngine 跑契约,DHTMLX skip 除非装了试用包)
pnpm -C frontend/packages/scheduling typecheck   # vue-tsc
```
