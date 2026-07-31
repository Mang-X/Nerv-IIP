/**
 * 质量域 · 前端受控值常量。
 *
 * NCR 处置类型是后端聚合的枚举（`NonconformanceReport.DispositionTypes`），Gateway 契约里
 * 却只是自由 `string`——类型层拦不住漂移。处置面此前给的 `use-as-is` 后端从不支持，选中即必 400；
 * 「需 MRB 评审 / 需中央审批链 / 需处置证据」三条分支规则界面也完全没体现，于是重大处置提交必 400（#1327）。
 * 取值与规则集中到这里，并由 `qualityReference.test.ts` 锁住，避免再次与后端各写各的。
 */
import type { RefOption } from './masterDataReference'

/** 处置类型：与后端 `NonconformanceReport.DispositionTypes` 逐字一致。 */
export const NCR_DISPOSITION_TYPE_OPTIONS: RefOption[] = [
  { value: 'rework', label: '返工' },
  { value: 'scrap', label: '报废' },
  { value: 'return-to-supplier', label: '退供应商' },
  { value: 'conditional-release', label: '让步接收' },
  { value: 'sort-and-screen', label: '全检挑选' },
]

/**
 * 需 MRB 评审 + 中央审批链的处置类型，与后端 `NonconformanceReport.RequiresCentralApproval` 一致：
 * 只有「全检挑选」属质量部门内部可决，其余四类都要 MRB 评审通过并挂一条已批准的处置审批链。
 */
export const NCR_CENTRAL_APPROVAL_DISPOSITION_TYPES = [
  'rework',
  'scrap',
  'return-to-supplier',
  'conditional-release',
]

/** 需要处置证据（附件）的类型，与后端 `RequiresDispositionEvidence` 一致。 */
export const NCR_EVIDENCE_REQUIRED_DISPOSITION_TYPES = ['conditional-release', 'sort-and-screen']

export function ncrDispositionRequiresCentralApproval(dispositionType: string) {
  return NCR_CENTRAL_APPROVAL_DISPOSITION_TYPES.includes(dispositionType.trim().toLowerCase())
}

export function ncrDispositionRequiresEvidence(dispositionType: string) {
  return NCR_EVIDENCE_REQUIRED_DISPOSITION_TYPES.includes(dispositionType.trim().toLowerCase())
}
