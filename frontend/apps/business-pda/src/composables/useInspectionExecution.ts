import type { BusinessConsoleInspectionPlanCharacteristicItem } from '@nerv-iip/api-client'
import {
  createQualityCharacteristicDraft,
  qualityCharacteristicRowsValid,
  qualityInspectionOverallVerdict,
  toQualityCharacteristicResultLines,
  type QualityCharacteristicDraftRow,
} from '@nerv-iip/business-core'
import { computed, reactive, shallowRef, watch, type Ref } from 'vue'

type PlanCharacteristic = BusinessConsoleInspectionPlanCharacteristicItem

export interface ExecutionRow extends QualityCharacteristicDraftRow {
  id: number
  /** 特性中文名（来自计划，仅展示）。 */
  name: string
  /** 是否为计划必检特性（必检不可移除、且缺失时禁止提交）。 */
  required: boolean
}

/** 提交后端返回的权威检验结论（后端按计划规格 + AQL 计算，含自动开出的 NCR）。 */
export interface AuthoritativeInspectionResult {
  /** 本次检验记录 id（供 NCR 详情页回链「来源检验记录」）。 */
  inspectionRecordId: string | null
  /** passed / rejected / conditional-release（后端口径）。 */
  result: string
  /** 不合格时后端自动开出并回链的 NCR id；合格为空。 */
  nonconformanceReportId: string | null
  /** NCR 业务编号（人读单号，供结果页展示与互查）；无 NCR 为空。 */
  nonconformanceReportCode: string | null
}

/** 结果页状态：提交成功（权威结论）或操作失败（网络等）。 */
export type QualityResultState =
  | { phase: 'submitted'; authoritative: AuthoritativeInspectionResult }
  | { phase: 'error'; message: string }

function kindOfCharacteristic(c: PlanCharacteristic) {
  return c.characteristicType === 'attribute' ? 'count' : 'measured'
}

/**
 * 检验任务执行（逐特性录结果）状态与副作用。把执行表单从路由页里抽出，页面只做步骤编排。
 *
 * - **特性来自检验计划**（可选可搜的数据源）：计划特性一到，自动补齐全部**必检**特性行
 *   （减少漏检），必检行不可移除；非必检可增删。
 * - **提交门禁（P1.1）**：`canSubmit` 除了逐行有效，还要求**无缺失必检特性**——否则后端
 *   `CalculatePlannedLines` 会因遗漏必检项拒绝，用户会在提交端才失败。
 * - **权威结论（P1.2）**：提交后用**后端返回的 result / ncrId**（passed/rejected/conditional-release
 *   + 自动开出的 NCR）驱动结果页，而不是提交前的客户端预判；客户端 `verdict` 仅用于提交前的
 *   按钮提示与「不合格→处置原因必填」引导。
 */
export function useInspectionExecution(options: {
  planCharacteristics: Ref<PlanCharacteristic[]>
  submitInspection: (
    inspectionTaskId: string,
    resultLines: ReturnType<typeof toQualityCharacteristicResultLines>,
    dispositionReason?: string,
  ) => Promise<unknown>
}) {
  const { planCharacteristics, submitInspection } = options

  let nextRowId = 1
  const rows = reactive<ExecutionRow[]>([])
  const dispositionReason = shallowRef('')

  const requiredCodes = computed(
    () =>
      new Set(
        planCharacteristics.value
          .filter((c) => c.required && c.characteristicCode)
          .map((c) => c.characteristicCode as string),
      ),
  )
  const addedCodes = computed(() => new Set(rows.map((r) => r.characteristicCode)))
  /** 尚未加入的必检特性码——非空则禁止提交（明确提示后端会拒）。 */
  const missingRequiredCodes = computed(() =>
    [...requiredCodes.value].filter((code) => !addedCodes.value.has(code)),
  )

  function rowFromCharacteristic(c: PlanCharacteristic): ExecutionRow {
    return {
      id: nextRowId++,
      ...createQualityCharacteristicDraft(kindOfCharacteristic(c)),
      characteristicCode: c.characteristicCode ?? '',
      name: c.name ?? c.characteristicCode ?? '',
      uomCode: c.unitCode ?? '',
      lowerSpecLimit: c.lowerSpecLimit ?? '',
      upperSpecLimit: c.upperSpecLimit ?? '',
      required: Boolean(c.required),
    }
  }

  function addCharacteristic(c: PlanCharacteristic) {
    const code = c.characteristicCode ?? ''
    if (!code || rows.some((r) => r.characteristicCode === code)) return
    rows.push(rowFromCharacteristic(c))
  }
  function addAllCharacteristics() {
    for (const c of planCharacteristics.value) addCharacteristic(c)
  }
  /** 必检行不可移除（避免漏检导致提交端失败）。 */
  function removeRow(id: number) {
    const index = rows.findIndex((r) => r.id === id)
    if (index < 0 || rows[index].required) return
    rows.splice(index, 1)
  }

  // 计划特性到达 → 自动补齐必检特性行（幂等：已加入的不重复）。
  watch(
    planCharacteristics,
    (chars) => {
      for (const c of chars) {
        if (c.required) addCharacteristic(c)
      }
    },
    { immediate: true },
  )

  const allRowsValid = computed(() => qualityCharacteristicRowsValid(rows))
  const overallVerdict = computed(() => qualityInspectionOverallVerdict(rows))
  const dispositionRequired = computed(() => overallVerdict.value === 'fail')
  const canSubmit = computed(
    () =>
      allRowsValid.value &&
      missingRequiredCodes.value.length === 0 &&
      (!dispositionRequired.value || dispositionReason.value.trim() !== ''),
  )

  async function submit(inspectionTaskId: string): Promise<AuthoritativeInspectionResult> {
    const lines = toQualityCharacteristicResultLines(rows)
    const response = (await submitInspection(inspectionTaskId, lines, dispositionReason.value)) as
      | {
          data?: {
            inspectionRecordId?: string | null
            result?: string
            nonconformanceReportId?: string | null
            nonconformanceReportCode?: string | null
          }
        }
      | undefined
    return {
      inspectionRecordId: response?.data?.inspectionRecordId ?? null,
      result: response?.data?.result ?? '',
      nonconformanceReportId: response?.data?.nonconformanceReportId ?? null,
      nonconformanceReportCode: response?.data?.nonconformanceReportCode ?? null,
    }
  }

  function reset() {
    rows.splice(0, rows.length)
    dispositionReason.value = ''
    nextRowId = 1
  }

  return {
    rows,
    dispositionReason,
    missingRequiredCodes,
    allRowsValid,
    overallVerdict,
    dispositionRequired,
    canSubmit,
    addCharacteristic,
    addAllCharacteristics,
    removeRow,
    submit,
    reset,
  }
}
