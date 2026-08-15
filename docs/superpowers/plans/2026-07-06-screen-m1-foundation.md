# 大屏 M1 薄共享地基实施计划

> **供代理执行者使用：**必须使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 子技能，逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**为 M1 三屏落一层极薄的共享地基——类型化 fetcher seam（获取器接缝）、`useAccessScope` 权限上下文（模拟角色，已为 IAM 接入做好准备）、`/` 大屏选择页 + 多工厂切换、共享主数据/fixture（夹具）——使随后三屏可独立并行开发。

**架构：**在既有 `apps/screen`（Vite-plus + Vue3 + vue-router 自动路由 + Pinia + `screen-kit` + `@nerv-iip/ui` 大屏层）之上新增 `src/data/`（contracts/mock/fetchers 三段式数据接缝）与 `src/access/`（访问范围）。`/` 改为权限驱动的大屏选择页；`/factory` 承接现有工厂演示（阶段 1 再精修），`/equipment`、`/line` 先建“建设中”占位页，供启动器链接与访问范围门禁落地。数据仍全部使用 mock，`#570` 就绪后逐屏只替换 `data/fetchers/*`。

**技术栈：**Vue 3.5 `<script setup>` + TS、Pinia 3、vue-router 5（自动路由）、`@vueuse/core`、`lucide-vue-next`、`@nerv-iip/ui` 大屏层、Vitest（`vp test`）。

## 全局约束

以下要求来自设计规格 `docs/superpowers/specs/2026-07-06-screen-m1-core-dashboards-design.md`，每个任务都必须遵守：

- **数据口径**：业务数据全走 mock，藏在类型化 fetcher 接口后；`#570` 就绪后逐屏换真实 `@nerv-iip/api-client`。仅轮询（`useScreenData`），无 SSE。
- **契约漂移防线**：mock 形状对齐 `@nerv-iip/api-client` business-console `types.gen.ts`；**禁止 `as` / 内联标注绕过契约**；无对应真实端点的 🟠 字段显式注释 `// 🟠 待 #570`。
- **设计哲学统一**（新建件硬门禁，依据 `frontend/packages/ui/src/components/screen/product.md` + `tokens.css`）：只用 `--sb-*` 令牌、无亮色模式；克制发光（辉光只给活数据）；动效只用 `--sb-ease` / `--sb-ease-emphasized`、press 收缩不回弹、每个动效有 `prefers-reduced-motion` 降级；数据驱动零 props 可渲染；不堆叠 `backdrop-filter`、不用大数字模板/侧边色条/渐变文字；shadcn/现有原版零改动，定制靠新建。
- **组件基准（权威来源，§1.6）**：复用/新建前先读设计系统文档站「大屏」分区 `frontend/apps/design-system/docs/components/screen/` + 组件源码 `frontend/packages/ui/src/components/screen/*.vue` 确认真实 props，不凭记忆。
- **诚实标注**：占位指标（OEE 性能/质量率=1、综合 OEE≈可用率）统一走占位 badge/tooltip 标注「≈可用率」「待 #570」；`IsSourceFresh` 驱动失联灰条防假绿；无闭环能力（安灯）标注「待 MAN-322」。
- **门禁（每任务/每屏）**：`pnpm -C frontend --filter @nerv-iip/screen typecheck && test && build` 全过；关键逻辑有 Vitest 测试；每屏另加预览实机截图确认。
- **分支**：本地基单独分支 `feat/screen-m1-foundation`；随后三屏各自 `mang/man-314-*` / `mang/man-317-*` / `mang/man-316-*`。
- **执行顺序（按依赖）**：**F2 → F1 → F3 → F4 → F5**（访问范围依赖主数据，故 F2 先于 F1）。每个任务提交时全树必须可编译。
- **测试命令**：统一运行整套 `pnpm -C frontend --filter @nerv-iip/screen test`（`vp test` 的名称过滤不保证透传；各任务“运行”项里的过滤名仅用于示意定位）。

---

## 文件结构（本计划落点）

```
frontend/apps/screen/src/
├── data/
│   ├── mock/
│   │   ├── masterdata.ts       # 新：工厂→车间→产线→工作中心→设备 映射字典 + 查询helpers
│   │   ├── fixtures.ts         # 新：seed 工具（jitter/spark/时钟/编号）
│   │   ├── scope.ts            # 新：ScreenKey + Persona 定义 + PERSONAS
│   │   └── factory.ts          # 移入：buildFactoryOverview()（原 mock/factory.ts 的 create*）
│   ├── contracts/
│   │   └── factory.ts          # 移入：FactoryOverview 等类型（原 mock/factory.ts 的 interface）
│   ├── fetchers/
│   │   └── factory.ts          # 移入：fetchFactoryOverview()（原 fetchFactoryOverviewMock）
│   └── screens.ts              # 新：大屏注册表 SCREENS（key→route/title/icon/accent）
├── access/
│   └── useAccessScope.ts       # 新：Pinia store，persona→可见工厂/车间/产线/大屏 + switchFactory
├── pages/
│   ├── index.vue               # 改写：/ 大屏选择页（launcher，读 scope）
│   ├── factory.vue             # 新（承接原 index.vue 工厂 demo 内容；Phase 1 精修）
│   ├── equipment.vue           # 新占位（"建设中"；Phase 2 替换）
│   └── line/
│       ├── index.vue           # 新占位（"建设中"；Phase 3 替换为产线选择器）
│       └── [id].vue            # 新占位（"建设中"；Phase 3 替换为单线详情）
├── router/index.ts             # 改：加 scope 路由守卫（不可见大屏→重定向 /）
└── mock/factory.ts             # 删除（内容已迁入 data/）
```

---

## 任务 F1：访问权限上下文 `useAccessScope`（模拟角色）

**文件：**
- 创建：`frontend/apps/screen/src/data/mock/scope.ts`
- 创建：`frontend/apps/screen/src/access/useAccessScope.ts`
- 测试：`frontend/apps/screen/src/access/useAccessScope.test.ts`

