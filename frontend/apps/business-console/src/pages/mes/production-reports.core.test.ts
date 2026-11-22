import { UUID_PATTERN, rows, mesState, mountReports } from './production-reports.fixture'
import { flushPromises } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

describe('production reports page — reversal permission & cross-page interlink', () => {
  it('shows the reporting operator in the list and original report detail', async () => {
    const row = { ...rows[0], reportedBy: 'user-operator', reportedByDisplayName: '王建国' }
    mesState.guardRows = [row]
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()
    expect(wrapper.get('[data-testid="data-table"]').text()).toContain('操作人')
    expect(wrapper.get('[data-testid="row"]').text()).toContain('王建国')
    mesState.detail.value = {
      report: { ...row, reportedBy: 'user-detail-operator', reportedByDisplayName: '李俊华' },
      consumedMaterialLots: [],
    }
    ;(wrapper.vm as unknown as { openReverse: (target: typeof row) => void }).openReverse(row)
    await flushPromises()
    const detail = wrapper.get('[aria-label="原报工明细"]')
    expect(detail.text()).toContain('李俊华')
    expect(detail.text()).not.toContain('王建国')
    expect(wrapper.text()).not.toContain('user-operator')
    expect(wrapper.text()).not.toContain('user-detail-operator')
  })

  it('shows name unavailable when IAM does not provide a display name', async () => {
    mesState.guardRows = [{ ...rows[0], reportedBy: 'user-admin', reportedByDisplayName: null }]
    const wrapper = mountReports(['business.mes.reporting.read'])
    await flushPromises()
    expect(wrapper.get('[data-testid="row"] [data-column="reportedByDisplayName"]').text()).toBe(
      '姓名不可用',
    )
    expect(wrapper.text()).not.toContain('user-admin')
  })

  it('shows the operation cumulative actual hours without treating them as per-report allocation', async () => {
    const wrapper = mountReports(['business.mes.reporting.read'])
    await flushPromises()

    expect(wrapper.text()).toContain('实际工时')
    const actualHourCells = wrapper.findAll('[data-testid="actual-hours"]')
    expect(actualHourCells).toHaveLength(3)
    expect(actualHourCells[0].text()).toMatch(/人工\s*1\.25 小时/)
    expect(actualHourCells[0].text()).toMatch(/机器\s*0\.5 小时/)
    expect(actualHourCells[1].text()).toMatch(/人工\s*0 小时/)
    expect(actualHourCells[1].text()).toMatch(/机器\s*0 小时/)
    expect(actualHourCells[2].text()).toMatch(/人工\s*暂无实绩/)
    expect(actualHourCells[2].text()).toMatch(/机器\s*暂无实绩/)
  })

  it('read-face guard shows DTO numbers and resolved catalog names without technical identifiers', async () => {
    const row = {
      productionReportId: '019fbb41-1111-7111-8111-111111111111',
      reportNo: 'PRPT-20260801-001',
      workOrderId: '019fbb41-2222-7222-8222-222222222222',
      workOrderNo: 'WO-2026-08001',
      operationTaskId: '019fbb41-3333-7333-8333-333333333333',
      operationTaskNo: 'WO-2026-08001-OP-10',
      goodQuantity: 8,
      scrapQuantity: 0,
      reworkQuantity: 0,
      reportedAtUtc: '2026-08-01T08:00:00Z',
      workOrderStatus: 'started',
    }
    mesState.guardRows = [row]
    mesState.candidateRows = [
      {
        candidateId: 'candidate-1',
        deviceAssetId: '019fbb41-4444-7444-8444-444444444444',
        tagKey: '产量计数',
        goodQuantity: 8,
        bucketEndUtc: '2026-08-01T08:00:00Z',
        status: 'pending-confirmation',
      },
    ]
    mesState.detail.value = {
      report: row,
      consumedMaterialLots: [
        {
          materialId: '019fbb41-5555-7555-8555-555555555555',
          materialLotId: 'LOT-20260801-01',
          consumedQuantity: 2,
          uomCode: 'kg',
          materialIssueRequestNo: 'MIR-20260801-01',
        },
      ],
    }
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    ;(wrapper.vm as unknown as { openReverse: (target: typeof row) => void }).openReverse(row)
    await flushPromises()
    const visibleText = wrapper.text()

    expect(visibleText).toContain('PRPT-20260801-001')
    expect(visibleText).toContain('WO-2026-08001')
    expect(visibleText).toContain('WO-2026-08001-OP-10')
    expect(visibleText).toContain('轴承钢')
    expect(visibleText).toContain('LOT-20260801-01')
    expect(visibleText).toContain('五轴加工中心')
    expect(visibleText).not.toMatch(UUID_PATTERN)
    expect(visibleText).not.toContain('user-emp-')
  })

  it('read-face guard shows placeholders when DTO fields and catalogs cannot resolve identifiers', async () => {
    const row = {
      productionReportId: '019fbb41-1111-7111-8111-111111111111',
      reportNo: 'PRPT-20260801-002',
      workOrderId: '019fbb41-2222-7222-8222-222222222222',
      operationTaskId: '019fbb41-3333-7333-8333-333333333333',
      goodQuantity: 8,
      scrapQuantity: 0,
      reworkQuantity: 0,
      reportedAtUtc: '2026-08-01T08:00:00Z',
      workOrderStatus: 'started',
    }
    mesState.catalogResolved = false
    mesState.guardRows = [row]
    mesState.candidateRows = [
      {
        candidateId: 'candidate-1',
        deviceAssetId: '019fbb41-4444-7444-8444-444444444444',
        tagKey: '产量计数',
        goodQuantity: 8,
        bucketEndUtc: '2026-08-01T08:00:00Z',
        status: 'pending-confirmation',
      },
    ]
    mesState.detail.value = {
      report: row,
      consumedMaterialLots: [
        {
          materialId: '019fbb41-5555-7555-8555-555555555555',
          materialLotId: '019fbb41-6666-7666-8666-666666666666',
          consumedQuantity: 2,
          uomCode: 'kg',
          materialIssueRequestNo: 'MIR-20260801-01',
        },
      ],
    }
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    ;(wrapper.vm as unknown as { openReverse: (target: typeof row) => void }).openReverse(row)
    await flushPromises()
    const visibleText = wrapper.text()

    expect(visibleText).toContain('—')
    expect(visibleText).toContain('未指定物料')
    expect(visibleText).toContain('未指定设备')
    expect(visibleText).not.toMatch(UUID_PATTERN)
    expect(visibleText).not.toContain('user-emp-')
  })

  it('重新报工入口只带出报工记录中的真实工单与工序上下文，不伪造工序状态', () => {
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    const vm = wrapper.vm as unknown as {
      reReport: (row: (typeof rows)[number]) => void
      reportOpen: boolean
      reportContext: {
        workOrderId: string
        operationTaskId: string
        operationStatus?: string
      } | null
    }

    vm.reReport(rows[2])

    expect(vm.reportOpen).toBe(true)
    expect(vm.reportContext).toMatchObject({
      workOrderId: 'WO-3',
      operationTaskId: 'WO-3-OP-10',
    })
    expect(vm.reportContext?.operationStatus).toBeUndefined()
  })

  it('shows every consumed material lot in the reversal dialog and preserves quantity summary', async () => {
    mesState.detail.value = {
      report: rows[0],
      consumedMaterialLots: [
        {
          materialId: 'MAT-1',
          materialLotId: 'LOT-1',
          consumedQuantity: 2.5,
          uomCode: 'KG',
          materialIssueRequestNo: 'MIR-1',
        },
        {
          materialId: 'MAT-2',
          materialLotId: 'LOT-2',
          consumedQuantity: 3,
          uomCode: 'EA',
          materialIssueRequestNo: 'MIR-2',
        },
      ],
    }
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    ;(wrapper.vm as unknown as { openReverse: (row: (typeof rows)[number]) => void }).openReverse(
      rows[0],
    )
    await flushPromises()

    expect(wrapper.text()).toContain('良品')
    expect(wrapper.text()).toContain('12')
    expect(wrapper.text()).toContain('MAT-1')
    expect(wrapper.text()).toContain('LOT-1')
    expect(wrapper.text()).toContain('2.5 KG')
    expect(wrapper.text()).toContain('MIR-1')
    expect(wrapper.text()).toContain('MAT-2')
    expect(wrapper.text()).toContain('LOT-2')
    expect(wrapper.text()).toContain('3 EA')
    expect(wrapper.text()).toContain('MIR-2')
    expect(mesState.activateReverseDetail).toHaveBeenCalledWith('PRPT-000001')
  })

  it('renders explicit loading, empty and failure states and locks confirmation until detail is ready', async () => {
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    const vm = wrapper.vm as unknown as {
      openReverse: (row: (typeof rows)[number]) => void
      reverseForm: { reasonCode: string }
    }
    vm.openReverse(rows[0])
    vm.reverseForm.reasonCode = 'mis-report'

    mesState.detailPending.value = true
    await flushPromises()
    expect(wrapper.text()).toContain('正在加载物料消耗明细')
    expect(
      wrapper
        .findAll('button')
        .find((button) => button.text().includes('确认冲销'))
        ?.attributes('disabled'),
    ).toBeDefined()

    mesState.detailPending.value = false
    mesState.detail.value = { report: rows[0], consumedMaterialLots: [] }
    await flushPromises()
    expect(wrapper.text()).toContain('本次报工没有物料批次消耗')

    mesState.detail.value = undefined
    mesState.detailError.value = new Error('detail unavailable')
    await flushPromises()
    expect(wrapper.text()).toContain('物料消耗明细加载失败')
    expect(
      wrapper
        .findAll('button')
        .find((button) => button.text().includes('确认冲销'))
        ?.attributes('disabled'),
    ).toBeDefined()
  })

  it('clears prior detail when closing or switching reversal reports', async () => {
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    const vm = wrapper.vm as unknown as {
      openReverse: (row: (typeof rows)[number]) => void
      onReverseOpenChange: (next: boolean) => void
    }
    vm.openReverse(rows[0])
    vm.onReverseOpenChange(false)
    expect(mesState.deactivateReverseDetail).toHaveBeenCalled()

    vm.openReverse({ ...rows[0], productionReportId: 'pr-d', reportNo: 'PRPT-000004' })
    expect(mesState.activateReverseDetail).toHaveBeenLastCalledWith('PRPT-000004')
  })

  it('shows the inline reverse action for a reversible report when the user has reporting write', async () => {
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    const rowEls = wrapper.findAll('[data-testid="row"]')
    // A 行(可冲销):有启用的「冲销」动作按钮(文本恰为"冲销",区别于互链按钮"查看冲销单/冲销自")
    const reverseBtn = rowEls[0].findAll('button').find((b) => b.text().trim() === '冲销')
    expect(reverseBtn).toBeTruthy()
    expect(reverseBtn!.attributes('disabled')).toBeUndefined()
  })

  it('hides reverse and re-report actions for a read-only user but keeps records visible', async () => {
    const wrapper = mountReports(['business.mes.reporting.read'])
    await flushPromises()

    // 记录仍可见(读权限)
    expect(wrapper.text()).toContain('PRPT-000001')
    // 无任何冲销 / 重新报工 写入口(互链按钮"查看冲销单/冲销自"是读信息,不算写动作,故按精确文本判定)
    const buttons = wrapper.findAll('button')
    expect(buttons.some((b) => b.text().trim() === '冲销')).toBe(false)
    expect(buttons.some((b) => b.text().includes('重新报工'))).toBe(false)
  })

  it('marks an already-reversed original (server reversalReportNo) as non-reversible even when its reversal row is on another page', async () => {
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    const rowEls = wrapper.findAll('[data-testid="row"]')
    // B 行(已冲销):徽章「已冲销」+ 互链「查看冲销单 PRPT-000900」+ 冲销按钮禁用
    expect(rowEls[1].text()).toContain('已冲销')
    expect(rowEls[1].text()).toContain('查看冲销单 PRPT-000900')
    const bReverseBtn = rowEls[1].findAll('button').find((b) => b.text().trim() === '冲销')
    expect(bReverseBtn!.attributes('disabled')).toBeDefined()
  })

  it('marks a reversal record row with a negative badge, back-link to its original, and a re-report action', async () => {
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    const rowEls = wrapper.findAll('[data-testid="row"]')
    // C 行(冲销记录):徽章「负向冲销」+ 互链「冲销自 PRPT-000899」+ 「重新报工」而非「冲销」
    expect(rowEls[2].text()).toContain('负向冲销')
    expect(rowEls[2].text()).toContain('冲销自 PRPT-000899')
    expect(rowEls[2].findAll('button').some((b) => b.text().includes('重新报工'))).toBe(true)
  })

  it('locates a cross-page reversal from the original: filters, resets page, scrolls and highlights the arriving row', async () => {
    mesState.filters.skip = 30 // 处于非首页,验证定位会回到第一页
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    // B 行的互链对方 PRPT-000900 不在本页;点击「查看冲销单」按其单号过滤 + 回首页
    const link = wrapper
      .findAll('[data-testid="row"]')[1]
      .findAll('button')
      .find((b) => b.text().includes('查看冲销单'))!
    await link.trigger('click')

    expect(mesState.filters.keyword).toBe('PRPT-000900')
    expect(mesState.filters.skip).toBe(0)

    // keyword 变更后 mock 返回对方那一行 → watch → nextTick → 滚动 + 高亮
    await flushPromises()
    const arrivedRow = wrapper
      .findAll('[data-testid="row"]')
      .find((r) => r.text().includes('PRPT-000900'))!
    expect(arrivedRow).toBeTruthy()
    expect(Element.prototype.scrollIntoView).toHaveBeenCalled()
    // 高亮:定位到的行报工单单元格带 ring 类
    expect(arrivedRow.html()).toContain('ring-brand')
  })

  it('locates a cross-page original from the reversal row (reverse direction) then clears the locate filter', async () => {
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    // C 行(冲销记录)的互链对方原单 PRPT-000899 不在本页;点击「冲销自」反向定位
    const backLink = wrapper
      .findAll('[data-testid="row"]')[2]
      .findAll('button')
      .find((b) => b.text().includes('冲销自'))!
    await backLink.trigger('click')
    await flushPromises()

    expect(mesState.filters.keyword).toBe('PRPT-000899')
    expect(Element.prototype.scrollIntoView).toHaveBeenCalled()
    expect(wrapper.text()).toContain('PRPT-000899')

    // 清除筛选:keyword 清空 + 原三行列表恢复 + 定位高亮清除
    const clearBtn = wrapper.findAll('button').find((b) => b.text().includes('清除筛选'))!
    await clearBtn.trigger('click')
    await flushPromises()
    expect(mesState.filters.keyword).toBe('')
    const restored = wrapper.findAll('[data-testid="row"]')
    expect(restored).toHaveLength(3)
    expect(wrapper.text()).toContain('PRPT-000001')
    expect(wrapper.html()).not.toContain('ring-brand')
  })

  it('freezes both idempotencyKey and reversedAtUtc across close (e.g. Escape) and reopen for the same report until success', async () => {
    mesState.reverseProductionReport.mockRejectedValue(new Error('timeout'))
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    const vm = wrapper.vm as unknown as {
      openReverse: (row: (typeof rows)[number]) => void
      onReverseOpenChange: (next: boolean) => void
      submitReverse: () => Promise<void>
      reverseForm: { reasonCode: string; remark: string }
    }

    // 第一次:打开 A、选原因、提交失败(服务端未知结果)
    vm.openReverse(rows[0])
    vm.reverseForm.reasonCode = 'mis-report'
    await vm.submitReverse()
    const first = mesState.reverseProductionReport.mock.calls[0]?.[1]

    // 经 Escape / 组件自身 close 关闭(绕过「返回」),再对同一报工重开、重试
    vm.onReverseOpenChange(false)
    vm.openReverse(rows[0])
    vm.reverseForm.reasonCode = 'mis-report'
    await vm.submitReverse()
    const second = mesState.reverseProductionReport.mock.calls[1]?.[1]

    // key 与 reversedAtUtc 都必须跨重试冻结:否则后端 fingerprint(含 ReversedAtUtc)变 → 幂等冲突而非重放
    expect(first?.idempotencyKey).toBeTruthy()
    expect(first?.reversedAtUtc).toBeTruthy()
    expect(second?.idempotencyKey).toBe(first?.idempotencyKey)
    expect(second?.reversedAtUtc).toBe(first?.reversedAtUtc)
  })

  it('keeps the first reason on retry even if the user changes it after reopening (fingerprint also covers reason)', async () => {
    mesState.reverseProductionReport.mockRejectedValue(new Error('timeout'))
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    const vm = wrapper.vm as unknown as {
      openReverse: (row: (typeof rows)[number]) => void
      onReverseOpenChange: (next: boolean) => void
      submitReverse: () => Promise<void>
      reverseReasonLocked: boolean
      reverseForm: { reasonCode: string; remark: string }
    }

    // 首次:原因「误报工」提交失败(结果未知)
    vm.openReverse(rows[0])
    vm.reverseForm.reasonCode = 'mis-report'
    await vm.submitReverse()
    const first = mesState.reverseProductionReport.mock.calls[0]?.[1]

    // Escape 关闭后重开:原因已锁定,即便用户试图改成「重复报工」,提交仍沿用首次冻结的原因
    vm.onReverseOpenChange(false)
    vm.openReverse(rows[0])
    expect(vm.reverseReasonLocked).toBe(true)
    expect(vm.reverseForm.reasonCode).toBe('mis-report') // 重开恢复首次原因
    vm.reverseForm.reasonCode = 'duplicate' // 试图更改(界面已禁用,此处直接改绕过)
    await vm.submitReverse()
    const second = mesState.reverseProductionReport.mock.calls[1]?.[1]

    // reason / key / reversedAtUtc 三者都跨重试稳定 → 后端 fingerprint 稳定,命中幂等重放
    expect(first?.reason).toBe('误报工')
    expect(second?.reason).toBe(first?.reason)
    expect(second?.idempotencyKey).toBe(first?.idempotencyKey)
    expect(second?.reversedAtUtc).toBe(first?.reversedAtUtc)
  })

  it('keeps a submitted-but-unknown intent locked after 返回 (cancel) then reopen — 返回 does not discard it', async () => {
    mesState.reverseProductionReport.mockRejectedValue(new Error('timeout'))
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    const vm = wrapper.vm as unknown as {
      openReverse: (row: (typeof rows)[number]) => void
      cancelReverse: () => void
      submitReverse: () => Promise<void>
      reverseReasonLocked: boolean
      reverseForm: { reasonCode: string; remark: string }
    }

    // 首次:原因「误报工」提交失败(结果未知)→ reason 已冻结
    vm.openReverse(rows[0])
    vm.reverseForm.reasonCode = 'mis-report'
    await vm.submitReverse()
    const first = mesState.reverseProductionReport.mock.calls[0]?.[1]

    // 点「返回」:已提交(结果未知)的意图有意保留,不丢弃
    vm.cancelReverse()
    // 重开同一报工:仍锁定、仍恢复原原因,不能借「返回」换原因
    vm.openReverse(rows[0])
    expect(vm.reverseReasonLocked).toBe(true)
    expect(vm.reverseForm.reasonCode).toBe('mis-report')
    vm.reverseForm.reasonCode = 'duplicate' // 试图更改
    await vm.submitReverse()
    const second = mesState.reverseProductionReport.mock.calls[1]?.[1]
    expect(second?.reason).toBe(first?.reason)
    expect(second?.idempotencyKey).toBe(first?.idempotencyKey)
  })

  it('blocks closing the reverse dialog while the request is pending', async () => {
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    const vm = wrapper.vm as unknown as {
      openReverse: (row: (typeof rows)[number]) => void
      onReverseOpenChange: (next: boolean) => void
      reverseOpen: boolean
    }

    vm.openReverse(rows[0])
    expect(vm.reverseOpen).toBe(true)
    // 请求进行中:Escape / 组件自身 close 触发的 update:open=false 被拦下,弹窗保持打开
    mesState.pending.value = true
    vm.onReverseOpenChange(false)
    expect(vm.reverseOpen).toBe(true)
    // 请求结束后允许关闭
    mesState.pending.value = false
    vm.onReverseOpenChange(false)
    expect(vm.reverseOpen).toBe(false)
  })
})

