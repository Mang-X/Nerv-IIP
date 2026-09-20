import { afterEach, describe, expect, it, vi } from 'vitest'

describe('Business Console Playwright diagnostics', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
    vi.resetModules()
  })

  it('retains failed CI diagnostics in the governed output directory', async () => {
    vi.stubEnv('CI', 'true')
    vi.stubEnv('NERV_IIP_OUT_DIR', '/tmp/nerv-iip-business-console-browser')
    vi.resetModules()

    const { default: config } = await import('../playwright.config')

    expect(config.outputDir).toBe('/tmp/nerv-iip-business-console-browser')
    expect(config.use).toMatchObject({
      screenshot: 'only-on-failure',
      trace: 'retain-on-failure',
    })
    expect(config.webServer).toMatchObject({
      command: 'vp preview --host 127.0.0.1 --port 5126',
    })
  })

  it('fails closed when the browser evidence output directory is not specified', async () => {
    vi.stubEnv('NERV_IIP_OUT_DIR', '')
    vi.resetModules()

    const { requireBrowserEvidenceOutputDir } = await import('../playwright.config')

    expect(() => requireBrowserEvidenceOutputDir()).toThrow('请显式指定产物目录')
  })
})
