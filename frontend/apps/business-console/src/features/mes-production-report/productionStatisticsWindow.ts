export function defaultProductionStatisticsWindow(now = new Date()) {
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 6)
  const end = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1)
  return { startUtc: start.toISOString(), endUtc: end.toISOString() }
}
