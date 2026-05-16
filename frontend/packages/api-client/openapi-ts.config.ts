import { defineConfig } from '@hey-api/openapi-ts'

export default defineConfig({
  input: './openapi/platform-gateway.v1.json',
  output: { path: './src/generated' },
  plugins: [
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
  ],
})
