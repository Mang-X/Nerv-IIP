/**
 * 工作中心「工序族」——甘特分色与图例的分类事实源。
 *
 * 事实顺序（不可倒置）：
 * 1. **主数据分类字段优先**：`master-data/resources?resourceType=work-center` 的 `category`；
 * 2. 主数据没维护 category 时，才落到本文件的**可配置编码前缀映射**兜底。
 *
 * 兜底表是权宜之计，不是设计：它按编码前缀猜工序族，换一家工厂就得改。
 * TODO(#1269)：主数据「工作中心」补 category（工序族）字段并在网关 facade 透出后，
 * 删掉 `CODE_PREFIX_FALLBACK`，只留主数据驱动。
 *
 * 色槽 key 对应 `@nerv-iip/scheduling` 的 `--nv-scheduling-category-*` 六色板，
 * **语义必须对齐**（包装用 pack、检测用 insp，不能借用 cut/bend 的槽位充数）。
 */

export interface WorkCenterFamily {
  /** 色槽 key（--nv-scheduling-category-<key>）。 */
  key: string
  /** 图例上的人话名。 */
  label: string
}

/** 工序族定义；图例按本对象的声明顺序渲染。 */
export const WORK_CENTER_FAMILIES = {
  machining: { key: 'mach', label: '机加' },
  assembly: { key: 'assy', label: '装配' },
  surfaceTreatment: { key: 'paint', label: '表面处理' },
  packaging: { key: 'pack', label: '包装' },
  inspection: { key: 'insp', label: '检测' },
  welding: { key: 'weld', label: '焊接' },
} as const satisfies Record<string, WorkCenterFamily>

export type WorkCenterFamilyId = keyof typeof WORK_CENTER_FAMILIES

/** 图例/分色遍历用的有序族列表。 */
export const WORK_CENTER_FAMILY_LIST: ReadonlyArray<WorkCenterFamily & { id: WorkCenterFamilyId }> =
  (Object.keys(WORK_CENTER_FAMILIES) as WorkCenterFamilyId[]).map((id) => ({
    id,
    ...WORK_CENTER_FAMILIES[id],
  }))

/**
 * 主数据 `category` 取值 → 工序族。中英文都收，比较时统一小写并去掉分隔符，
 * 让「Surface-Treatment」/「surface_treatment」/「表面处理」都能落位。
 */
const CATEGORY_ALIASES: Readonly<Record<string, WorkCenterFamilyId>> = {
  machining: 'machining',
  machine: 'machining',
  cnc: 'machining',
  机加: 'machining',
  机加工: 'machining',
  assembly: 'assembly',
  assy: 'assembly',
  装配: 'assembly',
  总装: 'assembly',
  surfacetreatment: 'surfaceTreatment',
  surface: 'surfaceTreatment',
  coating: 'surfaceTreatment',
  painting: 'surfaceTreatment',
  表面处理: 'surfaceTreatment',
  涂装: 'surfaceTreatment',
  电泳: 'surfaceTreatment',
  packaging: 'packaging',
  packing: 'packaging',
  包装: 'packaging',
  inspection: 'inspection',
  testing: 'inspection',
  quality: 'inspection',
  检测: 'inspection',
  检验: 'inspection',
  试验: 'inspection',
  welding: 'welding',
  weld: 'welding',
  焊接: 'welding',
}

/**
 * 编码前缀兜底（仅在主数据缺 category 时生效）。按前缀长度从长到短匹配，
 * 保证 `WC-SCALE-ROD`（装配）不会被 `WC-ROD`（机加）抢走。
 */
const CODE_PREFIX_FALLBACK: Readonly<Record<string, WorkCenterFamilyId>> = {
  'WC-ROD': 'machining',
  'WC-TUB': 'machining',
  'WC-GRD': 'machining',
  'WC-CNC': 'machining',
  'WC-FA': 'assembly',
  'WC-RA': 'assembly',
  'WC-VA': 'assembly',
  'WC-SCALE-SEAL': 'assembly',
  'WC-SCALE-ROD': 'assembly',
  'WC-CT': 'surfaceTreatment',
  'WC-PK': 'packaging',
  'WC-TS': 'inspection',
  'WC-SCALE-TEST': 'inspection',
  'WC-SCALE-WELD': 'welding',
}

const CODE_PREFIXES_LONGEST_FIRST = Object.keys(CODE_PREFIX_FALLBACK).sort(
  (a, b) => b.length - a.length,
)

function normalizeCategory(value: string) {
  return value
    .trim()
    .toLowerCase()
    .replace(/[\s\-_/]/g, '')
}

/**
 * 解析工作中心所属工序族。
 * @param workCenterId 工作中心编码（人读编码，如 `WC-CNC-01`）。
 * @param category 主数据上的分类字段；给了就以它为准。
 */
export function resolveWorkCenterFamily(
  workCenterId?: string | null,
  category?: string | null,
): WorkCenterFamily | undefined {
  const fromCategory = category ? CATEGORY_ALIASES[normalizeCategory(category)] : undefined
  if (fromCategory) return WORK_CENTER_FAMILIES[fromCategory]

  if (!workCenterId) return undefined
  const code = workCenterId.toUpperCase()
  const prefix = CODE_PREFIXES_LONGEST_FIRST.find((candidate) => code.startsWith(candidate))
  return prefix ? WORK_CENTER_FAMILIES[CODE_PREFIX_FALLBACK[prefix]] : undefined
}
