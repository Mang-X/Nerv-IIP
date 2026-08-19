import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, shallowRef } from 'vue'

import TeamMembersDialog from './TeamMembersDialog.vue'

const stub = vi.hoisted(() => ({
  removeMember: vi.fn().mockResolvedValue({}),
  refresh: vi.fn().mockResolvedValue({}),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
  setMemberError: undefined as undefined | ((error: unknown) => void),
}))

vi.mock('@/composables/useBusinessMasterData', () => {
  const memberError = shallowRef<unknown>()
  stub.setMemberError = (error) => {
    memberError.value = error
  }

  return {
    useBusinessWorkers: () => ({
      workers: computed(() => [
        {
          userId: 'usr-1',
          displayName: '张三',
          employeeNo: 'E001',
        },
      ]),
    }),
    useTeamMembers: () => ({
      members: computed(() => [
        { teamCode: 'TEAM-A', userId: 'usr-1', isLeader: false, active: true },
      ]),
      membersError: shallowRef(undefined),
      membersPending: shallowRef(false),
      memberError,
      addMember: vi.fn().mockResolvedValue({}),
      addPending: shallowRef(false),
      removeMember: stub.removeMember,
      removePending: shallowRef(false),
      refresh: stub.refresh,
    }),
  }
})

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

// 成员维护外层 Dialog 与本用例无关，就地渲染；移除确认的 NvAlertDialog 保留真件。
const dialogStubs = {
  NvDialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  WorkerSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<input :value="modelValue" />',
  },
}

let wrapper: ReturnType<typeof mount> | null = null

function mountDialog() {
  wrapper = mount(TeamMembersDialog, {
    props: { open: true, teamCode: 'TEAM-A', teamName: '总装一班' },
    attachTo: document.body,
    global: { stubs: dialogStubs },
  })
  return wrapper
}

function findButton(text: string) {
  return [...document.querySelectorAll('button')].find((button) =>
    button.textContent?.includes(text),
  )
}

async function openRemoveDialog() {
  mountDialog()
  await flushPromises()
  const removeButton = document.querySelector<HTMLButtonElement>(
    'button[aria-label="移除成员 张三（E001）"]',
  )
  expect(removeButton).not.toBeNull()
  removeButton!.click()
  await flushPromises()
  expect(document.querySelector('[role="alertdialog"]')).not.toBeNull()
}

async function setReason(value: string) {
  const input = document.querySelector<HTMLInputElement>('#team-member-remove-reason')
  expect(input).not.toBeNull()
  input!.value = value
  input!.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()
  return input!
}

afterEach(() => {
  wrapper?.unmount()
  wrapper = null
  document.body.innerHTML = ''
})

beforeEach(() => {
  stub.removeMember.mockReset().mockResolvedValue({})
  stub.refresh.mockClear()
  stub.toastSuccess.mockClear()
  stub.toastError.mockClear()
  stub.setMemberError?.(undefined)
})

describe('班组成员移除原因', () => {
  it('拒绝空与纯空白原因，并在真确认框显示计数和明确错误', async () => {
    await openRemoveDialog()

    expect(document.body.textContent).toContain('0 / 500')
    const emptyInput = document.querySelector<HTMLInputElement>('#team-member-remove-reason')!
    emptyInput.dispatchEvent(new Event('blur'))
    await flushPromises()
    expect(document.body.textContent).toContain('请输入移除原因。')

    await setReason('   ')
    expect(document.body.textContent).toContain('3 / 500')
    expect(document.body.textContent).toContain('移除原因不能只包含空白字符。')
    const confirm = findButton('确认移除')!
    expect(confirm.hasAttribute('disabled')).toBe(true)
    confirm.click()
    await flushPromises()
    expect(stub.removeMember).not.toHaveBeenCalled()
  })

  it('允许 500 字边界，并对 501 字的异常输入失败关闭', async () => {
    await openRemoveDialog()

    await setReason('甲'.repeat(501))
    expect(document.body.textContent).toContain('501 / 500')
    expect(document.body.textContent).toContain('移除原因不能超过 500 个字符。')
    expect(findButton('确认移除')!.hasAttribute('disabled')).toBe(true)
    expect(stub.removeMember).not.toHaveBeenCalled()

    await setReason('甲'.repeat(500))
    expect(document.body.textContent).toContain('500 / 500')
    expect(findButton('确认移除')!.hasAttribute('disabled')).toBe(false)
    findButton('确认移除')!.click()
    await flushPromises()
    expect(stub.removeMember).toHaveBeenCalledWith('usr-1', '甲'.repeat(500))
  })

  it('提交 trim 后的原因，成功后关框并清空', async () => {
    await openRemoveDialog()
    await setReason('  调入维修班组  ')

    findButton('确认移除')!.click()
    await flushPromises()

    expect(stub.removeMember).toHaveBeenCalledWith('usr-1', '调入维修班组')
    expect(stub.toastSuccess).toHaveBeenCalledWith('已移除成员。')
    expect(document.querySelector('[role="alertdialog"]')).toBeNull()

    document.querySelector<HTMLButtonElement>('button[aria-label="移除成员 张三（E001）"]')!.click()
    await flushPromises()
    expect(document.querySelector<HTMLInputElement>('#team-member-remove-reason')?.value).toBe('')
  })

  it('服务端失败时只显示一次移除失败，并保留原因与确认框', async () => {
    const mutationError = {}
    stub.removeMember.mockImplementationOnce(async () => {
      // 还原真实 composable：mutateAsync 拒绝时 mutation error ref 也会更新。
      stub.setMemberError?.(mutationError)
      throw mutationError
    })
    await openRemoveDialog()
    await setReason('岗位变更')

    findButton('确认移除')!.click()
    await flushPromises()

    expect(stub.toastError).toHaveBeenCalledTimes(1)
    expect(stub.toastError).toHaveBeenCalledWith('移除成员失败，请稍后重试。')
    expect(stub.toastError).not.toHaveBeenCalledWith('成员加载失败，请稍后重试。')
    expect(document.querySelector('[role="alertdialog"]')).not.toBeNull()
    expect(document.querySelector<HTMLInputElement>('#team-member-remove-reason')?.value).toBe(
      '岗位变更',
    )
  })

  it('取消不发请求，再次打开时不残留上次原因', async () => {
    await openRemoveDialog()
    await setReason('临时调班')

    findButton('取消')!.click()
    await flushPromises()
    expect(stub.removeMember).not.toHaveBeenCalled()
    expect(document.querySelector('[role="alertdialog"]')).toBeNull()

    document.querySelector<HTMLButtonElement>('button[aria-label="移除成员 张三（E001）"]')!.click()
    await flushPromises()
    expect(document.querySelector<HTMLInputElement>('#team-member-remove-reason')?.value).toBe('')
  })
})
