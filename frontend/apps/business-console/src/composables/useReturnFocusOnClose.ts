/**
 * 由调用方控制开关、没有 `NvDialogTrigger` 的弹窗：关闭后把焦点交还给打开前聚焦的元素
 * （通常是「新建」按钮）。reka 默认只还给 trigger，没有 trigger 时焦点会落到 body。
 *
 * 在弹窗 setup 里调用（每次打开都是新实例，此刻的焦点就是打开它的入口），把返回的处理函数绑到
 * `NvDialogContent` 的 `@close-auto-focus`。入口已经不在页面上时（如选择器面板已关）不接管，走默认。
 */
export function useReturnFocusOnClose() {
  const opener = document.activeElement
  return (event: Event) => {
    if (opener instanceof HTMLElement && opener.isConnected) {
      event.preventDefault()
      opener.focus()
    }
  }
}
