import { describe, expect, it } from 'vitest'
import { resolveGatewayBaseUrl } from './gateway-base-url'

describe('resolveGatewayBaseUrl', () => {
  it('returns undefined when the env var is absent (web/dev → relative api-client default)', () => {
    expect(resolveGatewayBaseUrl({} as unknown as ImportMetaEnv)).toBeUndefined()
  })

  it('treats an empty string as unset → undefined', () => {
    expect(
      resolveGatewayBaseUrl({ VITE_NERV_IIP_API_BASE_URL: '' } as unknown as ImportMetaEnv),
    ).toBeUndefined()
  })

  it('returns the absolute gateway URL when set (Capacitor/APK WebView has no proxy)', () => {
    expect(
      resolveGatewayBaseUrl({
        VITE_NERV_IIP_API_BASE_URL: 'https://gw.example.test',
      } as unknown as ImportMetaEnv),
    ).toBe('https://gw.example.test')
  })
})
