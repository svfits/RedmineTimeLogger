import { all, api } from './rm.mjs'

const me = (await api('/users/current.json')).user
const TARGET = new Set(['3:In-Progress', '15:On review', '12:On approve'])
const WANT = new Map([
  [3, 'In-Progress'],
  [15, 'On review'],
  [12, 'On approve'],
])
const norm = (s) => String(s || '').toLowerCase().replace(/[\s_-]+/g, '')

// all open issues assigned to the current user
const issues = await all(`/issues.json?assigned_to_id=${me.id}&status_id=open&sort=updated_on:desc`, 'issues')

const picked = issues.filter((i) => WANT.has(i.status?.id) || [...WANT.values()].some((n) => norm(n) === norm(i.status?.name)))

console.log('OPEN_ASSIGNED', issues.length)
console.log('MATCHED', picked.length)
for (const i of picked) {
  const cf = (i.custom_fields || [])
    .filter((f) => f.value !== null && f.value !== '' && f.value !== undefined)
    .map((f) => `${f.name}=${Array.isArray(f.value) ? f.value.join('/') : f.value}`)
  console.log(
    [
      `#${i.id}`,
      `[${i.status.name}]`,
      `proj=${i.project?.name}`,
      `tracker=${i.tracker?.name}`,
      `prio=${i.priority?.name}`,
      `assignee=${i.assigned_to?.name ?? '-'}`,
      `est=${i.estimated_hours ?? '-'}`,
      `spent=${i.spent_hours ?? 0}`,
      `done=${i.done_ratio}%`,
      `upd=${i.updated_on}`,
      `start=${i.start_date ?? '-'}`,
      `due=${i.due_date ?? '-'}`,
      `subj=${i.subject}`,
      cf.length ? `cf:${cf.join(';')}` : '',
    ].join(' | '),
  )
}
