import { rm } from 'node:fs/promises'

const generatedFiles = [
  '../src/generated/client',
  '../src/generated/core',
  '../src/generated/index.ts',
  '../src/generated/sdk.gen.ts',
  '../src/generated/types.gen.ts',
  '../src/generated/business-console/client',
  '../src/generated/business-console/core',
  '../src/generated/business-console/index.ts',
  '../src/generated/business-console/sdk.gen.ts',
  '../src/generated/business-console/types.gen.ts',
]

await Promise.all(
  generatedFiles.map((path) =>
    rm(new URL(path, import.meta.url), {
      force: true,
      recursive: true,
    }),
  ),
)
