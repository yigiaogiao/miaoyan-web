import { useEffect, useState } from 'react';

export type ResolvedTheme = 'light' | 'dark';

function currentTheme(): ResolvedTheme {
  const explicit = document.documentElement.dataset.theme;
  if (explicit === 'dark' || explicit === 'light') {
    return explicit;
  }
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

/** Theme actually in effect, including "follow system". */
export function useResolvedTheme(): ResolvedTheme {
  const [theme, setTheme] = useState<ResolvedTheme>(currentTheme);

  useEffect(() => {
    const update = () => setTheme(currentTheme());
    const observer = new MutationObserver(update);
    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['data-theme'],
    });
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    media.addEventListener('change', update);
    update();
    return () => {
      observer.disconnect();
      media.removeEventListener('change', update);
    };
  }, []);

  return theme;
}
