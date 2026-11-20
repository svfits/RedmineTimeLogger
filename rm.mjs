// Small Redmine REST helper for this workspace (read-only by default).
import { readFileSync } from 'node:fs'
import { homedir } from 'node:os'
import { join } from 'node:path'

const BASE = (process.env.REDMINE_URL || 'https://rm.burworld.ru').replace(/\/+$/, '')
const KEY = (() => {
  const direct = (process.env.REDMINE_API_KEY || '').trim()
  if (direct) return direct
  return readFileSync(join(homedir(), '.dsh', 'redmine-api-key'), 'utf8').trim().split(/\r?\n/)[0].trim()
})()

export async function api(path, { method = 'GET', body } = {}) {
  const res = await fetch(`${BASE}${path}`, {
    method,
    headers: {
      'X-Redmine-API-Key': KEY,
      Accept: 'application/json',
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  })
  const text = await res.text()
  if (!res.ok) throw new Error(`${method} ${path} -> HTTP ${res.status}: ${text.slice(0, 800)}`)
  return text ? JSON.parse(text) : null
}

export async function all(path, key) {
  const out = []
  let offset = 0
  for (;;) {
    const sep = path.includes('?') ? '&' : '?'
    const page = await api(`${path}${sep}limit=100&offset=${offset}`)
    const items = page[key] || []
    out.push(...items)
    const total = page.total_count ?? out.length
    offset += items.length
    if (!items.length || offset >= total) break
  }
  return out
}
