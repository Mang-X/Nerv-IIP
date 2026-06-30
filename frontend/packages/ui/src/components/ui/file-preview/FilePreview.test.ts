import { flushPromises, mount } from '@vue/test-utils'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { computed, defineComponent, h, shallowRef } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { FilePreview, getFilePreviewKindMeta } from '.'
import { Select } from '../select'
import OfficePreview from './OfficePreview.vue'
import PdfPreview from './PdfPreview.vue'

const officeViewerMocks = vi.hoisted(() => ({
  docxOptions: [] as Array<{ width?: number }>,
  pptxOptions: [] as Array<{ width?: number }>,
  xlsxOptions: [] as Array<{ showZoomSlider?: boolean }>,
  docxRenderWidths: [] as number[],
  pptxRenderWidths: [] as number[],
}))

const pdfEngineMocks = vi.hoisted(() => ({
  engine: {} as Record<string, unknown> | null,
  isLoading: false,
  error: null as Error | null,
}))

const pdfScrollMocks = vi.hoisted(() => ({
  currentPage: 1,
  totalPages: 3,
  scrollToPage: vi.fn(),
  scrollToPreviousPage: vi.fn(),
  scrollToNextPage: vi.fn(),
}))

vi.mock('@embedpdf/core', () => ({
  createPluginRegistration: (pluginPackage: unknown, config?: unknown) => ({ pluginPackage, config }),
}))

vi.mock('@embedpdf/engines/vue', () => ({
  usePdfiumEngine: () => ({
    engine: shallowRef(pdfEngineMocks.engine),
    isLoading: shallowRef(pdfEngineMocks.isLoading),
    error: shallowRef(pdfEngineMocks.error),
  }),
}))

vi.mock('@embedpdf/core/vue', () => ({
  EmbedPDF: defineComponent({
    name: 'EmbedPDF',
    props: ['engine', 'plugins'],
    setup(_props, { slots }) {
      return () =>
        h('div', { 'data-testid': 'embedpdf-provider' }, slots.default?.({ activeDocumentId: 'test-document' }))
    },
  }),
  useDocumentState: () =>
    computed(() => ({
      status: 'loaded',
      error: null,
    })),
}))

vi.mock('@embedpdf/plugin-document-manager/vue', () => ({
  DocumentManagerPluginPackage: {},
  DocumentContent: defineComponent({
    name: 'DocumentContent',
    props: ['documentId'],
    setup(_props, { slots }) {
      const documentState = { status: 'loaded', error: null }

      return () =>
        h(
          'div',
          { 'data-testid': 'embedpdf-document-content' },
          slots.default?.({
            documentState,
            isLoading: false,
            isError: false,
            isLoaded: true,
          }),
        )
    },
  }),
}))

vi.mock('@embedpdf/plugin-viewport/vue', () => ({
  ViewportPluginPackage: {},
  Viewport: defineComponent({
    name: 'Viewport',
    props: ['documentId'],
    setup(_props, { slots }) {
      return () => h('div', { 'data-testid': 'embedpdf-viewport' }, slots.default?.())
    },
  }),
}))

vi.mock('@embedpdf/plugin-scroll/vue', () => ({
  ScrollPluginPackage: {},
  ScrollStrategy: { Vertical: 'vertical' },
  Scroller: defineComponent({
    name: 'Scroller',
    props: ['documentId'],
    setup(_props, { slots }) {
      return () =>
        h(
          'div',
          { 'data-testid': 'embedpdf-scroller' },
          slots.default?.({
            page: {
              pageIndex: 0,
              width: 320,
              height: 420,
            },
          }),
        )
    },
  }),
  useScroll: () => ({
    provides: computed(() => ({
      scrollToPage: pdfScrollMocks.scrollToPage,
      scrollToPreviousPage: pdfScrollMocks.scrollToPreviousPage,
      scrollToNextPage: pdfScrollMocks.scrollToNextPage,
    })),
    state: computed(() => ({
      currentPage: pdfScrollMocks.currentPage,
      totalPages: pdfScrollMocks.totalPages,
    })),
  }),
}))

vi.mock('@embedpdf/plugin-render/vue', () => ({
  RenderPluginPackage: {},
  RenderLayer: defineComponent({
    name: 'RenderLayer',
    props: ['documentId', 'pageIndex'],
    setup(props) {
      return () => h('div', { 'data-testid': 'embedpdf-render-layer' }, String(props.pageIndex))
    },
  }),
}))

