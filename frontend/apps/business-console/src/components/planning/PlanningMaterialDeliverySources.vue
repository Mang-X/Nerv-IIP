<script setup lang="ts">
import type { BusinessConsoleMaterialDeliveryResponse } from '@nerv-iip/api-client'
import { NvButton } from '@nerv-iip/ui'
import {
  materialDeliverySourceLabel as label,
  materialDeliveryUtc as utc,
} from './materialDeliveryPresentation'
const props = defineProps<{ row: BusinessConsoleMaterialDeliveryResponse }>()
const emit = defineEmits<{ locateSuggestion: [suggestionId: string, runId: string] }>()
function dueSourceLabel(reference: string | null | undefined): string {
  if (!reference) return '—'
  const source = props.row.demandSources?.find(item => item.demandSourceId === reference)
  if (!source) return '未知需求来源'
  return `${source.sourceReference}${source.sourceLineReference ? ` · 行 ${source.sourceLineReference}` : ''}`
}
const netFields = [
  ['grossDemandQuantity', '毛需求'],
  ['onHandQuantity', '在库'],
  ['reservedQuantity', '已预留'],
  ['availableToNetQuantity', '可用净算'],
  ['scheduledReceiptQuantity', '计划接收'],
  ['safetyStockQuantity', '安全库存'],
  ['netRequirementQuantity', '净缺口'],
  ['plannedQuantity', '计划补充'],
  ['scrapRate', '损耗率'],
  ['yieldRate', '良率'],
] as const
</script>
<template>
  <div class="grid min-w-80 gap-4 whitespace-normal py-3 text-sm">
    <section>
      <h3 class="mb-2 font-semibold">净算依据</h3>
      <dl class="grid grid-cols-2 gap-x-4 gap-y-1">
        <template v-for="[key, title] in netFields" :key="key"
          ><dt class="text-muted-foreground">{{ title }}</dt>
          <dd class="tabular-nums">{{ row.netRequirementSource?.[key] ?? '—' }}</dd></template
        >
      </dl>
      <p v-if="row.netRequirementSource?.formula" class="mt-2">
        {{ row.netRequirementSource.formula }}
      </p>
      <p v-if="row.netRequirementSource?.uomConversionSummary">
        {{ row.netRequirementSource.uomConversionSummary }}
      </p>
      <p class="mt-2 text-xs text-muted-foreground">
        最晚采购日期 {{ row.latestProcurementDate }} →
        <span :title="row.latestProcurementUtc">{{ utc(row.latestProcurementUtc) }}</span>
      </p>
      <p v-if="row.expectedArrivalUtc" class="text-xs text-muted-foreground">
        预计到达日期 {{ row.expectedArrivalDate }} →
        <span :title="row.expectedArrivalUtc">{{ utc(row.expectedArrivalUtc) }}</span>
      </p>
    </section>
    <section>
      <h3 class="mb-2 font-semibold">销售与需求来源 · pegging</h3>
      <article v-for="(source, i) in row.demandSources" :key="i" class="mb-2 rounded-md border p-2">
        <p class="font-medium">
          {{ source.sourceReference }}
          <span v-if="source.sourceLineReference">· 行 {{ source.sourceLineReference }}</span> ·
          {{ label(source.sourceType) }}
        </p>
        <p>毛需求 {{ source.grossDemandQuantity ?? '—' }} · 交期 {{ source.dueDate ?? '—' }}</p>
        <p>
          上层物料 {{ source.parentSkuCode ?? '—' }} · 组件 {{ source.componentSkuCode ?? '—' }}
        </p>
        <p>
          生产版本 {{ source.productionVersionReference ?? '—' }} · BOM
          {{ source.manufacturingBomReference ?? '—' }} · 工艺 {{ source.routingReference ?? '—' }}
        </p>
      </article>
      <p v-if="!row.demandSources?.length" class="text-muted-foreground">需求来源缺失</p>
    </section>
    <section>
      <h3 class="mb-2 font-semibold">采购承诺供应</h3>
      <article v-for="(source, i) in row.supplySources" :key="i" class="mb-2 rounded-md border p-2">
        <p class="font-medium">{{ source.purchaseOrderNo }} · 行 {{ source.lineNo }}</p>
        <p>
          {{ source.skuCode }} · {{ source.siteCode }} · 未收 {{ source.openQuantity }}
          {{ source.uomCode }} · 承诺 {{ source.promisedDate }}
        </p>
        <p v-for="(purchase, j) in source.sources" :key="j">
          采购申请 {{ purchase.purchaseRequisitionNo }} · 行
          {{ purchase.purchaseRequisitionLineNo }} · 数量 {{ purchase.quantity }}
        </p>
      </article>
      <p v-if="!row.supplySources?.length" class="text-muted-foreground">暂无关联承诺供应</p>
      <p>
        覆盖 {{ row.coveredQuantity ?? '—' }} · 未覆盖 {{ row.uncoveredQuantity ?? '—' }}
        {{ row.uomCode }}
      </p>
    </section>
    <section>
      <h3 class="mb-2 font-semibold">APS 剩余工艺与排程事实</h3>
      <article
        v-for="(source, i) in row.schedulingSources"
        :key="i"
        class="mb-3 rounded-md border p-2"
      >
        <p class="font-medium">
          {{ source.workOrderId ?? '未关联工单' }} · {{ label(source.status) }}
        </p>
        <p>
          最紧交期来源
          <span :title="source.tightestDueSourceReference ?? undefined">{{
            dueSourceLabel(source.tightestDueSourceReference)
          }}</span>
        </p>
        <div v-for="(bound, j) in source.dueBounds" :key="j" class="mt-2 border-t pt-2">
          <p>
            <span :title="bound.sourceReference">{{ dueSourceLabel(bound.sourceReference) }}</span> · 交期
            <span :title="bound.dueUtc">{{ utc(bound.dueUtc) }}</span>
          </p>
          <p>
            剩余工艺 {{ bound.remainingMinutes ?? '—' }} 分钟 · 最晚开工
            <span :title="bound.latestStartUtc">{{ utc(bound.latestStartUtc) }}</span>
          </p>
          <p>关键路径 {{ bound.criticalPathOperationIds?.join(' → ') || '—' }}</p>
        </div>
        <div v-for="(operation, j) in source.operations" :key="j" class="mt-2 border-t pt-2">
          <p class="font-medium">
            工序 {{ operation.operationSequence ?? '—' }} · {{ operation.operationId }}
          </p>
          <p>
            净良品 {{ operation.netGoodQuantity ?? '—' }} · 剩余数量
            {{ operation.remainingQuantity ?? '—' }} · 剩余时长
            {{ operation.remainingMinutes ?? '—' }} 分钟
          </p>
          <p>
            输入最早开工
            <span data-input-earliest-start :title="operation.earliestStartUtc">{{
              utc(operation.earliestStartUtc)
            }}</span>
          </p>
          <p>
            实际排程开始
            <span data-assignment-start :title="operation.assignmentStartUtc ?? undefined">{{
              utc(operation.assignmentStartUtc)
            }}</span>
            · {{ label(operation.assignmentStatus) }}
          </p>
          <p>
            工艺版本 {{ operation.routingVersionId ?? '—' }} · 前序
            {{ operation.predecessorOperationIds?.join('、') || '—' }}
          </p>
        </div>
      </article>
      <p v-if="!row.schedulingSources?.length" class="text-muted-foreground">暂无排程来源</p>
    </section>
    <section>
      <h3 class="mb-2 font-semibold">拆批建议与承接单据</h3>
      <div
        v-for="source in row.suggestionSources"
        :key="source.suggestionId"
        class="mb-2 rounded-md border p-2"
      >
        <p>
          {{ label(source.status) }} · 建议量 {{ source.quantity ?? '—' }} · 计划量
          {{ source.plannedQuantity ?? '—' }} {{ row.uomCode }}
        </p>
        <p v-if="source.downstreamDocumentId">承接单据 {{ source.downstreamDocumentId }}</p>
        <NvButton
          v-if="source.suggestionId && props.row.runId"
          size="sm"
          variant="ghost"
          @click="emit('locateSuggestion', source.suggestionId, props.row.runId)"
          >定位计划建议</NvButton
        >
      </div>
    </section>
  </div>
</template>
