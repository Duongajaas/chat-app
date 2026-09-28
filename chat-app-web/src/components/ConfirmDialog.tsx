import { useEffect, useRef } from "react";

export function ConfirmDialog({ title, description, confirmLabel, busy, error, onConfirm, onCancel }: {
  title: string; description: string; confirmLabel: string; busy: boolean; error?: string;
  onConfirm: () => void; onCancel: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    const element = dialog.current!; element.showModal();
    return () => { element.close();
      if (previous?.isConnected) previous.focus();
      else document.querySelector<HTMLElement>(".profile-back-link")?.focus();
    };
  }, []);
  return <dialog ref={dialog} className="confirm-dialog" aria-labelledby="confirm-title" aria-describedby="confirm-description"
    aria-busy={busy} onCancel={e => { e.preventDefault(); if (!busy) onCancel(); }}>
    <h2 id="confirm-title">{title}</h2>
    <p id="confirm-description">{description}</p>
    {error && <p role="alert" className="alert alert-danger">{error}</p>}
    <div className="confirm-dialog__actions">
      <button type="button" autoFocus disabled={busy} onClick={onCancel}>Hủy</button>
      <button type="button" disabled={busy} onClick={onConfirm}>{busy ? "Đang xử lý…" : confirmLabel}</button>
    </div>
  </dialog>;
}
