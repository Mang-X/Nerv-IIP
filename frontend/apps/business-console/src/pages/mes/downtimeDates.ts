export function toDateInput(value?: string, dayOffset = 0) {
  if (!value) return null
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return null
  date.setDate(date.getDate() + dayOffset)
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 10)
}

export function toInclusiveEndDateInput(value?: string) {
  const date = value ? new Date(value) : null
  const isExclusiveDayBoundary =
    date?.getHours() === 0 &&
    date.getMinutes() === 0 &&
    date.getSeconds() === 0 &&
    date.getMilliseconds() === 0
  return toDateInput(value, isExclusiveDayBoundary ? -1 : 0)
}

export function fromDateInput(value: string, dayOffset: number) {
  const [year, month, day] = value.split('-').map(Number)
  return new Date(year!, month! - 1, day! + dayOffset).toISOString()
}

export function toLocalDateTimeInput(date: Date) {
  const offset = date.getTimezoneOffset() * 60_000
  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}
