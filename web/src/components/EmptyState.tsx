import { Feather, Keyboard, Plus } from 'lucide-react';
import { useI18n } from '../lib/i18n';

interface Props {
  onCreate: () => void;
}

export function EmptyState({ onCreate }: Props) {
  const { t } = useI18n();
  return (
    <div className="grid h-full place-items-center bg-[var(--surface)] px-8">
      <div className="max-w-sm text-center">
        <div className="mx-auto mb-5 grid size-14 place-items-center rounded-2xl bg-[var(--accent-soft)] text-[var(--accent-strong)]">
          <Feather size={26} strokeWidth={1.6} />
        </div>
        <h2 className="text-lg font-semibold">{t('empty.title')}</h2>
        <p className="mt-1.5 text-sm leading-relaxed text-[var(--text-muted)]">
          {t('empty.description')}
        </p>
        <button
          type="button"
          onClick={onCreate}
          className="mt-5 inline-flex items-center gap-1.5 rounded-xl bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white shadow-sm hover:brightness-105"
        >
          <Plus size={15} /> {t('empty.newNote')}
        </button>
        <div className="mt-6 flex items-center justify-center gap-1.5 text-[12px] text-[var(--text-faint)]">
          <Keyboard size={13} />
          <span>{t('empty.shortcuts')}</span>
        </div>
      </div>
    </div>
  );
}
