/**
 * Conservative "tidy up" used by the toolbar. It never reflows prose: it only
 * fixes whitespace and heading markers, so a stray reformat cannot destroy
 * hand-written Markdown.
 */
export function tidyMarkdown(source: string): string {
  const lines = source.replace(/\r\n/g, '\n').split('\n');
  const output: string[] = [];
  let fence: string | null = null;
  let blankRun = 0;

  for (const raw of lines) {
    let line = raw.replace(/[ \t]+$/, '');

    const fenceMatch = /^\s*(`{3,}|~{3,})/.exec(line);
    if (fenceMatch) {
      if (fence === null) {
        fence = fenceMatch[1][0];
      } else if (fenceMatch[1][0] === fence) {
        fence = null;
      }
      output.push(line);
      blankRun = 0;
      continue;
    }

    if (fence === null) {
      line = line
        .replace(/^(#{1,6})([^#\s])/, '$1 $2')
        .replace(/^(\s*[-*+])([^\s])/, '$1 $2')
        .replace(/^(\s*\d+\.)([^\s])/, '$1 $2');
    }

    if (line.trim() === '') {
      blankRun += 1;
      if (blankRun > 1) {
        continue;
      }
    } else {
      blankRun = 0;
    }
    output.push(line);
  }

  // Keep a single trailing newline.
  while (output.length > 0 && output[output.length - 1].trim() === '') {
    output.pop();
  }
  return `${output.join('\n')}\n`;
}
