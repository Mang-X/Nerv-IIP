import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref, shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import { useBusinessContextStore } from '@/stores/businessContext'
import {
  isTerminalDeviceControlStatus,
  useBusinessDeviceControlCommands,
} from './useBusinessDeviceControl'

const coladaState = vi.hoisted(() => ({
  mutationVars: [] as Array<{ body: Record<string, unknown> }>,
}))

vi.mock('@nerv-iip/api-client', () => ({
  createBusinessConsoleTelemetryDeviceControlCommandMutationOptions: vi.fn(() => ({
    key: [{ _id: 'createBusinessConsoleTelemetryDeviceControlCommand' }],
    mutation: vi.fn(),
  })),
  getBusinessConsoleTelemetryDeviceControlCommandQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleTelemetryDeviceControlCommand' }],
    query: vi.fn(),
  })),
  listBusinessConsoleTelemetryDeviceControlCommandsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleTelemetryDeviceControlCommands' }],
    query: vi.fn(),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useMutation: vi.fn((options) => ({
    error: shallowRef(),
    isLoading: shallowRef(false),
    mutateAsync: vi.fn(async (vars: { body: Record<string, unknown> }) => {
      coladaState.mutationVars.push(vars)
      await options.onSuccess?.()
      return { success: true, data: { operationTaskId: 'op-task-123' } }
    }),
  })),
  useQuery: vi.fn(() => ({
    data: shallowRef(undefined),
    error: shallowRef(),
    isLoading: shallowRef(false),
    refetch: vi.fn(),
  })),
  useQueryCache: vi.fn(() => ({ invalidateQueries: vi.fn() })),
}))

describe('useBusinessDeviceControlCommands', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    coladaState.mutationVars = []
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
  })

  it('builds a dispatch body without connector routing fields and with a fresh idempotency key', async () => {
    const { dispatchCommand } = useBusinessDeviceControlCommands(ref('DEV-CNC-01'))

    const commandId = await dispatchCommand({
      commandType: 'write-tag',
      tagKey: 'spindle.speed',
      value: '80',
      reason: 'ramp to setpoint',
    })

    expect(commandId).toBe('op-task-123')
    const body = coladaState.mutationVars[0]!.body
    expect(body).toMatchObject({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      deviceAssetId: 'DEV-CNC-01',
      commandType: 'write-tag',
      tagKey: 'spindle.speed',
      value: '80',
      reason: 'ramp to setpoint',
    })
    // Connector routing is resolved server-side from the device binding; the frontend must not send it.
    expect(body).not.toHaveProperty('connectorHostId')
    expect(body).not.toHaveProperty('instanceKey')
    expect(body).toHaveProperty('idempotencyKey')
    expect(body).toHaveProperty('correlationId')
    expect(body.parameters).toEqual({})
  })

  it('generates a distinct idempotency key per dispatch', async () => {
    const { dispatchCommand } = useBusinessDeviceControlCommands(ref('DEV-CNC-01'))

    await dispatchCommand({
      commandType: 'start-stop',
      tagKey: 'power',
      value: 'stop',
      reason: 'shutdown',
    })
    await dispatchCommand({
      commandType: 'start-stop',
      tagKey: 'power',
      value: 'stop',
      reason: 'shutdown',
    })

    expect(coladaState.mutationVars[0]!.body.idempotencyKey).not.toBe(
      coladaState.mutationVars[1]!.body.idempotencyKey,
    )
  })

  it('classifies terminal command statuses', () => {
    expect(isTerminalDeviceControlStatus('completed')).toBe(true)
    expect(isTerminalDeviceControlStatus('failed')).toBe(true)
    expect(isTerminalDeviceControlStatus('approval-pending')).toBe(false)
    expect(isTerminalDeviceControlStatus(null)).toBe(false)
  })
})
