/**
 * 停机原因目录的受控码（#3855）：分类按 TPM 六大损失 / 国内 OEE 实践，损失类别按 OEE 三率。
 * 码表归 Maintenance（`DowntimeReasonVocabulary`，写命令按它校验）；这里只是展示用的中文映射，
 * 码值须与之一致。界面只显示中文，不回吐码值。
 */
export const DOWNTIME_REASON_CATEGORY_OPTIONS = [
  { value: 'breakdown', label: '设备故障' },
  { value: 'setup', label: '换型调整' },
  { value: 'minor-stop', label: '小停机/空转' },
  { value: 'process', label: '工艺异常' },
  { value: 'quality', label: '质量问题' },
  { value: 'material', label: '物料短缺' },
  { value: 'labor', label: '人员短缺' },
  { value: 'external', label: '外部因素' },
  { value: 'planned', label: '计划停机' },
  { value: 'unclassified', label: '未分类' },
] as const

export const DOWNTIME_LOSS_CATEGORY_OPTIONS = [
  { value: 'availability', label: '可用率损失' },
  { value: 'performance', label: '性能损失' },
  { value: 'quality', label: '质量损失' },
  { value: 'planned', label: '计划停机（不计损失）' },
  { value: 'unclassified', label: '未分类' },
] as const

function labelOf(options: readonly { value: string; label: string }[], value?: string | null) {
  const code = (value ?? '').trim().toLowerCase()
  if (!code) return '—'
  // 词表外的历史码不回吐原值，统一说「其他」。
  return options.find((option) => option.value === code)?.label ?? '其他'
}

export function downtimeReasonCategoryLabel(value?: string | null) {
  return labelOf(DOWNTIME_REASON_CATEGORY_OPTIONS, value)
}

export function downtimeLossCategoryLabel(value?: string | null) {
  return labelOf(DOWNTIME_LOSS_CATEGORY_OPTIONS, value)
}
