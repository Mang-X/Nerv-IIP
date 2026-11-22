export const vReveal = {
  mounted(el: HTMLElement, binding: { value?: number }) {
    if (
      typeof window === 'undefined' ||
      window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ||
      !('IntersectionObserver' in window)
    ) {
      return
    }
    const delay = (Number(binding.value) || 0) * 45
    el.style.setProperty('--reveal-delay', `${delay}ms`)
    el.classList.add('reveal-init')
    const io = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            el.classList.add('reveal-in')
            io.unobserve(el)
          }
        }
      },
      { threshold: 0.1, rootMargin: '0px 0px -8% 0px' },
    )
    io.observe(el)
  },
}
