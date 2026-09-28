import { describeIamPasswordRejection } from '@nerv-iip/auth'

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

/**
 * IAM 发布的中文业务拒绝文案（`PlatformAdministratorProtection`、角色 / 用户应用服务）。
 * 只有命中这张表的才原样上屏：文案里带着管理员输入的名称，只看「含不含汉字」会把
 * `Role name '质检员' is already used.` 这类中英混合的句子放上屏。
 */
const KNOWN_IAM_REJECTIONS: readonly RegExp[] = [
  /^平台管理员账号不能停用。$/,
  /^平台管理员账号不能停用，也不能设置账号过期时间。$/,
  /^平台管理员角色的权限不能减少。$/,
  /^平台管理员角色必须保留默认组织的数据范围。$/,
  /^平台管理员在默认组织环境中必须保留平台管理员角色。$/,
  /^登录名「.+」已被使用。$/,
  /^邮箱「.+」已被使用。$/,
  /^角色名称「.+」已被使用。$/,
  /^请填写角色名称。$/,
  /^角色名称不能超过 \d+ 个字符。$/,
]

/**
 * 把身份与访问接口的失败转成管理员能看懂的一句话。
 *
 * generated client 在 `throwOnError` 下抛出的是解析后的响应体（普通对象，不是 `Error`），
 * 网关按 400 原样透传 IAM 的业务拒绝文案。已知的中文业务文案原样上屏；口令策略的英文文案
 * 按认证包的对照表译成中文；其余（英文文案、`iam-bad-request` 这类技术码）一律用中文兜底。
 */
export function toConsoleIamError(error: unknown, fallback = IAM_FALLBACK_MESSAGE) {
  if (error instanceof ConsoleIamError) {
    return error
  }

  return new ConsoleIamError(
    describeIamRejection(readMessage(error)) ?? fallback,
    readStatus(error),
  )
}

function describeIamRejection(message: string | undefined) {
  if (!message) {
    return undefined
  }

  if (KNOWN_IAM_REJECTIONS.some((pattern) => pattern.test(message))) {
    return message
  }

  return describeIamPasswordRejection(message)
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
