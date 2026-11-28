import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'
import NotificationTaskList from './NotificationTaskList.vue'

it('heads each task with its matching message title or a short task identity', () => {
  const taskId = '5c5dac65-02be-442f-b358-d6cc7200ee61'
  const secondTaskId = '5c5dac65-02be-442f-b358-d6cc7200ee62'
  const wrapper = mount(NotificationTaskList, {
    props: {
      messageTitles: new Map([['message-1', '检查盘点结果']]),
      tasks: [
        { taskId: 'task-1', messageId: 'message-1', taskType: 'review' },
        { taskId, messageId: 'unmatched', taskType: 'review', actionRef: taskId },
        { taskId: secondTaskId, messageId: 'unmatched', taskType: 'review' },
      ],
    },
  })

  const tasks = wrapper.findAll('li')
  expect(tasks[0]!.get('p').text()).toBe('检查盘点结果')
  expect(tasks[0]!.text()).toContain('复核')
  expect(tasks[1]!.get('p').text()).toBe('复核 · 7200ee61')
  expect(tasks[1]!.text()).toContain(taskId)
  expect(tasks[2]!.get('p').text()).toBe('复核 · 7200ee62')
})
