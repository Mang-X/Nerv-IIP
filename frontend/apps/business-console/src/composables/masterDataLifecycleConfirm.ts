import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import { computed, ref, shallowRef } from 'vue'
import type { MasterDataLifecyclePatch } from './useBusinessMasterData'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'

/** 来自 `useMasterDataResourceActions` 的停用/启用动作（编辑由页面自带表单处理）。 */
export interface MasterDataLifecycleActions {
  disable: (code: string, patch: MasterDataLifecyclePatch) => Promise<unknown>
  enable: (code: string, patch: MasterDataLifecyclePatch) => Promise<unknown>
  disablePending: { value: boolean }
  enablePending: { value: boolean }
}

/**
 * 主数据停用 / 重新启用的**页面层单实例确认框**控制器（#1591）。
 *
 * 为什么收在页面层：`confirm-destroy.md` 规则 5 要求确认框声明在 `v-for` 外、由 `target` 指向
 * 当前行。此前确认框装在 `MasterDataRowActions` 内部随行渲染，一页 N 行就是 N 个 `NvAlertDialog`
 * 实例——每行都挂一套 reka portal 与焦点陷阱，且组件测试用 stub 抹平弹层，**测不出**这个结构。
 *
 * 一页只需要一个控制器：`request()` 接收「哪一行 + 用哪套动作 + 叫什么」，所以一页有多张表
 * （工厂结构 4 层、计量单位与换算、班次与日历…）也共用同一个确认框，切换目标即可。
 */
export function useMasterDataLifecycleConfirm() {
  const open = ref(false)
  const row = shallowRef<BusinessConsoleResourceItem | null>(null)
  const actions = shallowRef<MasterDataLifecycleActions | null>(null)
  const entityLabel = ref('')
  const reason = ref('')

  /** 行上没有 `active` 字段时按「启用中」处理，与列表状态徽章的判定保持一致。 */
  const isActive = computed(() => row.value?.active !== false)
  const actionLabel = computed(() => (isActive.value ? '停用' : '启用'))
  const pending = computed(
    () =>
      Boolean(actions.value?.disablePending.value) || Boolean(actions.value?.enablePending.value),
  )
  const canConfirm = computed(() => reason.value.trim().length > 0 && !pending.value)

  function request(
    target: BusinessConsoleResourceItem,
    targetActions: MasterDataLifecycleActions,
    label: string,
  ) {
    if (!target.code) return
    row.value = target
    actions.value = targetActions
    entityLabel.value = label
    // 每次打开都从空白开始：上一条原因不能被当成这一次的理由带进审计。
    reason.value = ''
    open.value = true
  }

  async function confirm() {
    const target = row.value
    const targetActions = actions.value
    const trimmedReason = reason.value.trim()
    const label = entityLabel.value
    if (!target?.code || !targetActions || !trimmedReason) return

    const active = isActive.value
    try {
      if (active) {
        await targetActions.disable(target.code, { reason: trimmedReason })
        notifySuccess(`${label}已停用。`)
      } else {
        await targetActions.enable(target.code, { reason: trimmedReason })
        notifySuccess(`${label}已启用。`)
      }
      open.value = false
      row.value = null
      reason.value = ''
    } catch (error) {
      // 失败时保留已填原因与目标：用户重试不必重新组织措辞、也不必重新找那一行。
      notifyOperationFailure(
        `${label}${active ? '停用' : '启用'}失败`,
        error,
        `${label}${active ? '停用' : '启用'}失败，请稍后重试。`,
      )
    }
  }

  return {
    open,
    row,
    entityLabel,
    reason,
    isActive,
    actionLabel,
    pending,
    canConfirm,
    request,
    confirm,
  }
}

export type MasterDataLifecycleConfirm = ReturnType<typeof useMasterDataLifecycleConfirm>
