import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import type { FileUploadExpose } from '.'
import { FileUpload, fileUploadMotion } from '.'

import { stubFileUploadObjectUrls, createBaseProps, selectFiles } from './FileUpload.test.helpers'

vi.mock('motion-v', async () =>
  (await import('./FileUpload.test.helpers')).createFileUploadMotionMock(),
)

stubFileUploadObjectUrls()

describe('FileUpload', () => {
  it('exposes and renders with the shared motion-v upload curves', async () => {
    expect(fileUploadMotion.fastInvoke).toEqual({
      duration: 0.187,
      ease: [0, 0, 0, 1],
    })
    expect(fileUploadMotion.pointToPointShort).toEqual({
      duration: 0.187,
      ease: [0.55, 0.55, 0, 1],
    })

    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
        variant: 'queue',
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose

    await upload.addFiles([new File(['hello'], 'motion-evidence.txt', { type: 'text/plain' })])
    await flushPromises()

    expect(wrapper.find('[data-motion-component="motion-config"]').exists()).toBe(true)
    expect(
      wrapper.find('[data-motion-component="motion-button"][data-dragging="false"]').exists(),
    ).toBe(true)
    expect(
      wrapper.find('[data-motion-component="motion-div"][data-motion-row="true"]').exists(),
    ).toBe(true)
    expect(wrapper.get('[data-slot="file-upload-dropzone"]').classes()).toContain(
      'data-[dragging=true]:border-primary',
    )
    expect(wrapper.get('[data-slot="file-upload-dropzone"]').classes()).not.toContain(
      'data-[dragging=true]:border-brand',
    )
  })

  it('applies visible motion affordances to the dropzone and row state changes', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
        variant: 'queue',
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose

    await upload.addFiles([new File(['hello'], 'animated-status.txt', { type: 'text/plain' })])
    await flushPromises()

    const dropzone = wrapper.get('[data-slot="file-upload-dropzone"]')
    expect(dropzone.attributes('data-has-while-hover')).toBe('true')
    expect(dropzone.attributes('data-has-while-tap')).toBe('true')
    expect(
      wrapper.find('[data-motion-component="motion-span"][data-motion-status="true"]').exists(),
    ).toBe(true)
    expect(
      wrapper.find('[data-motion-component="motion-div"][data-motion-icon="true"]').exists(),
    ).toBe(true)
    expect(
      wrapper.find('[data-motion-component="motion-div"][data-motion-actions="true"]').exists(),
    ).toBe(true)
  })

  it('shows accepted file extensions in the upload affordance', () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        acceptedContentTypes: ['application/pdf', 'image/png', 'image/jpeg', 'image/svg+xml'],
        maxFileSizeBytes: 10 * 1024 * 1024,
        maxFiles: 8,
      }),
    })

    expect(wrapper.get('[data-slot="file-upload-accept-hint"]').text()).toBe(
      '支持 .pdf、.png、.jpg、.jpeg、.svg · 单个文件不超过 10.0 MB · 最多 8 个文件',
    )
  })

  it('renders the default upload affordance as a component-library button', () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        acceptedContentTypes: ['application/pdf'],
      }),
    })

    const trigger = wrapper.get('[data-file-upload-button="true"]')
    expect(trigger.element.tagName).toBe('BUTTON')
    expect(trigger.attributes('data-slot')).toBe('button')
    expect(trigger.text()).toContain('选择文件')
    expect(wrapper.find('[data-slot="file-upload-dropzone"]').exists()).toBe(false)
    expect(wrapper.get('[data-slot="file-upload-accept-hint"]').text()).toContain('支持 .pdf')
  })

  it('renders a ReUI-style queue summary with total size and built-in actions', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
        variant: 'queue',
        maxFiles: 3,
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose

    await upload.addFiles([
      new File(['hello'], 'first.txt', { type: 'text/plain' }),
      new File(['world'], 'second.txt', { type: 'text/plain' }),
    ])
    await flushPromises()

    expect(wrapper.get('[data-slot="file-upload-summary"]').text()).toContain('文件 (2/3)')
    expect(wrapper.get('[data-slot="file-upload-summary"]').text()).toContain('总计 10 B')
    expect(wrapper.get('[data-slot="file-upload-add-button"]').text()).toContain('添加文件')

    await wrapper.get('[data-slot="file-upload-clear-button"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-slot="file-upload-row"]').exists()).toBe(false)
    expect(wrapper.find('[data-slot="file-upload-summary"]').exists()).toBe(false)
  })

  it('matches common file families with readable type labels', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        maxFiles: 7,
        transport: vi.fn().mockResolvedValue(undefined),
      }),
    })

    await selectFiles(wrapper, [
      new File(['doc'], 'work-instruction.docx', {
        type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      }),
      new File(['sheet'], 'mrp.xlsx', {
        type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      }),
      new File(['deck'], 'review.pptx', {
        type: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      }),
      new File(['pdf'], 'inspection.pdf', { type: 'application/pdf' }),
      new File(['image'], 'evidence.png', { type: 'image/png' }),
      new File(['audio'], 'alarm.mp3', { type: 'audio/mpeg' }),
      new File(['video'], 'runoff.mp4', { type: 'video/mp4' }),
    ])

    expect(wrapper.text()).toContain('Word')
    expect(wrapper.text()).toContain('Excel')
    expect(wrapper.text()).toContain('PowerPoint')
    expect(wrapper.text()).toContain('PDF')
    expect(wrapper.text()).toContain('Image')
    expect(wrapper.text()).toContain('Audio')
    expect(wrapper.text()).toContain('Video')
  })

  it('formats very large files with a GB label', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose
    const file = new File(['cad'], 'plant-model.step', { type: 'application/step' })
    Object.defineProperty(file, 'size', {
      configurable: true,
      value: 3 * 1024 * 1024 * 1024,
    })

    await upload.addFiles([file])
    await flushPromises()

    expect(wrapper.text()).toContain('3.0 GB')
  })

  it('uses progress color instead of a completed row border highlight', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
        variant: 'queue',
        transport: vi.fn().mockImplementation(({ onProgress }) => {
          onProgress(100)
          return Promise.resolve()
        }),
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose

    await upload.addFiles([new File(['hello'], 'completed-evidence.txt', { type: 'text/plain' })])
    await upload.uploadQueued()
    await flushPromises()

    expect(wrapper.get('[data-slot="file-upload-row"]').attributes('data-status')).toBe('completed')
    expect(wrapper.get('[data-slot="progress"]').attributes('class')).toContain(
      '[&_[data-slot=progress-indicator]]:bg-success',
    )
  })

  it('renders image thumbnails for image uploads', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
        acceptedContentTypes: ['image/png'],
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose

    await upload.addFiles([new File(['png'], 'defect-photo.png', { type: 'image/png' })])
    await flushPromises()

    const thumbnail = wrapper.get('[data-slot="file-upload-thumbnail"]')
    expect(thumbnail.attributes('src')).toBe('blob:nerv-iip-preview')
    expect(thumbnail.attributes('alt')).toBe('defect-photo.png')
  })

  it('supports queue, compact, gallery, table, avatar, and image variants', async () => {
    for (const variant of ['queue', 'compact', 'gallery', 'table', 'avatar', 'image'] as const) {
      const wrapper = mount(FileUpload, {
        props: createBaseProps({
          autoUpload: false,
          variant,
          acceptedContentTypes: ['image/png'],
        }),
      })
      const upload = wrapper.vm as unknown as FileUploadExpose

      await upload.addFiles([new File(['png'], `${variant}-upload.png`, { type: 'image/png' })])
      await flushPromises()

      expect(wrapper.get('[data-slot="file-upload"]').attributes('data-variant')).toBe(variant)

      if (variant === 'compact' || variant === 'gallery' || variant === 'image') {
        const cta = wrapper.get('[data-file-upload-cta="true"]')
        expect(cta.attributes('data-slot')).toBe('button')
        expect(cta.element.tagName).toBe('SPAN')
      }

      if (variant === 'avatar') {
        expect(wrapper.find('[data-slot="file-upload-row"]').exists()).toBe(false)
        expect(wrapper.get('[data-slot="file-upload-thumbnail"]').attributes('alt')).toBe(
          'avatar-upload.png',
        )
        continue
      }

      expect(wrapper.get('[data-slot="file-upload-row"]').attributes('data-variant')).toBe(variant)

      if (variant === 'gallery' || variant === 'image') {
        expect(wrapper.find('[data-slot="file-upload-grid"]').exists()).toBe(true)
      }

      if (variant === 'table') {
        expect(wrapper.find('[data-slot="file-upload-table"]').exists()).toBe(true)
      }
    }
  })

  it('uses semantic badge variants for completed and rejected rows', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        acceptedContentTypes: ['application/pdf'],
        maxFiles: 3,
        transport: vi.fn().mockResolvedValue(undefined),
      }),
    })

    await selectFiles(wrapper, [
      new File(['pdf'], 'inspection.pdf', { type: 'application/pdf' }),
      new File(['hello'], 'wrong-type.txt', { type: 'text/plain' }),
    ])
    await flushPromises()

    const variants = wrapper
      .findAll('[data-slot="badge"]')
      .map((badge) => badge.attributes('data-variant'))

    expect(variants).toContain('success')
    expect(variants).toContain('destructive')
  })
})
