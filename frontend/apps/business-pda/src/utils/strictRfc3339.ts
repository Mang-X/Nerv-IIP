const OFFSET_DATE_TIME =
  /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d{1,9})?(Z|[+-]\d{2}:\d{2})$/

function isLeapYear(year: number) {
  return year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0)
}

export function isStrictRfc3339DateTime(value: unknown): value is string {
  if (typeof value !== 'string') return false
  const match = OFFSET_DATE_TIME.exec(value)
  if (!match) return false

  const [, yearText, monthText, dayText, hourText, minuteText, secondText, offset] = match
  const year = Number(yearText)
  const month = Number(monthText)
  const day = Number(dayText)
  const hour = Number(hourText)
  const minute = Number(minuteText)
  const second = Number(secondText)
  const daysInMonth = [31, isLeapYear(year) ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]

  if (
    year < 1 ||
    month < 1 ||
    month > 12 ||
    day < 1 ||
    day > daysInMonth[month - 1]! ||
    hour > 23 ||
    minute > 59 ||
    second > 59
  ) {
    return false
  }

  if (offset !== 'Z') {
    const offsetHour = Number(offset.slice(1, 3))
    const offsetMinute = Number(offset.slice(4, 6))
    if (offsetHour > 23 || offsetMinute > 59) return false
  }

  return true
}
