import { mount, flushPromises } from '@vue/test-utils'
import { defineComponent, h, shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { BusinessConsoleMesAndonCategory } from '@nerv-iip/api-client'
import { OfflineError, RequestTimeoutError } from '@/api/request-timeout'
import { useMesAndonCall, type AndonOperationContext } from './useMesAndonCall'

const sdk = vi.hoisted(() => ({ raise: vi.fn() }))
vi.mock('@nerv-iip/api-client', async (original) => ({
  ...(await original<typeof import('@nerv-iip/api-client')>()),
  raiseBusinessConsoleMesAndonCall: sdk.raise,
}))

const originalContext: AndonOperationContext = {
  identity: 'principal-1:org-1:env-1:work-center:WC-A',
  organizationId: 'org-1',
  environmentId: 'env-1',
  scopeKind: 'work-center',
  scopeId: 'WC-A',
  workOrderId: 'WO-1',
  operationTaskId: 'OP-1',
  workCenterId: 'WC-A',
}
const call = {
  id: 'andon-1',
  organizationId: 'org-1',
  environmentId: 'env-1',
  category: 'quality',
  status: 'open',
  workOrderId: 'WO-1',
  operationTaskId: 'OP-1',
  workCenterId: 'WC-A',
  callerId: 'principal-1',
  raisedAtUtc: '2026-09-20T08:00:00Z',
  responderId: null,
  firstRespondedAtUtc: null,
  responseDurationSeconds: null,
  closedAtUtc: null,
  escalatedAtUtc: null,
  escalationRecipientId: null,
}

function setup() {
  const context = shallowRef<AndonOperationContext | null>({ ...originalContext })
  let model!: ReturnType<typeof useMesAndonCall>
  mount(
    defineComponent({
      setup() {
        model = useMesAndonCall(context)
        return () => h('div', model.receipt.value?.id)
      },
    }),
  )
  return { context, model }
}

beforeEach(() => sdk.raise.mockReset())

describe('PDA Andon intent — Issue #3654', () => {
  it.each<BusinessConsoleMesAndonCategory>(['materialShortage', 'equipment', 'quality', 'process'])(
    'submits %s with the verified source pair and scope, and shows only the returned receipt',
    async (category) => {
      sdk.raise.mockResolvedValue({ data: { success: true, data: { ...call, category } } })
      const { model } = setup()
      model.selectCategory(category)
      await model.submit()
      expect(sdk.raise).toHaveBeenCalledWith({
        body: {
          organizationId: 'org-1',
          environmentId: 'env-1',
          scopeKind: 'work-center',
          scopeId: 'WC-A',
          workOrderId: 'WO-1',
          operationTaskId: 'OP-1',
          workCenterId: 'WC-A',
          category,
          idempotencyKey: expect.any(String),
        },
        throwOnError: true,
      })
      expect(model.receipt.value).toMatchObject({ id: 'andon-1', category })
    },
  )

  it('retains the exact key and payload after a timeout and blocks changing the category', async () => {
    sdk.raise
      .mockRejectedValueOnce(new RequestTimeoutError())
      .mockResolvedValueOnce({ data: { success: true, data: call } })
    const { model } = setup()
    model.selectCategory('quality')
    await model.submit()
    expect(model.receipt.value).toBeNull()
    expect(model.unresolved.value).toBe(true)
    model.selectCategory('equipment')
    await model.submit()
    expect(sdk.raise.mock.calls[1][0].body).toEqual(sdk.raise.mock.calls[0][0].body)
    expect(model.receipt.value?.id).toBe('andon-1')
    expect(model.unresolved.value).toBe(false)
  })

  it.each([
    { name: 'offline precheck', error: new OfflineError() },
    { name: 'authorization rejection', error: { status: 403 } },
  ])('keeps the original unknown call locked after a retry fails with $name', async ({ error }) => {
    sdk.raise
      .mockRejectedValueOnce(new RequestTimeoutError())
      .mockRejectedValueOnce(error)
      .mockResolvedValueOnce({ data: { success: true, data: call } })
    const { model } = setup()
    model.selectCategory('quality')
    await model.submit()
    await model.submit()
    expect(model.unresolved.value).toBe(true)
    expect(model.receipt.value).toBeNull()
    expect(model.message.value).toContain('结果待核实')
    model.selectCategory('equipment')
    model.reset()
    expect(model.category.value).toBe('quality')
    await model.submit()
    expect(sdk.raise.mock.calls.map(([request]) => request.body)).toEqual([
      sdk.raise.mock.calls[0][0].body,
      sdk.raise.mock.calls[0][0].body,
      sdk.raise.mock.calls[0][0].body,
    ])
    expect(model.receipt.value?.id).toBe('andon-1')
    expect(model.unresolved.value).toBe(false)
  })

  it.each([
    'identity',
    'organizationId',
    'environmentId',
    'scopeId',
    'workOrderId',
    'operationTaskId',
  ] as const)(
    'does not apply a late result or retry the frozen intent after %s changes',
    async (field) => {
      let resolve!: (value: unknown) => void
      sdk.raise.mockImplementationOnce(
        () =>
          new Promise((done) => {
            resolve = done
          }),
      )
      const { context, model } = setup()
      model.selectCategory('quality')
      const pending = model.submit()
      context.value = { ...originalContext, [field]: 'changed' }
      await flushPromises()
      resolve({ data: { success: true, data: call } })
      await pending
      expect(model.receipt.value).toBeNull()
      expect(model.message.value).toContain('恢复')
      await model.submit()
      expect(sdk.raise).toHaveBeenCalledTimes(1)
      context.value = { ...originalContext }
      await flushPromises()
      sdk.raise.mockResolvedValueOnce({ data: { success: true, data: call } })
      await model.submit()
      expect(sdk.raise.mock.calls[1][0].body).toEqual(sdk.raise.mock.calls[0][0].body)
      expect(model.receipt.value?.id).toBe('andon-1')
    },
  )

  it('does not dispatch without a verified context', async () => {
    const { context, model } = setup()
    context.value = null
    model.selectCategory('quality')
    await model.submit()
    expect(sdk.raise).not.toHaveBeenCalled()
    expect(model.canSubmit.value).toBe(false)
  })

  it('keeps an incomplete success response unresolved and retries the original intent', async () => {
    sdk.raise
      .mockResolvedValueOnce({ data: { success: true, data: null } })
      .mockResolvedValueOnce({ data: { success: true, data: call } })
    const { model } = setup()
    model.selectCategory('quality')
    await model.submit()
    expect(model.receipt.value).toBeNull()
    expect(model.unresolved.value).toBe(true)
    await model.submit()
    expect(sdk.raise.mock.calls[1][0].body).toEqual(sdk.raise.mock.calls[0][0].body)
    expect(model.receipt.value?.id).toBe('andon-1')
  })

  it('ignores a late retry failure after switching tasks and does not send it under the new task', async () => {
    let reject!: (error: unknown) => void
    sdk.raise.mockRejectedValueOnce(new RequestTimeoutError()).mockImplementationOnce(
      () =>
        new Promise((_resolve, fail) => {
          reject = fail
        }),
    )
    const { context, model } = setup()
    model.selectCategory('quality')
    await model.submit()
    const retry = model.submit()
    context.value = { ...originalContext, operationTaskId: 'OP-2' }
    await flushPromises()
    reject({ status: 403, message: 'old request denied' })
    await retry
    expect(model.receipt.value).toBeNull()
    expect(model.message.value).toContain('恢复')
    expect(model.message.value).not.toContain('old request denied')
    await model.submit()
    expect(sdk.raise).toHaveBeenCalledTimes(2)
  })

  it('does not render a business rejection as success and permits recovery', async () => {
    sdk.raise
      .mockResolvedValueOnce({ data: { success: false, message: '工序来源不可用' } })
      .mockResolvedValueOnce({ data: { success: true, data: call } })
    const { model } = setup()
    model.selectCategory('quality')
    await model.submit()
    expect(model.receipt.value).toBeNull()
    expect(model.message.value).toContain('工序来源不可用')
    await model.submit()
    expect(model.receipt.value?.id).toBe('andon-1')
  })
})
