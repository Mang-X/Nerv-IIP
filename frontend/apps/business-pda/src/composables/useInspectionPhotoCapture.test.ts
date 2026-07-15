import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const isNativePlatform = vi.fn(() => false)
const getPhoto = vi.fn()

vi.mock('@capacitor/core', () => ({
  Capacitor: {
    isNativePlatform: () => isNativePlatform(),
  },
}))
vi.mock('@capacitor/camera', () => ({
  Camera: { getPhoto: (...args: unknown[]) => getPhoto(...args) },
  CameraResultType: { Uri: 'uri' },
  CameraSource: { Camera: 'CAMERA' },
}))

import { photoCaptureSupported, useInspectionPhotoCapture } from './useInspectionPhotoCapture'

function setMediaDevices(value: unknown) {
  Object.defineProperty(navigator, 'mediaDevices', { value, configurable: true })
}

beforeEach(() => {
  isNativePlatform.mockReturnValue(false)
  getPhoto.mockReset()
})

afterEach(() => {
  setMediaDevices(undefined)
  vi.restoreAllMocks()
})

describe('photoCaptureSupported', () => {
  it('is true on a native platform', () => {
    isNativePlatform.mockReturnValue(true)
    expect(photoCaptureSupported()).toBe(true)
  })

  it('is true on web when a camera (getUserMedia) is available', () => {
    setMediaDevices({ getUserMedia: vi.fn() })
    expect(photoCaptureSupported()).toBe(true)
  })

  it('is false on web when no camera is available (entry hidden)', () => {
    setMediaDevices(undefined)
    expect(photoCaptureSupported()).toBe(false)
  })
})

describe('useInspectionPhotoCapture.capture', () => {
  it('web fallback: captures a File via <input capture> and previews it as an object URL', async () => {
    setMediaDevices({ getUserMedia: vi.fn() })
    const createObjectURL = vi.fn(() => 'blob:preview')
    vi.stubGlobal('URL', { ...URL, createObjectURL, revokeObjectURL: vi.fn() })
    // 模拟用户在 file input 上选了一张照片。
    vi.spyOn(HTMLInputElement.prototype, 'click').mockImplementation(
      function (this: HTMLInputElement) {
        Object.defineProperty(this, 'files', {
          value: [new File(['x'], 'shot.jpg', { type: 'image/jpeg' })],
          configurable: true,
        })
        this.dispatchEvent(new Event('change'))
      },
    )

    const { capture } = useInspectionPhotoCapture()
    const photo = await capture()

    expect(photo).toMatchObject({ name: 'shot.jpg', format: 'jpeg', url: 'blob:preview' })
    expect(photo?.file).toBeInstanceOf(File)
    expect(getPhoto).not.toHaveBeenCalled()
  })

  it('web fallback: resolves null when the user cancels (no file chosen)', async () => {
    setMediaDevices({ getUserMedia: vi.fn() })
    vi.spyOn(HTMLInputElement.prototype, 'click').mockImplementation(
      function (this: HTMLInputElement) {
        this.dispatchEvent(new Event('change'))
      },
    )
    const { capture } = useInspectionPhotoCapture()
    expect(await capture()).toBeNull()
  })

  it('native: captures via Capacitor Camera and uses webPath as the preview src', async () => {
    isNativePlatform.mockReturnValue(true)
    getPhoto.mockResolvedValue({ webPath: 'capacitor://localhost/x.jpg', format: 'jpeg' })

    const { capture } = useInspectionPhotoCapture()
    const photo = await capture()

    expect(getPhoto).toHaveBeenCalledOnce()
    expect(photo).toMatchObject({
      url: 'capacitor://localhost/x.jpg',
      webPath: 'capacitor://localhost/x.jpg',
      format: 'jpeg',
    })
    expect(photo?.file).toBeUndefined()
  })

  it('native: resolves null when the user cancels (getPhoto throws)', async () => {
    isNativePlatform.mockReturnValue(true)
    getPhoto.mockRejectedValue(new Error('User cancelled photos app'))

    const { capture } = useInspectionPhotoCapture()
    expect(await capture()).toBeNull()
  })
})
