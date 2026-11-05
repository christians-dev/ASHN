import { useId, useState } from 'react'
import type { ProductOption, Variant } from '../catalogQueries'
import styles from './catalog.module.css'

interface VariantSelectorProps {
  options: ProductOption[]
  variants: Variant[]
  /** The fully chosen, in-stock variant, or null while the choice is incomplete. */
  onChange: (variant: Variant | null) => void
}

type Selection = Record<string, string>

/** Does an in-stock variant exist with these values for the given options? */
function existsAvailable(variants: Variant[], wanted: Selection) {
  return variants.some(
    (v) =>
      v.isAvailable &&
      Object.entries(wanted).every(([name, value]) => v.optionValues[name] === value),
  )
}

/**
 * One choice per product option (1–3). Options are chosen in order: a value is disabled when no in-stock
 * variant has it together with the values picked for the earlier options. Changing an earlier option drops
 * later picks that no longer fit, so the shopper can never get stuck.
 */
export function VariantSelector({ options, variants, onChange }: VariantSelectorProps) {
  const groupId = useId()
  const [selection, setSelection] = useState<Selection>({})

  const earlierPicks = (index: number): Selection =>
    Object.fromEntries(
      options
        .slice(0, index)
        .filter((o) => selection[o.name] !== undefined)
        .map((o) => [o.name, selection[o.name]]),
    )

  function choose(index: number, value: string) {
    const next: Selection = { ...earlierPicks(index), [options[index].name]: value }
    for (const later of options.slice(index + 1)) {
      const kept = selection[later.name]
      if (kept !== undefined && existsAvailable(variants, { ...next, [later.name]: kept })) {
        next[later.name] = kept
      }
    }

    setSelection(next)
    const complete = options.every((o) => next[o.name] !== undefined)
    onChange(
      complete
        ? (variants.find(
            (v) => v.isAvailable && options.every((o) => v.optionValues[o.name] === next[o.name]),
          ) ?? null)
        : null,
    )
  }

  return (
    <>
      {options.map((option, index) => (
        <fieldset key={option.name} className={styles.optionGroup}>
          <legend>{option.name}</legend>
          <div className={styles.optionValues}>
            {option.values.map((value) => {
              const disabled = !existsAvailable(variants, {
                ...earlierPicks(index),
                [option.name]: value,
              })
              return (
                <label key={value} className={styles.optionValue}>
                  <input
                    type="radio"
                    name={`${groupId}-${option.name}`}
                    value={value}
                    checked={selection[option.name] === value}
                    disabled={disabled}
                    onChange={() => choose(index, value)}
                  />
                  {value}
                </label>
              )
            })}
          </div>
        </fieldset>
      ))}
    </>
  )
}
