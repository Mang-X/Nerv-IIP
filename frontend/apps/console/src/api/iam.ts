export class ConsoleIamError extends Error {
  constructor(
    message: string,
    readonly status?: number,
  ) {
    super(message)
    this.name = 'ConsoleIamError'
  }
}

export function toConsoleIamError(error: unknown, fallback = 'Unable to complete IAM request.') {
  if (error instanceof ConsoleIamError) {
    return error
  }

  if (error instanceof Error) {
    return new ConsoleIamError(error.message, getErrorStatus(error))
  }

  return new ConsoleIamError(fallback)
}

function getErrorStatus(error: Error) {
  if ('status' in error && typeof error.status === 'number') {
    return error.status
  }

  return undefined
}
