# MES 列表显示名前端解析（前端阶段 1 · F2）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MES 列表页把工作中心码（`WC-ASSY`）、SKU/物料码（`SKU-…`）解析成主数据真名显示，用前端 master-data 兜底 facade 当前为 null 的 `*Name`（#461 残留）。

**Architecture:** 新建 `useMesDisplayNames` composable，从 `useBusinessSkus` + `useBusinessMasterDataResources('work-center')` 建 code→displayName 映射，提供 `resolveSku/resolveWorkCenter`。各 MES 页列 accessor 改为 `r.<facadeName> ?? resolve(r.<code> ?? r.<id>) ?? '无'`——**与后端 #461 前向兼容**：后端一旦回填 `*Name`，accessor 自动优先用之，本兜底极少命中。

**Tech Stack:** Vue 3 `<script setup>` + TS、`@pinia/colada`（查询缓存，重复加载不重复请求）、`@nerv-iip/ui` DataTable accessor。

---

## File Structure

| 文件 | 责任 |
|---|---|
| `src/composables/mes/useMesDisplayNames.ts`（新建） | 加载 SKU + 工作中心主数据，提供 `resolveSku/resolveWorkCenter` |
| `src/pages/mes/operation-tasks.vue` | 工作中心列 accessor 接解析 |
| `src/pages/mes/wip.vue` | 工作中心列 accessor 接解析 |
| `src/pages/mes/work-orders/index.vue` | SKU 副行接 `resolveSku`；工序行工作中心接 `resolveWorkCenter` |
| `src/pages/mes/receipts.vue` | 成品 SKU 列接 `resolveSku` |
| `src/pages/mes/dispatch.vue` | 工作中心列接 `resolveWorkCenter` |
| `src/pages/mes/materials.vue` | 物料列接 `resolveSku`（物料码即 SKU 码；不确定则保留 code，不报错） |

---

## Task 1: 建 useMesDisplayNames composable

**Files:**
- Create: `frontend/apps/business-console/src/composables/mes/useMesDisplayNames.ts`

- [ ] **Step 1: 写 composable**

```ts
import { computed } from 'vue'
import { useBusinessSkus, useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'

/**
 * MES 列表显示名前端解析（兜底 facade 当前为 null 的 *Name，见 #461）。
 * 用法：accessor 写 `r.workCenterName ?? resolveWorkCenter(r.workCenterCode ?? r.workCenterId) ?? '无'`，
 * 后端回填 *Name 后自动优先用之，本兜底极少命中、可随后端落地移除。
 */
export function useMesDisplayNames() {
  const { skus } = useBusinessSkus()
  const { resources: workCenters } = useBusinessMasterDataResources('work-center')

  const skuByCode = computed(() => {
    const m = new Map<string, string>()
    for (const s of skus.value) if (s.code) m.set(s.code, s.displayName ?? s.code)
    return m
  })
  const workCenterByCode = computed(() => {
    const m = new Map<string, string>()
    for (const w of workCenters.value) if (w.code) m.set(w.code, w.displayName ?? w.code)
    return m
  })

  function resolveSku(code?: string | null): string | undefined {
    if (!code) return undefined
    return skuByCode.value.get(code) ?? code
  }
  function resolveWorkCenter(code?: string | null): string | undefined {
    if (!code) return undefined
    return workCenterByCode.value.get(code) ?? code
  }

  return { resolveSku, resolveWorkCenter }
}
```

- [ ] **Step 2: typecheck composable**

Run: `cd frontend/apps/business-console && pnpm typecheck 2>&1 | tail -8`
Expected: 0 错（确认 `useBusinessSkus().skus` / `useBusinessMasterDataResources('work-center').resources` 字段名正确）。

- [ ] **Step 3: commit**

```bash
git add frontend/apps/business-console/src/composables/mes/useMesDisplayNames.ts
git commit -m "feat(mes): 显示名前端解析 composable(兜底 facade null *Name)"
```

---

## Task 2: 各 MES 列表页接入解析

> 每页：① import + 实例化 ② 改相关列 accessor ③ grep 确认无遗漏 ④ typecheck。逐页改，最后统一 typecheck+commit。

**通用接法**（每页 `<script setup>` 顶部）：
```ts
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
const { resolveSku, resolveWorkCenter } = useMesDisplayNames()
```

- [ ] **Step 1: operation-tasks.vue** — 工作中心列 accessor 改：
  `accessor: (r) => r.workCenterName ?? resolveWorkCenter(r.workCenterCode ?? r.workCenterId) ?? '无'`
- [ ] **Step 2: wip.vue** — 同上工作中心列 accessor。
- [ ] **Step 3: work-orders/index.vue** — SKU 副行 `r.skuCode ?? r.skuId` 改 `resolveSku(r.skuCode ?? r.skuId) ?? '无'`；工序行若显工作中心，接 `resolveWorkCenter`。
- [ ] **Step 4: receipts.vue** — 成品列 accessor 接 `resolveSku(r.skuCode ?? r.skuId)`。
- [ ] **Step 5: dispatch.vue** — 工作中心列 accessor 接 `resolveWorkCenter`。
- [ ] **Step 6: materials.vue** — 物料列 `resolveSku(r.materialCode ?? r.materialId)`（物料码即 SKU 码；解析不到自动回退原码，不报错）。
- [ ] **Step 7: 统一门禁**：`pnpm typecheck 2>&1 | tail -8`（0 错）+ `pnpm build 2>&1 | tail -5`（成功）。
- [ ] **Step 8: commit**

```bash
git add frontend/apps/business-console/src/pages/mes/
git commit -m "feat(mes): 列表工作中心/物料/SKU 显真名(前端解析兜底)"
```

---

## Task 3: E2E + 实机走查 + push

- [ ] **Step 1: E2E** — `pnpm exec playwright test --project=desktop`（全过；mock 无 master-data 时解析回退原值，断言不变）。
- [ ] **Step 2: 实机走查** — 确保栈在跑（`curl 127.0.0.1:5125` = 200，否则 `.\nerv.ps1 dev` 等就绪 + 登录 admin），Chrome 看 `/mes/operation-tasks`、`/mes/work-orders`：工作中心/物料**显真名或回退码**（master-data 有 displayName 则名、无则码），不破坏其它。截图。
- [ ] **Step 3: push** — `git push`。

---

## Self-Review

- **Spec 覆盖**：composable(T1) + 6 页 accessor(T2) + 门禁/E2E/实机/push(T3)。✓
- **占位扫描**：composable 代码完整；accessor 模式具体。无 TBD。
- **类型一致**：`resolveSku/resolveWorkCenter` 返回 `string | undefined`，accessor 末尾 `?? '无'` 兜空；`skus`/`resources` 字段经 scout 确认含 `code/displayName`。
- **前向兼容**：accessor 先取 facade `*Name`，故后端 #461 落地后自动优先，无需回改。
