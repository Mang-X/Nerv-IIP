import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import DeviceAssetPicker from './DeviceAssetPicker.vue'

const directoryMock = vi.hoisted(() => vi.fn())

vi.mock('@/composables/useBusinessDeviceDirectory', () => ({
  useBusinessDeviceDirectory: directoryMock,
}))

function createDirectory(overrides: Record<string, unknown> = {}) {
  return {
    deviceAssets: ref([
      {
        deviceAssetId: 'device-1',
        displayName: '一号车床',
        code: 'LATHE-01',
        workshopCode: 'WS-1',
        lineCode: 'LINE-A',
        stationCode: 'ST-9',
      },
    ]),
    deviceAssetsTotal: ref(1),
    deviceAssetsPending: ref(false),
    deviceAssetsError: ref<unknown>(),
    deviceAssetFilters: { keyword: '', skip: 0, take: 20 },
    scopeReady: ref(true),
    canPreviousPage: ref(false),
    canNextPage: ref(false),
    search: vi.fn(),
    previousPage: vi.fn(),
    nextPage: vi.fn(),
    refreshDeviceAssets: vi.fn(),
    ...overrides,
  }
}

describe('DeviceAssetPicker', () => {
  beforeEach(() => {
    directoryMock.mockReset()
    directoryMock.mockReturnValue(createDirectory())
  })

  it('shows readable device context and emits only the selected stable-ID row', async () => {
    const wrapper = mount(DeviceAssetPicker, {
      props: { open: true },
      attachTo: document.body,
    })
    await flushPromises()

    expect(document.body.textContent).toContain('一号车床')
    expect(document.body.textContent).toContain('LATHE-01')
    expect(document.body.textContent).toContain('WS-1')
    expect(document.body.textContent).toContain('LINE-A')
    expect(document.body.textContent).toContain('ST-9')

    document.body
      .querySelector<HTMLElement>('[data-testid="device-option-device-1"]')!
      .dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
    await flushPromises()
    expect(wrapper.emitted('select')).toEqual([
      [
        expect.objectContaining({
          deviceAssetId: 'device-1',
          displayName: '一号车床',
          code: 'LATHE-01',
        }),
      ],
    ])
    expect(wrapper.emitted('update:open')?.at(-1)).toEqual([false])
  })

  it('submits trimmed keywords to server search on Enter and supports bounded paging', async () => {
    const directory = createDirectory({
      deviceAssetsTotal: ref(41),
      canNextPage: ref(true),
    })
    directoryMock.mockReturnValue(directory)
    mount(DeviceAssetPicker, {
      props: { open: true },
      attachTo: document.body,
    })
    await flushPromises()
    directory.search.mockClear()

    const search = document.body.querySelector<HTMLInputElement>('input[type="search"]')!
    search.value = '  车床  '
    search.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
    await flushPromises()
    expect(directory.search).toHaveBeenCalledWith('  车床  ')

    document.body.querySelector<HTMLButtonElement>('[data-testid="device-next-page"]')!.click()
    expect(directory.nextPage).toHaveBeenCalledTimes(1)
    expect(
      document.body.querySelector<HTMLButtonElement>('[data-testid="device-previous-page"]')!
        .disabled,
    ).toBe(true)
  })

  it.each([
    [{ deviceAssetsPending: ref(true) }, '正在加载设备…'],
    [{ deviceAssets: ref([]) }, '没有找到可选设备'],
    [{ scopeReady: ref(false), deviceAssets: ref([]) }, '登录范围尚未就绪'],
  ])('renders the explicit directory state %#', async (overrides, expected) => {
    directoryMock.mockReturnValue(createDirectory(overrides))
    mount(DeviceAssetPicker, {
      props: { open: true },
      attachTo: document.body,
    })
    await flushPromises()
    expect(document.body.textContent).toContain(expected)
  })

  it('shows a retry action for directory errors', async () => {
    const directory = createDirectory({ deviceAssetsError: ref(new Error('boom')) })
    directoryMock.mockReturnValue(directory)
    mount(DeviceAssetPicker, {
      props: { open: true },
      attachTo: document.body,
    })
    await flushPromises()

    document.body.querySelector<HTMLButtonElement>('[data-testid="device-retry"]')!.click()
    expect(directory.refreshDeviceAssets).toHaveBeenCalledTimes(1)
  })
})
