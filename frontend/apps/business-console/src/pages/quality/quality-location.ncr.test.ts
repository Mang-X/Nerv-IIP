import {
  resetQualityLocationFixture,
  routeState,
  authState,
  notifySpies,
  evidenceApi,
  ncrActionSpies,
  qualityState,
  mountQualityPage,
  nextRenderTick,
} from './quality-location.fixture'
import type { BusinessConsoleQualityNcrItem } from '@nerv-iip/api-client'
import { flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import NcrsPage from './ncrs.vue'
import NcrEvidenceUpload from '@/components/quality/NcrEvidenceUpload.vue'
import { FileUpload } from '@nerv-iip/ui'

describe('quality route location behavior', () => {
  beforeEach(resetQualityLocationFixture)

  it('keeps the user-selected NCR status filter when ncrId is removed from the route', async () => {
    routeState.route!.query = { ncrId: 'NCR-001' }
    mountQualityPage(NcrsPage)

    qualityState.ncrFilters!.status = 'open'
    routeState.route!.query = {}
    await nextRenderTick()

    expect(qualityState.ncrFilters!.keyword).toBeUndefined()
    expect(qualityState.ncrFilters!.status).toBe('open')
  })

  it.each<{ name: string; query: Record<string, string>; expected: string }>([
    {
      name: 'operation source',
      query: { workOrderId: 'WO-source', operationTaskId: 'OP-source' },
      expected: '/mes/work-orders/WO-source?operationTaskId=OP-source',
    },
    {
      name: 'work-order source',
      query: { workOrderId: 'WO-source' },
      expected: '/mes/work-orders/WO-source',
    },
  ])('returns NCR $name without guessing another operation', async ({ query, expected }) => {
    routeState.route!.query = query
    const wrapper = mountQualityPage(NcrsPage)
    await flushPromises()

    const link = wrapper
      .findAllComponents({ name: 'RouterLink' })
      .find((item) => item.text().startsWith('返回工单'))
    expect(link).toBeDefined()
    const router = createRouter({ history: createMemoryHistory(), routes: [] })
    expect(router.resolve(link!.props('to')).fullPath).toBe(expected)
    wrapper.unmount()
  })

  it.each<{ name: string; query: Record<string, string>; permissions: string[] }>([
    { name: 'no source', query: {}, permissions: ['business.mes.work-orders.read'] },
    {
      name: 'operation without work order',
      query: { operationTaskId: 'OP-source' },
      permissions: ['business.mes.work-orders.read'],
    },
    {
      name: 'no destination permission',
      query: { workOrderId: 'WO-source', operationTaskId: 'OP-source' },
      permissions: [],
    },
  ])('keeps NCR usable with $name and no return dead end', async ({ query, permissions }) => {
    routeState.route!.query = query
    authState.permissionCodes = ['business.quality.ncr.read', ...permissions]
    const wrapper = mountQualityPage(NcrsPage)
    await flushPromises()

    expect(wrapper.text()).toContain('不合格品处理')
    expect(
      wrapper
        .findAllComponents({ name: 'RouterLink' })
        .filter((item) => item.text().startsWith('返回工单')),
    ).toHaveLength(0)
    expect(wrapper.text()).toContain('查看详情')
    wrapper.unmount()
  })

  it('keeps NCR read access separate from manage actions and MES work-order navigation', async () => {
    authState.permissionCodes = ['business.quality.ncr.read']
    const wrapper = mountQualityPage(NcrsPage)
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((button) => button.text().trim() === '查看详情')!
      .trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('当前账号只有 NCR 查看权限')
    expect(wrapper.find('#ncr-disposition-files').exists()).toBe(false)
    expect(wrapper.find('#ncr-close-reason').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('查看返工工单')
  })

  it('shows the system-created rework work order link only with MES read permission', async () => {
    const wrapper = mountQualityPage(NcrsPage)
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((button) => button.text().trim() === '打开处置')!
      .trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('查看返工工单 RW-001')
  })

  it.each(['organizationId', 'environmentId'] as const)(
    'blocks both NCR actions when %s is empty and recovers when context returns',
    async (missingField) => {
      qualityState.ncrInitialContext[missingField] = ''
      const wrapper = mountQualityPage(NcrsPage)
      const vm = wrapper.vm as unknown as {
        canCloseNcr: boolean
        canSubmitDisposition: boolean
        closeForm: { reason: string }
        dispositionForm: { dispositionType: string }
        evidenceFiles: Array<{ fileId: string; fileName: string }>
        openNcr: (ncr: Record<string, unknown>) => void
        submitCloseNcr: () => Promise<void>
        submitNcrDisposition: () => Promise<void>
      }
      vm.openNcr(qualityState.ncrs[0]!)
      vm.closeForm.reason = '处置结果已核验'
      // 本用例只考察业务范围这一维门禁：选一个不需要 MRB 评审 / 中央审批链的处置类型，
      // 免得把 #1327 新增的处置前置条件混进来。
      vm.dispositionForm.dispositionType = 'sort-and-screen'
      await nextRenderTick()
      vm.evidenceFiles = [{ fileId: 'completed-file', fileName: '尺寸复检.txt' }]

      expect(vm.canSubmitDisposition).toBe(false)
      expect(vm.canCloseNcr).toBe(false)
      const dispositionButton = wrapper
        .findAll('button')
        .find((button) => button.text().includes('提交处置'))
      const closeButton = wrapper
        .findAll('button')
        .find((button) => button.text().includes('关闭不合格品'))
      expect(dispositionButton?.attributes('disabled')).toBeDefined()
      expect(closeButton?.attributes('disabled')).toBeDefined()
      await vm.submitNcrDisposition()
      await vm.submitCloseNcr()
      expect(notifySpies.error).toHaveBeenNthCalledWith(1, '业务范围尚未就绪，请稍后重试。')
      expect(notifySpies.error).toHaveBeenNthCalledWith(2, '业务范围尚未就绪，请稍后重试。')
      expect(ncrActionSpies.submitDisposition).not.toHaveBeenCalled()
      expect(ncrActionSpies.closeNcr).not.toHaveBeenCalled()

      qualityState.ncrFilters![missingField] =
        missingField === 'organizationId' ? 'org-001' : 'env-dev'
      await nextRenderTick()
      await wrapper
        .findComponent(FileUpload)
        .vm.addFiles([new File(['inspection'], '尺寸复检.txt', { type: 'text/plain' })])
      await nextRenderTick()

      expect(vm.canSubmitDisposition).toBe(true)
      expect(vm.canCloseNcr).toBe(false)
      expect(dispositionButton?.attributes('disabled')).toBeUndefined()
      expect(closeButton?.attributes('disabled')).toBeDefined()
    },
  )

  it('downloads persisted evidence from a reopened NCR with read permission', async () => {
    authState.permissionCodes = ['business.quality.ncr.read']
    const row = { ...qualityState.ncrs[0], status: 'disposition-in-progress' }
    qualityState.ncrs = [row]
    const data = new Blob(['尺寸复检记录'])
    evidenceApi.download.mockResolvedValue({
      data,
      response: new Response(null, {
        headers: { 'Content-Disposition': "attachment; filename*=UTF-8''%E5%A4%8D%E6%A3%80.txt" },
      }),
    })
    const create = vi.fn(() => 'blob:ncr-evidence')
    const revoke = vi.fn()
    vi.stubGlobal(
      'URL',
      class extends URL {
        static createObjectURL = create
        static revokeObjectURL = revoke
      },
    )
    const saved = vi.fn()
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      saved(this.href, this.download)
    })
    try {
      const wrapper = mountQualityPage(NcrsPage)
      const vm = wrapper.vm as unknown as { openNcr: (ncr: BusinessConsoleQualityNcrItem) => void }
      vm.openNcr(row)
      await flushPromises()
      const download = wrapper.findAll('button').find((button) => button.text() === '下载证据 1')
      expect(download).toBeDefined()
      await download!.trigger('click')
      await flushPromises()

      expect(evidenceApi.download).toHaveBeenCalledWith({
        path: { fileId: 'completed-file' },
        headers: { 'X-Organization-Id': 'org-001', 'X-Environment-Id': 'env-dev' },
        parseAs: 'blob',
        throwOnError: true,
      })
      expect(create).toHaveBeenCalledWith(data)
      expect(saved).toHaveBeenCalledWith('blob:ncr-evidence', '复检.txt')
      expect(revoke).toHaveBeenCalledWith('blob:ncr-evidence')

      evidenceApi.download.mockRejectedValueOnce(new Error('文件不可用'))
      await download!.trigger('click')
      await flushPromises()
      expect(notifySpies.operationFailure).toHaveBeenCalledWith(
        '证据下载失败',
        expect.any(Error),
        '证据下载失败，请稍后重试。',
      )
      expect(saved).toHaveBeenCalledTimes(1)
    } finally {
      click.mockRestore()
      vi.unstubAllGlobals()
    }
  })

  it('preserves disposition input after a lost final PATCH response and recovers HEAD 404 evidence with public readback', async () => {
    evidenceApi.patch.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    const wrapper = mountQualityPage(NcrsPage)
    const vm = wrapper.vm as unknown as {
      dispositionForm: { dispositionType: string; mrbComment: string }
      canSubmitDisposition: boolean
      selectedNcr: BusinessConsoleQualityNcrItem
      openNcr: (ncr: BusinessConsoleQualityNcrItem) => void
      submitNcrDisposition: () => Promise<void>
    }
    vm.openNcr(qualityState.ncrs[0]!)
    vm.dispositionForm.dispositionType = 'sort-and-screen'
    vm.dispositionForm.mrbComment = '保留尺寸复检说明'
    await nextRenderTick()
    const upload = wrapper.findComponent(FileUpload)
    await upload.vm.addFiles([new File(['inspection'], '尺寸复检.txt', { type: 'text/plain' })])
    await nextRenderTick()
    expect(vm.dispositionForm).toMatchObject({
      dispositionType: 'sort-and-screen',
      mrbComment: '保留尺寸复检说明',
    })
    expect(vm.canSubmitDisposition).toBe(false)
    expect(ncrActionSpies.submitDisposition).not.toHaveBeenCalled()
    const actual =
      await vi.importActual<typeof import('@nerv-iip/api-client')>('@nerv-iip/api-client')
    evidenceApi.head.mockImplementationOnce((options) =>
      actual.getBusinessConsoleQualityEvidenceTusOffset({
        ...options,
        baseUrl: 'http://quality.invalid',
        fetch: async () => new Response(null, { status: 404 }),
      }),
    )
    await upload.vm.retryFailed()
    await nextRenderTick()
    expect(evidenceApi.create).toHaveBeenCalledTimes(1)
    expect(evidenceApi.patch).toHaveBeenCalledTimes(1)
    expect(evidenceApi.complete).toHaveBeenCalledWith(
      expect.objectContaining({ path: { uploadSessionId: 'session-1' } }),
    )
    expect(vm.canSubmitDisposition).toBe(true)
    await vm.submitNcrDisposition()
    await nextRenderTick()
    expect(ncrActionSpies.submitDisposition).toHaveBeenCalledWith(
      'NCR-001',
      expect.objectContaining({
        dispositionType: 'sort-and-screen',
        attachmentFileIds: ['completed-file'],
        mrbReviews: undefined,
      }),
    )
    expect(evidenceApi.read).toHaveBeenCalledWith(
      expect.objectContaining({
        path: { ncrId: 'NCR-001' },
        query: { organizationId: 'org-001', environmentId: 'env-dev' },
      }),
    )
    expect(vm.selectedNcr).toMatchObject({
      id: 'NCR-001',
      status: 'disposition-in-progress',
      attachmentFileIds: ['completed-file'],
    })
    expect(wrapper.text()).toContain('已关联证据')
    expect(wrapper.text()).toContain('1 个')
  })

  it.each(['close', 'switch'] as const)(
    'discards late HEAD 404 recovery results after dialog %s',
    async (action) => {
      const actual =
        await vi.importActual<typeof import('@nerv-iip/api-client')>('@nerv-iip/api-client')
      evidenceApi.head.mockImplementationOnce((options) =>
        actual.getBusinessConsoleQualityEvidenceTusOffset({
          ...options,
          baseUrl: 'http://quality.invalid',
          fetch: async () => new Response(null, { status: 404 }),
        }),
      )
      let finish!: (value: unknown) => void
      evidenceApi.complete.mockReturnValueOnce(
        new Promise((resolve) => {
          finish = resolve
        }),
      )
      const wrapper = mountQualityPage(NcrsPage)
      const vm = wrapper.vm as unknown as {
        detailOpen: boolean
        evidenceFiles: Array<{ fileId: string }>
        dispositionForm: { dispositionType: string }
        canSubmitDisposition: boolean
        openNcr: (ncr: BusinessConsoleQualityNcrItem) => void
      }
      vm.openNcr(qualityState.ncrs[0]!)
      vm.dispositionForm.dispositionType = 'sort-and-screen'
      await nextRenderTick()
      const pending = wrapper
        .findComponent(FileUpload)
        .vm.addFiles([new File(['inspection'], '尺寸复检.txt', { type: 'text/plain' })])
      await flushPromises()
      expect(evidenceApi.complete).toHaveBeenCalledTimes(1)
      if (action === 'close') {
        vm.detailOpen = false
        await nextRenderTick()
      }
      vm.openNcr({
        id: 'NCR-002',
        code: 'NCR-002',
        status: 'open',
        reworkWorkOrderCreationStatus: 'not-requested',
      })
      await nextRenderTick()
      finish({ data: { success: true, data: { fileId: 'old-file' } } })
      await pending
      await nextRenderTick()
      expect(wrapper.findComponent(NcrEvidenceUpload).props('ncrId')).toBe('NCR-002')
      expect(vm.evidenceFiles).toEqual([])
      expect(vm.canSubmitDisposition).toBe(false)
    },
  )

  it('rechecks business context when close is confirmed after context is lost', async () => {
    const wrapper = mountQualityPage(NcrsPage)
    const vm = wrapper.vm as unknown as {
      closeForm: { reason: string }
      openNcr: (ncr: Record<string, unknown>) => void
      submitCloseNcr: () => Promise<void>
    }
    vm.openNcr(qualityState.ncrs[0]!)
    vm.closeForm.reason = '处置结果已核验'
    await nextRenderTick()
    const closeButton = wrapper
      .findAll('button')
      .find((button) => button.text().trim() === '关闭不合格品')
    expect(closeButton).toBeDefined()
    await closeButton!.trigger('click')

    qualityState.ncrFilters!.environmentId = ''
    await nextRenderTick()
    const confirmCloseButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('确认关闭'))

    expect(confirmCloseButton).toBeDefined()
    await confirmCloseButton!.trigger('click')
    expect(notifySpies.error).toHaveBeenCalledWith('业务范围尚未就绪，请稍后重试。')
    expect(ncrActionSpies.closeNcr).not.toHaveBeenCalled()
  })

  it('keeps existing NCR field validation silent when business context is ready', async () => {
    const wrapper = mountQualityPage(NcrsPage)
    const vm = wrapper.vm as unknown as {
      submitCloseNcr: () => Promise<void>
    }

    await vm.submitCloseNcr()

    expect(notifySpies.error).not.toHaveBeenCalled()
    expect(ncrActionSpies.closeNcr).not.toHaveBeenCalled()
  })
})