vi.mock('@embedpdf/plugin-zoom/vue', () => ({
  ZoomMode: { FitWidth: 'fit-width' },
  ZoomPluginPackage: {},
  useZoom: () => ({
    provides: computed(() => ({
      zoomIn: vi.fn(),
      zoomOut: vi.fn(),
      requestZoom: vi.fn(),
    })),
    state: shallowRef({
      currentZoomLevel: 1,
    }),
  }),
}))

vi.mock('@silurus/ooxml/docx', () => ({
  DocxViewer: class {
    pageCount = 2
    _opts: { width?: number, onPageChange?: (index: number, count: number) => void }

    constructor(
      public canvas: HTMLCanvasElement,
      private options: { width?: number, onPageChange?: (index: number, count: number) => void },
    ) {
      this._opts = options
      officeViewerMocks.docxOptions.push(options)
    }

    async load() {
      this.options.onPageChange?.(0, this.pageCount)
    }

    async nextPage() {}

    async prevPage() {}

    async goToPage(index: number) {
      this.options.onPageChange?.(index, this.pageCount)
    }

    async _render() {
      officeViewerMocks.docxRenderWidths.push(this._opts.width ?? 0)
    }

    destroy() {}
  },
}))

vi.mock('@silurus/ooxml/pptx', () => ({
  PptxViewer: class {
    slideCount = 3
    opts: { width?: number, onSlideChange?: (index: number, count: number) => void }

    constructor(
      public canvas: HTMLCanvasElement,
      private options: { width?: number, onSlideChange?: (index: number, count: number) => void },
    ) {
      this.opts = options
      officeViewerMocks.pptxOptions.push(options)
    }

    async load() {
      this.options.onSlideChange?.(0, this.slideCount)
    }

    async nextSlide() {}

    async prevSlide() {}

    async goToSlide(index: number) {
      this.options.onSlideChange?.(index, this.slideCount)
    }

    async renderCurrentSlide() {
      officeViewerMocks.pptxRenderWidths.push(this.opts.width ?? 0)
    }

    destroy() {}
  },
}))

vi.mock('@silurus/ooxml/xlsx', () => ({
  XlsxViewer: class {
    sheetNames = ['Summary', 'Details']
    sheetCount = 2

    constructor(
      public container: HTMLElement,
      private options: {
        showZoomSlider?: boolean
        onReady?: (names: string[]) => void
        onSheetChange?: (index: number, count: number) => void
      },
    ) {
      officeViewerMocks.xlsxOptions.push(options)
    }

    async load() {
      this.options.onReady?.(this.sheetNames)
      this.options.onSheetChange?.(0, this.sheetCount)
    }

    async goToSheet(index: number) {
      this.options.onSheetChange?.(index, this.sheetCount)
    }

    destroy() {}
  },
}))

vi.mock('motion-v', () => {
  const passthrough = (testId: string) =>
    defineComponent({
      name: testId,
      props: ['initial', 'animate', 'exit', 'transition', 'reducedMotion', 'mode'],
      setup(_props, { slots }) {
        return () => h('div', { 'data-testid': testId }, slots.default?.())
      },
    })

  return {
    AnimatePresence: passthrough('animate-presence'),
    MotionConfig: passthrough('motion-config'),
    motion: {
      div: passthrough('motion-div'),
      img: defineComponent({
        name: 'motion-img',
        props: ['src', 'alt', 'initial', 'animate', 'transition', 'style'],
        setup(props, { attrs }) {
          return () => h('img', {
            ...attrs,
            alt: props.alt,
            src: props.src,
            style: props.style,
          })
        },
      }),
    },
  }
})

