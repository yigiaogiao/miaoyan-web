export function formatRelativeTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '';
  }

  const seconds = Math.round((date.getTime() - Date.now()) / 1000);
  const absolute = Math.abs(seconds);
  const formatter = new Intl.RelativeTimeFormat('zh-CN', { numeric: 'auto' });

  if (absolute < 60) {
    return '刚刚';
  }
  if (absolute < 3600) {
    return formatter.format(Math.round(seconds / 60), 'minute');
  }
  if (absolute < 86400) {
    return formatter.format(Math.round(seconds / 3600), 'hour');
  }
  if (absolute < 86400 * 30) {
    return formatter.format(Math.round(seconds / 86400), 'day');
  }

  return new Intl.DateTimeFormat('zh-CN', {
    year: date.getFullYear() === new Date().getFullYear() ? undefined : 'numeric',
    month: 'short',
    day: 'numeric',
  }).format(date);
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`;
  }
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export function isMod(event: KeyboardEvent | React.KeyboardEvent): boolean {
  return event.metaKey || event.ctrlKey;
}

export function basename(path: string): string {
  const index = path.lastIndexOf('/');
  return index >= 0 ? path.slice(index + 1) : path;
}

export function isAudioPath(path: string): boolean {
  return /\.(m4a|mp3|wav|flac|ogg|oga|opus|aac|webm)$/i.test(path);
}

export function isImagePath(path: string): boolean {
  return /\.(png|jpe?g|gif|webp|avif|svg|bmp|ico)$/i.test(path);
}
