import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { ProductOption, Variant } from '../../src/features/catalog/catalogQueries'
import { VariantSelector } from '../../src/features/catalog/components/VariantSelector'

function variant(sku: string, optionValues: Record<string, string>, isAvailable = true): Variant {
  return { id: sku, sku, optionValues, price: { amount: 10, currency: 'USD' }, isAvailable }
}

describe('VariantSelector', () => {
  it('reports the variant once a single option is chosen', async () => {
    const options: ProductOption[] = [{ name: 'Size', values: ['S', 'M'] }]
    const variants = [variant('S', { Size: 'S' }), variant('M', { Size: 'M' }, false)]
    const onChange = vi.fn()
    render(<VariantSelector options={options} variants={variants} onChange={onChange} />)

    expect(screen.getByRole('radio', { name: 'M' })).toBeDisabled()
    await userEvent.click(screen.getByRole('radio', { name: 'S' }))

    expect(onChange).toHaveBeenLastCalledWith(variants[0])
  })

  it('disables combinations that are sold out or do not exist', async () => {
    const options: ProductOption[] = [
      { name: 'Size', values: ['S', 'M', 'L'] },
      { name: 'Colour', values: ['Black', 'Grey'] },
    ]
    const variants = [
      variant('S-BLK', { Size: 'S', Colour: 'Black' }),
      variant('M-BLK', { Size: 'M', Colour: 'Black' }, false), // sold out
      variant('L-GRY', { Size: 'L', Colour: 'Grey' }),
    ]
    const onChange = vi.fn()
    render(<VariantSelector options={options} variants={variants} onChange={onChange} />)

    expect(screen.getByRole('radio', { name: 'M' })).toBeDisabled()

    await userEvent.click(screen.getByRole('radio', { name: 'S' }))
    expect(screen.getByRole('radio', { name: 'Grey' })).toBeDisabled() // S/Grey does not exist
    expect(onChange).toHaveBeenLastCalledWith(null) // colour not chosen yet

    await userEvent.click(screen.getByRole('radio', { name: 'Black' }))
    expect(onChange).toHaveBeenLastCalledWith(variants[0])
  })

  it('handles three options and drops later picks that no longer fit', async () => {
    const options: ProductOption[] = [
      { name: 'Size', values: ['9', '10'] },
      { name: 'Colour', values: ['Grey'] },
      { name: 'Width', values: ['Regular', 'Wide'] },
    ]
    const variants = [
      variant('9-R', { Size: '9', Colour: 'Grey', Width: 'Regular' }),
      variant('9-W', { Size: '9', Colour: 'Grey', Width: 'Wide' }),
      variant('10-R', { Size: '10', Colour: 'Grey', Width: 'Regular' }),
    ]
    const onChange = vi.fn()
    render(<VariantSelector options={options} variants={variants} onChange={onChange} />)

    await userEvent.click(screen.getByRole('radio', { name: '9' }))
    await userEvent.click(screen.getByRole('radio', { name: 'Grey' }))
    await userEvent.click(screen.getByRole('radio', { name: 'Wide' }))
    expect(onChange).toHaveBeenLastCalledWith(variants[1])

    // Size 10 only comes in Regular, so the Wide pick is dropped.
    await userEvent.click(screen.getByRole('radio', { name: '10' }))
    expect(screen.getByRole('radio', { name: 'Wide' })).toBeDisabled()
    expect(screen.getByRole('radio', { name: 'Wide' })).not.toBeChecked()
    expect(onChange).toHaveBeenLastCalledWith(null)

    await userEvent.click(screen.getByRole('radio', { name: 'Regular' }))
    expect(onChange).toHaveBeenLastCalledWith(variants[2])
  })
})
