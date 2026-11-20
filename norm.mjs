import { all, api } from './rm.mjs'

const me = (await api('/users/current.json')).user
const today = new Date('2026-11-20T00:00:00+08:00')
const holidays = new Set(['2026-11-04']) // День народного единства (Wed)
const pad = (n) => String(n).padStart(2, '0')
const iso = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`

const entries = await all('/time_entries.json?user_id=' + me.id + '&from=2026-11-01&to=2026-11-30', 'time_entries')
const byDay = new Map()
for (const e of entries) byDay.set(e.spent_on, (byDay.get(e.spent_on) || 0) + Number(e.hours || 0))

const weekdays = []
for (let d = 1; d <= today.getDate(); d++) {
  const dt = new Date(today.getFullYear(), today.getMonth(), d)
  const wd = dt.getDay()
  if (wd === 0 || wd === 6) continue
  weekdays.push({ date: iso(dt), dow: ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'][wd], holiday: holidays.has(iso(dt)) })
}
const workdays = weekdays.filter((d) => !d.holiday)
const logged = entries.reduce((a, e) => a + Number(e.hours || 0), 0)

console.log('TODAY', iso(today))
console.log('WEEKDAYS_TO_TODAY', weekdays.length, '=', weekdays.length * 8, 'h')
console.log('HOLIDAYS', [...holidays].join(','))
console.log('WORKDAYS_EXCL_HOLIDAYS', workdays.length, '=', workdays.length * 8, 'h')
console.log('LOGGED', logged, 'h')
console.log('GAP_WEEKDAYS', weekdays.length * 8 - logged, 'h')
console.log('GAP_WORKDAYS', workdays.length * 8 - logged, 'h')
console.log('--- per day (hours logged):')
for (const d of weekdays) {
  const h = byDay.get(d.date) || 0
  console.log(`${d.date} ${d.dow}${d.holiday ? ' (holiday)' : ''}: ${h}h ${h === 0 ? '  <-- no entry' : ''}`)
}
const acts = (await api('/enumerations/time_entry_activities.json')).time_entry_activities
console.log('ACTIVITIES', acts.map((a) => `${a.id}:${a.name}`).join(', '))
