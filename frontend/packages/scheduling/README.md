# @nerv-iip/scheduling

统一接口的两个排程可视化组件：**工单甘特图**（`GanttChart`）与**资源甘特图**（`ResourceSchedulerBoard`）。
通过一层「引擎无关」适配,MVP 用 DHTMLX Gantt 试用专业版渲染,后续切换自研引擎**只换适配器**,组件层与业务层零改动。

## 三层架构(换引擎只换中间一层)

```
Vue 组件层      GanttChart · ResourceSchedulerBoard（稳定 props/emits）
   │ provide/inject 选引擎
SchedulingEngine 适配器接口   mount/setData/applyCommand/on/destroy(engine/engine.ts)
   └─ DhtmlxEngine   封装 DHTMLX 9.x 试用版 vanilla 核心(唯一产品引擎)
数据契约层      ScheduleModel + aps-mapper(model/) —— 引擎只消费它,不碰 APS 契约细节
```

DHTMLX vendor 缺失时(CI / 文档构建 / 未配置本地试用包),`readOnly=true` 的 `SchedulingCanvas` 使用本 package 的只读 DOM 时间轴,仍展示真实任务、资源泳道、班次/日刻度、冲突与锁定状态；编辑态继续显示「排程引擎未加载」。该适配层不提供拖拽/改派，也没有引入第二套第三方甘特库。

**可替换性由 `engine/conformance.ts` 背书**:任何 `SchedulingEngine` 实现传入 `runEngineConformance(makeEngine)` 必须通过同一套契约测试。单测里用一个内联 `FakeEngine` 测试替身(仅存在于 `engine/conformance.selfcheck.test.ts`,不入产品 src、不导出)自校验;DhtmlxEngine 在试用包存在时也跑(见下)。

## 安装 DHTMLX 试用专业版(生产渲染必需;缺失则显示占位)

试用版**评估许可禁止分发**,库文件**不入 git**。两种方式任选:

1. 私有源安装(推荐):
   ```bash
   npm config set @dhx:registry=https://npm.dhtmlx.com
   pnpm add @dhx/trial-gantt --filter @nerv-iip/scheduling
   ```
2. 本地试用包(已验证):把 `gantt_trial/codebase/` 拷到 `frontend/packages/scheduling/vendor/dhtmlx/`(已 gitignore):
   ```powershell
   Copy-Item 'C:\…\gantt_trial\codebase\*' 'frontend\packages\scheduling\vendor\dhtmlx\' -Recurse -Force
   ```
   business-console `vite.config.ts` 检测到 vendor 后自动把 `@dhx/trial-gantt`(es.js)和
   `@dhx/trial-gantt/codebase/dhtmlxgantt.css` 别名到 vendor;DHTMLX 布局/网格 CSS 在 `main.ts` 与
   预览入口 side-effect 导入。**css 子路径 alias 必须排在 `@dhx/trial-gantt` 之前**(Vite 字符串 alias 是前缀匹配)。

无论哪种,适配器都通过 `engine/dhtmlx/loader.ts` 动态加载;`engine-kind="auto"` 在检测到 DHTMLX 时用之,
否则**不挂载引擎、显示占位**(不再回落 NativeEngine;正式自研引擎见后续 PR)。
条件 alias 让 `@dhx/trial-gantt` 始终可解析(vendor 或 stub),保证无许可时 `vite build` 也不失败。

> CI 无 vendor 时只读组件渲染 package 内置时间轴(`data-testid="readonly-schedule-timeline"`);本地接入 vendor 后预览/页面用真实 DHTMLX(`data-engine="dhtmlx"`)。

## 换成自研引擎

1. 新建 `engine/<your>/YourEngine.ts`,实现 `SchedulingEngine` 接口(`engine/engine.ts`)。
2. 写 `YourEngine.test.ts`:`describe('YourEngine conformance', () => runEngineConformance(() => new YourEngine()))`。
3. 在 `components/useEngine.ts` 的 `build()` 选择逻辑里加入它(必要时扩展 `EngineKind`)。
4. 组件层、composable、业务页面**无需改动**。

## 公开导出

`GanttChart` · `ResourceSchedulerBoard` · `useSchedulingPlan` · `useSchedulingEdits` ·
`toModel` · `toLockedAssignments` · `runEngineConformance` · `isDhtmlxAvailable` · 全部模型与引擎类型。

## 编辑语义:锁定—重预览

后端(#206 BusinessScheduling)是确定性有限产能启发式,不做自动重排。前端「完整可编辑」落为:
拖动 → 标记分配 `locked` 到新位置 → 调 `preview` 围绕锁定项重算 → diff/冲突高亮 → `release` 提交。
撤销/重做为前端计划状态栈。`preview`/`release` 由 `useSchedulingEdits` 以注入函数提供,包本身不绑定后端问题定义形状。

## 命令

```bash
pnpm -C frontend/packages/scheduling test       # vitest(内联 FakeEngine 跑契约,DHTMLX skip 除非装了试用包)
pnpm -C frontend/packages/scheduling typecheck   # vue-tsc
```
