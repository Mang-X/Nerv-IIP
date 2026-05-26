<script setup lang="ts">
import BusinessActionSheet from '@/components/business/BusinessActionSheet.vue'
import BusinessEmptyState from '@/components/business/BusinessEmptyState.vue'
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useInventoryMovement } from '@/composables/useBusinessInventory'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type { BusinessConsolePostStockMovementRequest } from '@nerv-iip/api-client'
import {
  Button,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
} from '@nerv-iip/ui'
import { SendIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.movements',
  },
})

const { postMovement, postMovementError, postMovementPending } = useInventoryMovement()

const form = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  movementType: 'receipt',
  sourceService: 'business-console',
  sourceDocumentId: '',
  sourceDocumentLineId: '',
  idempotencyKey: '',
  skuCode: 'SKU-001',
  uomCode: 'EA',
  siteCode: 'S1',
  locationCode: '',
  lotNo: '',
  serialNo: '',
  qualityStatus: 'available',
  ownerType: 'owned',
  ownerId: '',
  quantity: '1',
})

const successMessage = shallowRef('')
const movementSheetOpen = shallowRef(false)
const errorMessage = computed(() => formatError(postMovementError.value))
const canSubmit = computed(
  () =>
    isNonEmpty(form.organizationId) &&
    isNonEmpty(form.environmentId) &&
    isNonEmpty(form.movementType) &&
    isNonEmpty(form.sourceDocumentId) &&
    isNonEmpty(form.skuCode) &&
    isNonEmpty(form.uomCode) &&
    isNonEmpty(form.siteCode) &&
    isNonEmpty(form.locationCode) &&
    toOptionalNumber(form.quantity) !== undefined,
)

async function submitMovement() {
  if (!canSubmit.value) return

  const body: BusinessConsolePostStockMovementRequest = {
    organizationId: form.organizationId.trim(),
    environmentId: form.environmentId.trim(),
    movementType: form.movementType,
    sourceService: form.sourceService.trim() || 'business-console',
    sourceDocumentId: form.sourceDocumentId.trim(),
    sourceDocumentLineId: optionalText(form.sourceDocumentLineId),
    idempotencyKey: optionalText(form.idempotencyKey) ?? `movement-${Date.now()}`,
    skuCode: form.skuCode.trim(),
    uomCode: form.uomCode.trim(),
    siteCode: form.siteCode.trim(),
    locationCode: form.locationCode.trim(),
    lotNo: optionalText(form.lotNo),
    serialNo: optionalText(form.serialNo),
    qualityStatus: form.qualityStatus.trim(),
    ownerType: form.ownerType.trim(),
    ownerId: optionalText(form.ownerId),
    quantity: toOptionalNumber(form.quantity),
  }

  const response = await postMovement(body)
  successMessage.value = `库存移动 ${response?.data?.movementId ?? body.idempotencyKey} 已受理。`
}

function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}

function toOptionalNumber(value: string) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}