**接口：**
- 消费：`frontend/apps/screen/src/data/mock/masterdata.ts` 的 `FACTORIES / WORKSHOPS / LINES / workshopsByFactory`（**任务 F2 产出；本任务先用最小内联桩，F2 落地后切换**——为避免顺序耦合，F1 与 F2 可并行，但若先做 F1，则在 `scope.ts` 内联 2 个工厂/若干车间的最小常量，F2 完成后本文件改为从 `masterdata.ts` 导入并删桩）。
- 产出：
  - `type ScreenKey = 'factory' | 'equipment' | 'line'`
  - `interface Persona { id: string; label: string; factoryIds: string[]; workshopIds: string[] | 'all'; lineIds: string[] | 'all'; allowedScreens: ScreenKey[] }`
  - `const PERSONAS: Persona[]`、`const DEFAULT_PERSONA_ID: string`
  - `useAccessScope()` 存储 → `{ persona, personaId, factories, currentFactoryId, visibleWorkshops, visibleLines, allowedScreens, canSeeScreen(k), switchFactory(id), setPersona(id) }`

> **落地建议**：先做**任务 F2**（主数据）再做 F1，可省去内联桩。以下按“F2 已就绪”书写。

- [ ] **步骤 1：编写失败测试**

```ts
// frontend/apps/screen/src/access/useAccessScope.test.ts
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import { useAccessScope } from './useAccessScope'

describe('useAccessScope', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('默认 plant-admin：可见全部大屏与两个工厂', () => {
    const s = useAccessScope()
    expect(s.allowedScreens).toEqual(['factory', 'equipment', 'line'])
    expect(s.factories.map((f) => f.id)).toEqual(['F01', 'F02'])
    expect(s.canSeeScreen('equipment')).toBe(true)
  })

  it('switchFactory 只接受 scope 内工厂，越界忽略', () => {
    const s = useAccessScope()
    s.switchFactory('F02')
    expect(s.currentFactoryId).toBe('F02')
    s.switchFactory('F99')
    expect(s.currentFactoryId).toBe('F02')
  })

  it('workshop-lead persona：仅本车间产线，仅放行 line 屏', () => {
    const s = useAccessScope()
    s.setPersona('workshop-lead')
    expect(s.allowedScreens).toEqual(['line'])
    expect(s.canSeeScreen('factory')).toBe(false)
    expect(s.currentFactoryId).toBe('F01')
    // 可见车间收窄到 1 个，且可见产线均属该车间
    expect(s.visibleWorkshops.length).toBe(1)
    const wsId = s.visibleWorkshops[0].id
    expect(s.visibleLines.every((l) => l.workshopId === wsId)).toBe(true)
    expect(s.visibleLines.length).toBeGreaterThan(0)
  })
})
```

- [ ] **步骤 2：运行测试确认失败**

运行：`pnpm -C frontend --filter @nerv-iip/screen test -- useAccessScope`
预期：失败（`scope.ts` / `useAccessScope.ts` 尚不存在）

- [ ] **步骤 3：编写 `scope.ts`**

```ts
// frontend/apps/screen/src/data/mock/scope.ts
export type ScreenKey = 'factory' | 'equipment' | 'line'

export interface Persona {
  id: string
  label: string
  factoryIds: string[]
  /** 'all' = 该工厂全部车间；否则白名单 workshopId */
  workshopIds: string[] | 'all'
  /** 'all' = 可见车间下全部产线；否则白名单 lineId */
  lineIds: string[] | 'all'
  allowedScreens: ScreenKey[]
}

// 演示 persona：只证明"按权限进入 + 收窄车间/产线"，不写死真实策略；
// IAM 接入后本表由真实 claims 派生。见 spec §1.2。
export const PERSONAS: Persona[] = [
  {
    id: 'plant-admin',
    label: '全厂管理',
    factoryIds: ['F01', 'F02'],
    workshopIds: 'all',
    lineIds: 'all',
    allowedScreens: ['factory', 'equipment', 'line'],
  },
  {
    id: 'workshop-lead',
    label: '电池车间线长',
    factoryIds: ['F01'],
    workshopIds: ['WS-BATTERY'],
    lineIds: 'all',
    allowedScreens: ['line'],
  },
]

export const DEFAULT_PERSONA_ID = 'plant-admin'
```

- [ ] **步骤 4：编写 `useAccessScope.ts`**

```ts
// frontend/apps/screen/src/access/useAccessScope.ts
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  FACTORIES,
  type LineRef,
  LINES,
  type WorkshopRef,
  workshopsByFactory,
} from '@/data/mock/masterdata'
import { DEFAULT_PERSONA_ID, PERSONAS, type ScreenKey } from '@/data/mock/scope'

/**
 * 大屏访问上下文（mock persona，IAM-ready）。
 * persona → 可见工厂/车间/产线/大屏；launcher 与各屏都读它决定可见范围。
 * 接真 IAM 后改为从 claims 派生，消费方无需改动。见 spec §1.2。
 */
export const useAccessScope = defineStore('screen-access-scope', () => {
  const personaId = ref(DEFAULT_PERSONA_ID)
  const persona = computed(() => PERSONAS.find((p) => p.id === personaId.value) ?? PERSONAS[0])

  const factories = computed(() => FACTORIES.filter((f) => persona.value.factoryIds.includes(f.id)))
  const currentFactoryId = ref(factories.value[0]?.id ?? FACTORIES[0].id)

  const visibleWorkshops = computed<WorkshopRef[]>(() => {
    const all = workshopsByFactory(currentFactoryId.value)
    const ids = persona.value.workshopIds
    return ids === 'all' ? all : all.filter((w) => ids.includes(w.id))
  })

  const visibleLines = computed<LineRef[]>(() => {
    const wsIds = new Set(visibleWorkshops.value.map((w) => w.id))
    const all = LINES.filter((l) => wsIds.has(l.workshopId))
    const ids = persona.value.lineIds
    return ids === 'all' ? all : all.filter((l) => ids.includes(l.id))
  })

  const allowedScreens = computed<ScreenKey[]>(() => persona.value.allowedScreens)
  function canSeeScreen(k: ScreenKey): boolean {
    return allowedScreens.value.includes(k)
  }

  function switchFactory(id: string): void {
    if (factories.value.some((f) => f.id === id)) currentFactoryId.value = id
  }

  function setPersona(id: string): void {
    if (!PERSONAS.some((p) => p.id === id)) return
    personaId.value = id
    currentFactoryId.value = factories.value[0]?.id ?? currentFactoryId.value
  }

  return {
    persona,
    personaId,
    factories,
    currentFactoryId,
    visibleWorkshops,
    visibleLines,
    allowedScreens,
    canSeeScreen,
    switchFactory,
    setPersona,
  }
})
```

