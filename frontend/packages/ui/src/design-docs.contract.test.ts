import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * Design-doc freshness guard.
 *
 * Prescriptive design docs (the ones agents and humans follow when writing NEW
 * code) must teach current identifiers. Historical/registry docs are exempt:
 * `tokens.md` and `governance.md` legitimately name old identifiers as
 * migration history / ban lists, and `roadmaps/` are point-in-time snapshots.
 *
 * Rationale: stale prescriptive docs actively caused regressions — e.g.
 * `foundation.md` taught `Badge variant="destructive"` while the shipped
 * component is `NvStatusBadge tone="danger"`, and `motion-interaction.md`
 * taught un-prefixed motion tokens after #790 moved them to `--nv-*`.
 */

const srcDir = dirname(fileURLToPath(import.meta.url))
const designRoot = resolve(srcDir, '../../../DESIGN')

const PRESCRIPTIVE: string[] = []
for (const top of ['index.md', 'foundation.md', 'motion-interaction.md', 'component-coverage.md']) {
  PRESCRIPTIVE.push(join(designRoot, top))
}
for (const dir of ['components', 'patterns']) {
  const walk = (d: string): void => {
    for (const e of readdirSync(d, { withFileTypes: true })) {
      const full = join(d, e.name)
      if (e.isDirectory()) walk(full)
      else if (e.name.endsWith('.md')) PRESCRIPTIVE.push(full)
    }
  }
  walk(join(designRoot, dir))
}

interface BannedPattern {
  re: RegExp
  why: string
}

const BANNED: BannedPattern[] = [
  { re: /--sb-/, why: 'screen 旧令牌 --sb-* 已全表迁移为 --nv-scr-*（#790 + 收口批）' },
  { re: /\.ds-[a-z]/, why: '旧内部类名 .ds-* 已改名 .nv-*（#787）' },
  {
    re: /\b[A-Z][A-Za-z]{2,}Pro\b/,
    why: '旧 *Pro 组件名已在 #787/#789 改名为 Nv*（如 ButtonPro → NvButton）',
  },
  {
    re: /var\(--(?:ease|duration)-/,
    why: '动效令牌规范名是 --nv-ease-* / --nv-duration-*（无前缀名仅是过渡别名）',
  },
  {
    re: /\bIamPagination\b/,
    why: 'IamPagination 已不存在，分页用 NvDataTable 内建或 NvPagination',
  },
]

describe('prescriptive design docs teach current identifiers', () => {
  it('contains no stale identifiers', () => {
    const offenders: string[] = []
    for (const file of PRESCRIPTIVE) {
      const text = readFileSync(file, 'utf8')
      const lines = text.split('\n')
      for (const { re, why } of BANNED) {
        lines.forEach((line, i) => {
          if (re.test(line)) {
            offenders.push(
              `${relative(designRoot, file)}:${i + 1} [${re}] ${why}\n    ${line.trim()}`,
            )
          }
        })
      }
    }
    expect(
      offenders,
      `stale identifiers in prescriptive design docs:\n${offenders.join('\n')}`,
    ).toEqual([])
  })
})
