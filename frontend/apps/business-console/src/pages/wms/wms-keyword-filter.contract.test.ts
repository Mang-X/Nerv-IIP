import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const pages = ['inbound.vue', 'outbound.vue', 'counts.vue']

describe('WMS PC 关键字筛选契约', () => {
  it.each(pages)('%s 通过 NvInput 绑定服务端 keyword 筛选', (page) => {
    const source = readFileSync(resolve(__dirname, page), 'utf8')

    expect(source).toMatch(
      /<NvInput(?=[^>]*v-model="filters\.keyword")(?=[^>]*aria-label="关键字搜索")[^>]*>/,
    )
  })
})
