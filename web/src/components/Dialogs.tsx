import * as Dialog from '@radix-ui/react-dialog';
import { AlertTriangle, Loader2, X } from 'lucide-react';
import { useEffect, useState, type FormEvent, type ReactNode } from 'react';

interface PromptDialogProps {
  open: boolean;
  title: string;
  description?: string;
  label: string;
  initialValue?: string;
  placeholder?: string;
  confirmText?: string;
  onOpenChange: (open: boolean) => void;
  onSubmit: (value: string) => Promise<void> | void;
}

export function PromptDialog({
  open,
  title,
  description,
  label,
  initialValue = '',
  placeholder,
  confirmText = '确定',
  onOpenChange,
  onSubmit,
}: PromptDialogProps) {
  const [value, setValue] = useState(initialValue);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setValue(initialValue);
      setError(null);
      setBusy(false);
    }
  }, [open, initialValue]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!value.trim() || busy) {
      return;
    }

    setBusy(true);
    setError(null);
    try {
      await onSubmit(value.trim());
      onOpenChange(false);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '操作失败');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-black/25 backdrop-blur-[1px]" />
        <Dialog.Content className="fixed left-1/2 top-1/2 z-50 w-[min(92vw,26rem)] -translate-x-1/2 -translate-y-1/2 rounded-2xl border border-[var(--border)] bg-[var(--surface-raised)] p-6 shadow-2xl">
          <div className="mb-4 flex items-start justify-between gap-4">
            <div>
              <Dialog.Title className="text-base font-semibold">{title}</Dialog.Title>
              {description && (
                <Dialog.Description className="mt-1 text-sm text-[var(--text-muted)]">
                  {description}
                </Dialog.Description>
              )}
            </div>
            <Dialog.Close asChild>
              <button
                type="button"
                className="rounded-md p-1 text-[var(--text-faint)] hover:bg-[var(--accent-soft)] hover:text-[var(--text)]"
                aria-label="关闭"
              >
                <X size={16} />
              </button>
            </Dialog.Close>
          </div>

          <form onSubmit={submit} className="space-y-4">
            <label className="block text-sm">
              <span className="mb-1.5 block text-[var(--text-muted)]">{label}</span>
              <input
                autoFocus
                value={value}
                placeholder={placeholder}
                onChange={(event) => setValue(event.target.value)}
                className="w-full rounded-lg border border-[var(--border)] bg-[var(--surface)] px-3 py-2 text-[var(--text)] outline-none focus:border-[var(--accent)]"
              />
            </label>

            {error && (
              <p className="rounded-lg bg-[var(--danger-soft)] px-3 py-2 text-sm text-[var(--danger)]">
                {error}
              </p>
            )}

            <div className="flex justify-end gap-2 pt-1">
              <Dialog.Close asChild>
                <button
                  type="button"
                  className="rounded-lg px-3.5 py-2 text-sm text-[var(--text-muted)] hover:bg-[var(--accent-soft)]"
                >
                  取消
                </button>
              </Dialog.Close>
              <button
                type="submit"
                disabled={!value.trim() || busy}
                className="inline-flex items-center gap-1.5 rounded-lg bg-[var(--accent)] px-3.5 py-2 text-sm font-medium text-white disabled:opacity-50"
              >
                {busy && <Loader2 size={14} className="animate-spin" />}
                {confirmText}
              </button>
            </div>
          </form>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

interface ConfirmDialogProps {
  open: boolean;
  title: string;
  description: ReactNode;
  confirmText?: string;
  tone?: 'danger' | 'normal';
  onOpenChange: (open: boolean) => void;
  onConfirm: () => Promise<void> | void;
}

export function ConfirmDialog({
  open,
  title,
  description,
  confirmText = '确定',
  tone = 'danger',
  onOpenChange,
  onConfirm,
}: ConfirmDialogProps) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setBusy(false);
      setError(null);
    }
  }, [open]);

  async function confirm() {
    if (busy) {
      return;
    }

    setBusy(true);
    setError(null);
    try {
      await onConfirm();
      onOpenChange(false);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '操作失败');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-black/25 backdrop-blur-[1px]" />
        <Dialog.Content className="fixed left-1/2 top-1/2 z-50 w-[min(92vw,26rem)] -translate-x-1/2 -translate-y-1/2 rounded-2xl border border-[var(--border)] bg-[var(--surface-raised)] p-6 shadow-2xl">
          <div className="flex items-start gap-3">
            <span
              className={
                tone === 'danger'
                  ? 'mt-0.5 grid size-8 shrink-0 place-items-center rounded-full bg-[var(--danger-soft)] text-[var(--danger)]'
                  : 'mt-0.5 grid size-8 shrink-0 place-items-center rounded-full bg-[var(--accent-soft)] text-[var(--accent)]'
              }
            >
              <AlertTriangle size={16} />
            </span>
            <div className="min-w-0 flex-1">
              <Dialog.Title className="text-base font-semibold">{title}</Dialog.Title>
              <Dialog.Description asChild>
                <div className="mt-1 text-sm leading-relaxed text-[var(--text-muted)]">{description}</div>
              </Dialog.Description>
            </div>
          </div>

          {error && (
            <p className="mt-4 rounded-lg bg-[var(--danger-soft)] px-3 py-2 text-sm text-[var(--danger)]">
              {error}
            </p>
          )}

          <div className="mt-6 flex justify-end gap-2">
            <Dialog.Close asChild>
              <button
                type="button"
                className="rounded-lg px-3.5 py-2 text-sm text-[var(--text-muted)] hover:bg-[var(--accent-soft)]"
              >
                取消
              </button>
            </Dialog.Close>
            <button
              type="button"
              disabled={busy}
              onClick={() => void confirm()}
              className={
                tone === 'danger'
                  ? 'inline-flex items-center gap-1.5 rounded-lg bg-[var(--danger)] px-3.5 py-2 text-sm font-medium text-white disabled:opacity-50'
                  : 'inline-flex items-center gap-1.5 rounded-lg bg-[var(--accent)] px-3.5 py-2 text-sm font-medium text-white disabled:opacity-50'
              }
            >
              {busy && <Loader2 size={14} className="animate-spin" />}
              {confirmText}
            </button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
