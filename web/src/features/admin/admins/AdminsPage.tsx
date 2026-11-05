import { useQuery } from '@tanstack/react-query'
import { type FormEvent, useState } from 'react'
import { api, unwrap } from '../../../api/client'
import { useSession } from '../../../shared/auth/useSession'
import { Button } from '../../../shared/ui/Button'
import { Alert } from '../../../shared/ui/Feedback'
import { Spinner } from '../../../shared/ui/Spinner'
import { TextField } from '../../../shared/ui/TextField'
import styles from '../admin.module.css'
import { useAdminAction } from '../adminQueries'

/**
 * Who can run the store (FR-014). Admin access is given to an existing account by email and can be taken back,
 * except from the last admin. Changes reach the person's open sessions within a minute.
 */
export function AdminsPage() {
  const session = useSession()
  const admins = useQuery({
    queryKey: ['admin', 'admins'],
    queryFn: () => unwrap(api.GET('/api/admin/admins')),
  })
  const { run, error, busy } = useAdminAction()
  const [notice, setNotice] = useState<string>()

  async function grant(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    const email = String(new FormData(form).get('email') ?? '').trim()
    setNotice(undefined)
    const granted = await run(() => unwrap(api.POST('/api/admin/admins', { body: { email } })))
    if (granted.ok) {
      form.reset()
      setNotice(`${email} is now an admin. They'll see the admin pages within a minute.`)
    }
  }

  async function revoke(userId: string, email: string, isYou: boolean) {
    const question = isYou
      ? 'Remove your own admin access? You will lose the admin pages within a minute.'
      : `Remove admin access from ${email}?`
    if (!window.confirm(question)) return
    setNotice(undefined)
    const revoked = await run(() =>
      unwrap(api.DELETE('/api/admin/admins/{userId}', { params: { path: { userId } } })),
    )
    if (revoked.ok) setNotice(`${email} is no longer an admin.`)
  }

  if (admins.isPending) return <Spinner />
  if (admins.isError) return <Alert tone="error">Admins couldn’t be loaded.</Alert>

  return (
    <>
      <h1>Admins</h1>
      {notice && <Alert tone="success">{notice}</Alert>}
      {error && <Alert tone="error">{error}</Alert>}

      <table className={styles.table}>
        <thead>
          <tr>
            <th scope="col">Name</th>
            <th scope="col">Email</th>
            <th scope="col" />
          </tr>
        </thead>
        <tbody>
          {admins.data.map((a) => {
            const isYou = a.userId === session.data?.userId
            return (
              <tr key={a.userId}>
                <td>
                  {a.displayName}
                  {isYou && ' (you)'}
                </td>
                <td>{a.email}</td>
                <td>
                  <Button
                    variant="secondary"
                    disabled={busy}
                    onClick={() => revoke(a.userId, a.email, isYou)}
                    aria-label={`Remove admin access from ${a.email}`}
                  >
                    Remove admin
                  </Button>
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>

      <section className={styles.section} aria-labelledby="grant-heading">
        <h2 id="grant-heading">Add an admin</h2>
        <p>They need an Ashen account first; ask them to register, then enter their email here.</p>
        <form onSubmit={grant} className={styles.inline}>
          <TextField label="Email" name="email" type="email" autoComplete="off" required />
          <div>
            <Button type="submit" disabled={busy}>
              Make admin
            </Button>
          </div>
        </form>
      </section>
    </>
  )
}
