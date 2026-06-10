// 动态加载 DHTMLX Gantt 试用核心。未安装时优雅缺失 → 回落 NativeEngine,
// 使包在无商业许可环境仍可构建/测试/运行。试用版评估许可禁止分发,库文件不入 git。

export interface GanttEnterprise {
  getGanttInstance: (settings?: unknown) => unknown
}
export interface GanttModule {
  gantt?: unknown
  Gantt?: GanttEnterprise
}

let cachedModule: GanttModule | null | undefined

async function resolveModule(): Promise<GanttModule | null> {
  if (cachedModule !== undefined) return cachedModule
  try {
    cachedModule = (await import('@dhx/trial-gantt')) as GanttModule
  } catch {
    cachedModule = null
  }
  return cachedModule
}

function moduleHasGantt(m: GanttModule | null): boolean {
  return !!(m && (typeof m.Gantt?.getGanttInstance === 'function' || m.gantt))
}

/** 预加载并缓存 DHTMLX 模块,使后续 createGanttInstanceSync() 可同步取实例。 */
export async function preloadGantt(): Promise<void> {
  await resolveModule()
}

/** 是否真正可用(模块存在且暴露 gantt 工厂/单例)。stub 别名时返回 false。 */
export async function isDhtmlxAvailable(): Promise<boolean> {
  return moduleHasGantt(await resolveModule())
}

/** 从已缓存模块同步创建一个独立 Gantt 实例(多实例优先,单例兜底)。未预加载/不可用返回 null。 */
export function createGanttInstanceSync(): unknown | null {
  const m = cachedModule
  if (!moduleHasGantt(m ?? null)) return null
  if (typeof m!.Gantt?.getGanttInstance === 'function') return m!.Gantt.getGanttInstance()
  return m!.gantt ?? null
}

/** 测试注入:直接设置缓存模块(传 null 模拟未安装)。 */
export function __setGanttModuleForTest(m: GanttModule | null): void {
  cachedModule = m
}
