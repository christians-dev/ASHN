import { type FormEvent, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { api, unwrap } from '../../api/client'
import { type FieldErrors, fieldErrorsFrom, formMessageFrom } from '../../shared/auth/formErrors'
import { Button } from '../../shared/ui/Button'
import { Alert } from '../../shared/ui/Feedback'
import { TextField } from '../../shared/ui/TextField'

export function ForgotPasswordPage() {
  const [sent, setSent] = useState(false)
  const [error, setError] = useState<string>()

  async function send(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const email = String(new FormData(event.currentTarget).get('email') ?? '').trim()
    if (!email) return setError('Enter your email address.')

    try {
      await unwrap(api.POST('/api/auth/password/forgot', { body: { email } }))
      setSent(true)
    } catch (e) {
      setError(formMessageFrom(e))
    }
  }

  return (
    <>
      <h1>Reset your password</h1>
      {sent ? (
        <Alert tone="success">
          If an account uses that address, we’ve emailed it a link. The link works once, for 1 hour.
        </Alert>
      ) : (
        <form onSubmit={send} noValidate>
          <TextField label="Email" name="email" type="email" autoComplete="email" error={error} />
          <Button type="submit">Email me a link</Button>
        </form>
      )}
    </>
  )
}

export function ResetPasswordPage() {
  const [params] = useSearchParams()
  const [errors, setErrors] = useState<FieldErrors>({})
  const [done, setDone] = useState(false)
  const [message, setMessage] = useState<string>()

  async function reset(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const newPassword = String(new FormData(event.currentTarget).get('password') ?? '')
    if (newPassword.length < 8) return setErrors({ password: 'Use at least 8 characters.' })

    try {
      await unwrap(
        api.POST('/api/auth/password/reset', {
          body: { email: params.get('email') ?? '', token: params.get('token') ?? '', newPassword },
        }),
      )
      setDone(true)
    } catch (e) {
      const fields = fieldErrorsFrom(e)
      setErrors(fields)
      setMessage(fields.password ? undefined : formMessageFrom(e))
    }
  }

  if (done) {
    return (
      <Alert tone="success">
        Your password has been changed. <Link to="/sign-in">Sign in</Link>
      </Alert>
    )
  }

  return (
    <>
      <h1>Choose a new password</h1>
      {message && (
        <Alert tone="error">
          {message} <Link to="/forgot-password">Request a new link</Link>
        </Alert>
      )}
      <form onSubmit={reset} noValidate>
        <TextField
          label="New password"
          name="password"
          type="password"
          autoComplete="new-password"
          error={errors.password}
        />
        <Button type="submit">Change password</Button>
      </form>
    </>
  )
}
