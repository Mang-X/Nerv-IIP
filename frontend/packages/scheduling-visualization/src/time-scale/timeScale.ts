export type SchedulingZoom = 'day' | 'week' | 'month'

export interface CreateTimeScaleOptions {
  start: string
  end: string
  width: number
  zoom: SchedulingZoom
}

export interface TimeScaleTick {
  date: string
  x: number
  label: string
}

export interface TimeScale {
  start: Date
  end: Date
  width: number
  zoom: SchedulingZoom
  ticks: TimeScaleTick[]
  dateToX(date: string): number
  xToDate(x: number): Date
}

const dayInMilliseconds = 24 * 60 * 60 * 1000

const tickSteps: Record<Exclude<SchedulingZoom, 'month'>, number> = {
  day: 1,
  week: 7,
}

const dayLabelFormatter = new Intl.DateTimeFormat('en-US', {
  month: 'short',
  day: 'numeric',
  timeZone: 'UTC',
})

const monthLabelFormatter = new Intl.DateTimeFormat('en-US', {
  month: 'short',
  year: 'numeric',
  timeZone: 'UTC',
})

function getIsoWeek(date: Date): number {
  const workingDate = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate()))
  const day = workingDate.getUTCDay() || 7
  workingDate.setUTCDate(workingDate.getUTCDate() + 4 - day)
  const yearStart = new Date(Date.UTC(workingDate.getUTCFullYear(), 0, 1))

  return Math.ceil(((workingDate.getTime() - yearStart.getTime()) / dayInMilliseconds + 1) / 7)
}

function formatTickLabel(date: Date, zoom: SchedulingZoom): string {
  if (zoom === 'week') {
    return `W${getIsoWeek(date)}`
  }

  if (zoom === 'month') {
    return monthLabelFormatter.format(date)
  }

  return dayLabelFormatter.format(date)
}

function nextTickDate(date: Date, zoom: SchedulingZoom): Date {
  if (zoom === 'month') {
    return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 1))
  }

  return new Date(date.getTime() + tickSteps[zoom] * dayInMilliseconds)
}

export function createTimeScale(options: CreateTimeScaleOptions): TimeScale {
  const start = new Date(options.start)
  const end = new Date(options.end)
  const startMs = start.getTime()
  const endMs = end.getTime()
  const duration = Math.max(endMs - startMs, 1)

  function dateToX(date: string): number {
    const value = new Date(date).getTime()
    return Math.round(((value - startMs) / duration) * options.width)
  }

  function xToDate(x: number): Date {
    const clampedX = Math.min(Math.max(x, 0), options.width)
    return new Date(startMs + (clampedX / options.width) * duration)
  }

  const ticks: TimeScaleTick[] = []
  for (let date = new Date(startMs); date.getTime() <= endMs; date = nextTickDate(date, options.zoom)) {
    ticks.push({
      date: date.toISOString(),
      x: dateToX(date.toISOString()),
      label: formatTickLabel(date, options.zoom),
    })
  }

  return {
    start,
    end,
    width: options.width,
    zoom: options.zoom,
    ticks,
    dateToX,
    xToDate,
  }
}