- [ ] **步骤 5：运行测试确认通过**

运行：`pnpm -C frontend --filter @nerv-iip/screen test -- useAccessScope`
预期：通过（3 个测试通过）

- [ ] **步骤 6：提交**

```bash
git add frontend/apps/screen/src/data/mock/scope.ts frontend/apps/screen/src/access/useAccessScope.ts frontend/apps/screen/src/access/useAccessScope.test.ts
git commit -m "feat(screen): 访问权限上下文 useAccessScope（mock persona，IAM-ready）"
```

---

## 任务 F2：共享主数据映射字典 + fixture（夹具）

**文件：**
- 创建：`frontend/apps/screen/src/data/mock/masterdata.ts`
- 创建：`frontend/apps/screen/src/data/mock/fixtures.ts`
- 测试：`frontend/apps/screen/src/data/mock/masterdata.test.ts`

**接口：**
- 产出（主数据）：
  - `interface FactoryRef { id; name }`、`interface WorkshopRef { id; code; name; factoryId; managerName }`、`interface LineRef { id; code; name; workshopId }`、`interface WorkCenterRef { id; code; name; workshopId; lineId }`、`interface DeviceRef { id; code; name; workshopId; lineId; workCenterId }`
  - 常量 `FACTORIES / WORKSHOPS / LINES / WORK_CENTERS / DEVICES`
  - 辅助函数：`workshopsByFactory(factoryId)`、`linesByWorkshop(workshopId)`、`devicesByWorkshop(workshopId)`、`devicesByLine(lineId)`、`workCentersByLine(lineId)`
- 产出（fixture）：`jitter(base, amp)`、`spark(n?)`、`clock(minsAgo?)`（`HH:mm`）、`seq(prefix, n, pad?)`

> **数据现实映射**（设计规格硬约束）：真实平台无车间/产线聚合维度，最细到 WorkCenter/Device，靠 `WorkCenter.WorkshopCode`、`DeviceAsset.LineCode` 映射。本字典即前端聚合所需的模拟映射权威来源，字段名对齐该语义。

- [ ] **步骤 1：编写失败测试**

```ts
// frontend/apps/screen/src/data/mock/masterdata.test.ts
import { describe, expect, it } from 'vitest'
import {
  DEVICES,
  devicesByLine,
  devicesByWorkshop,
  FACTORIES,
  linesByWorkshop,
  LINES,
  workshopsByFactory,
} from './masterdata'

describe('masterdata 映射字典', () => {
  it('两个工厂，且每个车间归属某工厂', () => {
    expect(FACTORIES.map((f) => f.id)).toEqual(['F01', 'F02'])
    expect(workshopsByFactory('F01').length).toBeGreaterThan(0)
  })

  it('产线归属车间、设备归属产线，映射自洽', () => {
    for (const l of LINES) {
      expect(devicesByLine(l.id).every((d) => d.lineId === l.id)).toBe(true)
    }
    const wsId = workshopsByFactory('F01')[0].id
    const lineIds = new Set(linesByWorkshop(wsId).map((l) => l.id))
    expect(devicesByWorkshop(wsId).every((d) => lineIds.has(d.lineId))).toBe(true)
  })

  it('电池车间存在且有产线（供 workshop-lead persona 用）', () => {
    expect(workshopsByFactory('F01').some((w) => w.id === 'WS-BATTERY')).toBe(true)
    expect(linesByWorkshop('WS-BATTERY').length).toBeGreaterThan(0)
    expect(DEVICES.length).toBeGreaterThan(10)
  })
})
```

- [ ] **步骤 2：运行测试确认失败**

运行：`pnpm -C frontend --filter @nerv-iip/screen test -- masterdata`
预期：失败（模块不存在）

- [ ] **步骤 3：编写 `fixtures.ts`**

```ts
// frontend/apps/screen/src/data/mock/fixtures.ts
// 大屏 mock 的 seed 工具：受控随机抖动 + 真实感编号/时钟。

export function jitter(base: number, amp: number): number {
  return Math.round(base + (Math.random() - 0.5) * amp)
}

export function spark(n = 11): number[] {
  return Array.from({ length: n }, (_, i) => jitter(58 + i * 2.5, 14))
}

/** 距现在 minsAgo 分钟的 HH:mm（默认当前）。大屏展示用，非持久时间。 */
export function clock(minsAgo = 0): string {
  const d = new Date(Date.now() - minsAgo * 60_000)
  const p = (x: number) => String(x).padStart(2, '0')
  return `${p(d.getHours())}:${p(d.getMinutes())}`
}

/** 生成如 WO-000123 的真实感编号。 */
export function seq(prefix: string, n: number, pad = 4): string {
  return `${prefix}-${String(n).padStart(pad, '0')}`
}
```

- [ ] **步骤 4：编写 `masterdata.ts`**