describe('FilePreview', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    officeViewerMocks.docxOptions = []
    officeViewerMocks.pptxOptions = []
    officeViewerMocks.xlsxOptions = []
    officeViewerMocks.docxRenderWidths = []
    officeViewerMocks.pptxRenderWidths = []
    pdfEngineMocks.engine = {}
    pdfEngineMocks.isLoading = false
    pdfEngineMocks.error = null
    pdfScrollMocks.currentPage = 1
    pdfScrollMocks.totalPages = 3
    pdfScrollMocks.scrollToPage.mockClear()
    pdfScrollMocks.scrollToPreviousPage.mockClear()
    pdfScrollMocks.scrollToNextPage.mockClear()
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('recognizes PDF files without storage coupling', async () => {
    const wrapper = mount(FilePreview, {
      props: {
        src: '/files/inspection.pdf',
        fileName: 'inspection.pdf',
        contentType: 'application/pdf',
      },
    })

    expect(wrapper.text()).toContain('PDF')
    expect(Object.keys(wrapper.props())).not.toContain('fileId')
    expect(Object.keys(wrapper.props())).not.toContain('downloadFile')
    expect(Object.keys(wrapper.props())).not.toContain('apiClient')
  })

  it('assigns distinguishable color accents by preview kind', () => {
    expect(getFilePreviewKindMeta('pdf').iconClass).toContain('text-red')
    expect(getFilePreviewKindMeta('office-docx').iconClass).toContain('text-blue')
    expect(getFilePreviewKindMeta('office-xlsx').iconClass).toContain('text-emerald')
    expect(getFilePreviewKindMeta('office-pptx').iconClass).toContain('text-orange')
    expect(getFilePreviewKindMeta('image').iconClass).toContain('text-violet')
    expect(getFilePreviewKindMeta('pdf').icon).not.toBe(getFilePreviewKindMeta('office-docx').icon)
  })

  it('renders PDF with headless embedpdf layers and the unified toolbar', () => {
    const wrapper = mount(PdfPreview, {
      props: {
        src: '/files/inspection.pdf',
      },
    })

    expect(wrapper.get('[data-slot="file-preview-pdf-toolbar"]')).toBeTruthy()
    expect(wrapper.get('[data-testid="embedpdf-render-layer"]').text()).toBe('0')
    expect(wrapper.get('[data-slot="file-preview-pdf-zoom"]').text()).toBe('100%')
    expect(wrapper.find('[data-testid="embedpdf-viewer"]').exists()).toBe(false)
  })

  it('allows jumping PDF pages from the unified toolbar', async () => {
    const wrapper = mount(PdfPreview, {
      props: {
        src: '/files/inspection.pdf',
      },
    })

    const pageSelect = wrapper.getComponent(Select)
    pageSelect.vm.$emit('update:modelValue', '3')
    await flushPromises()

    expect(pdfScrollMocks.scrollToPage).toHaveBeenCalledWith({
      pageNumber: 3,
      behavior: 'smooth',
    })
    expect(pdfScrollMocks.scrollToNextPage).not.toHaveBeenCalled()
    expect(pdfScrollMocks.scrollToPreviousPage).not.toHaveBeenCalled()
  })

  it('uses localized PDF loading copy', () => {
    pdfEngineMocks.engine = null
    pdfEngineMocks.isLoading = true

    const wrapper = mount(PdfPreview, {
      props: {
        src: '/files/inspection.pdf',
      },
    })

    expect(wrapper.text()).toContain('正在加载 PDF 预览')
    expect(wrapper.text()).not.toContain('Loading PDF preview')
  })

  it('renders a self-contained image preview with direct zoom controls', async () => {
    const wrapper = mount(FilePreview, {
      props: {
        src: '/files/evidence.png',
        fileName: 'evidence.png',
        contentType: 'image/png',
      },
    })

    expect(wrapper.get('[data-testid="file-preview-image"]').attributes('src')).toBe('/files/evidence.png')
    expect(wrapper.get('button[aria-label="Zoom in evidence.png"]')).toBeTruthy()

    await wrapper.get('button[aria-label="Zoom in evidence.png"]').trigger('click')

    expect(wrapper.get('[data-slot="file-preview-zoom"]').text()).toBe('110%')
  })

  it('emits source and ready events from the unified wrapper', async () => {
    const wrapper = mount(FilePreview, {
      props: {
        src: '/files/evidence.png',
        fileName: 'evidence.png',
        contentType: 'image/png',
      },
    })

    expect(wrapper.emitted('ready')).toBeUndefined()

    await wrapper.get('[data-testid="file-preview-image"]').trigger('load')
    await wrapper.get('button[aria-label="Open evidence.png"]').trigger('click')

    expect(wrapper.emitted('ready')?.[0]).toEqual(['image'])
    expect(wrapper.emitted('openSource')?.[0]).toEqual(['/files/evidence.png'])
  })

  it('surfaces image load errors through the unified error state', async () => {
    const wrapper = mount(FilePreview, {
      props: {
        src: '/files/missing.png',
        fileName: 'missing.png',
        contentType: 'image/png',
      },
    })

    await wrapper.get('[data-testid="file-preview-image"]').trigger('error')

    expect(wrapper.emitted('error')?.[0]).toEqual(['无法加载 missing.png。'])
    expect(wrapper.text()).toContain('预览失败')
    expect(wrapper.text()).toContain('无法加载 missing.png。')
  })

  it('clears child preview errors when the same source is retried', async () => {
    const wrapper = mount(FilePreview, {
      props: {
        src: '/files/missing.png',
        fileName: 'missing.png',
        contentType: 'image/png',
      },
    })

    await wrapper.get('[data-testid="file-preview-image"]').trigger('error')
    expect(wrapper.text()).toContain('预览失败')

    await wrapper.setProps({ loading: true })
    await wrapper.setProps({ loading: false })
    await flushPromises()

    expect(wrapper.text()).not.toContain('预览失败')
    expect(wrapper.find('[data-testid="file-preview-image"]').exists()).toBe(true)
  })

  it('uses a distinct empty-source state', () => {
    const wrapper = mount(FilePreview, {
      props: {
        fileName: 'work-instruction.pdf',
        contentType: 'application/pdf',
      },
    })

    expect(wrapper.text()).toContain('还没有可用的预览源')
    expect(wrapper.find('button[aria-label="Open work-instruction.pdf"]').exists()).toBe(false)
  })

  it('uses an unsupported state for unpreviewable files', () => {
    const wrapper = mount(FilePreview, {
      props: {
        src: '/files/legacy.doc',
        fileName: 'legacy.doc',
        contentType: 'application/msword',
      },
    })

    expect(wrapper.text()).toContain('暂不支持预览')
    expect(wrapper.text()).toContain('legacy.doc')
  })

  it('keeps office canvas stable when reloading a canvas-backed document', async () => {
    const wrapper = mount(OfficePreview, {
      props: {
        src: '/files/work-instruction.docx',
        kind: 'office-docx',
      },
    })

    await flushPromises()
    expect(wrapper.find('canvas').exists()).toBe(true)

    await wrapper.setProps({ src: '/files/work-instruction-v2.docx' })
    await flushPromises()

    expect(wrapper.find('canvas').exists()).toBe(true)
    expect(wrapper.emitted('error')).toBeUndefined()
  })

  it('does not compress canvas-backed office documents with CSS', async () => {
    const wrapper = mount(OfficePreview, {
      props: {
        src: '/files/work-instruction.docx',
        kind: 'office-docx',
      },
    })

    await flushPromises()

    const canvas = wrapper.get('canvas')
    expect(canvas.classes()).toContain('max-w-none')
    expect(canvas.classes()).not.toContain('max-w-full')
  })

  it('fits canvas-backed office documents to the available viewer width', async () => {
    vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(640)

    mount(OfficePreview, {
      props: {
        src: '/files/work-instruction.docx',
        kind: 'office-docx',
      },
    })
    mount(OfficePreview, {
      props: {
        src: '/files/review.pptx',
        kind: 'office-pptx',
      },
    })

    await flushPromises()
    await flushPromises()

    expect(officeViewerMocks.docxOptions.at(-1)?.width).toBe(640)
    expect(officeViewerMocks.pptxOptions.at(-1)?.width).toBe(640)
  })

  it('rerenders canvas-backed office documents on resize without reloading the viewer', async () => {
    vi.useFakeTimers()

    let resizeCallback: ResizeObserverCallback | undefined
    vi.stubGlobal('ResizeObserver', class {
      constructor(callback: ResizeObserverCallback) {
        resizeCallback = callback
      }

      observe() {}

      disconnect() {}
    })
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      callback(0)
      return 1
    })
    vi.stubGlobal('cancelAnimationFrame', vi.fn())

    let measuredWidth = 640
    vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockImplementation(() => measuredWidth)

    mount(OfficePreview, {
      props: {
        src: '/files/review.pptx',
        kind: 'office-pptx',
      },
    })

    await flushPromises()

    measuredWidth = 720
    resizeCallback?.([], {} as ResizeObserver)
    await vi.advanceTimersByTimeAsync(200)
    await flushPromises()

    expect(officeViewerMocks.pptxOptions).toHaveLength(1)
    expect(officeViewerMocks.pptxRenderWidths).toEqual([720])
  })

  it('zooms canvas-backed office documents without reloading the viewer', async () => {
    vi.useFakeTimers()
    vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(640)

    const wrapper = mount(OfficePreview, {
      props: {
        src: '/files/work-instruction.docx',
        kind: 'office-docx',
      },
    })

    await flushPromises()

    expect(wrapper.text()).toContain('100%')

    await wrapper.get('button[aria-label="Zoom in document"]').trigger('click')
    await vi.advanceTimersByTimeAsync(200)
    await flushPromises()

    expect(wrapper.text()).toContain('110%')
    expect(officeViewerMocks.docxOptions).toHaveLength(1)
    expect(officeViewerMocks.docxRenderWidths).toEqual([704])

    await wrapper.get('button[aria-label="Reset document zoom"]').trigger('click')
    await vi.advanceTimersByTimeAsync(200)
    await flushPromises()

    expect(wrapper.text()).toContain('100%')
    expect(officeViewerMocks.docxOptions).toHaveLength(1)
    expect(officeViewerMocks.docxRenderWidths).toEqual([704, 640])
  })

  it('documents every file preview demo with code examples and localized state labels', () => {
    const docs = readFileSync(
      resolve(process.cwd(), '../../apps/design-system/docs/components/desktop/file-preview.md'),
      'utf8',
    )

    for (const title of ['图片预览', 'PDF 预览', 'Word / DOCX 预览', 'Excel / XLSX 预览', 'PowerPoint / PPTX 预览', '状态示例']) {
      expect(docs, `${title} needs a VitePress outline heading`).toContain(`### ${title}`)

      const sectionStart = docs.indexOf(`<Demo title="${title}"`)
      expect(sectionStart, `${title} demo is missing`).toBeGreaterThanOrEqual(0)

      const nextSectionStart = docs.indexOf('<Demo title=', sectionStart + 1)
      const section = docs.slice(sectionStart, nextSectionStart === -1 ? undefined : nextSectionStart)
      expect(section, `${title} demo needs a Vue code example`).toContain('```vue')
    }

    expect(docs).toContain('待上传预览源')
    expect(docs).toContain('图片加载失败')
    expect(docs).toContain('不支持的图纸格式')
    expect(docs).toContain('包含 `Inspection / Trend / Summary` 3 个工作表')
    expect(docs).not.toContain('pending-preview.pdf')
    expect(docs).not.toContain('missing-image.png')
  })

  it('labels office navigation by document unit', async () => {
    const pptx = mount(OfficePreview, {
      props: {
        src: '/files/review.pptx',
        kind: 'office-pptx',
      },
    })
    const xlsx = mount(OfficePreview, {
      props: {
        src: '/files/schedule.xlsx',
        kind: 'office-xlsx',
      },
    })

    await flushPromises()

    expect(pptx.get('button[aria-label="Previous slide"]')).toBeTruthy()
    expect(pptx.get('button[aria-label="Next slide"]')).toBeTruthy()
    expect(xlsx.get('button[aria-label="Previous sheet"]')).toBeTruthy()
    expect(xlsx.get('button[aria-label="Next sheet"]')).toBeTruthy()
  })

  it('allows jumping Word pages from the toolbar', async () => {
    const wrapper = mount(OfficePreview, {
      props: {
        src: '/files/work-instruction.docx',
        kind: 'office-docx',
      },
    })

    await flushPromises()

    const pageSelect = wrapper.getComponent(Select)
    pageSelect.vm.$emit('update:modelValue', '1')
    await flushPromises()

    expect(wrapper.text()).toContain('2/2')
  })

  it('allows jumping PowerPoint slides from the toolbar', async () => {
    const wrapper = mount(OfficePreview, {
      props: {
        src: '/files/review.pptx',
        kind: 'office-pptx',
      },
    })

    await flushPromises()

    const slideSelect = wrapper.getComponent(Select)
    slideSelect.vm.$emit('update:modelValue', '2')
    await flushPromises()

    expect(wrapper.text()).toContain('3/3')
  })

  it('allows jumping Excel worksheets from the toolbar', async () => {
    const wrapper = mount(OfficePreview, {
      props: {
        src: '/files/schedule.xlsx',
        kind: 'office-xlsx',
      },
    })

    await flushPromises()

    expect(wrapper.text()).toContain('Summary · 1/2')

    const sheetSelect = wrapper.getComponent(Select)
    sheetSelect.vm.$emit('update:modelValue', '1')
    await flushPromises()

    expect(wrapper.text()).toContain('Details · 2/2')
  })

  it('keeps spreadsheet preview chrome inside the unified toolbar', async () => {
    const wrapper = mount(OfficePreview, {
      props: {
        src: '/files/schedule.xlsx',
        kind: 'office-xlsx',
      },
    })

    await flushPromises()

    expect(officeViewerMocks.xlsxOptions.at(-1)?.showZoomSlider).toBe(false)
    expect(wrapper.get('[data-slot="file-preview-spreadsheet-host"]').classes()).toContain('file-preview-spreadsheet-host')
  })
})
