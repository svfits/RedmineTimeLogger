import { api } from './rm.mjs'

const ids = process.argv.slice(2).map(Number)
for (const id of ids) {
  const i = (await api(`/issues/${id}.json?include=journals,relations,children,time_entries`)).issue
  console.log(`===== #${i.id} [${i.status.name}] ${i.subject}`)
  console.log(`project=${i.project?.name} tracker=${i.tracker?.name} prio=${i.priority?.name}`)
  console.log(`author=${i.author?.name} assignee=${i.assigned_to?.name ?? '-'} created=${i.created_on} updated=${i.updated_on}`)
  console.log(`start=${i.start_date} due=${i.due_date ?? '-'} est=${i.estimated_hours ?? '-'} spent=${i.spent_hours}`)
  const d = (i.description || '').replace(/\r/g, '').trim()
  console.log('description:', d.length > 900 ? d.slice(0, 900) + '…' : d)
  const te = i.time_entries || []
  console.log('time_entries:', te.map((t) => `${t.spent_on}:${t.hours}h/${t.user?.name ?? ''}`).join(', ') || '-')
  const j = (i.journals || []).filter((x) => x.notes).slice(-3)
  for (const x of j) console.log(`  note ${x.created_on} ${x.user?.name}: ${String(x.notes).replace(/\s+/g, ' ').slice(0, 300)}`)
  console.log()
}