```ts
// frontend/apps/screen/src/data/mock/masterdata.ts
// 工厂→车间→产线→工作中心→设备 映射字典（mock）。
// 真实平台无 workshop/line 聚合维度，最细到 WorkCenter/Device；此处提供前端聚合所需映射真相源。
// 见 spec §1.1「数据现实」。

export interface FactoryRef {
  id: string
  name: string
}
export interface WorkshopRef {
  id: string
  code: string
  name: string
  factoryId: string
  managerName: string
}
export interface LineRef {
  id: string
  code: string
  name: string
  workshopId: string
}
export interface WorkCenterRef {
  id: string
  code: string
  name: string
  workshopId: string
  lineId: string
}
export interface DeviceRef {
  id: string
  code: string
  name: string
  workshopId: string
  lineId: string
  workCenterId: string
}

export const FACTORIES: FactoryRef[] = [
  { id: 'F01', name: '华东智造基地' },
  { id: 'F02', name: '华南制造中心' },
]

export const WORKSHOPS: WorkshopRef[] = [
  { id: 'WS-STAMP', code: 'WS-STAMP', name: '冲压车间', factoryId: 'F01', managerName: '李国强' },
  { id: 'WS-WELD', code: 'WS-WELD', name: '焊装车间', factoryId: 'F01', managerName: '王海涛' },
  { id: 'WS-PAINT', code: 'WS-PAINT', name: '涂装车间', factoryId: 'F01', managerName: '陈晓东' },
  { id: 'WS-ASSY', code: 'WS-ASSY', name: '总装车间', factoryId: 'F01', managerName: '赵敏' },
  { id: 'WS-BATTERY', code: 'WS-BATTERY', name: '电池车间', factoryId: 'F01', managerName: '孙立军' },
  { id: 'WS-INJECT', code: 'WS-INJECT', name: '注塑车间', factoryId: 'F02', managerName: '周文斌' },
  { id: 'WS-MACH', code: 'WS-MACH', name: '机加车间', factoryId: 'F02', managerName: '吴俊' },
]

// 每车间 1–2 条产线
export const LINES: LineRef[] = [
  { id: 'LN-STAMP-1', code: 'LN-STAMP-1', name: '冲压一线', workshopId: 'WS-STAMP' },
  { id: 'LN-STAMP-2', code: 'LN-STAMP-2', name: '冲压二线', workshopId: 'WS-STAMP' },
  { id: 'LN-WELD-1', code: 'LN-WELD-1', name: '焊装一线', workshopId: 'WS-WELD' },
  { id: 'LN-WELD-2', code: 'LN-WELD-2', name: '焊装二线', workshopId: 'WS-WELD' },
  { id: 'LN-PAINT-1', code: 'LN-PAINT-1', name: '涂装线', workshopId: 'WS-PAINT' },
  { id: 'LN-ASSY-1', code: 'LN-ASSY-1', name: '总装一线', workshopId: 'WS-ASSY' },
  { id: 'LN-ASSY-2', code: 'LN-ASSY-2', name: '总装二线', workshopId: 'WS-ASSY' },
  { id: 'LN-BAT-1', code: 'LN-BAT-1', name: '电芯线', workshopId: 'WS-BATTERY' },
  { id: 'LN-BAT-2', code: 'LN-BAT-2', name: 'PACK 线', workshopId: 'WS-BATTERY' },
  { id: 'LN-INJ-1', code: 'LN-INJ-1', name: '注塑一线', workshopId: 'WS-INJECT' },
  { id: 'LN-MACH-1', code: 'LN-MACH-1', name: '机加线', workshopId: 'WS-MACH' },
]

// 每产线 1 个工作中心（mock 简化）
export const WORK_CENTERS: WorkCenterRef[] = LINES.map((l) => ({
  id: `WC-${l.code.replace('LN-', '')}`,
  code: `WC-${l.code.replace('LN-', '')}`,
  name: `${l.name}工作中心`,
  workshopId: l.workshopId,
  lineId: l.id,
}))

// 每产线 2 台设备
const DEVICE_KINDS = ['主机', '辅机']
export const DEVICES: DeviceRef[] = LINES.flatMap((l, li) =>
  DEVICE_KINDS.map((kind, ki) => {
    const n = li * DEVICE_KINDS.length + ki + 1
    return {
      id: `DEV-${String(n).padStart(3, '0')}`,
      code: `DEV-${String(n).padStart(3, '0')}`,
      name: `${l.name}${kind}`,
      workshopId: l.workshopId,
      lineId: l.id,
      workCenterId: `WC-${l.code.replace('LN-', '')}`,
    }
  }),
)

export function workshopsByFactory(factoryId: string): WorkshopRef[] {
  return WORKSHOPS.filter((w) => w.factoryId === factoryId)
}
export function linesByWorkshop(workshopId: string): LineRef[] {
  return LINES.filter((l) => l.workshopId === workshopId)
}
export function workCentersByLine(lineId: string): WorkCenterRef[] {
  return WORK_CENTERS.filter((wc) => wc.lineId === lineId)
}
export function devicesByLine(lineId: string): DeviceRef[] {
  return DEVICES.filter((d) => d.lineId === lineId)
}
export function devicesByWorkshop(workshopId: string): DeviceRef[] {
  return DEVICES.filter((d) => d.workshopId === workshopId)
}
```

- [ ] **步骤 5：运行测试确认通过**

运行：`pnpm -C frontend --filter @nerv-iip/screen test -- masterdata`
预期：通过（3 个测试通过）

- [ ] **步骤 6：提交**

```bash
git add frontend/apps/screen/src/data/mock/masterdata.ts frontend/apps/screen/src/data/mock/fixtures.ts frontend/apps/screen/src/data/mock/masterdata.test.ts
git commit -m "feat(screen): 共享 masterdata 映射字典 + fixtures 工具"
```

---

## 任务 F3：数据接缝——工厂数据迁入 `data/`（契约/mock/fetcher 三段式）

