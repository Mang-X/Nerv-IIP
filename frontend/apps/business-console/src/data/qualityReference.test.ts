import { describe, expect, it } from 'vitest'
import { NCR_DISPOSITION_DOCUMENT_TYPE } from './approvalReference'
import {
  NCR_DISPOSITION_TYPE_OPTIONS,
  ncrDispositionRequiresCentralApproval,
  ncrDispositionRequiresEvidence,
} from './qualityReference'

/**
 * #1327 回归锁：处置类型 / 分支规则 / 审批 documentType 都是与后端逐字对齐的受控值，
 * 契约层是自由 string，只有测试能拦住漂移。
 */
describe('NCR 处置受控值', () => {
  it('处置类型与后端 NonconformanceReport.DispositionTypes 逐字一致', () => {
    expect(NCR_DISPOSITION_TYPE_OPTIONS.map((option) => option.value)).toEqual([
      'rework',
      'scrap',
      'return-to-supplier',
      'conditional-release',
      'sort-and-screen',
    ])
  })

  it('后端从不支持的 use-as-is 不再出现在界面上', () => {
    expect(NCR_DISPOSITION_TYPE_OPTIONS.some((option) => option.value === 'use-as-is')).toBe(false)
  })

  it('需中央审批 / MRB 评审的判定与后端 RequiresCentralApproval 一致', () => {
    for (const type of ['rework', 'scrap', 'return-to-supplier', 'conditional-release']) {
      expect(ncrDispositionRequiresCentralApproval(type)).toBe(true)
    }
    expect(ncrDispositionRequiresCentralApproval('sort-and-screen')).toBe(false)
  })

  it('需处置证据的判定与后端 RequiresDispositionEvidence 一致', () => {
    expect(ncrDispositionRequiresEvidence('conditional-release')).toBe(true)
    expect(ncrDispositionRequiresEvidence('sort-and-screen')).toBe(true)
    expect(ncrDispositionRequiresEvidence('rework')).toBe(false)
  })

  it('处置审批的 documentType 与后端 ApprovalDocumentTypes.NcrDisposition 逐字一致', () => {
    expect(NCR_DISPOSITION_DOCUMENT_TYPE).toBe('ncr-disposition')
  })
})
