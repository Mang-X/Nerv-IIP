/** 用户是否偏好减少动效(所有 JS 动效在此守卫下降级为直接生效、无过渡)。 */
export function prefersReducedMotion(): boolean {
  return (
    typeof window !== 'undefined' &&
    typeof window.matchMedia === 'function' &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches
  )
}

/** 一次性动画类:加类前强制回流以重启动画,animationend 后自动移除(可重复触发)。 */
export function playOnceClass(el: HTMLElement, cls: string): void {
  el.classList.remove(cls)
  void el.offsetWidth // 强制回流,确保下次加类能重新触发关键帧
  el.classList.add(cls)
  const done = () => {
    el.classList.remove(cls)
    el.removeEventListener('animationend', done)
  }
  el.addEventListener('animationend', done)
}
