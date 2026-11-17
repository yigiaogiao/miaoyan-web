interface Props {
  className?: string;
}

/**
 * The MiaoYan Notes mark: a flat leaf on the accent green, sharing the
 * geometry of lazycat/icon.svg and favicon.svg so the store icon, the
 * sidebar badge and the empty state stay identical.
 */
export function AppIcon({ className = 'size-7' }: Props) {
  return (
    <svg
      viewBox="0 0 512 512"
      className={className}
      aria-hidden
      focusable="false"
    >
      <rect x="16" y="16" width="480" height="480" rx="112" fill="#2F7D63" />
      <path fill="#F7F3E8" d="M142 388c0-120 66-218 218-260 2 122-56 234-218 260Z" />
      <path
        fill="none"
        stroke="#F7F3E8"
        strokeWidth="18"
        strokeLinecap="round"
        d="M142 388 108 426"
      />
      <path
        fill="none"
        stroke="#2F7D63"
        strokeWidth="13"
        strokeLinecap="round"
        d="M158 376 336 148"
      />
      <path
        fill="none"
        stroke="#2F7D63"
        strokeWidth="9"
        strokeLinecap="round"
        d="M204 316 184 281M252 252 236 212M204 316 228 341M252 252 277 283M300 188 291 160M300 188 318 207"
      />
    </svg>
  );
}
