// 资源时间块(维护/停机/换线/换型)的单一事实源:类型集合、固定顺序、中文名与色板 token。
// 映射层(aps-mapper)、图例推导(legend)、图例渲染(SchedulingLegend)都从这里取,
// 免得同一个语义在三处各写一份、文案还各自漂移。
// 码值与后端 ScheduleBlockKindContract 逐字对应。

export type BlockKind = 'maintenance' | 'downtime' | 'lineChange' | 'changeover'

/** 固定展示顺序:先讲设备本身(维护/停机),再讲切换(换线/换型)。 */
export const BLOCK_KINDS: readonly BlockKind[] = [
  'maintenance',
  'downtime',
  'lineChange',
  'changeover',
]

/** 业务化名称。图上(块文本/提示)与图例用同一份,不许各写各的。 */
export const BLOCK_LABELS: Record<BlockKind, string> = {
  maintenance: '设备维护',
  downtime: '计划停机',
  lineChange: '换线',
  changeover: '换型',
}

/** 斜纹着色 token(与 --nv-scheduling-block-* 全局变量一致)。 */
export const BLOCK_TOKENS: Record<BlockKind, string> = {
  maintenance: '--nv-scheduling-block-maintenance',
  downtime: '--nv-scheduling-block-downtime',
  lineChange: '--nv-scheduling-block-linechange',
  changeover: '--nv-scheduling-block-changeover',
}

/** 契约码值 → 模型语义。未知码值不猜,按停机处理(与后端 ClassifyBlockKind 同口径),不丢窗口。 */
export function toBlockKind(value: unknown): BlockKind {
  return BLOCK_KINDS.includes(value as BlockKind) ? (value as BlockKind) : 'downtime'
}
