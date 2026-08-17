<script setup lang="ts">
import {
  NvAlertDialog,
  NvAlertDialogCancel,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvField,
  NvFieldDescription,
  NvFieldLabel,
  NvInput,
} from '@nerv-iip/ui'
import type { MasterDataLifecycleConfirm } from '@/composables/masterDataLifecycleConfirm'

/**
 * 停用 / 重新启用的二次确认框——**每页只渲染一个实例**，放在 `v-for` 外，
 * 由 `confirm-destroy.md` 规则 5 要求（#1591）。行操作只负责 `request()` 指向当前行。
 */
const props = defineProps<{ controller: MasterDataLifecycleConfirm }>()

// 原因上限与 MasterData 侧生命周期审计字段一致（500）。
const REASON_MAX_LENGTH = 500
</script>

<template>
  <NvAlertDialog v-model:open="props.controller.open.value">
    <NvAlertDialogContent>
      <NvAlertDialogHeader>
        <NvAlertDialogTitle>
          {{
            props.controller.isActive.value
              ? `确认停用该${props.controller.entityLabel.value}？`
              : `确认启用该${props.controller.entityLabel.value}？`
          }}
        </NvAlertDialogTitle>
        <NvAlertDialogDescription>
          {{
            props.controller.isActive.value
              ? '停用后将不能用于新建/计划，已有记录不受影响。'
              : '启用后可重新用于新建与计划。'
          }}
        </NvAlertDialogDescription>
      </NvAlertDialogHeader>
      <NvField>
        <NvFieldLabel for="masterdata-lifecycle-reason">
          {{ props.controller.actionLabel.value }}原因
          <span class="text-destructive">*</span>
        </NvFieldLabel>
        <NvInput
          id="masterdata-lifecycle-reason"
          v-model="props.controller.reason.value"
          data-testid="lifecycle-reason"
          required
          :maxlength="REASON_MAX_LENGTH"
          :placeholder="
            props.controller.isActive.value
              ? '说明停用依据，如设备报废、供应商终止合作'
              : '说明重新启用依据，如整改完成'
          "
        />
        <NvFieldDescription>原因会记入生命周期审计，可按对象回溯。</NvFieldDescription>
      </NvField>
      <NvAlertDialogFooter>
        <NvAlertDialogCancel>取消</NvAlertDialogCancel>
        <!--
          确认按钮**不能**用 NvAlertDialogAction：它包的是 reka AlertDialogAction，直接渲染成
          DialogClose，`@click` 里 `onOpenChange(false)` 无条件执行、不看 defaultPrevented——
          点下去框立刻关，异步请求之后才落地。那样「失败保留原因原地重试」与「pending 禁点」
          都只在控制器层成立、真 UI 走不到（#1607）。用普通 NvButton，由 confirm() 成功才关框。
        -->
        <NvButton
          type="button"
          :variant="props.controller.isActive.value ? 'destructive' : 'default'"
          :disabled="!props.controller.canConfirm.value"
          @click="props.controller.confirm"
        >
          {{ props.controller.isActive.value ? '确认停用' : '确认启用' }}
        </NvButton>
      </NvAlertDialogFooter>
    </NvAlertDialogContent>
  </NvAlertDialog>
</template>
