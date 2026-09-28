<script setup lang="ts">
/**
 * 把入库 / 出库 / 盘点单分配到作业池（#3849）。只列该单所在工厂的作业池；
 * 工厂下只有一个作业池时直接选中。可选指定池内的某名作业人员，不指定则全池可领。
 */
import type { EntityPickerOption } from '@nerv-iip/ui'
import { useBusinessWorkers } from '@/composables/useBusinessMasterData'
import { useWmsWorkPools } from '@/composables/useWmsWorkPools'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvEntityPicker,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
} from '@nerv-iip/ui'
import { computed, shallowRef, watch } from 'vue'
import { RouterLink } from 'vue-router'

const props = defineProps<{
  target: 'inbound' | 'outbound' | 'count'
  resourceId?: string | null
  /** 读屏与标题用的人读名称，如「入库单 IB-20260928-000001」。 */
  resourceLabel: string
  siteCode?: string | null
  version?: number | null
  currentPoolCode?: string | null
}>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ assigned: [] }>()

const { pools, poolsPending, assign, assignPending, newIdempotencyKey } = useWmsWorkPools()
const { workers } = useBusinessWorkers({ pageSize: 500 })

const sitePools = computed(() => pools.value.filter((pool) => pool.siteCode === props.siteCode))
const poolOptions = computed<EntityPickerOption[]>(() =>
  sitePools.value.flatMap((pool) =>
    pool.poolCode ? [{ value: pool.poolCode, label: pool.displayName || pool.poolCode }] : [],
  ),
)
const poolCode = shallowRef('')
const operatorId = shallowRef('')
const formError = shallowRef('')
const intentKey = shallowRef('')

const operatorOptions = computed<EntityPickerOption[]>(() =>
  (sitePools.value.find((pool) => pool.poolCode === poolCode.value)?.members ?? []).flatMap(
    (member) => {
      if (!member.principalId) return []
      const worker = workers.value.find((candidate) => candidate.userId === member.principalId)
      const name = worker?.displayName || '未命名人员'
      return [
        {
          value: member.principalId,
          label: worker?.employeeNo ? `${name}（${worker.employeeNo}）` : name,
        },
      ]
    },
  ),
)

// 打开时重置表单；本工厂只有一个作业池时直接选中（池目录晚到也补选）。
// 合成一个监听器：拆成两个时，同一轮里 open 与 siteCode 一起变化，谁先执行不确定，重置会盖掉默认选中。
watch(
  [open, poolOptions],
  ([isOpen, options], previous) => {
    if (!isOpen) return
    if (!previous?.[0]) {
      intentKey.value = newIdempotencyKey()
      poolCode.value = props.currentPoolCode ?? ''
      operatorId.value = ''
      formError.value = ''
    }
    if (!poolCode.value && options.length === 1) poolCode.value = options[0]!.value
  },
  { immediate: true },
)
watch(poolCode, () => {
  operatorId.value = ''
})

async function submit() {
  if (!props.resourceId || !poolCode.value) {
    formError.value = '请选择作业池。'
    return
  }
  try {
    await assign(props.target, props.resourceId, {
      poolCode: poolCode.value,
      ...(operatorId.value ? { operatorPrincipalId: operatorId.value } : {}),
      idempotencyKey: intentKey.value,
      expectedVersion: props.version ?? 0,
    })
    open.value = false
    notifySuccess(`${props.resourceLabel} 已分配到作业池`)
    emit('assigned')
  } catch (error) {
    notifyOperationFailure('分配作业池失败', error, '分配作业池失败，请稍后重试。')
  }
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent>
      <NvDialogHeader>
        <NvDialogTitle>分配作业池</NvDialogTitle>
        <NvDialogDescription>{{ resourceLabel }}</NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submit">
        <NvFieldGroup>
          <NvField>
            <NvFieldLabel for="wms-assign-pool">作业池</NvFieldLabel>
            <NvEntityPicker
              id="wms-assign-pool"
              v-model="poolCode"
              :options="poolOptions"
              title="选择作业池"
              placeholder="选择作业池"
              empty-text="该工厂还没有作业池"
              :loading="poolsPending"
              aria-label="作业池"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="wms-assign-operator">作业人员（可选）</NvFieldLabel>
            <NvEntityPicker
              id="wms-assign-operator"
              v-model="operatorId"
              :options="operatorOptions"
              title="选择作业人员"
              placeholder="不指定则池内成员均可领取"
              empty-text="该作业池还没有成员"
              clearable
              aria-label="作业人员"
            />
          </NvField>
          <NvFieldError v-if="formError" :errors="[formError]" />
        </NvFieldGroup>
        <p v-if="!poolsPending && poolOptions.length === 0" class="text-sm text-muted-foreground">
          该工厂还没有作业池，请先到
          <RouterLink class="underline" to="/wms/work-pools">作业池</RouterLink>
          新建。
        </p>
        <NvDialogFooter>
          <NvDialogClose as-child>
            <NvButton type="button" variant="outline">取消</NvButton>
          </NvDialogClose>
          <NvButton type="submit" :disabled="assignPending || !poolCode">确认分配</NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
