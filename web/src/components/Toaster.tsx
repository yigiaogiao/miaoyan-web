import { AlertTriangle, Check, Info, X } from 'lucide-react';
import { useToastStore } from '../store/toast';

export function Toaster() {
  const toasts = useToastStore((state) => state.toasts);
  const dismiss = useToastStore((state) => state.dismiss);

  return (
    <div className="pointer-events-none fixed inset-x-0 bottom-4 z-50 flex flex-col items-center gap-2 px-4">
      {toasts.map((toast) => (
        <div
          key={toast.id}
          className="pointer-events-auto flex max-w-lg items-start gap-2.5 rounded-xl border border-[var(--border)] bg-[var(--surface-raised)] px-3.5 py-2.5 text-sm shadow-lg"
        >
          <span className="mt-0.5 text-[var(--text-muted)]">
            {toast.tone === 'error' ? (
              <AlertTriangle size={15} className="text-[var(--danger)]" />
            ) : toast.tone === 'success' ? (
              <Check size={15} className="text-[var(--accent)]" />
            ) : (
              <Info size={15} />
            )}
          </span>
          <span className="flex-1 leading-snug">{toast.message}</span>
          <button
            type="button"
            onClick={() => dismiss(toast.id)}
            className="rounded p-0.5 text-[var(--text-faint)] hover:bg-[var(--accent-soft)] hover:text-[var(--text)]"
            aria-label="关闭"
          >
            <X size={14} />
          </button>
        </div>
      ))}
    </div>
  );
}