**文件：**
- 创建：`frontend/apps/screen/src/data/contracts/factory.ts`
- 创建：`frontend/apps/screen/src/data/mock/factory.ts`
- 创建：`frontend/apps/screen/src/data/fetchers/factory.ts`
- 测试：`frontend/apps/screen/src/data/fetchers/factory.test.ts`
- （旧 `mock/factory.ts` 的删除与 `index.vue`/`factory.vue` 的导入切换在 **F4** 完成，保证本任务独立可编译）

**接口：**
- 消费：无（迁移现有 `mock/factory.ts` 内容，`create*`→`build*`、`fetch*Mock`→`fetch*`）。
- 产出：
  - 契约：`KpiItem / WorkshopStatus / OeeItem / AlarmItem / FactoryOverview`（原样，来自现有 `mock/factory.ts`）
  - mock：`buildFactoryOverview(): FactoryOverview`
  - fetcher：`fetchFactoryOverview(): Promise<FactoryOverview>`

> 说明：本任务只做**无损迁移 + 命名规整**，确立三段式接缝样板；工厂屏的**字段扩充（车间聚合/健康度色/超期工单等）与专属版式**在阶段 1 完成。

- [ ] **步骤 1：编写失败测试**

```ts
// frontend/apps/screen/src/data/fetchers/factory.test.ts
import { describe, expect, it } from 'vitest'
import { fetchFactoryOverview } from './factory'

describe('fetchFactoryOverview', () => {
  it('返回工厂总览形状（kpis/workshops/oee/alarms 非空）', async () => {
    const ov = await fetchFactoryOverview()
    expect(ov.kpis.length).toBeGreaterThan(0)
    expect(ov.workshops.length).toBeGreaterThan(0)
    expect(ov.oee.length).toBeGreaterThan(0)
    expect(ov.alarms.length).toBeGreaterThan(0)
    expect(ov.workshops[0]).toHaveProperty('tone')
  })
})
```

- [ ] **步骤 2：运行测试确认失败**

运行：`pnpm -C frontend --filter @nerv-iip/screen test -- fetchers/factory`
预期：失败（模块不存在）

- [ ] **步骤 3：创建 `data/contracts/factory.ts`**（把现有 `mock/factory.ts` 的接口原样迁入）

```ts
// frontend/apps/screen/src/data/contracts/factory.ts
// 工厂总览大屏数据契约。字段对齐 @nerv-iip/api-client business-console；
// 🟠 车间维度聚合当前无真实端点（见 #570），mock 先按此形状产出。
export interface KpiItem {
  label: string
  value: number
  unit?: string
  delta?: string
  spark?: number[]
}
export interface WorkshopStatus {
  name: string
  state: string
  label: string
  tone: 'run' | 'idle' | 'alarm'
  plan: string
  actual: string
  rate: string
  downtime: string
}
export interface OeeItem {
  label: string
  value: number
}
export interface AlarmItem {
  id: string
  level: 'critical' | 'warning'
  text: string
  time: string
}
export interface FactoryOverview {
  kpis: KpiItem[]
  workshops: WorkshopStatus[]
  oee: OeeItem[]
  alarms: AlarmItem[]
}
```

- [ ] **步骤 4：创建 `data/mock/factory.ts`**（迁移 `createFactoryOverview`→`buildFactoryOverview`，改用 `fixtures` 的 `jitter/spark`）

```ts
// frontend/apps/screen/src/data/mock/factory.ts
import type { FactoryOverview } from '@/data/contracts/factory'
import { jitter, spark } from './fixtures'

export function buildFactoryOverview(): FactoryOverview {
  const output = jitter(12840, 520)
  const rate = +(95 + Math.random() * 3.5).toFixed(1)
  const oee = +(80 + Math.random() * 5).toFixed(1)
  return {
    kpis: [
      { label: '今日产量', value: output, unit: '件', delta: '较昨日 +6.2%', spark: spark() },
      { label: '计划达成率', value: rate, unit: '%', delta: '较昨日 +1.4%', spark: spark() },
      { label: '综合 OEE', value: oee, unit: '%', delta: '较昨日 +2.1%', spark: spark() },
      { label: '未恢复告警', value: 3, unit: '条', delta: '较昨日 -2', spark: spark() },
    ],
    workshops: [
      { name: '冲压车间', state: '运行', label: '运行中', tone: 'run', plan: '4,200', actual: '4,032', rate: '96%', downtime: '12 min' },
      { name: '焊装车间', state: '运行', label: '运行中', tone: 'run', plan: '3,800', actual: '3,610', rate: '95%', downtime: '8 min' },
      { name: '涂装车间', state: '待机', label: '换型待机', tone: 'idle', plan: '2,600', actual: '2,106', rate: '81%', downtime: '34 min' },
      { name: '总装车间', state: '运行', label: '运行中', tone: 'run', plan: '3,200', actual: '2,976', rate: '93%', downtime: '15 min' },
      { name: '电池车间', state: '报警', label: '设备报警', tone: 'alarm', plan: '1,800', actual: '1,260', rate: '70%', downtime: '46 min' },
      { name: '注塑车间', state: '运行', label: '运行中', tone: 'run', plan: '2,400', actual: '2,160', rate: '90%', downtime: '10 min' },
    ],
    oee: [
      { label: '可用率', value: oee },
      { label: '性能率', value: jitter(92, 4) },
      { label: '良品率', value: +(97 + Math.random() * 2).toFixed(1) },
    ],
    alarms: [
      { id: 'AL-2041', level: 'critical', text: '电池车间 PACK-03 急停触发', time: '14:31' },
      { id: 'AL-2040', level: 'warning', text: '注塑车间 IMM-07 料温偏高', time: '14:27' },
      { id: 'AL-2039', level: 'warning', text: '涂装车间物料齐套不足', time: '14:22' },
      { id: 'AL-2038', level: 'warning', text: '焊装车间 WS-02 节拍低于目标', time: '14:18' },
      { id: 'AL-2037', level: 'critical', text: '总装车间 AGV-11 通讯中断', time: '14:09' },
      { id: 'AL-2036', level: 'warning', text: '冲压车间模具寿命预警', time: '14:01' },
    ],
  }
}
```

