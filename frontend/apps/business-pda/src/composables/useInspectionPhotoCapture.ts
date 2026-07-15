import { Camera, CameraResultType, CameraSource } from '@capacitor/camera'
import { Capacitor } from '@capacitor/core'

/**
 * 点检拍照取证 —— 能力门控的取证照片采集（MAN-458 / #812）。
 *
 * - **原生 PDA**（Capacitor Android WebView）：走原生 `@capacitor/camera`
 *   `Camera.getPhoto`（系统相机、质量控制），返回可直接用作 `<img src>` 的 `webPath`。
 * - **Web / 浏览器**（前端验收）：降级到 Web 标准 `<input type="file" accept="image/*"
 *   capture="environment">`，返回原始 `File` + object URL 预览。
 *
 * **持久化边界**：照片的采集 / 预览 / 关联到测量值行是**纯前端**能力，已在此闭环。
 * 唯一未做的是「上传到 FileStorage 并随测量值上送」——那一段依赖后端图片字段 /
 * 附件契约（Maintenance `recordInspection` 的 measurement 尚无图片字段），单列后端
 * follow-up（#924）。故当前照片本地持有、提交时不上送。
 */
export interface CapturedPhoto {
  /** 客户端行内稳定 id（v-for key / 删除定位）。 */
  id: number
  /** 预览 `<img src>`：原生为 `webPath`，Web 降级为 object URL。 */
  url: string
  /** 图片格式（jpeg / png…）。 */
  format: string
  /** 展示名（缩略图 alt / 无障碍）。 */
  name: string
  /** Web 降级的原始文件（object URL 需 revoke，后端契约就绪后据此上送）。原生为 undefined。 */
  file?: File
  /** 原生返回的 webPath（后端契约就绪后据此读取文件上送）。Web 降级为 undefined。 */
  webPath?: string
}

let nextPhotoId = 1

/**
 * 相机能力是否可用（不可用时上层隐藏拍照入口，符合 #812「相机能力不可用时隐藏入口」）。
 * - 原生 PDA：Capacitor WebView 一定具备相机 → true。
 * - Web：具备 `MediaDevices.getUserMedia` 摄像头能力才显示（桌面无摄像头 → 隐藏）。
 */
export function photoCaptureSupported(): boolean {
  if (Capacitor.isNativePlatform()) return true
  if (typeof navigator === 'undefined' || typeof document === 'undefined') return false
  return Boolean(navigator.mediaDevices?.getUserMedia)
}

/** 原生相机采集（Capacitor Camera）；用户取消 → null。 */
async function captureNative(): Promise<CapturedPhoto | null> {
  try {
    const photo = await Camera.getPhoto({
      source: CameraSource.Camera,
      resultType: CameraResultType.Uri,
      quality: 80,
      allowEditing: false,
      saveToGallery: false,
    })
    if (!photo.webPath) return null
    const format = photo.format || 'jpeg'
    return {
      id: nextPhotoId++,
      url: photo.webPath,
      webPath: photo.webPath,
      format,
      name: `取证照片-${nextPhotoId}.${format}`,
    }
  } catch {
    // 用户取消 / 权限拒绝 → getPhoto 抛异常，静默返回 null（上层不报错）。
    return null
  }
}

/** Web 降级采集（`<input capture>`，浏览器可验收）；用户取消 → null。 */
function captureViaFileInput(): Promise<CapturedPhoto | null> {
  return new Promise((resolve) => {
    const input = document.createElement('input')
    input.type = 'file'
    input.accept = 'image/*'
    // Capacitor Android WebView 下 `capture` 直接拉起系统相机；桌面浏览器走文件选择。
    input.setAttribute('capture', 'environment')
    input.addEventListener('change', () => {
      const file = input.files?.[0]
      if (!file) {
        resolve(null)
        return
      }
      const format = file.type.split('/')[1] || 'jpeg'
      resolve({ id: nextPhotoId++, url: URL.createObjectURL(file), file, format, name: file.name })
    })
    input.click()
  })
}

export function useInspectionPhotoCapture() {
  const supported = photoCaptureSupported()

  /** 打开相机（原生）/ 文件选择（Web）采集一张照片；用户取消返回 null。 */
  function capture(): Promise<CapturedPhoto | null> {
    return Capacitor.isNativePlatform() ? captureNative() : captureViaFileInput()
  }

  /** 释放照片预览资源（Web 降级的 object URL 需 revoke；原生 webPath 无需）。 */
  function releasePhoto(photo: CapturedPhoto): void {
    if (photo.file) URL.revokeObjectURL(photo.url)
  }

  return { supported, capture, releasePhoto }
}
