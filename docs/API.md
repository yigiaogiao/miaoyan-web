# API

所有接口同源，无需 CORS。除 `/healthz` 和 `/api/auth/*` 外，懒猫请求由网关注入受信任的
`X-HC-User-ID`；独立部署设置 `MIAOYAN_WEB_PASSWORD` 或 `MIAOYAN_API_KEY` 后启用鉴权：
浏览器用密码登录获得 HMAC 会话 cookie，机器客户端发送 `X-API-Key`。未配置任何凭据时
（懒猫的默认形态）不拦截请求。错误统一返回 JSON：

```json
{ "error": "code", "message": "human readable detail" }
```

## 原生 Web API（M0–M3）

### `GET /healthz`

```json
{
  "status": "ok",
  "version": "0.1.0",
  "startedAt": "2026-11-05T11:05:25Z",
  "uptimeSeconds": 3,
  "vault": {
    "mode": "lazycat | explicit | development",
    "documentsRoot": "/lzcapp/documents",
    "explicitLibrary": null,
    "state": "/lzcapp/var/miaoyan",
    "userCount": 2,
    "defaultUid": null
  },
  "sqlite": { "version": "3.53.3", "fts5": true, "trigram": true },
  "capabilities": { "plantuml": true, "whisper": false, "ocr": false, "ffmpeg": true }
}
```

`/api/health` 302 到 `/healthz`。健康检查不解析用户，因此无需请求头。

### 鉴权（M5）

| 方法 | 路径 | 说明 |
|---|---|---|
| `GET` | `/api/auth/status` | `{required, authenticated, password, apiKey}`；SPA 启动时用它决定是否显示登录页 |
| `POST` | `/api/auth/login` | `{secret}`；密码或 API key 均可登录。失败返回 401，同一 IP 5 分钟内失败 8 次后返回 429 |
| `POST` | `/api/auth/logout` | 清除会话 cookie |

- 常量时间比较；`MIAOYAN_WEB_PASSWORD` / `MIAOYAN_API_KEY` 为空时不启用对应入口。
- 会话是 HMAC 签名的无状态 cookie（30 天），密钥保存在 `state/.auth-secret`；状态目录保留则重启不掉线。
- 使用 cookie 的写请求会校验 `Origin` 与当前站点一致，跨站写返回 403；带 `X-API-Key` 的机器调用不受此限制。
- 日志只记录凭据是否配置，不记录密码、API key、cookie 或笔记正文。

### 用户与书库解析

`/api/*` 每次请求都解析一次书库：

- 独立部署：`MIAOYAN_LIBRARY` 指定的目录。
- 开发：未设置 `MIAOYAN_LIBRARY` 时使用 `dev-library/`。
- 懒猫：`X-HC-User-ID`（缺省回退 `MIAOYAN_DEFAULT_UID`）映射到
  `/lzcapp/documents/<uid>/MiaoYan`，并按需创建。

原生 ID 由书库内相对路径哈希得到（`n` + 12 个十六进制字符）。重命名或移动会改变 ID；
客户端应使用响应里的新 ID 跳转，并对旧 ID 的 404 做刷新。

Papyra 兼容层写入的笔记会在 frontmatter 里带一个托管 `id`。该 id 是路径哈希之外的
**别名**：`GET/PUT /api/notes/{managed-id}` 在重命名/移动后仍然可用，而原生 ID 规则不变。
普通导入笔记不会被改写，只有经过兼容 API 创建或更新的笔记才带托管元数据。

### 已实现的 Web API

