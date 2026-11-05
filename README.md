# 妙言 Web · MiaoYan Web

以 [妙言 MiaoYan](https://github.com/tw93/MiaoYan) 的功能与交互为基准的自托管 Markdown 知识库，可以部署在懒猫微服（LazyCat）应用商店，也可以单容器运行在自己的服务器上。

笔记就是普通的 `.md` 文件，目录就是项目，数据始终在自己手里。本项目是独立实现，不修改妙言或 Papyra 的源码。

> 当前状态：**M5 发布候选**。可以直接指向一份现有的妙言书库，浏览、编辑、保存和添加附件；wikilink、反向链接、全文搜索、命令面板、版本历史/对比/恢复、置顶、副本、排序和拖动移动均已可用；数学公式、Mermaid/PlantUML/Markmap、代码高亮、图片灯箱、音视频、专注/打字机模式、大纲、格式工具栏、PPT 演示和 Markdown/HTML/PDF/书库 zip 导出也已可用。`papyra-ingest` 可以不改代码，通过兼容 API 创建/更新笔记、上传图片和音频；笔记 frontmatter 中的托管 `id` 在重命名/移动后仍然有效。M5 补齐了中英文界面、密码与 API key 鉴权、可选本地 OCR/转写、排版清理、未使用附件清理和 10k 笔记性能验证。进度见 [PLAN.md](PLAN.md)，与妙言的功能对照见 [COMPATIBILITY.md](COMPATIBILITY.md)。

## 现在能做什么

- 三栏书库：项目（顶层文件夹）、笔记列表、编辑器。笔记列表虚拟滚动，按项目过滤，面板宽度可拖动并记忆。
- 源码编辑 + 实时预览，编辑/分栏/预览三种模式；650 ms 防抖自动保存，⌘S 立即保存。
- 新建（空标题自动使用大写 UUID 文件名，显示为 “Untitled Note”）、⌘R 重命名、⌘D 创建副本、删除到应用内回收站并恢复。
- `[[标题]]` 双向链接：点击打开、反向链接面板、未解析链接一键创建笔记；只索引正文，代码块中的链接会被忽略。
- 全文搜索：FTS5（trigram）+ 短中文 LIKE 回退，标题匹配优先；⌘K 命令面板支持键盘导航与最近笔记。
- 版本历史：每次保存前快照，可对比当前内容并恢复；历史和笔记一起随重命名迁移。
- 置顶、按修改/创建时间或标题排序（置顶始终在前）、把笔记拖到左侧项目完成移动。
- 图片粘贴/拖拽上传到笔记旁的 `i/`，其他文件进入 `files/`；预览按笔记相对路径解析资源。
- 外部修改通过文件监听 + SSE 实时刷新；本地未保存时以 412 冲突返回，备份到 `.miaoyan-conflicts/`，可选择载入磁盘版本或保留本地文本。
- 预览支持 KaTeX 行内/块级公式、Mermaid、Markmap、GitHub alerts、脚注、代码高亮与一键复制、图片灯箱和内联音频。PlantUML 只调用容器内的本地 JAR，不会把笔记内容发到公共渲染服务；未安装时在代码块内明确提示。
- PPT 演示：`---` 分页，支持开头配置注释、`.slide:` 背景、`.element:` 片段和原始 HTML 布局；⌘⌥P 打开，Esc 退出，可全屏或打印为 PDF。
- 专注模式 / 打字机模式、字号/行高/编辑宽度设置（均持久化）、大纲跳转、格式工具栏、`/` 快捷模板和 Markdown 整理；分栏时编辑区与预览双向滚动同步。
- 导出 Markdown（字节一致）、独立 HTML（内联样式、KaTeX、图表与本地图片）、PDF（浏览器打印）和整个书库 zip；PPT 可打印为 PDF。
- 未使用附件清理：扫描全部笔记后把无引用文件移到 `.miaoyan/trash-assets`，可恢复。
- 中英文界面（默认中文，可在设置切换）；可选本地 OCR / 音频转写，命令只从环境变量读取，不经过 shell。
- 独立部署可设置网页密码和 API key；懒猫保持网关登录。可选大笔记性能门限：超过 1 MB 或 5000 行时自动降级高亮。
- Papyra 兼容 API：`papyra-ingest` 可直接创建/更新笔记（`If-Match` 冲突重试）、上传图片/音频；媒体按内容哈希去重，重复发布不会重复上传；`GET /api/media/{filename}` 提供预览，`/note/{id}` 直接打开笔记。
- 亮色 / 暗色 / 跟随系统主题，移动端自动切换为单栏。
- `GET /healthz`：返回版本、书库模式、SQLite/FTS5 能力、PlantUML/ffmpeg 等可选能力。
- 单容器同时提供 SPA、API 与媒体文件。
- 懒猫按用户隔离：从 `X-HC-User-ID` 解析 `/lzcapp/documents/<uid>/MiaoYan`，一个容器可服务多位用户。
- Dockerfile、docker-compose、懒猫 `package.yml` / `lzc-manifest.yml` / `lzc-build.yml` 与构建脚本。

## 技术栈

- 服务端：.NET 10 Minimal API、SQLite + FTS5（trigram）、文件系统为唯一事实来源。
- 前端：React 19、TypeScript、Vite、CodeMirror 6、markdown-it、KaTeX、Mermaid、Markmap、reveal.js、DOMPurify、TanStack、Zustand、Tailwind 4、Radix。
- 部署：单容器，无 Redis / 外部数据库 / 对象存储。PlantUML 在本地 JAR 内渲染，不调用公共渲染服务。

## 本地开发

要求：.NET SDK 10、Node.js 22+、npm 10+。

```bash
./scripts/dev.sh
# API:  http://127.0.0.1:5080/healthz
# 前端: http://127.0.0.1:5173
```

开发脚本默认把书库放在 `dev-library/`，状态放在 `dev-state/`，两者都已加入 `.gitignore`。

分别构建与测试：

```bash
npm --prefix web ci
npm --prefix web run build          # 输出到 server/MiaoYan.Web/wwwroot
dotnet test Miaoyan.slnx
dotnet run --project server/MiaoYan.Web --urls http://127.0.0.1:5080
```

完整发布产物（SPA 已内嵌）：

```bash
./scripts/build.sh
ASPNETCORE_URLS=http://127.0.0.1:5080 dotnet artifacts/publish/MiaoYan.Web.dll
```

## 单容器 / Docker Compose

```bash
docker compose up -d --build
# http://127.0.0.1:5080
```

- 书库卷：`miaoyan-library` → `/data/Miaoyan`
- 可重建状态卷：`miaoyan-state` → `/data/state`
- 如果端口暴露在不可信网络，请设置 `MIAOYAN_WEB_PASSWORD`（网页登录）或
  `MIAOYAN_API_KEY`（机器调用）；两者都为空时不拦截请求。
- 更多环境变量、备份与反向代理配置见 [docs/DEPLOY.md](docs/DEPLOY.md)。

## 懒猫微服

```bash
# 1. 构建并推送一个公网可访问的镜像（Docker Hub 等）
docker buildx build --platform linux/amd64,linux/arm64 \
  -t <your-dockerhub>/miaoyan-web:0.1.0 --push .

# 2. 复制到懒猫官方仓库并改写 manifest
./scripts/prepare-store-image.sh <your-dockerhub>/miaoyan-web:0.1.0

# 3. 构建 LPK
./scripts/package-lpk.sh
```

`lazycat/package.yml` 声明 `document.private` 权限；容器通过 `X-HC-User-ID` 为每位用户选择
`/lzcapp/documents/<uid>/MiaoYan`，索引等状态写入 `/lzcapp/var/miaoyan`。完整的上架清单、离线内嵌镜像方案和排障见
[lazycat/README.md](lazycat/README.md)。

## 数据布局

```text
<vault>/
  项目 A/                 # 顶层文件夹即项目，允许嵌套
    note.md               # 文件名即标题
    i/photo.png           # 图片
    files/audio.m4a       # 其他附件
  root-note.md
  .miaoyan/trash/         # 应用内回收站
  .miaoyan-conflicts/     # 外部修改冲突备份
```

状态目录（SQLite 索引、版本历史、缓存）可以随时删除重建，笔记永远不会写进数据库。

## 文档

- [PLAN.md](PLAN.md)：架构、数据模型、API、里程碑与决策记录
- [COMPATIBILITY.md](COMPATIBILITY.md)：与妙言的功能对照、快捷键与原生行为替代
- [docs/DEPLOY.md](docs/DEPLOY.md)：部署、环境变量、备份、升级
- [docs/API.md](docs/API.md)：Web API 与 Papyra 兼容 API
- [lazycat/README.md](lazycat/README.md)：懒猫打包与上架

## 不做的事

实时协作、评论、分享、多租户、原生 macOS 特性、iCloud 同步、爬虫、完整 Papyra 认证体系。v1 是单用户单库；懒猫上依靠目录隔离支持多位用户各自的书库。

## 许可

MIT，见 [LICENSE](LICENSE)。妙言仅作为功能与交互参考；Papyra 的 GPL-3.0 代码没有被复制。
