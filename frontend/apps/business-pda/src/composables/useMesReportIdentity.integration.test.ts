import { flushPromises, mount } from '@vue/test-utils'
import { PiniaColada, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { createPinia } from 'pinia'
import { describe, expect, it } from 'vitest'
import { computed, defineComponent, h, reactive, ref, watchEffect } from 'vue'
import { createMemoryHistory, createRouter, useRoute } from 'vue-router'

import { useMesReportIdentity } from './useMesReportIdentity'

type Task = {
  operationTaskId: string
  workOrderId: string
  status: 'ready'
  operationSequence: number
  workCenterId: string
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise
  })
  return { promise, resolve }
}

describe('MES report identity real router + Colada integration', () => {
  it('cancels the old keyed query, ignores its late response, and rebinds on back/forward', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/mes/report', component: { render: () => h('div') } }],
    })
    await router.push('/mes/report?workOrderId=WO-A&operationTaskId=OP-A')
    await router.isReady()

    const requests: Array<{
      workOrderId: string
      signal: AbortSignal
      resolve: (tasks: Task[]) => void
    }> = []
    const observedQueryWorkOrderId = ref('')

    const Harness = defineComponent({
      setup() {
        const route = useRoute()
        const taskFilters = reactive<{ workOrderId?: string }>({})
        const detail = computed(() => {
          const workOrderId = String(route.query.workOrderId ?? '')
          const operationTaskId = String(route.query.operationTaskId ?? '')
          return {
            workOrderId,
            skuId: `SKU-${workOrderId}`,
            quantity: 1,
            status: 'released',
            operationTasks: [
              {
                operationTaskId,
                workOrderId,
                status: 'ready' as const,
                operationSequence: workOrderId === 'WO-A' ? 10 : 20,
                workCenterId: 'WC-1',
              },
            ],
          }
        })
        const query = useQuery(() => ({
          key: ['integration-operation-tasks', taskFilters.workOrderId ?? 'none'],
          enabled: Boolean(taskFilters.workOrderId),
          query: ({ signal }) => {
            const pending = deferred<Task[]>()
            requests.push({
              workOrderId: taskFilters.workOrderId!,
              signal,
              resolve: pending.resolve,
            })
            return pending.promise
          },
        }))
        const queryCache = useQueryCache()
        watchEffect(() => {
          observedQueryWorkOrderId.value = query.data.value?.[0]?.workOrderId ?? ''
        })
        const identity = useMesReportIdentity({
          workOrders: ref([]),
          workOrdersPending: ref(false),
          workOrderDetail: detail,
          workOrderDetailPending: ref(false),
          workOrderDetailError: ref(null),
          operationTasks: computed(() => query.data.value ?? []),
          tasksPending: query.isLoading,
          taskFilters,
          cancelPendingTasks: () =>
            queryCache.cancelQueries({
              predicate: (entry: UseQueryEntry) =>
                Array.isArray(entry.key) && entry.key.includes('integration-operation-tasks'),
            }),
        })
        return () => h('div', identity.pair.value?.operationTaskId ?? 'none')
      },
    })

    const wrapper = mount(Harness, {
      global: {
        plugins: [createPinia(), [PiniaColada, { queryOptions: { gcTime: 300_000 } }], router],
      },
    })
    await flushPromises()
    expect(requests.at(-1)?.workOrderId).toBe('WO-A')
    const requestA = requests.at(-1)!

    await router.push('/mes/report?workOrderId=WO-B&operationTaskId=OP-B')
    await flushPromises()
    expect(requestA.signal.aborted).toBe(true)
    expect(requests.at(-1)?.workOrderId).toBe('WO-B')
    const requestB = requests.at(-1)!
    requestB.resolve([
      {
        operationTaskId: 'OP-B',
        workOrderId: 'WO-B',
        status: 'ready',
        operationSequence: 20,
        workCenterId: 'WC-1',
      },
    ])
    await flushPromises()
    expect(wrapper.text()).toBe('OP-B')
    expect(observedQueryWorkOrderId.value).toBe('WO-B')

    requestA.resolve([
      {
        operationTaskId: 'OP-A',
        workOrderId: 'WO-A',
        status: 'ready',
        operationSequence: 10,
        workCenterId: 'WC-1',
      },
    ])
    await flushPromises()
    expect(wrapper.text()).toBe('OP-B')
    expect(observedQueryWorkOrderId.value).toBe('WO-B')

    router.back()
    await flushPromises()
    expect(router.currentRoute.value.query).toMatchObject({
      workOrderId: 'WO-A',
      operationTaskId: 'OP-A',
    })
    router.forward()
    await flushPromises()
    expect(router.currentRoute.value.query).toMatchObject({
      workOrderId: 'WO-B',
      operationTaskId: 'OP-B',
    })
  })
})