- [ ] **步骤 5：创建 `data/fetchers/factory.ts`**

```ts
// frontend/apps/screen/src/data/fetchers/factory.ts
// 工厂总览 fetcher。当前 mock；#570 就绪后只换本文件实现，契约与页面不变。
import type { FactoryOverview } from '@/data/contracts/factory'
import { buildFactoryOverview } from '@/data/mock/factory'

export async function fetchFactoryOverview(): Promise<FactoryOverview> {
  await new Promise((resolve) => setTimeout(resolve, 280))
  return buildFactoryOverview()
}
```

- [ ] **步骤 6：运行测试确认通过（不删旧文件、不改 index.vue，保持可编译）**

运行：`pnpm -C frontend --filter @nerv-iip/screen test`
预期：全部通过；新增的 `fetchers/factory`（1 个测试）通过。本任务**只新增 `data/` 文件**，不删除 `mock/factory.ts`、不修改 `pages/index.vue`，因此全树可编译（`data/mock/factory.ts` 与旧 `mock/factory.ts` 暂时重复，由 F4 删除旧文件并切换导入）。

- [ ] **步骤 7：提交**

```bash
git add frontend/apps/screen/src/data/
git commit -m "refactor(screen): 新增 data/ 三段式 seam（工厂契约/mock/fetcher）"
```

---

## 任务 F4：`/` 大屏选择页 + 屏注册表 + 访问范围路由守卫 + 占位页

**文件：**
- 创建：`frontend/apps/screen/src/data/screens.ts`
- 创建：`frontend/apps/screen/src/pages/factory.vue`（承接原 `index.vue` 工厂演示，导入改为 `@/data/*`）
- 改写：`frontend/apps/screen/src/pages/index.vue`（启动器）
- 创建：`frontend/apps/screen/src/pages/equipment.vue`（占位）
- 创建：`frontend/apps/screen/src/pages/line/index.vue`（占位）
- 创建：`frontend/apps/screen/src/pages/line/[id].vue`（占位）
- 修改：`frontend/apps/screen/src/router/index.ts`（访问范围守卫）
- 删除：`frontend/apps/screen/src/mock/factory.ts`（内容已在 F3 迁入 `data/`；本任务切换导入后删除）
- 测试：`frontend/apps/screen/src/data/screens.test.ts`

**接口：**
- 消费：`useAccessScope`（F1）、`SCREENS`（本任务）、`fetchFactoryOverview`/契约（F3）。
- 产出：
  - `interface ScreenDef { key: ScreenKey; route: string; title: string; desc: string; icon: string; accent: 'cyan'|'green'|'amber'|'red'|'indigo' }`
  - `const SCREENS: ScreenDef[]`
  - `screenForPath(path: string): ScreenKey | undefined`（纯函数，供守卫与测试）

- [ ] **步骤 1：编写失败测试**

```ts
// frontend/apps/screen/src/data/screens.test.ts
import { describe, expect, it } from 'vitest'
import { SCREENS, screenForPath } from './screens'

describe('screens 注册表 + screenForPath', () => {
  it('三块大屏均注册，route 唯一', () => {
    expect(SCREENS.map((s) => s.key).sort()).toEqual(['equipment', 'factory', 'line'])
    const routes = SCREENS.map((s) => s.route)
    expect(new Set(routes).size).toBe(routes.length)
  })

  it('screenForPath 命中大屏路由与其子路由', () => {
    expect(screenForPath('/factory')).toBe('factory')
    expect(screenForPath('/equipment')).toBe('equipment')
    expect(screenForPath('/line')).toBe('line')
    expect(screenForPath('/line/LN-BAT-1')).toBe('line')
    expect(screenForPath('/')).toBeUndefined()
    expect(screenForPath('/login')).toBeUndefined()
  })
})
```

- [ ] **步骤 2：运行测试确认失败**

运行：`pnpm -C frontend --filter @nerv-iip/screen test -- screens`
预期：失败（模块不存在）

- [ ] **步骤 3：将工厂演示迁移到 `pages/factory.vue`**

把当前 `pages/index.vue` 的**全部内容**原样复制到新文件 `pages/factory.vue`，只修改数据导入行：

```diff
- import { createFactoryOverview, type FactoryOverview, fetchFactoryOverviewMock } from '@/mock/factory'
+ import type { FactoryOverview } from '@/data/contracts/factory'
+ import { buildFactoryOverview } from '@/data/mock/factory'
+ import { fetchFactoryOverview } from '@/data/fetchers/factory'
```
并把用到处改名：`fetchFactoryOverviewMock`→`fetchFactoryOverview`、`createFactoryOverview`→`buildFactoryOverview`。其余模板/样式不动。

- [ ] **步骤 4：编写 `data/screens.ts`**

```ts
// frontend/apps/screen/src/data/screens.ts
import type { ScreenKey } from '@/data/mock/scope'

export interface ScreenDef {
  key: ScreenKey
  route: string
  title: string
  desc: string
  icon: string // lucide-vue-next 图标名
  accent: 'cyan' | 'green' | 'amber' | 'red' | 'indigo'
}

export const SCREENS: ScreenDef[] = [
  { key: 'factory', route: '/factory', title: '工厂总览', desc: '全厂健康度 · 指挥中心', icon: 'LayoutDashboard', accent: 'cyan' },
  { key: 'equipment', route: '/equipment', title: '设备监控', desc: '设备健康 + 维修作战图', icon: 'Cpu', accent: 'green' },
  { key: 'line', route: '/line', title: '产线监控', desc: '现场作业状态监控屏', icon: 'Factory', accent: 'amber' },
]

/** 路径归属哪块大屏（含子路由如 /line/[id]）；非大屏路由返回 undefined。 */
export function screenForPath(path: string): ScreenKey | undefined {
  const hit = SCREENS.find((s) => path === s.route || path.startsWith(`${s.route}/`))
  return hit?.key
}
```

