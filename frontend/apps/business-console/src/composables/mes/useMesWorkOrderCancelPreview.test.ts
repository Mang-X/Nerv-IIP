import { configureApiClient } from '@nerv-iip/api-client'
import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { afterEach, describe, expect, it } from 'vitest'
import { defineComponent, shallowRef } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMesWorkOrderDetail, useMesWorkScopeSelection } from '.'

const receiptPath = '/api/business-console/v1/mes/finished-goods-receipt-requests'
const materialPath = '/api/business-console/v1/mes/material-issue-requests'
const orderA = 'WO-20261017-01'
const orderB = 'WO-20261017-02'

function ok(data: unknown) {
  return Response.json({ success: true, data })
}

function compensationPage(path: string, workOrderId: string, skip: number, total: number) {
  const items = Array.from({ length: Math.min(200, total - skip) }, (_, index) =>
    path === receiptPath
      ? { receiptRequestId: `${workOrderId}-FG-${skip + index + 1}`, workOrderId }
      : { requestId: `${workOrderId}-MI-${skip + index + 1}`, workOrderId },
  )
  return ok({ items, total })
}

function mountPreview() {
  const requests: Array<{ url: URL; resolve: (response: Response) => void }> = []
  let scopeAvailable = true
  configureApiClient({
    baseUrl: 'http://business-gateway.test',
    fetch: ((request: Request) => {
      const url = new URL(request.url)
      if (request.method !== 'GET') throw new Error(`Unexpected request: ${request.method} ${url}`)
      if (url.pathname === '/api/business-console/v1/me/work-context') {
        return Promise.resolve(
          ok({
            selectedScope: scopeAvailable ? { kind: 'work-center', id: 'WC-A' } : null,
          }),
        )
      }
      if (
        /^\/api\/business-console\/v1\/mes\/work-orders\/[^/]+(\/material-readiness)?$/.test(
          url.pathname,
        )
      ) {
        return Promise.resolve(ok({}))
      }
      return new Promise<Response>((resolve) => requests.push({ url, resolve }))
    }) as typeof fetch,
  })

  const pinia = createPinia()
  useAuthStore(pinia).principal = {
    permissionCodes: ['business.mes.work-orders.read', 'business.mes.work-orders.manage'],
  }
  useBusinessContextStore(pinia).patchContext({
    organizationId: 'org-001',
    environmentId: 'env-dev',
  })
  const workOrderId = shallowRef(orderA)
  let readScope!: ReturnType<typeof useMesWorkScopeSelection>
  let preview!: ReturnType<typeof useMesWorkOrderDetail>
  mount(
    defineComponent({
      setup() {
        preview = useMesWorkOrderDetail(workOrderId)
        readScope = useMesWorkScopeSelection('business.mes.work-orders.read')
        return () => null
      },
    }),
    { global: { plugins: [pinia, PiniaColada] } },
  )

  function respond(path: string, order: string, skip: number, response: Response) {
    const index = requests.findIndex(
      ({ url }) =>
        url.pathname === path &&
        url.searchParams.get('workOrderId') === order &&
        url.searchParams.get('skip') === String(skip),
    )
    expect(index, `GET ${path} for ${order}, skip ${skip}`).toBeGreaterThanOrEqual(0)
    const [request] = requests.splice(index, 1)
    expect(Object.fromEntries(request!.url.searchParams)).toEqual({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: order,
      skip: String(skip),
      take: '200',
    })
    request!.resolve(response)
  }

  function page(path: string, order: string, skip: number, total: number) {
    respond(path, order, skip, compensationPage(path, order, skip, total))
  }

  return {
    workOrderId,
    preview,
    requests,
    page,
    respond,
    loseReadScope() {
      scopeAvailable = false
      return readScope.refreshScope()
    },
  }
}

