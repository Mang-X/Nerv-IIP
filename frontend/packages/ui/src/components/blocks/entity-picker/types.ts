/** 实体选择器的一个候选项。 */
export interface EntityPickerOption {
  /** 实体的人读业务编码（选中后回传的值）。 */
  value: string
  /** 实体名称（展示主文案）。 */
  label: string
  /** 辅助识别信息（分类 / 单位 / 状态…）。 */
  hint?: string
}

/**
 * 实体选择器的呈现形态。
 *
 * - `dropdown`（默认）：点一下直接在原地展开下拉，下拉内自带搜索框。
 *   绝大多数场景用它 —— 筛选条、表单字段、抽屉/弹窗内的字段。
 * - `dialog`：先开一个居中对话框再选。只在需要更大展示空间时用：
 *   多列信息、需要分页的上百条目录、选之前得先读一段说明的场景。
 */
export type EntityPickerVariant = 'dropdown' | 'dialog'
