import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'

/** Read a route and its template-rendered, co-located components. */
export function readPageComponents(file: string) {
  const sources = new Map<string, string>()

  function visit(path: string) {
    if (sources.has(path)) return
    const source = readFileSync(path, 'utf8')
    sources.set(path, source)

    const template = source.slice(source.indexOf('<template>'))
    for (const [, component, relativePath] of source.matchAll(
      /import (\w+) from ['"](\.\/components\/[\w/-]+\.vue)['"]/g,
    )) {
      if (new RegExp(`<${component}[\\s/>]`).test(template)) {
        visit(resolve(dirname(path), relativePath))
      }
    }
  }

  visit(file)
  return [...sources.values()].join('\n')
}
