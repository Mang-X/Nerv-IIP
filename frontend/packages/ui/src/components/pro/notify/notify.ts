import { reactive } from 'vue'

/**
 * Pro — feedback store. Two distinct channels, aligned to traditional component
 * libraries (Arco / Ant):
 *   - message:      lightweight, top-center, single line, short-lived.
 *   - notification: richer card, top-right, title + description, longer-lived.
 * Self-contained (no vue-sonner dependency), fully token-themed.
 */
export type NotifyKind = 'info' | 'success' | 'warning' | 'error'
export type NotifyChannel = 'message' | 'notification'

export interface NotifyItem {
  id: number
  channel: NotifyChannel
  kind: NotifyKind
  title: string
  description?: string
  duration: number
}

export interface MessageOptions {
  duration?: number
}
export interface NotificationOptions {
  description?: string
  duration?: number
}

const state = reactive<{ items: NotifyItem[] }>({ items: [] })
let seq = 0

export function useNotifyStore() {
  return state
}

export function dismissNotify(id: number): void {
  const idx = state.items.findIndex((item) => item.id === id)
  if (idx !== -1) state.items.splice(idx, 1)
}

function add(item: Omit<NotifyItem, 'id'>): number {
  const id = ++seq
  state.items.push({ ...item, id })
  if (item.duration > 0 && typeof window !== 'undefined') {
    window.setTimeout(() => dismissNotify(id), item.duration)
  }
  return id
}

function messageFactory(kind: NotifyKind) {
  return (title: string, options: MessageOptions = {}): number =>
    add({ channel: 'message', kind, title, duration: options.duration ?? 2600 })
}
function notificationFactory(kind: NotifyKind) {
  return (title: string, options: NotificationOptions = {}): number =>
    add({
      channel: 'notification',
      kind,
      title,
      description: options.description,
      duration: options.duration ?? 4500,
    })
}

/** Lightweight, top-center status line. */
export const messagePro = {
  info: messageFactory('info'),
  success: messageFactory('success'),
  warning: messageFactory('warning'),
  error: messageFactory('error'),
}

/** Richer corner card with title + description. */
export const notificationPro = {
  info: notificationFactory('info'),
  success: notificationFactory('success'),
  warning: notificationFactory('warning'),
  error: notificationFactory('error'),
}
