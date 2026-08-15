import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import type { CascadePickerLevel } from '@nerv-iip/ui'
import { computed, ref, watch } from 'vue'
import { useBusinessMasterDataResources } from './useBusinessMasterData'

export interface EquipmentScopeSelection {
  workshop: string
  line: string
  device: string
}

const SCOPE_TAKE = 500

function toOption(item: BusinessConsoleResourceItem) {
  const code = item.code ?? ''
  return {
    value: code,
    label: item.displayName?.trim() ? item.displayName : code,
    hint: code,
  }
}

/**
 * 设备域四页（历史趋势 / OEE / 可靠性 / 可用窗口）共用的
 * 车间 → 产线 → 设备 级联范围选择：数据来自主数据 facade
 * （workshop / production-line / device-asset 资源目录），层级间按
 * 资源项的 workshopCode / lineCode 关联字段在前端过滤。
 *
 * - `scope` 三级值均为空串时代表「全厂」；
 * - `devicesInScope` 是当前范围（全厂 / 车间 / 产线 / 单台）内的设备资源，
 *   供页面聚合视图汇总或逐台下钻。
 */
export function useEquipmentScopeSelection(initial?: Partial<EquipmentScopeSelection>) {
  const workshopSource = useBusinessMasterDataResources('workshop')
  const lineSource = useBusinessMasterDataResources('production-line')
  const deviceSource = useBusinessMasterDataResources('device-asset')
  workshopSource.filters.take = SCOPE_TAKE
  lineSource.filters.take = SCOPE_TAKE
  deviceSource.filters.take = SCOPE_TAKE

  const scope = ref<EquipmentScopeSelection>({
    workshop: initial?.workshop ?? '',
    line: initial?.line ?? '',
    device: initial?.device ?? '',
  })

  const linesInScope = computed(() => {
    const workshop = scope.value.workshop
    if (!workshop) return lineSource.resources.value
    return lineSource.resources.value.filter((line) => line.workshopCode === workshop)
  })

  // 车间 / 产线两级收窄后的设备目录（设备层选择框的候选，与是否已选中某台无关）。
  const devicesInParentScope = computed(() => {
    const { workshop, line } = scope.value
    let devices = deviceSource.resources.value
    if (line) {
      devices = devices.filter((item) => item.lineCode === line)
    } else if (workshop) {
      const lineCodes = new Set(
        linesInScope.value.map((item) => item.code).filter((code): code is string => !!code),
      )
      devices = devices.filter(
        (item) =>
          item.workshopCode === workshop || (!!item.lineCode && lineCodes.has(item.lineCode)),
      )
    }
    return devices
  })

  const devicesInScope = computed(() => {
    const device = scope.value.device
    if (!device) return devicesInParentScope.value
    return devicesInParentScope.value.filter((item) => item.code === device)
  })

  // 目录刷新后已选值可能不再落在范围内（如产线改挂到别的车间）——静默回退到「全部」。
  watch(devicesInScope, (devices) => {
    if (scope.value.device && !devices.some((item) => item.code === scope.value.device)) {
      scope.value = { ...scope.value, device: '' }
    }
  })

  const levels = computed<CascadePickerLevel[]>(() => [
    {
      key: 'workshop',
      label: '车间',
      allLabel: '全厂',
      options: workshopSource.resources.value.map(toOption),
      loading: workshopSource.resourcesPending.value,
    },
    {
      key: 'line',
      label: '产线',
      options: linesInScope.value.map(toOption),
      loading: lineSource.resourcesPending.value,
    },
    {
      key: 'device',
      label: '设备',
      options: devicesInParentScope.value.map(toOption),
      loading: deviceSource.resourcesPending.value,
    },
  ])

  const selectedDevice = computed(() =>
    scope.value.device
      ? deviceSource.resources.value.find((item) => item.code === scope.value.device)
      : undefined,
  )

  const scopeLabel = computed(() => {
    const { workshop, line, device } = scope.value
    if (device) {
      const name = selectedDevice.value?.displayName?.trim()
      return name ? `${name}（${device}）` : device
    }
    if (line) {
      const item = lineSource.resources.value.find((entry) => entry.code === line)
      return item?.displayName?.trim() ? `${item.displayName}（${line}）` : line
    }
    if (workshop) {
      const item = workshopSource.resources.value.find((entry) => entry.code === workshop)
      return item?.displayName?.trim() ? `${item.displayName}（${workshop}）` : workshop
    }
    return '全厂'
  })

  const scopePending = computed(
    () =>
      workshopSource.resourcesPending.value ||
      lineSource.resourcesPending.value ||
      deviceSource.resourcesPending.value,
  )

  return {
    devicesInScope,
    levels,
    scope,
    scopeLabel,
    scopePending,
    selectedDevice,
  }
}