- [ ] **步骤 5：将 `pages/index.vue` 改写为启动器**

> 视觉可在预览中微调；此为可编译的起点，遵守 `--sb-*` / 克制发光 / 减弱动态效果。图标使用 `lucide-vue-next` 动态组件。

```vue
<script setup lang="ts">
import * as icons from 'lucide-vue-next'
import { ScreenPanel } from '@nerv-iip/ui'
import { type Component, computed } from 'vue'
import { RouterLink } from 'vue-router'
import { useAccessScope } from '@/access/useAccessScope'
import { SCREENS } from '@/data/screens'

const scope = useAccessScope()
const cards = computed(() => SCREENS.filter((s) => scope.canSeeScreen(s.key)))
// lucide 图标按名取用；显式转 Component 以过 vue-tsc 的 <component :is> 类型校验。
const iconMap = icons as unknown as Record<string, Component>
function iconOf(name: string): Component {
  return iconMap[name] ?? iconMap.SquareDashed
}
</script>

<template>
  <div class="launcher">
    <header class="launcher__top">
      <div class="brand">
        <span class="brand__title">Nerv-IIP 工业数据大屏</span>
        <span class="brand__sub">生产指挥中心</span>
      </div>
      <div v-if="scope.factories.length > 1" class="factory-switch">
        <button
          v-for="f in scope.factories"
          :key="f.id"
          type="button"
          :class="['factory-switch__btn', { active: f.id === scope.currentFactoryId }]"
          @click="scope.switchFactory(f.id)"
        >
          {{ f.name }}
        </button>
      </div>
    </header>

    <main class="launcher__grid">
      <RouterLink v-for="s in cards" :key="s.key" :to="s.route" class="card-link">
        <ScreenPanel :accent="s.accent" class="card">
          <component :is="iconOf(s.icon)" class="card__icon" :size="46" :stroke-width="1.4" />
          <div class="card__title">{{ s.title }}</div>
          <div class="card__desc">{{ s.desc }}</div>
        </ScreenPanel>
      </RouterLink>
      <p v-if="cards.length === 0" class="empty">当前账号无可见大屏</p>
    </main>
  </div>
</template>

<style scoped>
.launcher {
  min-height: 100vh;
  background: var(--sb-bg);
  color: var(--sb-text);
  padding: 48px 64px;
  display: flex;
  flex-direction: column;
  gap: 40px;
}
.launcher__top {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.brand__title {
  font-size: 30px;
  font-weight: 600;
  letter-spacing: 0.06em;
}
.brand__sub {
  margin-left: 14px;
  color: var(--sb-muted);
}
.factory-switch {
  display: flex;
  gap: 8px;
}
.factory-switch__btn {
  padding: 8px 18px;
  border: 1px solid var(--sb-line-2);
  border-radius: var(--sb-radius);
  background: transparent;
  color: var(--sb-muted);
  cursor: pointer;
  transition: color 0.18s var(--sb-ease), border-color 0.18s var(--sb-ease);
}
.factory-switch__btn.active {
  color: var(--sb-cyan);
  border-color: var(--sb-cyan-dim);
}
.factory-switch__btn:active {
  transform: scale(0.985);
}
.launcher__grid {
  flex: 1;
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 28px;
  align-content: start;
}
.card-link {
  text-decoration: none;
}
.card {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 40px 32px;
  min-height: 220px;
  cursor: pointer;
  transition: transform 0.2s var(--sb-ease-emphasized);
}
.card-link:hover .card {
  transform: translateY(-4px);
}
.card__icon {
  color: var(--sb-cyan);
}
.card__title {
  font-size: 24px;
  font-weight: 600;
}
.card__desc {
  color: var(--sb-muted);
  font-size: 15px;
}
.empty {
  color: var(--sb-faint);
}
@media (prefers-reduced-motion: reduce) {
  .card,
  .factory-switch__btn {
    transition: none;
  }
  .card-link:hover .card {
    transform: none;
  }
}
</style>
```

- [ ] **步骤 6：创建 3 个占位页**

`pages/equipment.vue`、`pages/line/index.vue`、`pages/line/[id].vue` 使用同一占位骨架（分别修改标题）：

```vue
<!-- pages/equipment.vue（line/index.vue、line/[id].vue 同构，改 title/subtitle） -->
<script setup lang="ts">
import ScreenLayout from '@/layouts/ScreenLayout.vue'
</script>

<template>
  <ScreenLayout title="设备监控大屏" screen="设备 · 建设中">
    <div class="ph">
      <p class="ph__t">设备监控大屏</p>
      <p class="ph__s">建设中（M1 Phase 2）</p>
    </div>
  </ScreenLayout>
</template>

<style scoped>
.ph {
  height: 100%;
  display: flex;
  flex-direction: column;
  gap: 10px;
  align-items: center;
  justify-content: center;
  color: var(--sb-muted);
}
.ph__t {
  font-size: 28px;
  color: var(--sb-text-2);
}
.ph__s {
  color: var(--sb-faint);
}
</style>
```

（`line/[id].vue` 可用 `useRoute().params.id` 显示"产线 {id} · 建设中"，但占位阶段可省。）

- [ ] **步骤 7：添加访问范围路由守卫**

```ts
// frontend/apps/screen/src/router/index.ts
import { createRouter, createWebHistory } from 'vue-router'
import { handleHotUpdate, routes } from 'vue-router/auto-routes'
import { useAccessScope } from '@/access/useAccessScope'
import { screenForPath } from '@/data/screens'

export const router = createRouter({
  history: createWebHistory(),
  routes,
})

// 权限守卫：目标是某大屏、但当前 persona 不可见 → 回大屏选择页。
// pinia 在 main.ts 中先于 router 安装，导航期 store 可用。
router.beforeEach((to) => {
  const key = screenForPath(to.path)
  if (!key) return true
  const scope = useAccessScope()
  return scope.canSeeScreen(key) ? true : '/'
})

if (import.meta.hot) {
  handleHotUpdate(router)
}
```

