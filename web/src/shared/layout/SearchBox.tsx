import type { FormEvent } from 'react'
import { useNavigate } from 'react-router'

export function SearchBox() {
  const navigate = useNavigate()

  function search(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const q = String(new FormData(event.currentTarget).get('q') ?? '').trim()
    if (q) navigate(`/search?q=${encodeURIComponent(q)}`)
  }

  return (
    <form role="search" onSubmit={search}>
      <label htmlFor="site-search" className="visually-hidden">
        Search products
      </label>
      <input id="site-search" name="q" type="search" placeholder="Search" />
    </form>
  )
}
