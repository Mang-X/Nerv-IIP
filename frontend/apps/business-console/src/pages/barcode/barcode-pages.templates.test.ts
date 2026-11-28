import {
  barcode,
  toastMock,
  layoutStub,
  dialogStubs,
  selectStubs,
  setInput,
  uploadTemplateFile,
  openNewTemplate,
  resetFixture,
} from './barcode-pages.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import TemplatesPage from './templates.vue'

describe('barcode pages', () => {
  beforeEach(resetFixture)

  it('assembles the variable list from data item rows without showing variable names', async () => {
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('SKU_BOX')
    expect(wrapper.text()).toContain('物料编码、批次号、有效期')
    // 目录外且没有显示名称的数据项只显示「其他数据项」，变量名不上屏。
    expect(wrapper.text()).toContain('其他数据项、成品编码')
    expect(wrapper.text()).not.toContain('palletGrade')
    expect(wrapper.text()).not.toContain('skuCode')

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建模板'))!
      .trigger('click')
    await flushPromises()
    await setInput(wrapper, '#barcode-template-code', 'PALLET_LABEL')
    await setInput(wrapper, '#barcode-template-name', '托盘标签')
    await uploadTemplateFile(wrapper)
    await wrapper.find('#barcode-template-item-0').setValue('skuCode')
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('添加数据项'))!
      .trigger('click')
    await flushPromises()
    await wrapper.find('#barcode-template-item-1').setValue('lotNo')
    await setInput(wrapper, '#barcode-template-label-1', '来料批次')
    await setInput(wrapper, '#barcode-template-max-1', '30')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveTemplate).toHaveBeenCalledWith(
      expect.objectContaining({
        templateCode: 'PALLET_LABEL',
        templateFileId: 'file-pallet',
        variableSchemaJson: JSON.stringify({
          version: 1,
          variables: [
            { name: 'skuCode', label: '物料编码', type: 'string', required: true, maxLength: 200 },
            { name: 'lotNo', label: '来料批次', type: 'string', required: true, maxLength: 30 },
          ],
        }),
      }),
    )
  })

  it('uploads the template file in the dialog and never shows its file id', async () => {
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    // 列表只说「已上传」，不显示文件标识。
    await flushPromises()
    expect(wrapper.text()).toContain('已上传')
    expect(wrapper.text()).not.toContain('file-label-box')

    await openNewTemplate(wrapper)
    const example = wrapper.get('a[download="label-template.json"]')
    const content = decodeURIComponent((example.attributes('href') ?? '').split(',')[1]!)
    expect(JSON.parse(content)).toMatchObject({
      format: 'nerv-iip.label-template',
      version: 1,
      media: { dpi: 203, widthDots: 812, heightDots: 406 },
      fields: [
        { kind: 'text', variable: 'skuCode' },
        { kind: 'barcode', variable: 'label.value' },
      ],
    })

    expect(wrapper.find('[data-testid="barcode-template-file-status"]').text()).toBe('未上传')
    await setInput(wrapper, '#barcode-template-code', ' PALLET_LABEL ')
    const file = await uploadTemplateFile(wrapper)

    expect(barcode.uploadTemplateAsset).toHaveBeenCalledWith(file, {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      templateCode: 'PALLET_LABEL',
    })
    expect(wrapper.find('[data-testid="barcode-template-file-status"]').text()).toBe(
      '「pallet-label.json」已上传',
    )
    expect(wrapper.find('#barcode-template-file').text()).toContain('重新上传')
    expect(wrapper.text()).not.toContain('file-pallet')

    await setInput(wrapper, '#barcode-template-name', '托盘标签')
    await wrapper.find('#barcode-template-item-0').setValue('skuCode')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(barcode.saveTemplate).toHaveBeenCalledWith(
      expect.objectContaining({ templateCode: 'PALLET_LABEL', templateFileId: 'file-pallet' }),
    )
  })

  it('asks for the template code before choosing a file', async () => {
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await openNewTemplate(wrapper)

    await wrapper.find('#barcode-template-file').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('请先填写模板编码，再上传模板文件。')
    expect(barcode.uploadTemplateAsset).not.toHaveBeenCalled()
  })

  it('drops an uploaded file when the template code changes afterwards', async () => {
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await openNewTemplate(wrapper)
    await setInput(wrapper, '#barcode-template-code', 'PALLET_LABEL')
    await uploadTemplateFile(wrapper)
    await setInput(wrapper, '#barcode-template-code', 'PALLET_LABEL_2')
    await flushPromises()

    expect(wrapper.find('[data-testid="barcode-template-file-status"]').text()).toBe('未上传')
    expect(wrapper.text()).toContain('模板编码改了，请重新上传模板文件。')
    await setInput(wrapper, '#barcode-template-name', '托盘标签')
    await wrapper.find('#barcode-template-item-0').setValue('skuCode')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(barcode.saveTemplate).not.toHaveBeenCalled()
  })

  it('keeps a precheck problem next to the file field', async () => {
    const { TemplateAssetPrecheckError } = await import('@/components/barcode/templateAssetUpload')
    barcode.uploadTemplateAsset.mockRejectedValueOnce(
      new TemplateAssetPrecheckError('模板文件只支持 .json 格式。'),
    )
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await openNewTemplate(wrapper)
    await setInput(wrapper, '#barcode-template-code', 'PALLET_LABEL')
    await uploadTemplateFile(wrapper, 'pallet.txt')

    expect(wrapper.text()).toContain('模板文件只支持 .json 格式。')
    expect(toastMock.error).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="barcode-template-file-status"]').text()).toBe('未上传')
  })

  it('reports a failed upload request as a toast instead of a lasting field message', async () => {
    barcode.uploadTemplateAsset.mockRejectedValueOnce(new Error('上传会话已失效，请重新上传。'))
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await openNewTemplate(wrapper)
    await setInput(wrapper, '#barcode-template-code', 'PALLET_LABEL')
    await uploadTemplateFile(wrapper)

    expect(toastMock.error).toHaveBeenCalledWith('上传模板文件失败：上传会话已失效，请重新上传。')
    expect(wrapper.text()).not.toContain('上传会话已失效')
    expect(wrapper.find('[data-testid="barcode-template-file-status"]').text()).toBe('未上传')
  })

  it('never submits a code changed during upload together with the file uploaded for the old code', async () => {
    let finishUpload!: (value: { fileId: string; fileName: string; sizeBytes: number }) => void
    barcode.uploadTemplateAsset.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finishUpload = resolve
        }),
    )
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await openNewTemplate(wrapper)
    await setInput(wrapper, '#barcode-template-code', 'CODE_A')
    await uploadTemplateFile(wrapper)

    // 上传进行中：编码输入框锁住；即便有人改了编码，完成时也会按上传所用的编码核对。
    expect(wrapper.find('#barcode-template-code').attributes('disabled')).toBeDefined()
    await setInput(wrapper, '#barcode-template-code', 'CODE_B')
    finishUpload({ fileId: 'file-code-a', fileName: 'a.json', sizeBytes: 363 })
    await flushPromises()
    expect(wrapper.find('#barcode-template-code').attributes('disabled')).toBeUndefined()

    await setInput(wrapper, '#barcode-template-name', '托盘标签')
    await wrapper.find('#barcode-template-item-0').setValue('skuCode')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const saved = barcode.saveTemplate.mock.calls.map((call) => call[0])
    expect(saved).not.toContainEqual(
      expect.objectContaining({ templateCode: 'CODE_B', templateFileId: 'file-code-a' }),
    )
  })

  it('drops an upload that finishes after its dialog was closed and a new one opened', async () => {
    let finishUpload!: (value: { fileId: string; fileName: string; sizeBytes: number }) => void
    barcode.uploadTemplateAsset.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finishUpload = resolve
        }),
    )
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await openNewTemplate(wrapper)
    await setInput(wrapper, '#barcode-template-code', 'CODE_A')
    await uploadTemplateFile(wrapper)

    // 上传挂起时关掉弹窗，重开「新建」，换一个编码。
    await wrapper
      .findAll('button')
      .find((b) => b.text() === '取消')!
      .trigger('click')
    await openNewTemplate(wrapper)
    await setInput(wrapper, '#barcode-template-code', 'CODE_B')
    finishUpload({ fileId: 'file-code-a', fileName: 'a.json', sizeBytes: 363 })
    await flushPromises()

    // 旧弹窗的上传结果被丢弃，也不在新弹窗里留提示。
    expect(wrapper.find('[data-testid="barcode-template-file-status"]').text()).toBe('未上传')
    expect(wrapper.text()).not.toContain('模板编码改了')
    await setInput(wrapper, '#barcode-template-name', '托盘标签')
    await wrapper.find('#barcode-template-item-0').setValue('skuCode')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(barcode.saveTemplate).not.toHaveBeenCalled()
  })

  it('keeps the existing file when an edited template is saved without re-uploading', async () => {
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('编辑'))!
      .trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="barcode-template-file-status"]').text()).toBe('已上传')
    expect(wrapper.text()).not.toContain('file-label-box')
  })

  it('does not submit a template until every data item row is chosen', async () => {
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建模板'))!
      .trigger('click')
    await flushPromises()
    await setInput(wrapper, '#barcode-template-code', 'PALLET_LABEL')
    await setInput(wrapper, '#barcode-template-name', '托盘标签')
    await uploadTemplateFile(wrapper)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveTemplate).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('第 1 行：请选择数据项。')
  })

  it.each([
    ['duplicate data item', 'skuCode', '200', '第 2 行：数据项与前面重复。'],
    ['non-positive max length', 'lotNo', '0', '第 2 行：最大长度需为正整数。'],
  ])('does not submit a template with a %s', async (_case, secondItem, maxLength, message) => {
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建模板'))!
      .trigger('click')
    await flushPromises()
    await setInput(wrapper, '#barcode-template-code', 'PALLET_LABEL')
    await setInput(wrapper, '#barcode-template-name', '托盘标签')
    await uploadTemplateFile(wrapper)
    await wrapper.find('#barcode-template-item-0').setValue('skuCode')
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('添加数据项'))!
      .trigger('click')
    await flushPromises()
    await wrapper.find('#barcode-template-item-1').setValue(secondItem)
    await setInput(wrapper, '#barcode-template-max-1', maxLength)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveTemplate).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain(message)
  })

  it('rewrites a legacy field list into the variable list the printer accepts on update', async () => {
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('编辑'))!
      .trigger('click')
    await flushPromises()

    const carried = wrapper.find('[data-slot="carried-context"]')
    expect(carried.text()).toContain('SKU_BOX')
    expect(wrapper.find('#barcode-template-code').exists()).toBe(false)
    await setInput(wrapper, '#barcode-template-name', '外箱标签 V2')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveTemplate).toHaveBeenCalledWith(
      expect.objectContaining({
        templateCode: 'SKU_BOX',
        templateName: '外箱标签 V2',
        templateFileId: 'file-label-box',
        variableSchemaJson: JSON.stringify({
          version: 1,
          variables: [
            { name: 'skuCode', label: '物料编码', type: 'string', required: true, maxLength: 200 },
            { name: 'lotNo', label: '批次号', type: 'string', required: true, maxLength: 200 },
            { name: 'expiryDate', label: '有效期', type: 'string', required: true, maxLength: 200 },
          ],
        }),
      }),
    )
  })

  it('keeps an unlisted data item and its settings when a template is edited', async () => {
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()

    await wrapper
      .findAll('button')
      .filter((b) => b.text().includes('编辑'))[1]!
      .trigger('click')
    await flushPromises()
    const firstItem = wrapper.find('#barcode-template-item-0')
    expect(firstItem.text()).toContain('其他数据项')
    expect(firstItem.text()).not.toContain('palletGrade')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveTemplate).toHaveBeenCalledWith(
      expect.objectContaining({
        templateCode: 'PALLET',
        variableSchemaJson: JSON.stringify({
          version: 1,
          variables: [
            { name: 'palletGrade', type: 'string', required: true, maxLength: 200 },
            { name: 'skuCode', label: '成品编码', type: 'string', required: false, maxLength: 40 },
          ],
        }),
      }),
    )
  })
})
