import { mount } from '@vue/test-utils'
import { ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import CountsPage from './counts.vue'
import MovementsPage from './movements.vue'

const inventoryState = vi.hoisted(() => ({
  confirmAdjustment: vi.fn(),
  createCountTask: vi.fn(),
  postMovement: vi.fn(),
}))

vi.mock('@/composables/useBusinessInventory', () => ({
  useInventoryCounts: () => ({
    confirmAdjustment: inventoryState.confirmAdjustment,
    confirmAdjustmentError: ref(undefined),
    confirmAdjustmentPending: ref(false),
    createCountTask: inventoryState.createCountTask,
    createCountTaskError: ref(undefined),
    createCountTaskPending: ref(false),
    filters: {
      environmentId: 'env-dev',
      organizationId: 'org-001',
    },
  }),
  useInventoryMovement: () => ({
    postMovement: inventoryState.postMovement,
    postMovementError: ref(undefined),
    postMovementPending: ref(false),
  }),
}))

const businessStubs = {
  BusinessActionSheet: {
    props: ['open', 'title', 'description'],
    template: '<section><h2>{{ title }}</h2><p>{{ description }}</p><slot /></section>',
  },
  BusinessEmptyState: {
    props: ['title', 'description', 'action'],
    template: '<div>{{ title }} {{ description }} {{ action }}</div>',
  },
  BusinessFormStatus: true,
  BusinessLayout: {
    template: '<main><slot /></main>',
  },
  BusinessPageHeader: {
    props: ['domain', 'title', 'summary'],
    template: '<header><h1>{{ title }}</h1><p>{{ summary }}</p><slot name="actions" /></header>',
  },
}

const uiStubs = {
  Button: {
    template: '<button v-bind="$attrs"><slot /></button>',
  },
  Field: {
    template: '<div><slot /></div>',
  },
  FieldGroup: {
    template: '<div><slot /></div>',
  },
  FieldLabel: {
    template: '<label><slot /></label>',
  },
  Input: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<input :value="modelValue" v-bind="$attrs" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  Select: {
    template: '<div><slot /></div>',
  },
  SelectContent: {
    template: '<div><slot /></div>',
  },
  SelectItem: {
    props: ['value'],
    template: '<div><slot /></div>',
  },
  SelectTrigger: {
    template: '<button><slot /></button>',
  },
  SelectValue: true,
  Spinner: true,
  Table: {
    template: '<table data-ui-table><slot /></table>',
  },
  TableBody: {
    template: '<tbody><slot /></tbody>',
  },
  TableCell: {
    template: '<td><slot /></td>',
  },
  TableHead: {
    template: '<th><slot /></th>',
  },
  TableHeader: {
    template: '<thead><slot /></thead>',
  },
  TableRow: {
    template: '<tr><slot /></tr>',
  },
}

function mountInventoryPage(component: unknown) {
  return mount(component, {
    global: {
      stubs: {
        ...businessStubs,
        ...uiStubs,
      },
    },
  })
}

describe('inventory workflow pages', () => {
  beforeEach(() => {
    inventoryState.confirmAdjustment.mockReset()
    inventoryState.createCountTask.mockReset()
    inventoryState.postMovement.mockReset()
  })

  it('uses design-system table components for local stock count queue', () => {
    const wrapper = mountInventoryPage(CountsPage)

    expect(wrapper.find('[data-ui-table]').exists()).toBe(true)
  })

  it('uses design-system table components for local movement queue', () => {
    const wrapper = mountInventoryPage(MovementsPage)

    expect(wrapper.find('[data-ui-table]').exists()).toBe(true)
  })

  it('generates a fresh idempotency key each time the same count task is adjusted', async () => {
    inventoryState.createCountTask.mockResolvedValue({ data: { countTaskId: 'COUNT-TASK-1' } })
    inventoryState.confirmAdjustment.mockResolvedValue({ data: { movementId: 'MOVE-1' } })

    const wrapper = mountInventoryPage(CountsPage)

    await wrapper.find('#count-task-location').setValue('A-01')
    await wrapper.findAll('form')[0]!.trigger('submit')
    await wrapper.findAll('button').find((button) => button.text() === '确认差异')!.trigger('click')
    await wrapper.find('#count-adjust-quantity').setValue('5')
    await wrapper.findAll('form')[1]!.trigger('submit')

    await wrapper.findAll('button').find((button) => button.text() === '确认差异')!.trigger('click')
    await wrapper.find('#count-adjust-quantity').setValue('6')
    await wrapper.findAll('form')[1]!.trigger('submit')

    expect(inventoryState.confirmAdjustment).toHaveBeenCalledTimes(2)
    const firstKey = inventoryState.confirmAdjustment.mock.calls[0][1].idempotencyKey
    const secondKey = inventoryState.confirmAdjustment.mock.calls[1][1].idempotencyKey
    expect(firstKey).toMatch(/^count-COUNT-TASK-1-\d+-\d+$/)
    expect(secondKey).toMatch(/^count-COUNT-TASK-1-\d+-\d+$/)
    expect(secondKey).not.toBe(firstKey)
  })

  it('requires the adjustment action to be opened from a count task row before submitting', async () => {
    const wrapper = mountInventoryPage(CountsPage)

    await wrapper.find('#count-adjust-task-id').setValue('COUNT-TASK-ORPHAN')
    await wrapper.find('#count-adjust-quantity').setValue('7')
    await wrapper.findAll('form')[1]!.trigger('submit')

    expect(inventoryState.confirmAdjustment).not.toHaveBeenCalled()
  })
})
