# MiaoYan Web — Compatibility Matrix

> MiaoYan is a native macOS app. "1:1" here means **the same tasks through the
> same information architecture and interaction flow**, with browser-safe
> substitutes where a native capability does not exist. This file is updated as
> each milestone lands. Status: `Done`, `Partial`, `Substituted`, `N/A (native)`.

Reference sources: MiaoYan `README_CN.md`, `ARCHITECTURE.md`,
`skills/miaoyan/SKILL.md`, `Resources/Initial/*.md`, `AGENTS.md`, and the
relevant Swift sources (`Business/Storage.swift`, `Business/Note.swift`,
`Helpers/NameHelper.swift`, `Packages/MiaoYanCore/Frontmatter.swift`).

## 1. Layout and navigation

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| Three columns: folders/projects, note list, editor | Same three columns, resizable, with collapsible sidebar and list | Planned (M1) |
| Sidebar shows All Notes, folders, Trash | All Notes, project rows with counts, Trash | Planned (M1) |
| Brand row at the top of the sidebar | Wordmark "妙言 Web" + small bird mark (original asset, not MiaoYan's) | Planned (M1) |
| Note list shows title, excerpt, modified time, pin | Same; virtualised; relative time; tag chips when present | Planned (M1) |
| Sort by modified / created / title | Same, plus pinned-first | Planned (M2) |
| Drag note to a folder | HTML5 drag to project row; move is atomic on the server | Planned (M1) |
| ⌘1 / ⌘2 collapse sidebar / list | Same logical action; keyboard aliases documented below | Planned (M1) |
| Window title bar is part of the chrome | Slim app bar with breadcrumb and actions; no traffic lights | Substituted |
| Native macOS window/menu bar | In-app menus: title-bar action menu, context menus, help dialog | Substituted |

## 2. Notes and projects

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| New note (⌘N) in the selected folder | Same; title inline-editable, default "未命名笔记" in Web (MiaoYan uses a UUID file shown as "Untitled Note") | Planned (M1) |
| New folder (⇧⌘N) | Same | Planned (M1) |
| Rename note (⌘R) by filename | Same; file renamed atomically; `" 2"` suffix on conflict as MiaoYan's `NameHelper` | Planned (M1) |
| Duplicate note (⌘D) -> `Name Copy`, `Name Copy 2` | Same naming | Planned (M1) |
| Delete (⌘⌫) -> system Trash | App trash `.miaoyan/trash/<timestamp>-<name>/` + `meta.json`, restorable in-app | Substituted |
| Restore from Trash | Trash view with restore; collision restores beside the existing file | Planned (M1) |
| Note = file; title = filename | Same | Planned (M1) |
| Root-level notes are valid | Same; shown in All Notes | Planned (M1) |
| Nested folders inside a project | Same; project tree supports subfolders, attachments resolve relative to the note | Planned (M1) |
| Pin note | Same; stored in the index (`pinned`), not in the Markdown file (MiaoYan also keeps UI state outside the file) | Planned (M2) |
| Empty note fallback title | Web shows "未命名笔记" while the file name remains the source of truth | Substituted |
| Reveal in Finder | "Reveal path" shows the relative vault path and copies it; no OS file manager integration | N/A (native) |
| Open note in a separate window (⌘O) | Opens `/note/{id}` in a new browser tab | Substituted |
| Finder file drop import | Drop `.md`/images/audio on the note list; imported into the selected project | Substituted |
| Single-file mode (open one .md) | Not supported; the vault model is the product | N/A (native) |

## 3. Editor

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| Source editing, never WYSIWYG | CodeMirror 6 source editing, same philosophy | Planned (M1) |
| Markdown syntax highlighting (MiaoYan palette) | CodeMirror Markdown + custom highlight styles: headings `#5E3DB3`, emphasis `#9A7400`, links `#24837B`, markup `#7A7872` | Planned (M1) |
| Split editor/preview (⌘\) | Same; split is the desktop default | Planned (M1) |
| Editor-only / preview-only (⌘3) | Same | Planned (M1) |
| Active line, bracket matching, undo/redo | Same | Planned (M1) |
| Search/replace in editor (⌘F) | CodeMirror search panel; browser find remains available via ⌘F when focus is outside the editor | Planned (M1) |
| Autosave (debounced) | 600 ms debounce, status 未保存/保存中/已保存/冲突 | Planned (M1) |
| Typing performance gates (1 MB, 5000 lines) | > 1 MB: skip syntax highlighting and auto diagram render; > 5000 lines: simplified highlight | Planned (M3/M5) |
| Toolbar formatting (bold, italic, strikethrough, code, link, image, etc.) | Same plus table/task/math/Mermaid/PlantUML/Markmap/PPT separator/fold | Planned (M1/M3) |
| Slash templates + Tab (`/table`, `/task`, `/time`, `/img`, `/video`, `/mermaid`, `/plantuml`, `/markmap`, `/fold`) | Same trigger and expansion | Planned (M1/M3) |
| Word count / reading time | Same, CJK-aware | Planned (M1) |
| TOC / outline (⌘5) | Outline drawer in preview and editor; click to jump | Planned (M1) |
| Focus mode / typewriter mode | Both; persisted per browser | Planned (M3) |
| Font size / line height / editor width settings | Same; persisted | Planned (M3) |
| Auto-format (⇧⌘L, Prettier-like) | Browser-side Markdown formatter (headings, list spacing, blank lines, fences); never rewrites code/math/wikilinks/frontmatter | Planned (M3) |
| Native CJK IME handling | Browser IME; CodeMirror handles composition events correctly | Substituted |
| Smart typography cleaning | A "clean typography" command that protects code/math/links/frontmatter, mirroring MiaoYan's `TypographyCleaner` rules | Partial (M3) |

## 4. Preview and rendering

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| CommonMark + GFM (tables, tasks, footnotes, strikethrough, autolink) | markdown-it + task/footnote plugins | Planned (M1/M3) |
| Code highlight (Highlightr) | highlight.js, GitHub light/dark palettes | Planned (M1) |
| KaTeX inline/block math | KaTeX with `$...$`, `$$...$$`; currency heuristic avoids false positives | Planned (M3) |
| Mermaid diagrams | mermaid 11, client-side, theme-aware, lazy | Planned (M3) |
| PlantUML diagrams | Local `plantuml.jar` over stdin; SVG returned by `/api/render/plantuml`; no public server | Planned (M3) |
| Markmap mind maps | markmap-lib + markmap-view, interactive | Planned (M3) |
| GitHub alerts NOTE/TIP/IMPORTANT/WARNING/CAUTION | Same rendering (tinted block, coloured title, no stripe) | Planned (M3) |
| `<details>/<summary>` passthrough | Allowed by the sanitiser | Planned (M3) |
| Frontmatter hidden when it looks like YAML | Same rule as `MiaoYanCore.Frontmatter`; a `---` deck opener without keys stays content | Planned (M1) |
| Image display, remote images | Same; lightbox on click | Planned (M1/M3) |
| Audio playback for `![[file.m4a]]` | Inline `<audio controls>`; audio links also become players | Planned (M3) |
| Video embedding | Not stored; video upload extracts audio (ffmpeg) and transcript; remote video links remain links | Substituted |
| Two-way scroll sync at 60 fps | Line-based sync via token source maps; preview click jumps to the source line | Planned (M1) |
| Raw HTML allowed (responsive media, columns) | DOMPurify allowlist; `style` allowed on safe elements; scripts/iframes/forms/objects removed | Partial (M3) |
| Iframe slide backgrounds (`data-background-iframe`) | Disabled for privacy; a styled placeholder is shown | N/A (security) |
| Print / export PDF | Browser print stylesheet ("另存为 PDF"); no bundled Chromium | Substituted |

## 5. Wikilinks, backlinks, unresolved links

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| `[[Exact title]]` links by title, no `.md` | Same parser; exact title first, case-insensitive fallback; no alias/heading syntax (per MiaoYan skill) | Planned (M2) |
| Click a wikilink to open the note | Same; `miaoyan://` / `nv://` routes are not used, in-app navigation instead | Substituted |
| Backlinks panel | Same; context excerpt per backlink | Planned (M2) |
| Unresolved link style | Dashed/muted link with "创建笔记" action; creates a note with that title in the current project | Planned (M2) |
| Rename updates link index | Index updates on rename; note text elsewhere is not rewritten (MiaoYan also indexes rather than rewrites) | Planned (M2) |
| Links across any folder depth | Same; Trash excluded | Planned (M2) |

## 6. Search

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| Note-list search filters within the selected folder | Same: project scope first, then title/body match | Planned (M2) |
| Search reads unloaded note bodies off the main thread | Server-side FTS5; client never loads all bodies | Planned (M2) |
| Command palette (⌘K in Web; MiaoYan uses its search field) | ⌘K / Ctrl+K palette with title, snippet, project; Enter opens | Planned (M2) |
| Chinese search | FTS5 trigram tokenizer + LIKE fallback for 1–2 character queries | Planned (M2) |
| Result speed | Target < 150 ms on 10k notes | Planned (M2) |

## 7. Versions, conflicts, external changes

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| `NoteVersionManager` per-note history in Application Support | `{state}/versions/<note-id>/`, newest 50 or 30 days | Planned (M2) |
| Move history with rename/folder move/trash | Note ID is stable in the index, so history follows renames | Planned (M2) |
| Version list + restore | Dialog with timestamp, size, preview; diff vs current; restore snapshots current first | Planned (M2) |
| `FileSystemEventManager` watcher | Server `FileSystemWatcher` + debounce + SSE | Planned (M1) |
| Ignore own writes | Write suppression by path + content hash | Planned (M1) |
| Conflict backup while editing | `.miaoyan-conflicts/<project>-<note>-<timestamp>.md`, plus dialog and disk reload | Planned (M2) |
| External change when clean | Quiet reload, cursor anchored by line | Planned (M1) |

## 8. Themes and display

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| Light / dark / follow system | Same; CSS variables + `prefers-color-scheme`; follows changes live | Planned (M1) |
| Warm paper palette (`#FDFCF9` / `#1E1D1B`), sand selection | Same values; 7% hairline dividers | Planned (M1) |
| MiaoYan green for brand/active buttons | Same accent role; exact hex may differ (no copied assets) | Partial |
| Five UI languages | Chinese (default) + English; structure ready for more | Partial (v1) |
| Code font follows text font option | Web uses a dedicated code stack; a "code follows text" toggle is omitted in v1 | N/A (native font stack) |

## 9. PPT mode

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| `---` on its own line separates slides | Same, outside fenced code and frontmatter | Planned (M3) |
| ⌘⌥P opens PPT | Ctrl/Cmd+Alt+P; also the toolbar button | Planned (M3) |
| Only notes with `---` can present | Same; button disabled with a tooltip otherwise | Planned (M3) |
| Leading HTML comment config for Reveal | Same keys (`transition`, `backgroundTransition`, `transitionSpeed`, `controls`, `progress`, `slideNumber`); nested dot keys supported | Planned (M3) |
| `.slide:` background directives | Same (`data-background` colours/gradients/images) | Planned (M3) |
| `.element:` fragments | Supported for paragraphs/list items; complex arbitrary element injection is partial | Partial (M3) |
| Code line ranges `[1|2-3]` | Supported via reveal highlight plugin | Planned (M3) |
| Enter shows slide overview, Esc exits | Same reveal behaviour | Planned (M3) |
| Print deck to PDF | Print view stacks all slides; browser print dialog | Substituted |
| Background iframes | Disabled (security); static colour/image backgrounds supported | N/A (security) |
| Full-screen native presentation | Browser fullscreen API; Esc exits | Substituted |

## 10. Attachments and ingest

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| Images in `i/`, other files in `files/` | Same, relative to the note's folder | Planned (M1) |
| Paste image | Clipboard image -> upload -> `![](/i/name.png)` | Planned (M1) |
| Drag image/file | Drag/drop into the editor | Planned (M1) |
| Root-relative `/i/...` references | Same in Markdown; the Web resolver maps them to the note-relative asset endpoint | Planned (M1) |
| Media cleanup for unused attachments | "Clean unused attachments" scans all note bodies before deleting; off by default | Planned (M5) |
| Audio attachment playback | Inline player in preview | Planned (M3) |
| Video support | Not stored; audio extraction + transcript (optional ffmpeg) | Substituted |
| OCR images | Optional local `tesseract` command, off by default, managed block or sidecar | Planned (M5) |
| Whisper transcription | Optional local command, off by default, background job | Planned (M5) |
| URL/text ingestion (Papyra ecosystem) | Not built in; `papyra-ingest` works through the compatibility API | Planned (M4) |

## 11. Import / export

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| Library is a user-picked folder | `MIAOYAN_LIBRARY` points at the folder; import copies or adopts an existing tree | Planned (M1) |
| iCloud / WebDAV sync is external | The vault is a normal folder; external sync tools can be used; watcher catches changes | Substituted |
| Export note (Markdown) | Byte-faithful `.md` download | Planned (M3) |
| Export HTML | Self-contained HTML with inline CSS; local media embedded where possible | Planned (M3) |
| Export all | Zip of the vault (excluding `.miaoyan*` and state) | Planned (M5) |
| Swap library folder | Settings -> vault path is fixed by env in v1 (single library); import wizard supports merging | Partial (v1) |

## 12. Papyra-compatible API

| Method | Path | Contract | Status |
|---|---|---|---|
| GET | `/api/notes` | `[{id,title,tags,updated,excerpt}]` | Planned (M4) |
| GET | `/api/notes/{id}` | note + `ETag`; body excludes managed frontmatter | Planned (M4) |
| PUT | `/api/notes/{id}` | `{title,tags,body,color,pinned,archived,kind}`; `If-Match`; 412/409 on stale; create if missing; `?archive=false` | Planned (M4) |
| POST | `/api/media/upload` | multipart `file` -> `{filename}`; hash dedupe | Planned (M4) |
| HEAD/GET | `/api/media/{filename}` | 200 / bytes | Planned (M4) |
| GET | `/api/search?q=` | `[{id,title,snippet,score}]` | Planned (M4) |
| POST | `/api/categories` | `{name}` -> idempotent tag registration | Planned (M4) |
| GET | `/note/{id}` | SPA note route | Planned (M4) |
| Auth | `X-API-Key` | Required when `MIAOYAN_API_KEY` is set | Planned (M4) |

Acceptance: real `papyra-ingest` publishes an X post and a Bilibili video,
updates the same note on re-publish, shows images/audio, and does not create
duplicates.

## 13. Keyboard shortcuts

MiaoYan shortcuts follow the native menu. Browsers reserve many `Cmd/Ctrl+number`
and `Ctrl+N/R/T/W` combinations; every action gets a browser-safe alias.

| Action | MiaoYan | Web primary | Web fallback |
|---|---|---|---|
| Collapse/expand sidebar | ⌘1 | `Mod+1` (when not reserved) | `Mod+Alt+1` |
| Collapse/expand note list | ⌘2 | `Mod+2` | `Mod+Alt+2` |
| Toggle preview | ⌘3 | `Mod+3` | `Mod+Alt+3` |
| Toggle split view | ⌘\\ | `Mod+\` | — |
| PPT mode | ⌘⌥P | `Mod+Alt+P` | toolbar button |
| TOC / outline | ⌘5 | `Mod+5` | `Mod+Alt+5` |
| Show main window | ⌘⌥0 | — | focus browser tab |
| Global show/hide | ⌘⌥M | — | N/A (native) |
| New note | ⌘N | `Mod+N` (prevented where possible) | `Mod+Alt+N` |
| Rename note | ⌘R | `Mod+R` (prevented where possible) | `Mod+Alt+R` |
| Duplicate note | ⌘D | `Mod+D` | `Mod+Alt+D` |
| Delete note | ⌘⌫ | `Mod+Backspace` | toolbar menu |
| New folder | ⇧⌘N | `Mod+Shift+N` | `Mod+Alt+Shift+N` |
| Auto-format | ⇧⌘L | `Mod+Shift+L` | toolbar button |
| Search / command palette | menu search | `Mod+K` | `Mod+Alt+K` |
| Save now | — | `Mod+S` | — |
| Bold / italic / strike | menu | `Mod+B` / `Mod+I` / `Mod+Shift+X` | toolbar |
| Insert link | menu | `Mod+K` in editor (link dialog; palette when not editing) | toolbar |
| Open note | ⌘O | — | `Mod+Shift+O` opens a new tab |
| Reveal in Finder | ⌘⌥R | — | copy path |
| Note info | ⌘⌥I | — | info dialog |
| Focus mode | — | `Mod+Shift+F` | toolbar |
| Typewriter mode | — | `Mod+Shift+Y` | toolbar |
| Theme toggle | — | `Mod+Shift+D` | title-bar button |
| Help / shortcuts | — | `?` | title-bar menu |

`Mod` = ⌘ on macOS, Ctrl elsewhere. The in-app help lists every active binding
and marks unavailable browser-reserved ones.

## 14. Native macOS features replaced or omitted

| Native feature | Why it cannot be 1:1 | Web substitute |
|---|---|---|
| System Trash / Finder recovery | The container cannot control the host Trash | App trash with restore; files remain plain `.md` |
| Global hotkey (⌘⌥M) | Browser sandbox | None; use the LazyCat app icon |
| iCloud / security-scoped bookmarks | No macOS sandbox in a container | Vault is a normal folder; external sync tools handle sync |
| Finder reveal, Quick Look | No host file manager | Copy relative path; preview in-app |
| Native menu bar | Browser chrome | In-app menus and command palette |
| Separate note windows | Browser tabs | `/note/{id}` route opens in a new tab |
| Sparkle auto-update | LazyCat handles packages | LazyCat upgrade |
| App Store / iPhone / iPad sync | Out of scope | Same folder via external sync, if the user sets it up |
| Prettier-based auto-format | Prettier is native/bundled there | Browser formatter with protected regions |
| 60 fps native split scroll | Browser rendering | Line-anchored scroll sync; degrades gracefully |

## 15. File-format compatibility

| Rule | MiaoYan | MiaoYan Web |
|---|---|---|
| Note title | File name without `.md` | Same |
| Duplicate file name | `<name> 2.md`, `<name> 3.md` | Same |
| Title characters | `:` <-> `/` translation for Finder display | `/` and `:` normalised to `-`; documented difference |
| Images | `i/` at the note's folder, referenced as `/i/name` | Same |
| Other attachments | `files/`, `/files/name` | Same |
| Frontmatter | Leading YAML block hidden; `---` without keys stays content | Same rule |
| Wikilinks | `[[Exact title]]` | Same; no aliases |
| PPT | `---` separators + reveal config comment | Same |
| Math | `$...$`, `$$...$$` | Same |
| Diagrams | ` ```mermaid `, ` ```plantuml `, ` ```markmap ` | Same |
| Alerts | `> [!NOTE]` etc. | Same |
| Task lists / footnotes | GFM | Same |
| API-created frontmatter | Not applicable | Adds `id`, `source_url`, `tags`, `created`, `updated`; imported notes never rewritten |
| Conflict backups | `.miaoyan-conflicts/` | Same path, plus in-app dialog |

## 16. Deployment, users, and app-store parity

| MiaoYan | MiaoYan Web | Status / notes |
|---|---|---|
| One library per machine/user | One library per LazyCat user, selected by trusted `X-HC-User-ID` | M0; `/lzcapp/documents/<uid>/MiaoYan`; no cross-user reads |
| Native upgrade (Sparkle) | LazyCat store upgrade / container image pull | M0 manifest + docs |
| Library folder chosen in Preferences | `MIAOYAN_LIBRARY` or auto-discovery | M0; explicit value pins a single library |
| Local-only data | Notes in `/lzcapp/documents`, rebuildable state in `/lzcapp/var` | M0; state loss never loses notes |
| No login (macOS user session) | LazyCat ingress authentication; no in-app login | M0; standalone password planned M5 |
| Retina 60fps native preview | Browser rendering, line-anchored scroll sync | M3; same task, not identical rendering |
| Multiple windows | Browser tabs, one note per tab | Accepted substitution |
| Menu bar / Dock | LazyCat launcher and in-app menus | Accepted substitution |
| System Trash | App trash at `.miaoyan/trash` | M2; restore is in-app |
| Global hotkey | None (browser sandbox) | Accepted; use the LazyCat app icon |

App-store constraints that shape the implementation:

- `document.private` is declared in `package.yml`; without the mount the app
  refuses to start instead of writing to ephemeral storage.
- `services` + explicit `entrypoint` are used because `application.image` does
  not execute the image ENTRYPOINT.
- `run_as: "1000:1000"` provides `/lzcapp` owner mapping; no `setup_script`.
- Every submitted image must exist under `registry.lazycat.cloud`; the build
  script copies and rewrites the reference.
- Passwordless entry is required, so the app trusts ingress identity rather
  than showing its own login page.

## 17. Verification checklist (updated per milestone)

- [ ] Import a real MiaoYan library with nested projects, images, audio, math,
      Mermaid, PlantUML, Markmap, PPT, alerts, and footnotes.
- [ ] Edit a note, watch autosave, kill the connection, verify no text is lost.
- [ ] External `echo >> note.md` refreshes the open note; a dirty editor gets a
      conflict backup and a clear dialog.
- [ ] Rename a linked note; backlinks still resolve.
- [ ] Search Chinese 2-character and 4-character queries.
- [ ] Restore a deleted note and an old version.
- [ ] Present a deck; exit; print to PDF.
- [ ] Upload/paste an image and an audio file; play audio inline.
- [ ] Run `papyra-ingest` twice on the same source; no duplicate note.
- [ ] 10k-note library opens, searches, and scrolls within targets.
