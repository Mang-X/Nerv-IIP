import { describe, expect, it, vi } from 'vitest'
import { effectScope, nextTick, ref } from 'vue'
import { useInspectionExecution } from './useInspectionExecution'

type Char = {
  characteristicCode: string
  name: string
  characteristicType: string
  required: boolean
  unitCode?: string | null
  lowerSpecLimit?: number | null
  upperSpecLimit?: number | null
  nominalValue?: number | null
}

function runInScope<T>(fn: () => T): { value: T; stop: () => void } {
  const scope = effectScope()
  const value = scope.run(fn) as T
  return { value, stop: () => scope.stop() }
}

const OD: Char = {
  characteristicCode: 'od',
  name: '外径',
  characteristicType: 'variable',
  required: true,
  unitCode: 'mm',
  lowerSpecLimit: 9.9,
  upperSpecLimit: 10.1,
}
const APPEARANCE: Char = {
  characteristicCode: 'appearance',
  name: '外观',
  characteristicType: 'attribute',
  required: true,
}
const OPTIONAL: Char = {
  characteristicCode: 'weight',
  name: '重量',
  characteristicType: 'variable',
  required: false,
  unitCode: 'g',
}

describe('useInspectionExecution', () => {
  it('auto-adds all required characteristics from the plan (reduces missed inspections)', () => {
    const chars = ref<Char[]>([OD, APPEARANCE, OPTIONAL])
    const { value: exec, stop } = runInScope(() =>
      useInspectionExecution({ planCharacteristics: chars as never, submitInspection: vi.fn() }),
    )
    // 必检项自动加入；非必检不自动加入。
    expect(exec.rows.map((r) => r.characteristicCode).sort()).toEqual(['appearance', 'od'])
    expect(exec.rows.every((r) => r.required)).toBe(true)
    expect(exec.missingRequiredCodes.value).toEqual([])
    stop()
  })

  it('blocks submit while any required characteristic is missing, and forbids removing required rows', async () => {
    const chars = ref<Char[]>([])
    const { value: exec, stop } = runInScope(() =>
      useInspectionExecution({ planCharacteristics: chars as never, submitInspection: vi.fn() }),
    )
    // 计划尚未加载 → 无行 → 无法提交。
    expect(exec.canSubmit.value).toBe(false)

    chars.value = [OD, APPEARANCE]
    await nextTick()
    expect(exec.rows).toHaveLength(2)

    // 尝试移除必检行 → 被拒（避免漏检导致后端提交端失败）。
    exec.removeRow(exec.rows[0].id)
    expect(exec.rows).toHaveLength(2)

    // 录入合格结果 → 可提交。
    const od = exec.rows.find((r) => r.characteristicCode === 'od')!
    od.measuredValue = '10'
    const ap = exec.rows.find((r) => r.characteristicCode === 'appearance')!
    ap.countResult = 'pass'
    expect(exec.missingRequiredCodes.value).toEqual([])
    expect(exec.canSubmit.value).toBe(true)
    stop()
  })

  it('requires a disposition reason when the client verdict is fail', () => {
    const chars = ref<Char[]>([APPEARANCE])
    const { value: exec, stop } = runInScope(() =>
      useInspectionExecution({ planCharacteristics: chars as never, submitInspection: vi.fn() }),
    )
    const ap = exec.rows[0]
    ap.countResult = 'fail'
    ap.defectReason = 'SCRATCH'
    expect(exec.overallVerdict.value).toBe('fail')
    expect(exec.dispositionRequired.value).toBe(true)
    // 不合格但未填处置原因 → 不可提交。
    expect(exec.canSubmit.value).toBe(false)
    exec.dispositionReason.value = '判退'
    expect(exec.canSubmit.value).toBe(true)
    stop()
  })

  it('returns the backend authoritative result + ncr id (not the client verdict)', async () => {
    const chars = ref<Char[]>([APPEARANCE])
    const submitInspection = vi.fn().mockResolvedValue({
      data: { inspectionRecordId: 'rec-1', result: 'rejected', nonconformanceReportId: 'ncr-9' },
    })
    const { value: exec, stop } = runInScope(() =>
      useInspectionExecution({ planCharacteristics: chars as never, submitInspection }),
    )
    const ap = exec.rows[0]
    ap.countResult = 'fail'
    ap.defectReason = 'SCRATCH'
    exec.dispositionReason.value = '判退'

    const result = await exec.submit('TASK-1')
    expect(submitInspection).toHaveBeenCalledTimes(1)
    const [taskId, lines, disposition] = submitInspection.mock.calls[0]
    expect(taskId).toBe('TASK-1')
    expect(disposition).toBe('判退')
    expect(lines[0].result).toBe('failed') // 提交口径 passed/failed
    // 权威结论来自后端响应，而非客户端 verdict。
    expect(result).toEqual({ result: 'rejected', nonconformanceReportId: 'ncr-9' })
    stop()
  })
})
