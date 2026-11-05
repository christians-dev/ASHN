import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { CardEntry } from '../../src/features/checkout/CardEntry'

afterEach(() => vi.restoreAllMocks())

async function fill(number: string, expiry: string, cvc: string) {
  if (number) await userEvent.type(screen.getByLabelText('Card number'), number)
  if (expiry) await userEvent.type(screen.getByLabelText('Expiry (MM/YY)'), expiry)
  if (cvc) await userEvent.type(screen.getByLabelText('Security code'), cvc)
  await userEvent.click(screen.getByRole('button', { name: 'Pay' }))
}

describe('CardEntry', () => {
  it('groups the number in fours and keeps only digits', async () => {
    render(<CardEntry submitLabel="Pay" onSubmit={vi.fn()} />)

    await userEvent.type(screen.getByLabelText('Card number'), '4242-4242abc42424242')

    expect(screen.getByLabelText('Card number')).toHaveValue('4242 4242 4242 4242')
  })

  it('emits clean values on submit', async () => {
    const onSubmit = vi.fn()
    render(<CardEntry submitLabel="Pay" onSubmit={onSubmit} />)

    await fill('4242424242424242', '12/30', '123')

    expect(onSubmit).toHaveBeenCalledWith({
      number: '4242424242424242',
      expMonth: 12,
      expYear: 2030,
      cvc: '123',
    })
  })

  it('checks the expiry format and rejects past dates', async () => {
    const onSubmit = vi.fn()
    render(<CardEntry submitLabel="Pay" onSubmit={onSubmit} />)

    await fill('4242424242424242', '13/30', '123')
    expect(screen.getByLabelText('Expiry (MM/YY)')).toHaveAccessibleDescription(
      'Enter the expiry date as MM/YY.',
    )

    await userEvent.clear(screen.getByLabelText('Expiry (MM/YY)'))
    await userEvent.type(screen.getByLabelText('Expiry (MM/YY)'), '01/20')
    await userEvent.click(screen.getByRole('button', { name: 'Pay' }))
    expect(screen.getByLabelText('Expiry (MM/YY)')).toHaveAccessibleDescription(
      'This card has expired.',
    )
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('needs a 3 or 4 digit security code and a full number', async () => {
    const onSubmit = vi.fn()
    render(<CardEntry submitLabel="Pay" onSubmit={onSubmit} />)

    await fill('4242', '12/30', '12')

    expect(screen.getByText('Enter the card number.')).toBeInTheDocument()
    expect(screen.getByText('Enter the 3 or 4 digit security code.')).toBeInTheDocument()
    expect(screen.getByLabelText('Security code')).toHaveValue('12')
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('never writes card details to browser storage', async () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem')
    render(<CardEntry submitLabel="Pay" onSubmit={vi.fn()} />)

    await fill('4242424242424242', '12/30', '123')

    expect(setItem).not.toHaveBeenCalled()
  })
})
