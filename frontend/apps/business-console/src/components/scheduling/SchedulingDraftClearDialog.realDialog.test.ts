import { flushPromises, mount } from '@vue/test-utils'
import { defineComponent, ref } from 'vue'
import { afterEach, expect, it, vi } from 'vitest'
import SchedulingDraftClearDialog from './SchedulingDraftClearDialog.vue'

let mounted: ReturnType<typeof mount>
afterEach(() => {
  mounted?.unmount()
  document.body.innerHTML = ''
})
function dialog() {
  return document.querySelector('[role="alertdialog"]')
}
function button(label: string) {
  return [...document.querySelectorAll('[role="alertdialog"] button')].find((button) =>
    button.textContent?.includes(label),
  ) as HTMLButtonElement
}
async function open(clear: () => Promise<boolean>, pending = ref(false)) {
  const host = defineComponent({
    components: { SchedulingDraftClearDialog },
    setup: () => ({ open: ref(false), clear, pending }),
    template:
      '<button @click="open = true">清空草稿</button><SchedulingDraftClearDialog v-model:open="open" :pending="pending" :clear="clear" />',
  })
  mounted = mount(host, { attachTo: document.body })
  await mounted.get('button').trigger('click')
  await flushPromises()
}
it('opening and cancelling preserve the saved draft without a delete request', async () => {
  const clear = vi.fn().mockResolvedValue(true)
  await open(clear)
  expect(dialog()).not.toBeNull()
  expect(clear).not.toHaveBeenCalled()
  button('取消').click()
  await flushPromises()
  expect(dialog()).toBeNull()
  expect(clear).not.toHaveBeenCalled()
})
it('keeps the real dialog open while deletion is pending or fails, and closes only after success', async () => {
  let resolve!: (success: boolean) => void
  const pending = ref(false)
  const clear = vi.fn(async () => {
    pending.value = true
    const success = await new Promise<boolean>((done) => {
      resolve = done
    })
    pending.value = false
    return success
  })
  await open(clear, pending)
  button('确认清空').click()
  await flushPromises()
  expect(button('确认清空').disabled).toBe(true)
  expect(button('取消').disabled).toBe(false)
  expect(dialog()).not.toBeNull()
  resolve(false)
  await flushPromises()
  expect(dialog()).not.toBeNull()
  expect(button('确认清空').disabled).toBe(false)
  button('确认清空').click()
  await flushPromises()
  resolve(true)
  await flushPromises()
  expect(dialog()).toBeNull()
  expect(clear).toHaveBeenCalledTimes(2)
})
