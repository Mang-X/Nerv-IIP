export interface DownloadGrantLike {
  downloadUrl?: string | null
  downloadHeaders?: Record<string, string> | null
}

export async function openDownloadGrantBlob(grant: DownloadGrantLike): Promise<void> {
  const downloadUrl = grant.downloadUrl?.trim()
  if (!downloadUrl) throw new Error('文件服务未返回可用的SOP查看链接。')

  const response = await fetch(downloadUrl, {
    headers: normalizeHeaders(grant.downloadHeaders),
  })
  if (!response.ok) throw new Error('无法下载SOP文件，请稍后重试。')

  const blobUrl = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = blobUrl
  link.target = '_blank'
  link.rel = 'noopener'
  document.body.appendChild(link)
  link.click()
  link.remove()
  window.setTimeout(() => URL.revokeObjectURL(blobUrl), 60_000)
}

function normalizeHeaders(headers?: Record<string, string> | null): HeadersInit {
  if (!headers) return {}
  return Object.fromEntries(
    Object.entries(headers).filter(([key, value]) => key.trim() && value.trim()),
  )
}
