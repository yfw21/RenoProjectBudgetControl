import { useEffect, useMemo, useRef, useState } from 'react'

export type ComboLine = { id: number; category: string; description: string }

type Props = {
  value: number | null
  lines: ComboLine[]
  onChange: (id: number | null) => void
  /** If set, this line is shown with a ✦ marker and listed first in the suggestions panel. */
  suggestedId?: number | null
  /** Free-form label shown next to the suggested line ("High confidence", "Plausible…"). */
  suggestedLabel?: string | null
  placeholder?: string
  disabled?: boolean
}

function labelFor(l: ComboLine) {
  return `${l.category} · ${l.description}`
}

export function BudgetLineCombobox({
  value, lines, onChange, suggestedId, suggestedLabel, placeholder, disabled,
}: Props) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [touched, setTouched] = useState(false) // user has typed since opening
  const [highlight, setHighlight] = useState(0)
  const containerRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  const selected = useMemo(() => lines.find(l => l.id === value) || null, [lines, value])
  const suggested = useMemo(
    () => (suggestedId != null ? lines.find(l => l.id === suggestedId) : null) ?? null,
    [lines, suggestedId])

  // Filter + sort: suggested line first (if it matches the filter), then alphabetical.
  // While the user hasn't typed anything since opening, show the full list (don't filter
  // by the pre-populated current selection — that would hide everything but itself).
  const filtered = useMemo(() => {
    const q = touched ? query.trim().toLowerCase() : ''
    const all = q
      ? lines.filter(l => labelFor(l).toLowerCase().includes(q))
      : lines
    if (!suggested) return all
    const rest = all.filter(l => l.id !== suggested.id)
    return all.some(l => l.id === suggested.id) ? [suggested, ...rest] : rest
  }, [lines, query, suggested])

  // Close on outside click
  useEffect(() => {
    if (!open) return
    function handler(e: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false)
        setQuery('')
      }
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [open])

  // Reset highlight when filtered list changes
  useEffect(() => { setHighlight(0) }, [filtered.length, query])

  function commit(line: ComboLine | null) {
    onChange(line ? line.id : null)
    setOpen(false)
    setQuery('')
    setTouched(false)
  }

  function openWithCurrent() {
    setOpen(true)
    setQuery(selected ? labelFor(selected) : '')
    setTouched(false)
    // Select-all on next tick so typing replaces the current text.
    setTimeout(() => inputRef.current?.select(), 0)
  }

  function handleKey(e: React.KeyboardEvent) {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setOpen(true)
      setHighlight(h => Math.min(h + 1, filtered.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setHighlight(h => Math.max(h - 1, 0))
    } else if (e.key === 'Enter') {
      e.preventDefault()
      if (open && filtered[highlight]) commit(filtered[highlight])
    } else if (e.key === 'Escape') {
      setOpen(false)
      setQuery('')
    } else if (e.key === 'Backspace' && !query && selected) {
      // Empty input + Backspace clears the current pick.
      commit(null)
    }
  }

  const displayText = open ? query : (selected ? labelFor(selected) : '')

  return (
    <div className="combo" ref={containerRef}>
      <div className="combo-input-wrap">
        <input
          ref={inputRef}
          className="combo-input"
          value={displayText}
          placeholder={placeholder ?? (lines.length === 0 ? 'No budget lines' : 'Type to search…')}
          disabled={disabled || lines.length === 0}
          onFocus={openWithCurrent}
          onClick={openWithCurrent}
          onChange={e => { setOpen(true); setQuery(e.target.value); setTouched(true) }}
          onKeyDown={handleKey}
          autoComplete="off"
          spellCheck={false}
        />
        {selected && !open && (
          <button
            type="button"
            className="combo-clear"
            title="Clear"
            tabIndex={-1}
            onClick={e => { e.stopPropagation(); commit(null) }}
          >×</button>
        )}
        <span className="combo-caret" aria-hidden="true">▾</span>
      </div>
      {open && filtered.length > 0 && (
        <ul className="combo-list" role="listbox">
          {filtered.map((l, i) => {
            const isSuggested = suggested?.id === l.id
            const isSelected = selected?.id === l.id
            return (
              <li
                key={l.id}
                className={
                  'combo-item' +
                  (i === highlight ? ' is-highlight' : '') +
                  (isSelected ? ' is-selected' : '') +
                  (isSuggested ? ' is-suggested' : '')
                }
                onMouseEnter={() => setHighlight(i)}
                onMouseDown={e => { e.preventDefault(); commit(l) }}
                role="option"
                aria-selected={isSelected}
              >
                {isSuggested && <span className="combo-mark" title={suggestedLabel ?? 'AI match'}>✦</span>}
                <span className="combo-cat">{l.category}</span>
                <span className="combo-sep">·</span>
                <span className="combo-desc">{l.description}</span>
                {isSuggested && suggestedLabel && <span className="combo-conf">{suggestedLabel}</span>}
              </li>
            )
          })}
        </ul>
      )}
      {open && filtered.length === 0 && (
        <div className="combo-empty">No lines match “{query}”.</div>
      )}
    </div>
  )
}