- [ ] **步骤 8：运行测试 + 全部门禁**

运行：`pnpm -C frontend --filter @nerv-iip/screen test -- screens`
预期：通过（2 个测试通过）

运行：`pnpm -C frontend --filter @nerv-iip/screen typecheck`
预期：通过（无悬空导入）

- [ ] **步骤 9：删除旧文件并提交**

```bash
git rm frontend/apps/screen/src/mock/factory.ts
git add frontend/apps/screen/src/data/screens.ts frontend/apps/screen/src/data/screens.test.ts frontend/apps/screen/src/pages/ frontend/apps/screen/src/router/index.ts
git commit -m "feat(screen): / 大屏选择页 + 屏注册表 + scope 路由守卫 + 占位页（工厂迁 /factory，删旧 mock）"
```

> 提交前运行 `pnpm -C frontend --filter @nerv-iip/screen typecheck`，确认 `pages/index.vue` 已无 `@/mock/factory` 悬空导入，且 `pages/factory.vue` 已切换到 `@/data/*`。

---

## 任务 F5：地基验收（门禁 + 实机预览）

**文件：**无新增文件（验证任务）。

- [ ] **步骤 1：全量门禁**

运行：`pnpm -C frontend --filter @nerv-iip/screen typecheck && pnpm -C frontend --filter @nerv-iip/screen test && pnpm -C frontend --filter @nerv-iip/screen build`
预期：三者全部通过；测试包括 useAccessScope（3）+ masterdata（3）+ fetchers/factory（1）+ screens（2）+ 既有 screen-kit（13）。

- [ ] **步骤 2：实机预览截图**

用预览工具启动 `@nerv-iip/screen`（5128），核对：
- `/` 启动器渲染三张卡片（plant-admin 角色），多工厂切换器可点击；
- 点击卡片进入 `/factory`（现有工厂演示）、`/equipment`、`/line`（占位）；
- 临时把 `DEFAULT_PERSONA_ID` 改为 `workshop-lead` 验证：启动器只显示产线卡，直接访问 `/factory` 会被重定向回 `/`（验证后改回 `plant-admin`）；
- 控制台和服务器均无报错。

- [ ] **步骤 3：汇报并交回主控**

汇报门禁结果和截图；地基分支 `feat/screen-m1-foundation` 已就绪，等待评审合入，随后三屏并行。

---

## 自审（对照设计规格）

- **§1.1 fetcher seam（获取器接缝）**：F3 三段式（contracts/mock/fetchers）落地并以工厂为样板 ✅
- **§1.2 useAccessScope + 多工厂 + 2 个角色**：F1 ✅（门禁测试覆盖 plant-admin / workshop-lead）
- **§1.3 `/` 启动器 + 工厂切换 + 现有 index 内容迁移至 /factory**：F4 ✅
- **§1.4 组件复用/新建门禁**：启动器复用 `ScreenPanel`，新建件遵守 `--sb-*`/减弱动态效果（全局约束）✅
- **§1.5 诚实标注**：地基无占位指标；约定已写入全局约束，在大屏阶段执行 ✅
- **§1.6 权威来源**：全局约束明确“动手前读文档站/源码”✅
- **占位扫描**：无 TBD/TODO；`line/[id].vue` 占位是有意的 M1 阶段 3 前置，不是计划占位 ✅
- **类型一致**：`ScreenKey`/`Persona`/`ScreenDef`/`screenForPath` 在 F1/F4 定义与消费一致 ✅

---

## 下游三屏子计划（独立子系统，各自执行前即时产出代码精确计划）

三屏是独立子系统（设计规格决策），按 writing-plans 的范围检查各自形成一份计划；在对应分支执行前，根据**已落地地基接口** + §1.6 真实组件 + 首屏预览反馈即时产出，避免现在内联臆测组件 props。每份计划使用统一结构：

1. **契约** `data/contracts/<screen>.ts`：按 spec 该屏 ✅/🟡/🟠 分层字段，🟠 显式 `// 🟠 待 #570`。
2. **mock 构建器** `data/mock/<screen>.ts`：从 `masterdata`/`fixtures` 实现**前端聚合真实算法**（车间/产线/设备汇总、健康度色合成、状态计数、达成率、节拍反推），可采用 TDD。
3. **fetcher** `data/fetchers/<screen>.ts`：`useScreenData` 挂接；换真实 = 换本文件。
4. **聚合单测**：汇总 / 健康度色 / 状态计数 / 节拍等确定性逻辑。
5. **页面 + 专属组合件** `pages/*` + `components/screen-blocks/*`：先读 §1.6 确认真实 props，复用优先、缺件按大屏风格新建；针对该屏内容单独设计版式（不模板化）；诚实标注。
6. **门禁 + 预览截图**：typecheck/test/build + 实机远距可读性/五态色/诚实标注核对；单屏单分支单 PR。

- **阶段 1 · 工厂总览**（MAN-314，分支 `mang/man-314-*`）：车间状态矩阵（6 个 🟡 字段 + 健康度色默认规则见设计规格 §二）+ 全厂 KPI 带 + 告警/停机流；工厂范围使用 `useAccessScope.visibleWorkshops`。
- **阶段 2 · 设备监控**（MAN-317，分支 `mang/man-317-*`）：设备状态全景墙（运行/待机/停机/报警/断线五态 + 计数）+ 未恢复报警表 + 维修进度 + 稼动率（标注≈可用率）/MTBF/MTTR + PM/点检；`deviceAssetIds≤50` 分批策略在契约中体现。
- **阶段 3 · 产线监控**（MAN-316，分支 `mang/man-316-*`）：`/line` 选择器（按访问范围收窄）+ `/line/[id]` 超大红绿灯 + 当班产量/达成环 + 停机/报警横幅 + 节拍偏差 + 距交付倒计时；诚实标注“监控屏非安灯，闭环待 MAN-322”。
