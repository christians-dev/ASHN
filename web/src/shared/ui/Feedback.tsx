import type { ReactNode } from 'react'
import styles from './ui.module.css'

interface AlertProps {
  tone?: 'error' | 'info' | 'success'
  children: ReactNode
}

/** Errors interrupt screen readers (role=alert); other messages are announced politely. */
export function Alert({ tone = 'info', children }: AlertProps) {
  return (
    <div role={tone === 'error' ? 'alert' : 'status'} className={`${styles.alert} ${styles[tone]}`}>
      {children}
    </div>
  )
}

export function EmptyState({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className={styles.empty}>
      <h2>{title}</h2>
      {children}
    </div>
  )
}
