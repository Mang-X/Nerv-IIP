export class ConsoleIamError extends Error {
  constructor(
    message: string,
    readonly status?: number,
  ) {
    super(message)
    this.name = 'ConsoleIamError'
  }
}

const IAM_FALLBACK_MESSAGE = '操作未完成，请稍后重试。'
const CHINESE_TEXT = /[一-龥]/

/**
 * 把身份与访问接口的失败转成管理员能看懂的一句话。
 *
 * generated client 在 `throwOnError` 下抛出的是解析后的响应体（普通对象，不是 `Error`），
 * 网关按 400 原样透传 IAM 的业务拒绝文案（如「平台管理员账号不能停用。」）。
 * 中文业务文案原样上屏；英文文案与 `iam-bad-request` 这类技术码不上屏，改用中文兜底。
 */
export function toConsoleIamError(error: unknown, fallback = IAM_FALLBACK_MESSAGE) {
  if (error instanceof ConsoleIamError) {
    return error
  }

  const message = readMessage(error)
  return new ConsoleIamError(
    message && CHINESE_TEXT.test(message) ? message : fallback,
    readStatus(error),
  )
}

function readMessage(error: unknown) {
  if (typeof error !== 'object' || error === null || !('message' in error)) {
    return undefined
  }

  const { message } = error
  return typeof message === 'string' ? message.trim() : undefined
}

function readStatus(error: unknown) {
  if (typeof error === 'object' && error !== null && 'status' in error) {
    return typeof error.status === 'number' ? error.status : undefined
  }

  return undefined
}
