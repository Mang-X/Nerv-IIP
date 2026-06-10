# @nerv-iip/scheduling

统一接口的排程可视化组件:**工单甘特**(`GanttChart`)与**资源排产板**(`ResourceSchedulerBoard`)。
通过一层「引擎无关」适配,MVP 用 DHTMLX Gantt 试用专业版渲染,后续切换自研引擎**只换适配器**,组件层与业务层零改动。

## 三层架构(换引擎只换中间一层)

```
Vue 组件层      GanttChart · ResourceSchedulerBoard · SchedulingWorkbench(稳定 props/emits/slots)
   │ provide/inject 选引擎
SchedulingEngine 适配器接口   mount/setData/applyCommand/on/destroy(engine/engine.ts)
   ├─ DhtmlxEngine   封装 DHTMLX 9.x 试用版 vanilla 核心
   └─ NativeEngine   轻量 SVG;CI/视觉/性能基线 + 自研对接位 + 降级兜底
数据契约层      ScheduleModel + aps-mapper(model/) —— 引擎只消费它,不碰 APS 契约细节
```

**可替换性由 `engine/conformance.ts` 背书**:任何 `SchedulingEngine` 实现传入 `runEngineConformance(makeEngine)` 必须通过同一套契约测试。NativeEngine 始终跑;DhtmlxEngine 在试用包存在时跑(见下)。

## 安装 DHTMLX 试用专业版(可选,默认不装也能跑 NativeEngine)

试用版**评估许可禁止分发**,库文件**不入 git**。两种方式任选:

1. 私有源安装(推荐):
   ```bash
   npm config set @dhx:registry=https://npm.dhtmlx.com
   pnpm add @dhx/trial-gantt --filter @nerv-iip/scheduling
   ```
2. 本地试用包:把 `gantt_trial/codebase/` 拷到 `frontend/packages/scheduling/vendor/dhtmlx/`(已 gitignore),
   并在 business-console `vite.config.ts` 把 `@dhx/trial-gantt` 别名到该路径。

无论哪种,适配器都通过 `engine/dhtmlx/loader.ts` 动态加载;**缺失时优雅回落 NativeEngine**。
business-console `vite.config.ts` 用条件 alias 让 `@dhx/trial-gantt` 始终可解析(真库或 stub),保证 `vite build` 在无许可时也不失败。

## 换成自研引擎

1. 新建 `engine/<your>/YourEngine.ts`,实现 `SchedulingEngine` 接口(`engine/engine.ts`)。
2. 写 `YourEngine.test.ts`:`describe('YourEngine conformance', () => runEngineConformance(() => new YourEngine()))`。
3. 在 `components/useEngine.ts` 的 `EngineKind` 与选择逻辑里加入它。
4. 组件层、composable、业务页面**无需改动**。

## 公开导出

`GanttChart` · `ResourceSchedulerBoard` · `SchedulingWorkbench` · `useSchedulingPlan` · `useSchedulingEdits` ·
`toModel` · `toLockedAssignments` · `runEngineConformance` · `isDhtmlxAvailable` · 全部模型与引擎类型。

## 编辑语义:锁定—重预览

后端(#206 BusinessScheduling)是确定性有限产能启发式,不做自动重排。前端「完整可编辑」落为:
拖动 → 标记分配 `locked` 到新位置 → 调 `preview` 围绕锁定项重算 → diff/冲突高亮 → `release` 提交。
撤销/重做为前端计划状态栈。`preview`/`release` 由 `useSchedulingEdits` 以注入函数提供,包本身不绑定后端问题定义形状。

## 命令

```bash
pnpm -C frontend/packages/scheduling test       # vitest(NativeEngine 全跑,DHTMLX skip 除非装了试用包)
pnpm -C frontend/packages/scheduling typecheck   # vue-tsc
```
