# MiaoYan Web — Compatibility Matrix

> MiaoYan is a native macOS app. "1:1" here means **the same tasks through the
> same information architecture and interaction flow**, with browser-safe
> substitutes where a native capability does not exist. This file is updated as
> each milestone lands. Status: `Done`, `Partial`, `Substituted`, `N/A (native)`.

**M1 status (2026-11-05):** the import → edit → attachment main line is
complete and exercised in a browser against an imported sample vault:
three-pane UI, create/rename/delete/restore, 650 ms autosave, live split
preview, image drag-and-drop to `i/`, external edit refresh, conflict backup
with “载入磁盘版本 / 保留我的文本”, light/dark/system themes, and a single-pane
mobile layout.

**M2 status (2026-11-05):** wikilinks, backlinks, unresolved links, FTS5 search
(trigram + LIKE fallback), the ⌘K command palette, version history with diff and
restore, pin, duplicate, sort, drag-to-project, resizable panes and `⇧⌘N` are
implemented. Browser checks: 14/14 main-line plus 11/11 for pin/duplicate/project
shortcut. Server suite: 19 passing. On a generated 500-note library, backlinks
returned exactly the 60 real `[[中心]]` links (plain-text mentions ignored) and
searches stayed at 2–3 ms median / 39 ms max. The nested-folder tree UI remains
deferred (see rows marked `Partial`).

**M3 status (2026-11-05):** KaTeX, Mermaid, local PlantUML, Markmap, GitHub
alerts, footnotes, code highlighting, code-copy buttons, image lightbox, audio,
reveal.js PPT with config/slide/fragment directives, focus/typewriter modes,
font settings, outline, formatting toolbar, `/` menu, tidy, line-anchored
scroll sync, and Markdown/HTML/PDF export are implemented. Browser checks:
18/18 rendering, 18/18 editor UX, 16/16 PPT, 3/3 PPT sanitisation; server suite
26/26. Remaining differences are in the PPT and export rows: code line-range
stepping, speaker notes/zoom plugins, iframe backgrounds, and the fact that the
exported HTML references KaTeX fonts at the app's absolute URL.

**M5 status (2026-11-05):** optional local OCR/Whisper, constant-time password
and API-key auth, large-note gates, word count/reading time, MiaoYan's
typography cleaner, the remaining slash templates, unused-attachment cleanup,
whole-vault zip export, and Chinese/English UI are implemented. The 10k-note
benchmark met every target. Server suite 47/47. The real LazyCat container
mount and `.lpk` install remain the first release-checklist items because no
container runtime was available on this workstation.

**M4 status (2026-11-05):** the Papyra-compatible subset used by
`papyra-ingest` is implemented and was exercised with the real client against a
local instance. Notes created by the API carry a small managed frontmatter
(id/title/tags/color/kind/archived/created/updated) whose id survives native
renames and moves; foreign frontmatter keys are preserved. `GET /api/notes`
returns full bodies (the SPA asks for `?summary=1`), `PUT` accepts
`If-Match` and creates missing notes, and root media is content-addressed with
`GET/HEAD /api/media/{filename}`. A real text ingest, a real image upload
(`uploaded:1`, then `reused:1` on republish) and repeated publishes reused the
same note id. Server suite 32/32. Remaining differences: `archived` has no
native UI yet (stored and returned only), API-key enforcement is M5, and
Papyra's `DELETE /api/categories?fromNotes=true` tag-stripping variant is not
implemented (only the registry colour is removed).

Reference sources: MiaoYan `README_CN.md`, `ARCHITECTURE.md`,
`skills/miaoyan/SKILL.md`, `Resources/Initial/*.md`, `AGENTS.md`, and the
relevant Swift sources (`Business/Storage.swift`, `Business/Note.swift`,
`Helpers/NameHelper.swift`, `Packages/MiaoYanCore/Frontmatter.swift`).