describe('报工操作人展示', () => {
  it('报工只读用户在列表和独立详情看到 IAM 人名，未关联员工也不展示裸引用', async () => {
    const wrapper = mountReports(['business.mes.reporting.read'])
    expect(wrapper.findAll('[data-testid="row"]')[0]!.text()).toContain('王建国')
    const report = wrapper.findAll('button').find((button) => button.text() === 'PRPT-000001')
    expect(report).toBeDefined()
    await report!.trigger('click')
    await flushPromises()
    expect(wrapper.get('[role="dialog"]').text()).toContain('王建国')
    expect(wrapper.get('[role="dialog"]').text()).not.toContain('user-admin')
    expect(wrapper.get('[role="dialog"]').text()).not.toContain('冲销')
    wrapper.unmount()
  })

  it('IAM 姓名缺失时显示姓名不可用，不以身份引用兜底', async () => {
    const saved = rows[0]!.reportedByDisplayName
    rows[0]!.reportedByDisplayName = null
    const wrapper = mountReports(['business.mes.reporting.read'])
    expect(wrapper.findAll('[data-testid="row"]')[0]!.text()).toContain('姓名不可用')
    const report = wrapper.findAll('button').find((button) => button.text() === 'PRPT-000001')
    await report!.trigger('click')
    await flushPromises()
    expect(wrapper.get('[role="dialog"]').text()).toContain('姓名不可用')
    expect(wrapper.text()).not.toContain('user-admin')
    wrapper.unmount()
    rows[0]!.reportedByDisplayName = saved
  })
})

describe('报工详情日期展示', () => {
  it.each([
    ['2026-10-13T09:00:00', '2026-10-13 09:00'],
    ['本次录入', '本次录入'],
  ])('将 %s 按统一日期语义显示为 %s', async (value, expected) => {
    const saved = rows[0]!.reportedAtUtc
    rows[0]!.reportedAtUtc = value
    const wrapper = mountReports(['business.mes.reporting.read'])
    try {
      const button = wrapper.findAll('button').find((item) => item.text() === 'PRPT-000001')!
      await button.trigger('click')
      await flushPromises()
      expect(wrapper.get('[role="dialog"]').text()).toContain(expected)
      expect(wrapper.get('[role="dialog"]').text()).not.toContain('Invalid Date')
    } finally {
      wrapper.unmount()
      rows[0]!.reportedAtUtc = saved
    }
  })
})
