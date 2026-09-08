import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { join, relative, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

/**
 * `NvSearchBar` 的关键词被清空时（点清除按钮、或退格删到空）会 emit `search('')`，消费方
 * 只要「列表跟着关键词走」就不会出现「输入框空了、列表还按旧关键词过滤」的错位——空结果
 * 继续显示「没有匹配的 XXX」，用户据此误判本组织没配这个码，会直接产生错误数据（#3128）。
 *
 * 这条不变量落在两侧，缺一不可：
 * - 组件侧由 `packages/ui-mobile/src/components/search-bar/SearchBar.test.ts` 承担；
 * - 消费方侧由本登记表承担：新增 `NvSearchBar` 的页面/组件必须在这里登记它靠哪条通路
 *   恢复全量，登记的口径还必须与源码里那个标签实际绑没绑 `@search` 一致。
 *
 * 两种合法口径：
 * - `model-derived`：列表直接由 `v-model` 绑定的关键词派生（computed 过滤，或写进响应式
 *   查询参数），关键词一空列表自然回到全量，不需要 `@search`。
 * - `search-event`：列表由一次显式检索驱动（多为服务端重查），必须监听 `@search`——包括
 *   `NvSearchBar` 在关键词被清空时补发的 `search('')`。
 */
type KeywordResetChannel = 'model-derived' | 'search-event'

const registry: Record<string, KeywordResetChannel> = {
  // 服务端目录重查：@search 驱动 useBusinessDeviceDirectory().search()，清空即重查全量。
  'components/equipment/DeviceAssetPicker.vue': 'search-event',
  // 服务端停机原因目录重查：@search 转发给报修页的目录 composable。
  'components/equipment/DowntimeReasonPicker.vue': 'search-event',
  // 本地 computed 过滤 props.characteristics，关键词一空即回到全量。
  'components/quality/QualityCharacteristicPicker.vue': 'model-derived',
  // v-model 经 computed setter emit update:keyword，父页响应式查询参数自动重查。
  'components/wms/WarehouseTaskExecutionView.vue': 'model-derived',
  // 本地 computed 过滤 props.locationOptions / lotOptions。
  'components/wms/WmsOperationalCandidatePicker.vue': 'model-derived',
  // v-model 写进 useMaintenanceSelfWorkOrders 的响应式 filters.keyword。
  'pages/equipment/repair.vue': 'model-derived',
  'pages/equipment/work-orders/components/MaintenanceWorkOrderFilters.vue': 'model-derived',
}

// 与 WmsOperationalCandidatePicker.test.ts 同一套路径口径：包内跑用包相对路径，
// 从仓库根跑则回落到全路径。
const packageSrc = resolve('src')
const srcRoot = existsSync(join(packageSrc, 'components'))
  ? packageSrc
  : resolve('frontend/apps/business-pda/src')

function vueFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const full = join(directory, entry.name)
    if (entry.isDirectory()) return vueFiles(full)
    return entry.isFile() && entry.name.endsWith('.vue') ? [full] : []
  })
}

/** 只看 `<NvSearchBar …>` 这个标签本身，别把消费方自己 emit 的 `search` 算进来。 */
function searchBarTags(source: string): string[] {
  return source.match(/<NvSearchBar\b[^>]*>/g) ?? []
}

const scanned = new Map<string, KeywordResetChannel>()
for (const file of vueFiles(srcRoot)) {
  const source = readFileSync(file, 'utf8')
  const tags = searchBarTags(source)
  if (tags.length === 0) continue
  scanned.set(
    relative(srcRoot, file).replaceAll('\\', '/'),
    tags.some((tag) => /@search\b|v-on:search\b/.test(tag)) ? 'search-event' : 'model-derived',
  )
}

describe('NvSearchBar 消费方关键词清空通路登记表', () => {
  it('finds the consumers it is supposed to govern', () => {
    expect(scanned.size).toBeGreaterThanOrEqual(Object.keys(registry).length)
  })

  it('has every NvSearchBar consumer registered with the channel its source actually uses', () => {
    const unregistered = [...scanned.keys()].filter((file) => !(file in registry)).sort()
    expect(
      unregistered,
      '新增了 NvSearchBar 消费方却没登记：请在本文件登记它靠哪条通路在关键词被清空后恢复全量',
    ).toEqual([])

    const stale = Object.keys(registry)
      .filter((file) => !scanned.has(file))
      .sort()
    expect(stale, '登记表里的文件已不再使用 NvSearchBar，请删除该条登记').toEqual([])

    const drifted = [...scanned.entries()]
      .filter(([file, channel]) => file in registry && registry[file] !== channel)
      .map(([file, channel]) => `${file}: 源码是 ${channel}，登记表写的是 ${registry[file]}`)
      .sort()
    expect(drifted, '消费方改了关键词通路却没更新登记：请重新判定清空后是否仍能恢复全量').toEqual(
      [],
    )
  })
})
