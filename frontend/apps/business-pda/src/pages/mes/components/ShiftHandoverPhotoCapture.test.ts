import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import ShiftHandoverPhotoCapture from './ShiftHandoverPhotoCapture.vue'
import type { ShiftHandoverAttachment } from '@/composables/useBusinessShiftHandover'

const ATTACHMENT: ShiftHandoverAttachment = {
  fileId: 'file-1',
  fileName: 'handover-photo.jpg',
  contentType: 'image/jpeg',
  sizeBytes: 2048,
}

function mountCapture(
  upload: (file: File) => Promise<ShiftHandoverAttachment>,
  extra: { disabled?: boolean; disabledReason?: string } = {},
) {
  const attachments = ref<ShiftHandoverAttachment[]>([])
  const wrapper = mount(ShiftHandoverPhotoCapture, {
    props: {
      upload,
      ...extra,
      attachments: attachments.value,
      'onUpdate:attachments': async (value: ShiftHandoverAttachment[]) => {
        attachments.value = value
        await wrapper.setProps({ attachments: value })
      },
    },
  })
  return { wrapper, attachments }
}

/** 模拟相机回图：把 File 塞进隐藏 input 再触发 change，走的是页面上真实那条路径。 */
async function selectPhoto(
  wrapper: ReturnType<typeof mountCapture>['wrapper'],
  file = new File([new Uint8Array(4)], 'IMG_0001.jpg', { type: 'image/jpeg' }),
) {
  const input = wrapper.get('[data-testid="photo-input"]').element as HTMLInputElement
  Object.defineProperty(input, 'files', { configurable: true, value: [file] })
  await wrapper.get('[data-testid="photo-input"]').trigger('change')
  await flushPromises()
  return file
}

describe('ShiftHandoverPhotoCapture', () => {
  it('opens the camera-capable file input when the operator taps 拍照并上传', async () => {
    const { wrapper } = mountCapture(vi.fn(async () => ATTACHMENT))
    const input = wrapper.get('[data-testid="photo-input"]')
    const click = vi.spyOn(input.element as HTMLInputElement, 'click')

    await wrapper.get('[data-testid="capture-photo"]').trigger('click')

    expect(click).toHaveBeenCalledTimes(1)
    // 这两个属性是 Android WebView 直接开后置相机 + 只放行用途允许类型的全部依据。
    expect(input.attributes('capture')).toBe('environment')
    expect(input.attributes('accept')).toBe('image/jpeg,image/png')
  })

  it('appends the attachment ONLY after the upload resolved with a complete receipt', async () => {
    let resolveUpload: (value: ShiftHandoverAttachment) => void = () => {}
    const upload = vi.fn(
      () =>
        new Promise<ShiftHandoverAttachment>((resolve) => {
          resolveUpload = resolve
        }),
    )
    const { wrapper, attachments } = mountCapture(upload)

    await selectPhoto(wrapper)
    // 上传还没回执 —— 列表里必须还是空的，否则操作工会以为照片已经在单子上了。
    expect(attachments.value).toHaveLength(0)
    expect(wrapper.get('[data-testid="capture-photo"]').text()).toContain('照片上传中')

    resolveUpload(ATTACHMENT)
    await flushPromises()

    expect(attachments.value).toEqual([ATTACHMENT])
    expect(wrapper.get('[data-testid="attachment-rows"]').text()).toContain('handover-photo.jpg')
    expect(wrapper.get('[data-testid="attachment-rows"]').text()).toContain('2.0 KB')
  })

  it('hands the selected File through to the uploader unchanged', async () => {
    const upload = vi.fn(async (_file: File) => ATTACHMENT)
    const { wrapper } = mountCapture(upload)
    const file = await selectPhoto(wrapper)

    expect(upload).toHaveBeenCalledTimes(1)
    expect(upload.mock.calls[0][0]).toBe(file)
  })

  it('keeps the list untouched and shows the reason when the upload fails', async () => {
    const upload = vi.fn(async () => {
      throw new Error('照片格式不被接受，交接班附件只支持 JPG / PNG。')
    })
    const { wrapper, attachments } = mountCapture(upload)

    await selectPhoto(wrapper)

    expect(attachments.value).toHaveLength(0)
    expect(wrapper.get('[data-testid="attachment-error"]').text()).toContain('照片格式不被接受')
    // 失败后按钮必须回到可再拍状态，否则一次失败等于这次交班再也加不了照片。
    expect(wrapper.get('[data-testid="capture-photo"]').text()).toContain('拍照并上传')
  })

  it('clears the input value so the SAME photo can be picked again after a failure', async () => {
    const upload = vi.fn(async () => {
      throw new Error('boom')
    })
    const { wrapper } = mountCapture(upload)
    await selectPhoto(wrapper)

    expect((wrapper.get('[data-testid="photo-input"]').element as HTMLInputElement).value).toBe('')
  })

  it('does nothing when the picker was dismissed without choosing a file', async () => {
    const upload = vi.fn(async () => ATTACHMENT)
    const { wrapper, attachments } = mountCapture(upload)

    await wrapper.get('[data-testid="photo-input"]').trigger('change')
    await flushPromises()

    expect(upload).not.toHaveBeenCalled()
    expect(attachments.value).toHaveLength(0)
  })

  it('states the blocker instead of silently disabling the button', async () => {
    const { wrapper } = mountCapture(
      vi.fn(async () => ATTACHMENT),
      {
        disabled: true,
        disabledReason: '当前账号没有交班权限，不能提交交接单。请联系班组长或管理员开通。',
      },
    )

    expect(wrapper.text()).toContain('当前账号没有交班权限')
    expect(wrapper.text()).not.toMatch(/business\.[a-z0-9.-]+|HTTP\s*\d{3}/i)
  })

  it('removes exactly the attachment the operator tapped', async () => {
    const second: ShiftHandoverAttachment = { ...ATTACHMENT, fileId: 'file-2', fileName: 'b.png' }
    const upload = vi
      .fn<(file: File) => Promise<ShiftHandoverAttachment>>()
      .mockResolvedValueOnce(ATTACHMENT)
      .mockResolvedValueOnce(second)
    const { wrapper, attachments } = mountCapture(upload)

    await selectPhoto(wrapper)
    await selectPhoto(wrapper)
    expect(attachments.value.map((a) => a.fileId)).toEqual(['file-1', 'file-2'])

    await wrapper.get('[data-testid="remove-attachment-0"]').trigger('click')
    expect(attachments.value.map((a) => a.fileId)).toEqual(['file-2'])
  })
})