| 方法 | 路径 | 说明 |
|---|---|---|
| `GET` | `/api/vault` | `{name, root, noteCount, projectCount}` |
| `GET` | `/api/projects` | 项目列表（顶层文件夹，含递归笔记数） |
| `POST` | `/api/projects` | `{name}`，空名自动生成“新建项目 N” |
| `PATCH` | `/api/projects/{id}` | `{name}` 重命名；目标已存在返回 409 |
| `DELETE` | `/api/projects/{id}` | 整个文件夹移入 `.miaoyan/trash`，返回 204 |
| `GET` | `/api/notes?project=&q=` | 完整笔记列表（含 `body`，Papyra 兼容）；`project` 为空表示全部，`q` 匹配标题/正文；置顶笔记排在最前 |
| `GET` | `/api/notes?summary=1` | 原生 SPA 使用的轻量列表，只含元数据和 `excerpt`，不读取正文 |
| `POST` | `/api/notes` | `{projectId?, title?}`；空标题使用大写 UUID 文件名，显示为 “Untitled Note” |
| `GET` | `/api/notes/{id}` | 正文 + 元数据 + `ETag` 响应头 |
| `PUT` | `/api/notes/{id}` | 原生：`{content, etag}`；Papyra：`{title,tags,color,pinned,archived,body,kind}` + `If-Match`，缺失时创建；冲突返回 412 和冲突 JSON |
| `PATCH` | `/api/notes/{id}` | `{title?, projectId?, pinned?}` 重命名/移动/置顶；目标存在返回 409；可能返回新 `id` |
| `POST` | `/api/notes/{id}/duplicate` | 创建副本，返回 201 与新笔记；命名为 `Name Copy`、`Name Copy 2`… |
| `DELETE` | `/api/notes/{id}` | 移入 `.miaoyan/trash`，返回 204 |
| `GET` | `/api/links` | wikilink 索引：已解析目标与未解析标题 |
| `GET` | `/api/notes/{id}/backlinks` | 反向链接（来源笔记、行号、上下文片段） |
| `GET` | `/api/search?q=&project=&limit=` | FTS5 搜索；`limit` 默认 50，限制在 1–500 |
| `GET` | `/api/notes/{id}/versions` | 版本列表，最新在前 |
| `GET` | `/api/notes/{id}/versions/{versionId}` | 版本正文 |
| `POST` | `/api/notes/{id}/versions/{versionId}/restore` | `{etag?}` 恢复；当前内容先存为版本；返回恢复后的笔记 |
| `POST` | `/api/notes/{id}/assets?kind=image\|file` | multipart 字段 `file`；返回 `AssetDto` |
| `GET` | `/api/notes/{id}/assets/{**path}` | 读取笔记目录下 `i/` 或 `files/` 中的附件，支持 Range |
| `POST` | `/api/notes/{id}/ocr` | `{path}`；调用配置好的本地 OCR 命令，返回 `{kind:"ocr", text}`；未配置返回 503 |
| `POST` | `/api/notes/{id}/transcribe` | `{path}`；对音频或视频调用本地转写命令，返回 `{kind:"transcribe", text}`；未配置返回 503 |
| `GET` | `/api/maintenance/unused-assets` | 扫描全部笔记，返回 `{scannedNotes, files}`；`files` 是未被引用的 `i/`、`files/` 相对路径。任一笔记读取失败则返回 500 且不建议清理 |
| `POST` | `/api/maintenance/unused-assets/clean` | 重新扫描并把未引用附件移到 `.miaoyan/trash-assets/<timestamp>/`，返回 `{removed, failed}` |
| `GET` | `/api/export/vault` | 流式下载 `MiaoYan-export-YYYYMMDD.zip`；跳过 `.miaoyan*`、其他点目录、`node_modules` 与符号链接 |
| `GET` | `/api/trash` | 回收站条目（笔记与项目） |
| `POST` | `/api/trash/{id}/restore` | `{kind, noteId?, projectId?}`；重名时恢复到副本名 |
| `DELETE` | `/api/trash/{id}` | 彻底删除，返回 204 |
| `GET` | `/api/events` | SSE；具名事件 `change` |

`NoteDto` 字段：

```json
{
  "id": "n3f2a1c0b9d8",
  "title": "会议/周报",
  "path": "工作/会议:周报.md",
  "projectId": "p71c0d2a4e5f",
  "projectName": "工作",
  "folder": "工作",
  "content": "# 周报\n...",
  "etag": "c1d2e3f4...",
  "modifiedAt": "2026-11-05T10:00:00+00:00",
  "createdAt": "2026-10-01T08:00:00+00:00",
  "size": 1234,
  "isUntitled": false,
  "pinned": false,
  "hasBom": false,
  "body": "# 周报\n...",
  "tags": ["工作"],
  "color": null,
  "kind": "note",
  "archived": false,
  "secure": false,
  "trashed": false,
  "updated": "2026-11-05T10:00:00+00:00",
  "managedId": null
}
```

