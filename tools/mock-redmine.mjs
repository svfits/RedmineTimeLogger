// Локальная "заглушка" Redmine REST API — только для проверки приложения
// без обращения к боевому серверу: ничего не пишет в настоящий Redmine.
//
// Запуск:  node tools/mock-redmine.mjs [порт]
// Проверка: RedmineTimeLogger --url http://127.0.0.1:8787 ...

import http from 'node:http'

const port = Number(process.argv[2] || process.env.PORT || 8787)

const entries = [
  ['2026-11-16', 8, 24819],
  ['2026-11-17', 8, 26114],
  ['2026-11-18', 8, 25778],
  ['2026-11-20', 8, 22619],
  ['2026-11-20', 8, 12347],
].map(([spent_on, hours, issue], index) => ({
  id: 9000 + index,
  project: { id: 1, name: 'New_Compass_RU' },
  issue: { id: issue },
  user: { id: 116, name: 'Dmitry Afanasyev' },
  activity: { id: 7, name: 'Разработка' },
  hours,
  comments: '',
  spent_on,
}))

const issues = [
  { id: 26422, subject: 'Добавить атрибут tool name в массиве в ответ запроса на запросе /ActualUndoRedo/GetActualTrajectory', status: { id: 3, name: 'In-Progress' }, spent_hours: 0, updated_on: '2026-11-20T05:16:57Z', start_date: '2026-11-19', project: { id: 1, name: 'New_Compass_RU' }, tracker: { id: 1, name: 'Default' } },
  { id: 12347, subject: 'Пересчет координат объектов при изменении EPSG ID в свойствах Месторождения', status: { id: 15, name: 'On review' }, spent_hours: 8, updated_on: '2026-11-20T05:39:39Z', project: { id: 1, name: 'New_Compass_RU' }, tracker: { id: 1, name: 'Default' } },
  { id: 22619, subject: 'Удаление объекта и целей с ним связанных', status: { id: 12, name: 'On approve' }, spent_hours: 8, updated_on: '2026-11-16T13:21:07Z', project: { id: 1, name: 'New_Compass_RU' }, tracker: { id: 1, name: 'Default' } },
  { id: 13244, subject: 'Создать эндпоинт для объекта WELL', status: { id: 12, name: 'On approve' }, spent_hours: 0, updated_on: '2025-11-17T07:02:45Z', project: { id: 2, name: 'EDM API CORE' }, tracker: { id: 1, name: 'Default' } },
]

const activities = [
  { id: 6, name: 'Совещание' },
  { id: 7, name: 'Разработка' },
  { id: 8, name: 'Исследование' },
]

const statuses = [
  { id: 1, name: 'Open' },
  { id: 3, name: 'In-Progress' },
  { id: 12, name: 'On approve' },
  { id: 15, name: 'On review' },
  { id: 5, name: 'Closed' },
]

function send(res, code, payload) {
  const body = JSON.stringify(payload)
  res.writeHead(code, { 'Content-Type': 'application/json; charset=utf-8', 'Content-Length': Buffer.byteLength(body) })
  res.end(body)
}

const server = http.createServer((req, res) => {
  const url = new URL(req.url, `http://127.0.0.1:${port}`)
  const key = req.headers['x-redmine-api-key']

  if (!key) {
    console.log(`  ! ${req.method} ${url.pathname} — нет X-Redmine-API-Key`)
    return send(res, 401, {})
  }

  console.log(`  ${req.method} ${url.pathname}${url.search}`)

  if (req.method === 'GET' && url.pathname === '/users/current.json') {
    return send(res, 200, { user: { id: 116, login: 'B002441', firstname: 'Dmitry', lastname: 'Afanasyev', mail: 'dmitry.afanasyev@burservis.ru' } })
  }

  if (req.method === 'GET' && url.pathname === '/time_entries.json') {
    return send(res, 200, { time_entries: entries, total_count: entries.length, offset: 0, limit: 100 })
  }

  if (req.method === 'GET' && url.pathname === '/issues.json') {
    const statusFilter = url.searchParams.get('status_id')
    const list = statusFilter === 'open' ? issues : issues
    return send(res, 200, { issues: list, total_count: list.length, offset: 0, limit: 100 })
  }

  if (req.method === 'GET' && url.pathname === '/issue_statuses.json') {
    return send(res, 200, { issue_statuses: statuses })
  }

  if (req.method === 'GET' && url.pathname === '/enumerations/time_entry_activities.json') {
    return send(res, 200, { time_entry_activities: activities })
  }

  const issueMatch = url.pathname.match(/^\/issues\/(\d+)\.json$/)
  if (req.method === 'GET' && issueMatch) {
    const found = issues.find((i) => i.id === Number(issueMatch[1]))
    if (!found) return send(res, 404, {})
    return send(res, 200, { issue: found })
  }

  if (req.method === 'POST' && url.pathname === '/time_entries.json') {
    let raw = ''
    req.on('data', (chunk) => (raw += chunk))
    req.on('end', () => {
      let parsed
      try {
        parsed = JSON.parse(raw)
      } catch {
        return send(res, 400, {})
      }

      const te = parsed?.time_entry ?? {}
      console.log('  >>> получено тело:', JSON.stringify(te))

      if (!te.issue_id || !te.hours || !te.activity_id) {
        return send(res, 422, { errors: ['Issue is invalid', 'Hours is invalid', 'Activity is not included in the list'] })
      }

      const created = {
        id: 123456,
        project: { id: 1, name: 'New_Compass_RU' },
        issue: { id: te.issue_id },
        user: { id: 116, name: 'Dmitry Afanasyev' },
        activity: activities.find((a) => a.id === te.activity_id) ?? { id: te.activity_id, name: '—' },
        hours: te.hours,
        comments: te.comments ?? '',
        spent_on: te.spent_on,
      }

      return send(res, 201, { time_entry: created })
    })
    return
  }

  send(res, 404, { errors: ['Not found (mock)'] })
})

server.listen(port, '127.0.0.1', () => {
  console.log(`mock Redmine слушает http://127.0.0.1:${port}`)
})
