import { defineConfig } from '@hey-api/openapi-ts'

const plugins = [
  '@hey-api/client-fetch',
  '@hey-api/typescript',
  '@hey-api/sdk',
  {
    name: '@pinia/colada',
    includeInEntry: true,
    queryKeys: { tags: true },
    queryOptions: { name: '{{name}}QueryOptions' },
    mutationOptions: { name: '{{name}}MutationOptions' },
  },
] as const

export default defineConfig([
  {
    input: './openapi/platform-gateway.v1.json',
    output: { path: './src/generated' },
    plugins,
  },
  {
    input: './openapi/business-gateway-console.v1.json',
    output: { path: './src/generated/business-console' },
    plugins,
  },
])