describe('MES work-order cancel preview over the generated HTTP client', () => {
  afterEach(() => configureApiClient())

  // 工作包 D：按当前工单取全两类补偿单据，迟到的旧工单响应不能改变当前预览。
  it('reads all pages for the current work order even when the previous order finishes late', async () => {
    const { workOrderId, preview, requests, page } = mountPreview()
    await flushPromises()
    expect(requests.map(({ url }) => url.pathname)).toEqual([materialPath])
    expect(preview.cancelPreviewReady.value).toBe(false)

    preview.activateCancelPreview()
    await flushPromises()
    page(materialPath, orderA, 0, 201)
    page(receiptPath, orderA, 0, 201)
    await flushPromises()
    expect(requests.map(({ url }) => url.searchParams.get('skip'))).toEqual(['200', '200'])

    workOrderId.value = orderB
    await flushPromises()
    expect(preview.cancelPreviewReady.value).toBe(false)
    expect(preview.materialIssueRequests.value).toEqual([])
    expect(preview.finishedGoodsReceiptRequests.value).toEqual([])
    page(materialPath, orderB, 0, 202)
    page(receiptPath, orderB, 0, 203)
    await flushPromises()
    page(materialPath, orderB, 200, 202)
    await flushPromises()
    expect(preview.cancelPreviewReady.value).toBe(false)
    page(receiptPath, orderB, 200, 203)
    await flushPromises()

    const materials = preview.materialIssueRequests.value.map((row) => row.requestId)
    const receipts = preview.finishedGoodsReceiptRequests.value.map((row) => row.receiptRequestId)
    expect(materials).toHaveLength(202)
    expect(materials.slice(199)).toEqual([
      'WO-20261017-02-MI-200',
      'WO-20261017-02-MI-201',
      'WO-20261017-02-MI-202',
    ])
    expect(receipts).toHaveLength(203)
    expect(receipts.slice(199)).toEqual([
      'WO-20261017-02-FG-200',
      'WO-20261017-02-FG-201',
      'WO-20261017-02-FG-202',
      'WO-20261017-02-FG-203',
    ])
    expect(preview.cancelPreviewReady.value).toBe(true)

    page(materialPath, orderA, 200, 201)
    page(receiptPath, orderA, 200, 201)
    await flushPromises()
    expect(requests).toHaveLength(0)
    expect(preview.cancelPreviewReady.value).toBe(true)
    expect(preview.materialIssueRequests.value.map((row) => row.requestId)).toEqual(materials)
    expect(preview.finishedGoodsReceiptRequests.value.map((row) => row.receiptRequestId)).toEqual(
      receipts,
    )
  })

  it.each([
    { label: '领料申请', failingPath: materialPath, otherPath: receiptPath },
    { label: '完工入库请求', failingPath: receiptPath, otherPath: materialPath },
  ])(
    'blocks confirmation when page two of $label returns a failed HTTP 200 envelope',
    async ({ failingPath, otherPath }) => {
      const { preview, page, respond } = mountPreview()
      preview.activateCancelPreview()
      await flushPromises()
      page(otherPath, orderA, 0, 0)
      page(failingPath, orderA, 0, 0)
      await flushPromises()
      expect(preview.cancelPreviewReady.value).toBe(true)

      preview.retryCancelPreview()
      await flushPromises()
      expect(preview.cancelPreviewPending.value).toBe(true)
      expect(preview.cancelPreviewReady.value).toBe(false)
      page(otherPath, orderA, 0, 0)
      page(failingPath, orderA, 0, 201)
      await flushPromises()
      respond(
        failingPath,
        orderA,
        200,
        Response.json({ success: false, message: '无法读取关联单据', data: null }),
      )
      await flushPromises()

      expect(preview.cancelPreviewReady.value).toBe(false)
      expect(preview.cancelPreviewError.value).toEqual(new Error('无法读取关联单据'))
      expect(preview.cancelPreviewPending.value).toBe(false)

      preview.retryCancelPreview()
      await flushPromises()
      expect(preview.cancelPreviewReady.value).toBe(false)
      page(materialPath, orderA, 0, 0)
      page(receiptPath, orderA, 0, 0)
      await flushPromises()
      expect(preview.cancelPreviewError.value).toBeNull()
      expect(preview.cancelPreviewReady.value).toBe(true)
    },
  )

  it('blocks confirmation after the current scope becomes unavailable', async () => {
    const { preview, requests, page, loseReadScope } = mountPreview()
    preview.activateCancelPreview()
    await flushPromises()
    page(materialPath, orderA, 0, 0)
    page(receiptPath, orderA, 0, 0)
    await flushPromises()
    expect(preview.cancelPreviewReady.value).toBe(true)

    await loseReadScope()
    await flushPromises()
    expect(preview.workOrderReadScopeReady.value).toBe(false)
    expect(preview.workOrderManageScopeReady.value).toBe(true)
    expect(preview.cancelPreviewReady.value).toBe(false)
    expect(requests).toHaveLength(0)
  })
})
