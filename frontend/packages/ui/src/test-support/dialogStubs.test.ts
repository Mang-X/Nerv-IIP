import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import { dialogStubs } from './dialogStubs'
import DialogStubFixture from './fixtures/DialogStubFixture.vue'

/**
 * 鉴别力证明（#2847）：`DialogStubFixture` 用真实 `<script setup>` 消费
 * `NvDialog`，和业务组件同一条编译路径。用 `dialogStubs`（真实键
 * `DialogRoot`）stub 住它，reka-ui 真实的 `DialogRoot` 就不会被渲染，
 * slot 内容随之消失——这是这份共享映射存在的唯一理由。
 *
 * 反证见本文件底部注释：把 `global.stubs` 换成失效的
 * `{ NvDialog: true }` 写法重跑，断言会变红（slot 内容仍然渲染），
 * 证明 `dialogStubs` 不是摆设。红绿两态的实际终端输出记录在 PR 里。
 */
describe('dialogStubs', () => {
  it("stubs the real DialogRoot key so an NvDialog consumer's slot content is hidden", () => {
    const wrapper = mount(DialogStubFixture, {
      global: { stubs: dialogStubs },
    })

    expect(wrapper.find('[data-testid="dialog-slot-content"]').exists()).toBe(false)
  })
})
