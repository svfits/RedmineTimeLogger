import { all, api } from './rm.mjs'

const me = (await api('/users/current.json')).user
const from = '2026-11-01'
const to = '2026-11-30'

const entries = await all(`/time_entries.json?user_id=${me.id}&from=${from}&to=${to}`, 'time_entries')
const sum = entries.reduce((a, e) => a + Number(e.hours || 0), 0)

console.log('USER', JSON.stringify({ id: me.id, login: me.login, name: `${me.firstname} ${me.lastname}` }))
console.log('RANGE', from, to)
console.log('ENTRIES_COUNT', entries.length)
console.log('ENTRIES_SUM', sum)
for (const e of entries) {
  console.log(
    [
      e.spent_on,
      e.hours,
      'issue#' + (e.issue?.id ?? '-'),
      'proj:' + (e.project?.name ?? '-'),
      'act:' + (e.activity?.name ?? '-'),
      'user:' + (e.user?.name ?? '-'),
      'cmt:' + (e.comments ?? ''),
    ].join(' | '),
  )
}

const statuses = (await api('/issue_statuses.json')).issue_statuses
console.log('STATUSES', statuses.map((s) => `${s.id}:${s.name}`).join(', '))
