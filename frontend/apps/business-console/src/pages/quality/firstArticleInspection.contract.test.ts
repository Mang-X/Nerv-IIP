import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const pageSource = readFileSync(resolve(process.cwd(), 'src/pages/quality/inspections.vue'), 'utf8')
const detailSource = readFileSync(
  resolve(process.cwd(), 'src/components/quality/InspectionRecordDetailSheet.vue'),
  'utf8',
)
const planSheetSource = readFileSync(
  resolve(process.cwd(), 'src/components/quality/FirstArticlePlanSheet.vue'),
  'utf8',
)
const recordsSource = readFileSync(
  resolve(process.cwd(), 'src/components/quality/FirstArticleInspectionRecords.vue'),
  'utf8',
)
const composableSource = readFileSync(
  resolve(process.cwd(), 'src/composables/useBusinessQuality.ts'),
  'utf8',
)

describe('首件检验工作台合同', () => {
  it('在既有检验页提供首件方案与首件记录两个平级入口', () => {
    expect(pageSource).toContain('首件检验方案')
    expect(pageSource).toContain('首件检验记录')
    expect(pageSource).toContain('FirstArticlePlanSheet')
    expect(pageSource).toContain('FirstArticleInspectionRecords')
    expect(pageSource).toContain('启用首件方案')
    expect(pageSource).toContain(':count="pageHeaderCount"')
    expect(pageSource).not.toContain('{{ inspectionPlansTotal }} 个方案')
  })

  it('首件方案固定业务分类并要求 SKU、工序工作中心和至少一个检验项', () => {
    expect(planSheetSource).toContain("category: 'first-article'")
    expect(pageSource).toContain("'first-article': '首件检验'")
    expect(planSheetSource).toContain('请选择适用物料。')
    expect(planSheetSource).toContain('请选择工序工作中心。')
    expect(planSheetSource).toContain('请至少添加一个检验项。')
    expect(planSheetSource).toContain('方案已创建但未启用')
  })

  it('首件记录读面固定 sourceType 并提供 SKU、结果筛选与详情定位', () => {
    expect(recordsSource).toContain('useQualityFirstArticleInspections')
    expect(composableSource).toContain("sourceType: 'first-article'")
    expect(pageSource).toContain('inspectionRecordId')
    expect(pageSource).toContain('skuCode')
    expect(pageSource).toContain('result')
  })

  it('记录详情提供浏览器打印动作及仅打印详情内容的样式', () => {
    expect(detailSource).toContain('window.print()')
    expect(detailSource).toContain('打印检验记录')
    expect(detailSource).toContain('@media print')
    expect(detailSource).toContain('data-printable-inspection-record')
  })
})