`content` 是磁盘上的完整文本（原生编辑器使用，包含 frontmatter）；`body` 是去掉
YAML frontmatter 后的正文（Papyra 客户端使用）。没有托管元数据的普通笔记，`body`
就是完整正文，`managedId` 为 `null`。

轻量列表项 `NoteSummaryDto` 与 `NoteDto` 相同，但没有 `content`、`etag`、`hasBom`，
并以 `excerpt` 代替正文；完整列表额外返回 `body`、`tags` 等 Papyra 字段。

`ETag` 由内容哈希计算，同时放在响应头（带引号）和 JSON 的 `etag` 字段里。
原生 `PUT` 优先读取 JSON 里的 `etag`；Papyra 兼容 `PUT` 读取标准的 `If-Match` 头。

保存冲突（412）返回：

```json
{
  "error": "conflict",
  "message": "The note changed on disk.",
  "currentEtag": "9a8b...",
  "conflictPath": ".miaoyan-conflicts/工作/会议-周报-20261105-100000.md",
  "currentContent": "..."
}
```

服务端在返回 412 前已把本次提交的文本写入 `conflictPath`，磁盘上的当前内容放在
`currentContent` 里；两边都不会被覆盖。

附件上传返回：

```json
{
  "url": "/api/notes/n3f2a1c0b9d8/assets/i/20261105-100000-abc.png",
  "markdown": "![](i/20261105-100000-abc.png)",
  "name": "20261105-100000-abc.png",
  "size": 20480,
  "kind": "image"
}
```

`kind=image` 时文件进入笔记旁边的 `i/` 并返回图片 Markdown；其他文件进入 `files/`，
返回链接 Markdown。`url` 可直接用于 `<img>`/`<audio>`，且带 `private, max-age=3600`。

回收站条目：

```json
{
  "id": "20261105-100000-abcd1234",
  "kind": "note | project",
  "title": "会议/周报",
  "originalPath": "工作/会议:周报.md",
  "deletedAt": "2026-11-05T10:00:00+00:00",
  "size": 1234
}
```

SSE 事件：

```text
retry: 3000

event: change
data: {"kind":"note|project|trash","noteId":"n...","path":"工作/x.md","at":"..."}
```

服务端每 20 秒发送 `: ping` 保活；前端断开后 3 秒重连。

### 链接、反向链接与搜索

`GET /api/links`：

```json
{
  "targets": [{ "title": "周报", "noteId": "n523a27722adb", "projectName": "工作", "path": "工作/周报.md" }],
  "unresolved": [{ "title": "还没写的笔记", "count": 1 }]
}
```

`[[标题]]` 只在正文中统计，代码围栏与行内代码里的内容会被忽略；标题匹配区分大小写优先，
不区分大小写仅作为回退。未解析标题用于预览中的“创建笔记”动作。

`GET /api/notes/{id}/backlinks`：

```json
[
  {
    "noteId": "n4e08abd6b4ba",
    "title": "欢迎",
    "projectName": "未分类",
    "line": 6,
    "excerpt": "相关：[[欢迎]]"
  }
]
```

`GET /api/search?q=量子纠缠&limit=40`：

```json
[
  {
    "id": "n4e08abd6b4ba",
    "title": "量子纠缠",
    "projectId": "root",
    "projectName": "未分类",
    "snippet": "…关于量子纠缠的正文…",
    "modifiedAt": "2026-11-05T10:00:00+00:00",
    "size": 1234,
    "isUntitled": false
  }
]
```

