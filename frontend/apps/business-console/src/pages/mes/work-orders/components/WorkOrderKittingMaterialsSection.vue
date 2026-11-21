<script setup lang="ts">
import { MATERIAL_READINESS_SCOPE_NOTE } from '@/composables/mes/materialReadinessScope'
import { formatDateTime as formatSnapshotCapturedAt } from '@/utils/format'
import { NvButton, NvDataTable, NvStatusBadge } from '@nerv-iip/ui'
import { PackageCheckIcon } from '@lucide/vue'
import { RouterLink } from 'vue-router'
import type { ShallowUnwrapRef } from 'vue'
import type { useWorkOrderDetailPage } from '../useWorkOrderDetailPage'

type View = ShallowUnwrapRef<ReturnType<typeof useWorkOrderDetailPage>>

defineProps<{
  canManageMaterials: View['canManageMaterials']
  canReadInventory: View['canReadInventory']
  formatQuantity: View['formatQuantity']
  materialColumns: View['materialColumns']
  materialReadForbidden: View['materialReadForbidden']
  materialReadPermissionMessage: View['materialReadPermissionMessage']
  materialReadiness: View['materialReadiness']
  materialReadinessError: View['materialReadinessError']
  materialReadinessErrorMessage: View['materialReadinessErrorMessage']
  materialReadinessPending: View['materialReadinessPending']
  materialRowStatus: View['materialRowStatus']
  materialRows: View['materialRows']
  materialShortageCount: View['materialShortageCount']
  openIssueDialog: View['openIssueDialog']
  refreshMaterialReadiness: View['refreshMaterialReadiness']
}>()
</script>

<template>
  <div class="grid gap-2">
    <div class="flex items-center justify-between gap-2">
      <span class="text-sm font-semibold text-foreground">用料齐套</span>
      <!-- 齐套区的「下一步动作」在 PC 上必须真的能执行：这里就是发起领料的入口（#1324）。 -->
      <NvButton
        v-if="canManageMaterials"
        type="button"
        variant="outline"
        size="sm"
        data-testid="open-material-issue"
        @click="openIssueDialog()"
      >
        <PackageCheckIcon aria-hidden="true" />
        发起领料
      </NvButton>
    </div>
    <!-- 口径自解释：齐套只认线边/备料范围，与 MRP 的全厂库存口径本来就不同（#1291）。 -->
    <p
      v-if="materialReadiness"
      class="text-xs text-muted-foreground"
      data-testid="material-readiness-snapshot"
    >
      齐套快照捕获时间：{{
        materialReadiness.snapshotCapturedAtUtc
          ? formatSnapshotCapturedAt(materialReadiness.snapshotCapturedAtUtc)
          : '未记录'
      }}
    </p>
    <p class="text-xs text-muted-foreground" data-testid="material-readiness-scope">
      {{ MATERIAL_READINESS_SCOPE_NOTE }}
    </p>
    <p v-if="materialReadForbidden" class="text-sm text-muted-foreground" role="status">
      {{ materialReadPermissionMessage }}
    </p>
    <NvDataTable
      v-else
      :columns="materialColumns"
      :rows="materialRows"
      :row-key="(r) => `${r.materialId}-${r.materialLotId}`"
      :loading="materialReadinessPending"
      :error="materialReadinessError"
      :error-message="materialReadinessErrorMessage"
      empty-message="暂无用料行。先到「制造 BOM」发布物料清单；还需工单已下达/排产、且有生效生产版本，三者缺一不可。"
      :searchable="false"
      :column-settings="false"
      @retry="refreshMaterialReadiness"
    >
      <template #cell-requiredQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.requiredQuantity) }}</span></template
      >
      <template #cell-availableQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.availableQuantity) }}</span></template
      >
      <template #cell-stagedQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.stagedQuantity) }}</span></template
      >
      <template #cell-shortageQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.shortageQuantity) }}</span></template
      >
      <template #cell-status="{ row }">
        <NvStatusBadge
          :label="materialRowStatus(row.status).label"
          :tone="materialRowStatus(row.status).tone"
        />
      </template>
      <template #cell-shortageStage="{ row }">
        <div class="grid gap-0.5">
          <NvStatusBadge
            class="justify-self-start"
            :value="row.stage.label"
            :label="row.stage.label"
            :tone="row.stage.tone"
          />
          <span class="text-xs text-muted-foreground">{{ row.stage.nextAction }}</span>
        </div>
      </template>
      <template #cell-actions="{ row }">
        <NvButton
          v-if="canManageMaterials"
          type="button"
          variant="ghost"
          size="sm"
          :data-testid="`issue-material-${row.materialId}`"
          @click="openIssueDialog(row.materialId ?? '')"
        >
          发起领料
        </NvButton>
        <span v-else class="text-xs text-muted-foreground">无领料权限</span>
      </template>
    </NvDataTable>
    <!-- 全厂库存对照入口：穿透到库存可用量页，核对「原料仓有没有货」。 -->
    <RouterLink
      v-if="materialShortageCount > 0 && canReadInventory"
      class="justify-self-start text-sm font-medium text-brand hover:underline"
      :to="{ path: '/inventory/availability' }"
      >去库存可用量核对全厂是否有货</RouterLink
    >
  </div>
</template>
