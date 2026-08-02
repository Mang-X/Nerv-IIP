import type {
  BusinessConsoleCurrentSopDocumentItem,
  BusinessConsoleMesOperationTaskRow,
} from '@nerv-iip/api-client'
import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import MesOperationExecutionPanel from './MesOperationExecutionPanel.vue'

const task: BusinessConsoleMesOperationTaskRow = {
  operationTaskId: 'operation-task-internal-20',
  workOrderId: 'work-order-internal-42',
  workOrderNo: 'MO-2026-0042',
  operationTaskNo: undefined,
  operationCode: 'OP-STANDARD-20',
  status: 'queued',
  operationSequence: 20,
  workCenterId: 'WC-A',
  allowedActions: ['start'],
  blockReasons: ['MATERIAL_SHORTAGE: 物料 MAT-STEEL 缺口 2'],
  evaluatedAtUtc: '2026-08-02T08:31:00.000Z',
}

const sops: BusinessConsoleCurrentSopDocumentItem[] = [
  {
    documentNumber: 'SOP-20',
    revision: 'A',
    operationCode: 'OP-STANDARD-20',
    fileId: 'file-20',
    fileName: '标准工序 20',
  },
]

function mountPanel(overrides: Record<string, unknown> = {}) {
  return mount(MesOperationExecutionPanel, {
    attachTo: document.body,
    props: {
      result: null,
      selected: task,
      open: true,
      actionPending: false,
      operationScopeReady: true,
      confirmingComplete: false,
      currentSops: sops,
      sopsPending: false,
      sopsError: undefined,
      openingSopFileId: null,
      sopFileError: '',
      ...overrides,
    },
  })
}

describe('MesOperationExecutionPanel', () => {
  it('keeps task-instance and standard-operation labels distinct', async () => {
    mountPanel()
    await flushPromises()

    const taskDefinition = [...document.body.querySelectorAll('dt')].find(
      (term) => term.textContent === '工序任务',
    )?.nextElementSibling
    expect(taskDefinition?.textContent).toBe('工序任务信息未提供')
    expect(document.body.textContent).toContain('OP-STANDARD-20')
    expect(document.body.textContent).not.toContain('operation-task-internal-20')
    expect(document.body.textContent).not.toContain('work-order-internal-42')
  })

  it('renders shared gate details and emits only a recognized server action', async () => {
    const wrapper = mountPanel()
    await flushPromises()

    expect(document.body.textContent).toContain('物料齐套')
    expect(document.body.textContent).toContain('物料 MAT-STEEL 缺口 2')
    document.body.querySelector<HTMLButtonElement>('[data-testid="action-start"]')!.click()
    expect(wrapper.emitted('action')).toEqual([['start']])
  })

  it('renders every action through the NvUI mobile button boundary', async () => {
    mountPanel()
    await flushPromises()

    const buttons = [...document.body.querySelectorAll('button')]
    expect(buttons.length).toBeGreaterThan(0)
    expect(buttons.every((button) => button.dataset.slot === 'mobile-button')).toBe(true)
    expect(document.body.querySelector('[data-testid="action-start"]')?.className).toContain(
      'min-h-touch',
    )
  })

  it('renders an indeterminate readable result and emits retry without exposing raw IDs', async () => {
    const wrapper = mountPanel({
      selected: null,
      open: false,
      result: {
        status: 'error',
        title: '操作失败',
        description: 'MO-2026-0042 · 工序任务信息未提供\n结果尚未核实，请重试。',
        action: 'complete',
        displayReference: 'MO-2026-0042 · 工序任务信息未提供',
        workOrderId: 'work-order-internal-42',
        taskId: 'operation-task-internal-20',
        context: {
          principalId: 'principal-001',
          organizationId: 'org-001',
          environmentId: 'env-dev',
          scopeKind: 'work-center',
          scopeId: 'WC-A',
          action: 'complete',
          workOrderId: 'work-order-internal-42',
          operationTaskId: 'operation-task-internal-20',
        },
      },
    })

    expect(wrapper.text()).toContain('结果尚未核实')
    expect(wrapper.text()).not.toContain('work-order-internal-42')
    expect(wrapper.text()).not.toContain('operation-task-internal-20')
    await wrapper.get('[data-testid="retry-action"]').trigger('click')
    expect(wrapper.emitted('retry')).toHaveLength(1)
  })
})
