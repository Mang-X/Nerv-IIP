import {
  refreshSops,
  openSopFileContent,
  tasksErrorRef,
  sopsErrorRef,
  operationTasksRef,
  currentSopsRef,
  resetFixture,
} from './operation.fixture'
import { RequestTimeoutError } from '@/api/request-timeout'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import OperationPage from './operation.vue'

describe('PDA MES operation execution page', () => {
  beforeEach(resetFixture)

  // P1：SOP 查询失败也要有可操作错误态 + 重试入口（#814 所有 facade）。
  it('SOP 查询超时：SOP 区显示可操作错误面板 + 重试调 refresh', async () => {
    sopsErrorRef.value = new RequestTimeoutError()
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click') // OP-1 绑定了标准工序
    await flushPromises()
    const panel = document.body.querySelector<HTMLElement>('[data-testid="sops-error"]')!
    expect(panel).toBeTruthy()
    expect(panel.textContent).toContain('网络超时，请检查连接后重试')
    panel.querySelector<HTMLElement>('[data-testid="retry-list"]')!.click()
    expect(refreshSops).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  // P2：加载失败时空态与错误态互斥，不把网络错误误报成"暂无"。
  it('工序列表加载失败时不显示"暂无工序任务"空态', () => {
    operationTasksRef.value = []
    tasksErrorRef.value = new RequestTimeoutError()
    const wrapper = mount(OperationPage)
    expect(wrapper.find('[data-testid="operation-tasks-error"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('暂无工序任务')
    wrapper.unmount()
  })

  // P1：SOP 打开文件失败（超时/离线）时不隐藏 SOP 列表——保留"查看SOP"作为重试入口。
  it('打开 SOP 失败（超时）时保留 SOP 列表与"查看SOP"按钮以便重试', async () => {
    currentSopsRef.value = [
      { fileId: 'F1', fileName: 'SOP-1', documentNumber: 'D1', revision: 'A', effectiveDate: null },
    ]
    openSopFileContent.mockRejectedValueOnce(new Error('网络超时，请检查连接后重试'))
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    const viewBtn = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find((b) =>
      b.textContent?.includes('查看SOP'),
    )!
    expect(viewBtn).toBeTruthy()
    viewBtn.click()
    await flushPromises()
    // 打开失败文案出现
    expect(document.body.querySelector('[data-testid="sop-file-error"]')?.textContent).toContain(
      '网络超时，请检查连接后重试',
    )
    // 且 SOP 列表与"查看SOP"按钮仍在（可再次点击重试），不被错误文本隐藏
    expect(
      [...document.body.querySelectorAll('button')].some((b) => b.textContent?.includes('查看SOP')),
    ).toBe(true)
    wrapper.unmount()
  })
})
