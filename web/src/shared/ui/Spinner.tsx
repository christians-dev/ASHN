import styles from './ui.module.css'

export function Spinner({ label = 'Loading…' }: { label?: string }) {
  return (
    <div role="status" className={styles.spinner}>
      {label}
    </div>
  )
}
