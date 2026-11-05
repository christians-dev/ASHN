// SC-006: 200 shoppers browsing at once without visible slowdown (p95 < 2 s, errors < 1%).
// Runs in the grafana/k6 Docker image against the container from the Dockerfile; see README "Load test".
import http from 'k6/http'
import { check, sleep } from 'k6'

const BASE = __ENV.BASE_URL || 'http://localhost:8080'

export const options = {
  scenarios: {
    browsing: {
      executor: 'ramping-vus',
      stages: [
        { duration: '30s', target: 200 }, // ramp up
        { duration: __ENV.DURATION || '5m', target: 200 }, // hold 200 shoppers
        { duration: '15s', target: 0 },
      ],
    },
  },
  thresholds: {
    http_req_duration: ['p(95)<2000'],
    http_req_failed: ['rate<0.01'],
  },
}

const sorts = ['newest', 'price_asc', 'price_desc', 'rating']

export default function () {
  const home = http.get(`${BASE}/api/home`, { tags: { page: 'home' } })
  check(home, { 'home 200': (r) => r.status === 200 })
  sleep(1 + Math.random() * 2) // a shopper looks around before clicking

  const page = 1 + Math.floor(Math.random() * 40)
  const sort = sorts[Math.floor(Math.random() * sorts.length)]
  const list = http.get(`${BASE}/api/products?page=${page}&sort=${sort}`, { tags: { page: 'list' } })
  check(list, { 'list 200': (r) => r.status === 200 })
  sleep(1 + Math.random() * 2)

  const items = list.status === 200 ? list.json('items') : []
  if (items.length > 0) {
    const slug = items[Math.floor(Math.random() * items.length)].slug
    const detail = http.get(`${BASE}/api/products/${slug}`, { tags: { page: 'detail' } })
    check(detail, { 'detail 200': (r) => r.status === 200 })
  }
  sleep(1 + Math.random() * 3)
}
