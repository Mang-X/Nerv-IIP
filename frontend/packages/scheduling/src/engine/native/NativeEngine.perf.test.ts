import { existsSync, mkdirSync, appendFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { toModel } from '../../model/aps-mapper'
import { makeLargePlan } from '../../model/perf-fixtures'
import { NativeEngine } from './NativeEngine'

// 性能基线门禁:大数据集(~2000 工序)的 toModel + 首屏 setData 必须在阈值内。
// 指标写入 JSONL 供留档;超阈值则 fail。jsdom 无真实布局,数值偏保守但可比较回归。
const MAP_BUDGET_MS = 400
const RENDER_BUDGET_MS = 6000

const here = dirname(fileURLToPath(import.meta.url))
const resultsFile = resolve(here, '../../../perf/results.jsonl')

function record(metric: Record<string, unknown>) {
  try {
    if (!existsSync(dirname(resultsFile))) mkdirSync(dirname(resultsFile), { recursive: true })
    appendFileSync(resultsFile, `${JSON.stringify(metric)}\n`)
  } catch {
    /* 记录失败不影响门禁断言。 */
  }
}

describe('NativeEngine performance baseline', () => {
  it('maps and renders ~2000 operations within budget', () => {
    const plan = makeLargePlan(200, 10, 200)

    const t0 = performance.now()
    const model = toModel(plan)
    const t1 = performance.now()

    const el = document.createElement('div')
    const engine = new NativeEngine()
    engine.mount(el, {
      view: 'order',
      readOnly: true,
      scale: 'day',
      locale: 'zh',
      theme: { isDark: true, tokens: {} },
    })
    engine.setData(model)
    const t2 = performance.now()

    const mapMs = +(t1 - t0).toFixed(1)
    const renderMs = +(t2 - t1).toFixed(1)
    const nodes = el.querySelectorAll('[data-task-id]').length

    record({ case: 'order-2000', operations: 2000, tasks: model.tasks.length, nodes, mapMs, renderMs, mapBudgetMs: MAP_BUDGET_MS, renderBudgetMs: RENDER_BUDGET_MS })

    expect(model.tasks.length).toBeGreaterThanOrEqual(2000)
    expect(nodes).toBe(model.tasks.length)
    expect(mapMs).toBeLessThan(MAP_BUDGET_MS)
    expect(renderMs).toBeLessThan(RENDER_BUDGET_MS)

    engine.destroy()
  })
})
