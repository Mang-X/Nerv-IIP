import {
  BusinessOperationUnconfirmedError,
  raiseBusinessConsoleMesAndonCall,
  type BusinessConsoleMesAndonCategory,
  type BusinessConsoleMesAndonCallResponse,
  type BusinessConsoleMesRaiseAndonCallRequest,
} from '@nerv-iip/api-client'
import { computed, shallowRef, watch, type Ref } from 'vue'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import { useIdempotentWriteIntent } from '@/composables/useIdempotentWriteIntent'

export type AndonOperationContext = Omit<
  BusinessConsoleMesRaiseAndonCallRequest,
  'category' | 'idempotencyKey'
> & { identity: string }

export function useMesAndonCall(context: Readonly<Ref<AndonOperationContext | null>>) {
  const intent =
    useIdempotentWriteIntent<BusinessConsoleMesRaiseAndonCallRequest>(makeIdempotencyKey)
  const category = shallowRef<BusinessConsoleMesAndonCategory | null>(null)
  const pending = shallowRef(false)
  const receipt = shallowRef<BusinessConsoleMesAndonCallResponse | null>(null)
  const errorMessage = shallowRef('')
  const frozenIdentity = shallowRef('')
  const identity = computed(() => JSON.stringify(context.value))
  const contextConflict = computed(() =>
    Boolean(frozenIdentity.value && frozenIdentity.value !== identity.value),
  )
  const unresolved = computed(() => pending.value || intent.locked.value)
  const message = computed(() =>
    contextConflict.value && unresolved.value
      ? '有一笔呼叫结果待核实，请恢复原账号、组织环境、作业范围及工序后重试。'
      : errorMessage.value,
  )
  const canSubmit = computed(() =>
    Boolean(
      context.value && category.value && !pending.value && !receipt.value && !contextConflict.value,
    ),
  )
  let generation = 0

  function reset() {
    if (unresolved.value) return
    intent.reset()
    category.value = null
    receipt.value = null
    errorMessage.value = ''
    frozenIdentity.value = ''
  }

  watch(
    identity,
    () => {
      generation += 1
      receipt.value = null
      errorMessage.value = ''
      if (!unresolved.value) reset()
    },
    { flush: 'sync' },
  )

  function selectCategory(value: BusinessConsoleMesAndonCategory) {
    if (unresolved.value || receipt.value) return
    if (!intent.key.value) intent.start()
    if (category.value !== value) intent.inputChanged()
    category.value = value
    errorMessage.value = ''
  }

  async function submit() {
    const current = context.value
    const selectedCategory = category.value
    if (!canSubmit.value || !current || !selectedCategory) return
    frozenIdentity.value = identity.value
    const { identity: _identity, ...source } = current
    const body = intent.payload((idempotencyKey) => ({
      ...source,
      category: selectedCategory,
      idempotencyKey,
    }))
    const requestGeneration = generation
    pending.value = true
    errorMessage.value = ''
    intent.markCommandAttempt()
    try {
      const { data: envelope } = await raiseBusinessConsoleMesAndonCall({
        body,
        throwOnError: true,
      })
      if (requestGeneration !== generation) {
        intent.recordFailure(new BusinessOperationUnconfirmedError('呼叫上下文已变化'), '')
        return
      }
      if (envelope?.success === false) throw envelope
      const result = envelope?.data
      if (
        !envelope?.success ||
        !result?.id ||
        result.workOrderId !== body.workOrderId ||
        result.operationTaskId !== body.operationTaskId ||
        result.category !== body.category ||
        result.organizationId !== body.organizationId ||
        result.environmentId !== body.environmentId
      ) {
        throw new BusinessOperationUnconfirmedError('未取得呼叫成功凭据')
      }
      receipt.value = result
      intent.reset()
    } catch (error) {
      const info = intent.recordFailure(
        requestGeneration === generation
          ? error
          : new BusinessOperationUnconfirmedError('呼叫上下文已变化'),
        '呼叫失败，请重试。',
      )
      if (requestGeneration === generation) {
        errorMessage.value = info.indeterminate
          ? `${info.message}。结果待核实，请重试原呼叫，勿重复发起。`
          : info.message
      }
    } finally {
      pending.value = false
    }
  }

  return {
    category,
    pending,
    receipt,
    unresolved,
    message,
    canSubmit,
    selectCategory,
    submit,
    reset,
  }
}
