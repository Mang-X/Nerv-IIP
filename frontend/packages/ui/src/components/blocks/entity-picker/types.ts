/** 实体选择器的一个候选项。 */
export interface EntityPickerOption {
  /**
   * 选中后回传的值。
   *
   * **它不一定是人读编码。** 有的调用点回传的是内部标识（GUID / 自增 id），
   * 这种情况下**不要**让它显示出来 —— 要么给 `code` 传真正的人读编码，
   * 要么在组件上关掉编码行（`:show-code="false"`）。
   */
  value: string
  /** 实体名称（展示主文案）。 */
  label: string
  /**
   * 人读业务编码（`SKU-FG-100`、`WO-2026-0007`…），显示用。
   *
   * 缺省时回落到 `value`——只有在 `value` 本身就是人读编码时才可以省略。
   * `value` 是 GUID 却不传 `code`，界面上就会直接印出一串 GUID。
   */
  code?: string
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