function isNonEmpty(value: string) {
  return value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="库存"
        title="库存移动过账"
        summary="以库存流水和来源单据为中心处理入库、出库、调拨和调整。"
      >
        <template #actions>
          <Button size="sm" type="button" @click="movementSheetOpen = true">
            <SendIcon data-icon="inline-start" />
            新建移动
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-4 lg:grid-cols-[minmax(0,1fr)_320px]">
        <section class="rounded-lg border bg-background">
          <div class="border-b px-4 py-3">
            <h2 class="text-sm font-semibold text-foreground">库存移动工作台</h2>
            <p class="mt-1 text-sm text-muted-foreground">后续接入库存流水后，主页面展示待确认、失败和最近过账记录。</p>
          </div>
          <BusinessEmptyState
            title="暂未接入库存移动列表"
            description="库存过账属于高风险动作，先从右上角进入抽屉填写业务单据，技术字段由系统生成。"
            action="建议优先从收货、完工入库、领料或盘点任务上下文发起。"
          />
        </section>

        <section class="rounded-lg border bg-background p-4">
          <h2 class="text-sm font-semibold text-foreground">操作原则</h2>
          <div class="mt-3 grid gap-2 text-sm text-muted-foreground">
            <p>来源服务固定为业务控制台，不让一线用户选择技术来源。</p>
            <p>幂等键默认自动生成，只在排查重复过账时才展开。</p>
            <p>库存移动完成后应回到库存流水和可用量页面确认影响。</p>
          </div>
        </section>
      </div>

      <BusinessActionSheet
        v-model:open="movementSheetOpen"
        title="新建库存移动"
        description="用于少量人工补录和异常处理；常规业务应从来源单据自动发起。"
      >
      <form class="grid gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitMovement">
        <BusinessFormStatus :error="errorMessage" :success="successMessage" />

        <FieldGroup class="grid gap-3 md:grid-cols-3">
          <Field>
            <FieldLabel for="movement-org">组织</FieldLabel>
            <Input id="movement-org" v-model="form.organizationId" required />
          </Field>
          <Field>
            <FieldLabel for="movement-env">环境</FieldLabel>
            <Input id="movement-env" v-model="form.environmentId" required />
          </Field>
          <Field>
            <FieldLabel>移动类型</FieldLabel>
            <Select v-model="form.movementType">
              <SelectTrigger aria-label="移动类型">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="receipt">入库</SelectItem>
                <SelectItem value="issue">出库</SelectItem>
                <SelectItem value="transfer">调拨</SelectItem>
                <SelectItem value="adjustment">调整</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel for="movement-source-document">来源单据</FieldLabel>
            <Input id="movement-source-document" v-model="form.sourceDocumentId" required />
          </Field>
          <Field>
            <FieldLabel for="movement-source-line">来源行</FieldLabel>
            <Input id="movement-source-line" v-model="form.sourceDocumentLineId" />
          </Field>
          <Field class="md:col-span-3">
            <FieldLabel for="movement-idempotency">幂等键</FieldLabel>
            <Input id="movement-idempotency" v-model="form.idempotencyKey" placeholder="默认自动生成，排查重复过账时填写" />
          </Field>
          <Field>
            <FieldLabel for="movement-sku">SKU</FieldLabel>
            <Input id="movement-sku" v-model="form.skuCode" required />
          </Field>
          <Field>
            <FieldLabel for="movement-uom">单位</FieldLabel>
            <Input id="movement-uom" v-model="form.uomCode" required />
          </Field>
          <Field>
            <FieldLabel for="movement-site">工厂</FieldLabel>
            <Input id="movement-site" v-model="form.siteCode" required />
          </Field>
          <Field>
            <FieldLabel for="movement-location">库位</FieldLabel>
            <Input id="movement-location" v-model="form.locationCode" required />
          </Field>
          <Field>
            <FieldLabel for="movement-quantity">数量</FieldLabel>
            <Input id="movement-quantity" v-model="form.quantity" inputmode="decimal" required type="number" />
          </Field>
          <Field>
            <FieldLabel for="movement-quality">质量状态</FieldLabel>
            <Input id="movement-quality" v-model="form.qualityStatus" required />
          </Field>
          <Field>
            <FieldLabel for="movement-owner-type">货主类型</FieldLabel>
            <Input id="movement-owner-type" v-model="form.ownerType" required />
          </Field>
          <Field>
            <FieldLabel for="movement-owner-id">货主 ID</FieldLabel>
            <Input id="movement-owner-id" v-model="form.ownerId" />
          </Field>
          <Field>
            <FieldLabel for="movement-lot">批次</FieldLabel>
            <Input id="movement-lot" v-model="form.lotNo" />
          </Field>
          <Field>
            <FieldLabel for="movement-serial">序列号</FieldLabel>
            <Input id="movement-serial" v-model="form.serialNo" />
          </Field>
        </FieldGroup>

        <div class="flex justify-end">
          <Button type="submit" :disabled="postMovementPending || !canSubmit">
            <Spinner v-if="postMovementPending" data-icon="inline-start" />
            <SendIcon v-else data-icon="inline-start" />
            提交库存移动
          </Button>
        </div>
      </form>
      </BusinessActionSheet>
    </section>
  </BusinessLayout>
</template>
