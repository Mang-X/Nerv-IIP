import { configureApiClient } from '@nerv-iip/api-client'
import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { afterEach, describe, expect, it } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import DirectorySuggestInput from './DirectorySuggestInput.vue'

// #3832：收货批次可能是新批次号，输入的文本就是值；同时拿它去批次目录做服务端搜索给建议。
describe('DirectorySuggestInput', () => {
  afterEach(() => {
    configureApiClient()
    document.body.innerHTML = ''
  })

  it('键入新批次号：值就是输入的文本，建议按这个关键字去批次目录搜', async () => {
    const requests: URL[] = []
    configureApiClient({
      baseUrl: 'http://gateway.local',
      fetch: (async (request: Request) => {
        const url = new URL(request.url)
        requests.push(url)
        return Response.json({ success: true, data: { items: [], total: 0 } })
      }) as typeof fetch,
    })
    const model = ref('')
    const wrapper = mount(
      defineComponent({
        setup() {
          useBusinessContextStore().patchContext({
            organizationId: 'org-a',
            environmentId: 'env-a',
          })
          return () =>
            h(DirectorySuggestInput, {
              directoryType: 'batch',
              modelValue: model.value,
              'onUpdate:modelValue': (value: string) => (model.value = value),
            })
        },
      }),
      { global: { plugins: [createPinia(), PiniaColada] }, attachTo: document.body },
    )

    await wrapper.get('input').setValue('LOT-NEW-9')
    // 搜索词去抖 300ms 后才发请求。
    await new Promise((resolve) => setTimeout(resolve, 350))
    await flushPromises()

    expect(model.value).toBe('LOT-NEW-9')
    const last = requests.at(-1)!
    expect(last.pathname).toBe('/api/business-console/v1/directories/batch')
    expect(last.searchParams.get('keyword')).toBe('LOT-NEW-9')
    wrapper.unmount()
  })
})