## 1. Layout and navigation

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| Three columns: folders/projects, note list, editor | Same three columns with collapsible sidebar and list; pointer-resizable, persisted widths | Done |
| Sidebar shows All Notes, folders, Trash | All Notes, project rows with counts, Trash | Done (M1) |
| Brand row at the top of the sidebar | Wordmark "妙言 Web" + original feather mark (not MiaoYan's asset) | Done (M1) |
| Note list shows title, excerpt, modified time, pin | Same; virtualised; relative time; pin icon on pinned notes | Done |
| Sort by modified / created / title | Same menu, plus pinned notes always first | Done (M2) |
| Drag note to a folder | HTML5 drag to a project row or 未分类; the move is atomic on the server | Done (M2) |
| ⌘1 / ⌘2 collapse sidebar / list | Same logical action; keyboard aliases documented below | Done (M1) |
| Window title bar is part of the chrome | Slim app bar with breadcrumb and actions; no traffic lights | Substituted |
| Native macOS window/menu bar | In-app menus: title-bar action menu, context menus, help dialog | Substituted |

## 2. Notes and projects

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| New note (⌘N) in the selected folder | Same: empty title creates an uppercase UUID file displayed as "Untitled Note" | Done (M1) |
| New folder (⇧⌘N) | ⇧⌘N opens the sidebar's 新建项目 dialog; the `+` button remains | Done (M2) |
| Rename note (⌘R) by filename | Same; ⌘R opens the rename dialog; atomic rename; refuses when the destination exists (MiaoYan also refuses); `:` on disk displays as `/` | Done |
| Duplicate note (⌘D) -> `Name Copy`, `Name Copy 2` | Same naming, byte-faithful copy; ⌘D on the open note and a 创建副本 menu item | Done (M2) |
| Delete (⌘⌫) -> system Trash | App trash `.miaoyan/trash/<timestamp>-<id>/` + `meta.json`, restorable in-app | Substituted |
| Restore from Trash | Trash view with restore; collision restores beside the existing file | Done (M1) |
| Note = file; title = filename | Same | Done (M1) |
| Root-level notes are valid | Same; shown in All Notes | Done (M1) |
| Nested folders inside a project | Recursive scan and per-note relative assets; a top-level project row lists all descendants as one flat list. A collapsible sub-folder tree is deferred: the same notes are reachable, only the hierarchy in the sidebar is missing | Partial (deferred; recorded substitution) |
| Pin note | Same; pin survives rename and reload, pinned-first ordering; stored in the index (`pinned`), not in the Markdown file (MiaoYan also keeps UI state outside the file) | Done (M2) |
| Empty note fallback title | UUID file displays as "Untitled Note" (same as MiaoYan); file name remains the source of truth | Done (M1) |
| Reveal in Finder | "Reveal path" shows the relative vault path and copies it; no OS file manager integration | N/A (native) |
| Open note in a separate window (⌘O) | Opens `/note/{id}` in a new browser tab | Substituted |
| Finder file drop import | Drop `.md`/images/audio on the note list; imported into the selected project | Substituted |
| Single-file mode (open one .md) | Not supported; the vault model is the product | N/A (native) |

## 3. Editor

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| Source editing, never WYSIWYG | CodeMirror 6 source editing, same philosophy | Done (M1) |
| Markdown syntax highlighting (MiaoYan palette) | CodeMirror Markdown with a theme-aware highlight style (headings/emphasis/links/code/quote); the exact MiaoYan palette is not copied | Substituted (M1) |
| Split editor/preview (⌘\) | Same; split is the desktop default; ⌘\ toggles split/editor | Done (M1) |
| Editor-only / preview-only (⌘3) | Same toolbar modes; ⌘3 alias added | Done (M3) |
| Active line, bracket matching, undo/redo | Bracket matching, undo/redo, multiple selections; active line is deliberately unhighlighted for a quieter canvas | Partial (M1) |
| Search/replace in editor (⌘F) | CodeMirror search panel; browser find remains available via ⌘F when focus is outside the editor | Done (M1) |
| Autosave (debounced) | 650 ms debounce, status 未保存/正在保存/已保存/有冲突; flushes on note switch and `pagehide` | Done (M1) |
| Typing performance gates (1 MB, 5000 lines) | > 1 MB: skip syntax highlighting and auto diagram render; > 5000 lines: simplified highlight and no `/` menu | Done (M5) |
| Toolbar formatting (bold, italic, strikethrough, code, link, image, etc.) | H1–H3, bold, italic, strikethrough, inline code, link, quote, bullet/ordered/task list, table, math, divider, tidy, plus a `/` hint; images are inserted by paste/drag rather than a toolbar button | Partial (M3) |
| Slash templates + Tab (`/table`, `/task`, `/time`, `/img`, `/video`, `/mermaid`, `/plantuml`, `/markmap`, `/fold`) | `/` opens a CodeMirror menu with MiaoYan's templates (and extra headings/lists/date/alert/PPT entries): `/time`, `/table`, `/task`, `/img`, `/video`, `/plantuml`, `/fold`, `/mermaid`, `/markmap`, plus `/h1`–`/h3`, `/ul`, `/ol`, `/quote`, `/code`, `/math`, `/alert`, `/ppt`, `/divider`, `/date`, `/link` | Done (M5) |
| Word count / reading time | Word count and an estimated reading time in the editor header | Done (M5) |
| TOC / outline (⌘5) | Outline drawer in preview and editor; click to jump | Done (M3) |
| Focus mode / typewriter mode | Both; persisted per browser; focus hides both side columns and Esc/⌘⇧F exits | Done (M3) |
| Font size / line height / editor width settings | Same; persisted | Done (M3) |
| Auto-format (⇧⌘L, Prettier-like) | Tidy command normalises headings, list markers, blank runs and trailing spaces while leaving code fences, math and frontmatter intact; it is not a full Prettier port | Substituted (M3) |
| Native CJK IME handling | Browser IME; CodeMirror handles composition events correctly | Substituted |
| Smart typography cleaning | Format-bar action ports MiaoYan's `TypographyCleaner`: pangu spacing, fullwidth punctuation, em-dash/ellipsis normalisation and blank-run collapse, while protecting code, math, link targets, wikilinks, URLs, frontmatter and hard breaks | Done (M5) |

## 4. Preview and rendering

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| CommonMark + GFM (tables, tasks, footnotes, strikethrough, autolink) | markdown-it + task-list + footnote plugins; tables, tasks, strikethrough, autolinks and footnotes render | Done (M3) |
| Code highlight (Highlightr) | highlight.js with a 24-language core subset and light/dark palettes; a copy button appears on hover | Done (M3) |
| KaTeX inline/block math | KaTeX with `$...$`, `$$...$$`; currency heuristic avoids false positives | Done (M3) |
| Mermaid diagrams | mermaid 11, client-side, theme-aware, lazy, strict security level | Done (M3) |
| PlantUML diagrams | Local `plantuml.jar` over stdin; SVG returned by `/api/render/plantuml` and sanitised; no public server. A missing jar shows “PlantUML is not installed” in the block | Done (M3) |
| Markmap mind maps | markmap-lib + markmap-view, interactive, theme-aware (dark palette via `.markmap-dark`) | Done (M3) |
| GitHub alerts NOTE/TIP/IMPORTANT/WARNING/CAUTION | Same rendering (tinted block, coloured title, no stripe) | Done (M3) |
| `<details>/<summary>` passthrough | Allowed by the sanitiser | Done (M3) |
| Frontmatter hidden when it looks like YAML | Same rule as `MiaoYanCore.Frontmatter`; a `---` deck opener without keys stays content | Done (M1) |
| Image display, remote images | Relative `i/` / `files/` images resolve to the note's asset endpoint; remote images display with lazy loading; click opens a lightbox (Esc closes) | Done (M3) |
| Audio playback for `![[file.m4a]]` | Image-token audio (`![](...m4a)`) becomes an inline `<audio controls>`; plain audio links remain links | Done (M3) |
| Video embedding | Not stored; remote video links remain links; local audio extraction is M5 | Substituted |
| Two-way scroll sync at 60 fps | Line-anchored sync via token `data-source-line` maps; editor→preview follows the visible line and preview→editor jumps on scroll; a short guard prevents feedback loops. Inside a very long paragraph the anchor drifts | Partial (M3) |
| Raw HTML allowed (responsive media, columns) | DOMPurify allowlist; scripts/iframes/forms/objects/`<style>` elements removed, event-handler attributes removed, inline `style` on safe elements preserved | Done (M3) |
| Iframe slide backgrounds (`data-background-iframe`) | Disabled for privacy; a styled placeholder is shown | N/A (security) |
| Print / export PDF | Browser print stylesheet and a print window ("另存为 PDF"); no bundled Chromium | Substituted (M3) |

## 5. Wikilinks, backlinks, unresolved links

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| `[[Exact title]]` links by title, no `.md` | Same parser; exact title first, case-insensitive fallback; no alias/heading syntax (per MiaoYan skill); code fences and inline code are excluded | Done (M2) |
| Click a wikilink to open the note | Same; `miaoyan://` / `nv://` routes are not used, in-app navigation instead | Substituted |
| Backlinks panel | Same; context excerpt and source line per backlink; updates when the source is renamed | Done (M2) |
| Unresolved link style | Dashed/muted link with "创建笔记" action; creates a note with that title in the current project | Done (M2) |
| Rename updates link index | Index updates on rename; note text elsewhere is not rewritten (MiaoYan also indexes rather than rewrites), so links to the old title become unresolved | Done (M2) |
| Links across any folder depth | Same; Trash excluded | Done (M2) |

## 6. Search

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| Note-list search filters within the selected folder | Same: project scope first, then title/body match; title matches rank above body matches | Done (M2) |
| Search reads unloaded note bodies off the main thread | Server-side FTS5; client never loads all bodies | Done (M2) |
| Command palette (⌘K in Web; MiaoYan uses its search field) | ⌘K / Ctrl+K palette with title, snippet, project; arrow keys + Enter; recent notes when empty | Done (M2) |
| Chinese search | FTS5 trigram tokenizer + LIKE fallback (with `%`/`_` escaping) for 1–2 character queries | Done (M2) |
| Result speed | Measured 2–3 ms median, 39 ms max on a 500-note library; 10k-note target still to be benchmarked | Partial (M2; meets target at 500 notes) |

## 7. Versions, conflicts, external changes

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| `NoteVersionManager` per-note history in Application Support | `{state}/versions/<note-id>/`, newest 50 or, if older, 30 days; the pre-edit text is snapshotted too, so the first save is undoable | Done (M2) |
| Move history with rename/folder move/trash | Version directories are migrated when a rename/move changes the path-derived ID | Done (M2) |
| Version list + restore | Dialog with timestamp, size, preview; diff vs current; restore snapshots current first | Done (M2) |
| `FileSystemEventManager` watcher | Server `FileSystemWatcher` with 300 ms debounce, SSE `change` events, plus a 1 s self-write suppression window | Done (M1) |
| Ignore own writes | Path + one-second suppression window after an API write | Done (M1) |
| Conflict backup while editing | `.miaoyan-conflicts/<project>-<note>-<timestamp>.md` on 412, dialog offering 载入磁盘版本 / 保留我的文本 | Done (M1) |
| External change when clean | Quiet reload of editor and preview; CodeMirror's external-value sync keeps the cursor near the edit, but exact cursor line anchoring after an external rewrite is still approximate (split-view scroll sync is line-anchored) | Partial |

## 8. Themes and display

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| Light / dark / follow system | Same; CSS variables + `prefers-color-scheme`; explicit override via toolbar menu; follows system live | Done (M1) |
| Warm paper palette (`#FDFCF9` / `#1E1D1B`), sand selection | Warm paper palette in the same family (`#f6f5f1` / `#171815`), sand-green selection; exact values differ | Substituted (M1) |
| MiaoYan green for brand/active buttons | Same accent role (`#2f7d63` light / `#7cc4a6` dark); exact hex differs (no copied assets) | Substituted (M1) |
| Five UI languages | Chinese (default) + English; structure ready for more | Partial (v1) |
| Code font follows text font option | Web uses a dedicated code stack; a "code follows text" toggle is omitted in v1 | N/A (native font stack) |

## 9. PPT mode

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| `---` on its own line separates slides | Same, outside fenced code and frontmatter | Done (M3) |
| ⌘⌥P opens PPT | Ctrl/Cmd+Alt+P; also the toolbar button | Done (M3) |
| Only notes with `---` can present | The toolbar button is always enabled; a note without separators presents as a single full-screen slide instead of being blocked | Superset (M3) |
| Leading HTML comment config for Reveal | `transition`, `backgroundTransition`, `transitionSpeed`, `controls`, `progress`, `slideNumber` parsed from `key: value` comments, including nested dot keys; `history`/`hash` are forced off to protect the SPA router | Partial (M3) |
| `.slide:` background directives | Same (`data-background` colours/gradients/images); iframe and local-video backgrounds are dropped | Done (M3) |
| `.element:` fragments | Works for paragraphs and list items, including stacked attributes and authored fragment order; arbitrary element injection is not attempted | Partial (M3) |
| Code line ranges `[1|2-3]` | Code is highlighted, but the range suffix is left in the info string and cannot advance line-by-line (reveal's highlight plugin is not bundled) | Partial (M3) |
| Enter shows slide overview, Esc exits | Enter opens reveal's overview; Esc is captured first and closes the deck (second Esc would leave overview) | Substituted (M3) |
| Print deck to PDF | “打印” adds a print stylesheet and opens the browser print dialog; all slides stack for “Save as PDF” | Substituted (M3) |
| Background iframes | Dropped (privacy/security); colour and image backgrounds supported | N/A (security) |
| Full-screen native presentation | Browser fullscreen API on the deck overlay; Esc exits fullscreen and the deck | Substituted (M3) |

## 10. Attachments and ingest

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| Images in `i/`, other files in `files/` | Same, relative to the note's folder; server forces the `i`/`files` subdirectory from the MIME kind | Done (M1) |
| Paste image | Clipboard image -> upload -> `![](i/name.png)` inserted at the cursor | Done (M1) |
| Drag image/file | Drag/drop into the editor, inserted at the drop position; other files become `[name](files/name)` | Done (M1) |
| Root-relative `/i/...` references | Same in Markdown; the Web resolver maps them to the note-relative `/api/notes/{id}/assets/...` endpoint | Done (M1) |
| Path traversal / symlink escape | Asset names sanitised; resolved path must stay inside the note's `i`/`files` directory; symlinks rejected | Done (M1) |
| Media cleanup for unused attachments | Sidebar action scans every note body (including Trash references by filename), then moves unreferenced files from `i/` and `files/` to `.miaoyan/trash-assets/<timestamp>/`; the scan aborts if any note cannot be read | Done (M5; app trash instead of the OS Trash) |
| Audio attachment playback | Inline player in preview | Done (M3) |
| Video support | Raw `<video>`/`<source>` HTML plays in preview (relative `i/`/`files/` paths resolve to the asset endpoint); uploaded video stays in `files/`; no ffmpeg audio extraction is needed because transcription reads the video directly | Done (M5) |
| OCR images | Optional local `tesseract` command (`MIAOYAN_OCR_CMD`), off by default; Format/settings action inserts the text at the cursor | Done (M5; opt-in) |
| Whisper transcription | Optional local command (`MIAOYAN_WHISPER_CMD`), audio or video, 10-minute timeout; the transcript is inserted at the cursor | Done (M5; opt-in) |
| URL/text ingestion (Papyra ecosystem) | Not built in; `papyra-ingest` works through the compatibility API, including local media uploads and repeated publishes | Done (M4) |

## 11. Import / export

| MiaoYan behaviour | MiaoYan Web implementation | Status |
|---|---|---|
| Library is a user-picked folder | `MIAOYAN_LIBRARY` points at the folder; an existing MiaoYan tree is adopted in place, never rewritten on import | Done (M1) |
| iCloud / WebDAV sync is external | The vault is a normal folder; external sync tools can be used; watcher catches changes | Substituted |
| Export note (Markdown) | Byte-faithful `.md` download | Done (M3) |
| Export HTML | One standalone HTML file with inline CSS, KaTeX CSS, rendered diagrams and same-origin images inlined as data URLs; remote images stay remote; KaTeX font URLs point at the running app | Done (M3) |
| Export PDF | A print window with the same standalone HTML, then the browser's “Save as PDF” dialog; no bundled Chromium | Substituted (M3) |
| Export all | Sidebar download streams `MiaoYan-export-<date>.zip`; walks the vault skipping dot-directories, `node_modules` and symlinks, so app trash/conflicts and state are excluded | Done (M5) |
| Swap library folder | Settings -> vault path is fixed by env in v1 (single library); import wizard supports merging | Partial (v1) |

## 12. Papyra-compatible API

| Method | Path | Contract | Status |
|---|---|---|---|
| GET | `/api/notes` | Full bodies plus `tags`, `color`, `kind`, `archived`, `updated`, `managedId`; `?summary=1` keeps the native cheap list | Done (M4) |
| GET | `/api/notes/{id}` | Note + `ETag`; `body` excludes managed frontmatter, `content` stays byte-faithful for the native editor; id may be the path hash or the managed id | Done (M4) |
| PUT | `/api/notes/{id}` | `{title,tags,body,color,pinned,archived,kind}`; `If-Match`; 412 on stale; create if missing; `?archive=false` is accepted (state is stored, not filtered) | Done (M4) |
| POST | `/api/media/upload` | multipart `file` -> `{filename,kind,mime,size,version,url,markdown,…}`; SHA-256 content dedupe | Done (M4) |
| HEAD/GET | `/api/media/{filename}` | 200 with length/type; bytes with Range; root, then project `i/`/`files/` fallback | Done (M4) |
| GET | `/api/search?q=` | `[{id,title,snippet,score,…}]`; score descends with result order | Done (M4) |
| POST | `/api/categories` | `{name,color}` idempotent upsert; GET unions registered tags with tags found in notes; DELETE removes the registry colour only | Done (M4) |
| GET | `/note/{id}` | SPA note route; resolves native and managed ids through `GET /api/notes/{id}` | Done (M4) |
| Auth | `X-API-Key` | Constant-time check against `MIAOYAN_API_KEY` when configured; browsers can also log in with `MIAOYAN_WEB_PASSWORD`, which issues an HMAC session cookie (rate-limited, Origin-checked writes). Unset credentials keep LazyCat passwordless behind the signed user gateway | Done (M5) |

Acceptance: real `papyra-ingest` publishes text and a local image, updates the
same note on re-publish, shows the uploaded image, and does not create
duplicate notes. The real-client run was done with a text source and a local
PNG; X/Bilibili network sources were not exercised here because they depend on
external sites and credentials.

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
| No login (macOS user session) | LazyCat ingress authentication; no in-app login unless `MIAOYAN_WEB_PASSWORD` is set for a standalone deployment | Done (M5) |
| Retina 60fps native preview | Browser rendering, line-anchored scroll sync | M3; same task, not identical rendering |
| Multiple windows | Browser tabs, one note per tab | Accepted substitution |
| Menu bar / Dock | LazyCat launcher and in-app menus | Accepted substitution |
| System Trash | App trash at `.miaoyan/trash` | M1; restore is in-app |
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
- [x] Edit a note, watch autosave, kill the connection, verify no text is lost.
- [x] External `echo >> note.md` refreshes the open note; a dirty editor gets a
      conflict backup and a clear dialog (M1; also covered by a server test).
- [x] Rename a linked note; renaming the source keeps its backlink; renaming the
      target leaves old-title links unresolved by design (both covered by a
      server test).
- [x] Search Chinese 2-character and 4-character queries.
- [x] Restore a deleted note and an old version.
- [ ] Present a deck; exit; print to PDF.
- [ ] Upload/paste an image and an audio file; play audio inline.
- [ ] Run `papyra-ingest` twice on the same source; no duplicate note.
- [x] 10k-note API targets: cold list 378 ms, warm list 22 ms, full-body list
      94 ms, warm 2-char search 20 ms, 4-char search 28 ms, note fetch 4 ms.
      Browser scroll smoothness on the same library remains a manual check.
- [ ] Real LazyCat container: `/lzcapp/documents/<uid>` mount, `.lpk` install,
      passwordless gateway entry, upgrade persistence.
- [x] Unused-attachment cleanup keeps referenced files and moves an orphan to
      `.miaoyan/trash-assets` (server test).
- [x] Whole-vault zip export includes notes and excludes dot-directories
      (server test).
