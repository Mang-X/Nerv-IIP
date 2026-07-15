import { Capacitor } from '@capacitor/core'

/**
 * 点检拍照取证 —— 能力门控的取证照片采集（MAN-458 / #812）。
 *
 * **预留说明**：后端点检测量值当前无图片字段 / 附件契约（Maintenance `recordInspection`
 * 的 measurement 仅 characteristicCode / measuredValue / uomCode / 上下限）。因此照片
 * 仅**本地持有**（缩略图预览 + 可删除），提交时暂不上送 —— 前端入口先行预留，
 * 后端图片字段 + FileStorage 关联 + gateway facade 由 follow-up issue 跟进，落地后
 * 再把此处替换为真正上送 + 原生 `@capacitor/camera`（EXIF / 质量控制）。
 *
 * 采集走 Web 标准 `<input type="file" accept="image/*" capture="environment">`：
 * 在 Capacitor Android WebView 中会拉起系统相机；桌面浏览器走文件选择。不引入新的
 * 原生依赖，保证门禁全绿与走查可演示。
 */
export interface CapturedPhoto {
  /** 客户端行内稳定 id（用于 v-for key / 删除定位）。 */
  id: number
  /** 预览用 object URL（移除时须 revoke，防内存泄漏）。 */
  url: string
  /** 原始文件（后端契约就绪后据此上送）。 */
  file: File
  /** 文件名（缩略图 alt / 无障碍）。 */
  name: string
}

let nextPhotoId = 1

/**
 * 相机能力是否可用（不可用时上层隐藏拍照入口，符合 #812「相机能力不可用时隐藏入口」）。
 * - 原生 PDA：Capacitor WebView 一定具备相机 → true。
 * - Web：具备 `MediaDevices.getUserMedia` 摄像头能力才显示（桌面无摄像头 → 隐藏）。
 */
export function photoCaptureSupported(): boolean {
  if (typeof navigator === 'undefined' || typeof document === 'undefined') return false
  if (Capacitor.isNativePlatform()) return true
  return Boolean(navigator.mediaDevices?.getUserMedia)
}

export function useInspectionPhotoCapture() {
  const supported = photoCaptureSupported()

  /** 打开相机 / 文件选择，返回采集到的照片；用户取消返回 null。 */
  function capture(): Promise<CapturedPhoto | null> {
    return new Promise((resolve) => {
      const input = document.createElement('input')
      input.type = 'file'
      input.accept = 'image/*'
      // Capacitor Android WebView 下 `capture` 使系统直接拉起后置相机。
      input.setAttribute('capture', 'environment')
      input.addEventListener('change', () => {
        const file = input.files?.[0]
        if (!file) {
          resolve(null)
          return
        }
        resolve({ id: nextPhotoId++, url: URL.createObjectURL(file), file, name: file.name })
      })
      input.click()
    })
  }

  /** 释放照片预览 URL（移除照片 / 清空表单时调用，防 object URL 泄漏）。 */
  function releasePhoto(photo: CapturedPhoto): void {
    URL.revokeObjectURL(photo.url)
  }

  return { supported, capture, releasePhoto }
}