三个及以上字符使用 FTS5 trigram；一至两个字符回退到 `LIKE`，`%`、`_`、`\` 会被转义。
标题精确匹配、标题包含匹配排在正文匹配之前。

### 版本

`GET /api/notes/{id}/versions`：

```json
[
  { "id": "638900000000000000-a1b2c3d4", "createdAt": "2026-11-05T10:00:00+00:00", "size": 1234, "etag": "a1b2…", "isCurrent": true }
]
```

`GET /api/notes/{id}/versions/{versionId}` 返回 `{id, content, createdAt, size, etag}`。
`POST …/restore` 接受 `{etag?}`；恢复前会把当前内容存为版本，`etag` 不匹配同样返回 412。
重命名或移动导致笔记 ID 变化时，版本目录会一起迁移；保留最新 50 个版本或 30 天内的版本。

### 导出

单篇 Markdown/HTML/PDF 与 PPT 在浏览器端完成；完整书库由 `GET /api/export/vault`
在服务端打包下载。zip 内是书库根目录下的普通文件，应用自己的 `.miaoyan`（回收站、
清理暂存）和 `.miaoyan-conflicts` 不会进入导出；需要完整备份时仍应复制目录（见
[BACKUP.md](BACKUP.md)）。

## Papyra 兼容 API（M4，已完成）

目标：现有 `papyra-ingest` 不改代码即可发布笔记。已完成真实客户端验证。

| 方法 | 路径 | 说明 |
|---|---|---|
| `GET` | `/api/notes` | 完整笔记列表，含 `body`、`tags`、`color`、`kind`、`archived`、`updated`、`managedId` |
| `GET` | `/api/notes/{id}` | 笔记 + `ETag`；id 可以是原生路径哈希或托管 id |
| `PUT` | `/api/notes/{id}` | `{title,tags,color,pinned,archived,body,kind}`；`If-Match`；缺失时创建；`?archive=false` 可接受 |
| `POST` | `/api/media/upload` | multipart 字段 `file`；返回 `{filename, kind, mime, size, version, url, markdown, …}` |
| `HEAD` | `/api/media/{filename}` | 200 + `Content-Length`/`Content-Type`（含 `ETag`） |
| `GET` | `/api/media/{filename}` | 媒体内容，支持 Range；根目录、项目 `i/`/`files/` 依次回退 |
| `GET` | `/api/search?q=` | `[{id,title,snippet,score,…}]` |
| `GET` | `/api/categories` | 注册分类与笔记 tag 的并集，附 `count` |
| `POST` | `/api/categories` | `{name,color}` 幂等写入；分类颜色存 `state/categories.json` |
| `DELETE` | `/api/categories/{name}` | 只删除注册颜色，不改写笔记 tag |
| `GET` | `/note/{id}` | SPA 笔记路由；兼容 id 通过 `GET /api/notes/{id}` 解析 |

兼容细节：

- `PUT` 在 `409/412/428` 时客户端重试；服务端用 `If-Match` 判定并保留冲突备份。
- 缺失笔记的 `PUT` 视为创建，避免首次发布失败。
- 上传文件名由服务端生成（`<stem>-<sha256[..12]><ext>`），客户端必须使用响应中的
  `filename`；相同内容重复上传返回已有文件。
- `papyra-ingest` 的 `/api/media/<name>` 与 `![[name]]` 引用会重写为
  `i/<name>` / `files/<name>`，Markdown 预览及导出都能解析。
- 托管 frontmatter 键为 `id/title/tags/color/kind/archived/created/updated`；
  其他 frontmatter 键逐行保留。
- `POST /api/media/upload` 只接受白名单扩展名（常见图片、音频、视频和文档），
  响应带 CSP `sandbox`，防止上传的 HTML/SVG 成为脚本源。
- 设置 `MIAOYAN_API_KEY` 后，所有 `/api/*`（健康检查与 `/api/auth/*` 除外）都要求
  `X-API-Key` 常量时间匹配；未设置时不校验。懒猫部署由网关注入的 `X-HC-User-ID`
  提供用户隔离，通常不设置该变量。
- `archived=true` 会写入 frontmatter 并在 API 返回，但原生 UI 暂未提供归档视图。
- 没有实现 Papyra 的完整分类管理（例如 `DELETE /api/categories?fromNotes=true`
  同时移除笔记 tag）；当前 DELETE 只清除注册颜色。

## 约定

- 时间：ISO 8601 UTC。
- ID：原生接口使用路径哈希；Papyra 兼容层创建/更新的笔记另写托管 frontmatter `id`，
  重命名/移动后仍可作为别名访问。普通导入笔记不会被写入任何托管字段。
- 写操作原子替换：同目录临时文件 + `File.Move`。
- 路径参数经过规范化和符号链接检查，任何逃逸出书库的路径返回 400。
- 附件路径只允许笔记目录下的 `i/` 与 `files/`；文件名由服务端清洗，符号链接被拒绝。
- 上传大小上限由 `MIAOYAN_MAX_UPLOAD_MB` 控制。
- 错误统一为 `{error, message}`；保存冲突额外返回 `currentEtag`、`conflictPath`、`currentContent`。
