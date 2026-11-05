# 部署

## 形态

一个容器同时提供：

- `/` 与 `/note/*`：React SPA（静态文件，`index.html` 不缓存，`/assets` 带哈希长缓存）
- `/api/*`：Web API 与 Papyra 兼容 API
- `/api/media/*`：Papyra 媒体文件（图片另可用 `/api/notes/{id}/assets/i/*`）
- `/healthz`：健康检查
- `/api/events`：SSE（M1 起），反向代理需允许长连接

没有 Redis、外部数据库、对象存储或第二进程。PlantUML 使用镜像内的 JAR 本地渲染。

## 环境变量

| 变量 | 默认 | 说明 |
|---|---|---|
| `MIAOYAN_LIBRARY` | 懒猫自动发现；否则 `./data/MiaoYan` | 固定书库根目录。懒猫上**不要设置**，否则所有用户会共用一本书库 |
| `MIAOYAN_STATE_DIR` | `/lzcapp/var/miaoyan` 或 `./data/.miaoyan-state` | 索引、版本、缓存；可重建，但版本历史在重建后丢失 |
| `MIAOYAN_DEFAULT_UID` | 空 | 多用户设备上，给不带 `X-HC-User-ID` 的 API-key 请求指定用户 |
| `MIAOYAN_WEB_PASSWORD` | 空 | 独立部署的网页密码；设置后 SPA 显示登录页，会话为 30 天 HMAC cookie。懒猫上保持为空，由网关登录 |
| `MIAOYAN_API_KEY` | 空 | Papyra-ingest 等机器调用的密钥；设置后 `/api/*` 要求 `X-API-Key`，常量时间比较。同一 IP 5 分钟失败 8 次会限流 |
| `MIAOYAN_MAX_UPLOAD_MB` | `100` | 单次上传上限，Kestrel 与 `MultipartReader` 共用 |
| `MIAOYAN_PLANTUML_JAR` | `/opt/plantuml/plantuml.jar` | 镜像内已内置；设为空可禁用 |
| `MIAOYAN_WHISPER_CMD` | 空 | 可选转写命令，如 `whisper --model small --output txt {input}` |
| `MIAOYAN_OCR_CMD` | 空 | 可选 OCR 命令，如 `tesseract {input} stdout -l chi_sim+eng` |
| `ASPNETCORE_URLS` | `http://+:5080`（镜像） | 监听地址 |

配置也可写在 `appsettings.json` 的 `Miaoyan` 节，环境变量优先。`MIAOYAN_MAX_UPLOAD_MB` 是唯一以
MB 为单位的变量，其余路径均为绝对路径。

## Docker Compose（推荐给个人服务器）

```bash
docker compose up -d --build
curl http://127.0.0.1:5080/healthz
```

默认两个命名卷：

- `miaoyan-library`：`/data/Miaoyan`，用户笔记，必须备份。
- `miaoyan-state`：`/data/state`，索引/版本，可选备份。

改为宿主目录：

```yaml
volumes:
  - /srv/miaoyan/library:/data/Miaoyan
  - /srv/miaoyan/state:/data/state
```

容器以 UID/GID 1000 运行；宿主目录需要可写：

```bash
sudo chown -R 1000:1000 /srv/miaoyan
```

如果以 root 启动（`docker run` 未指定 `--user`），入口脚本只对 `/data` 做一次 owner 修正，
随后 `gosu` 降权到 1000。

## 反向代理

推荐 Caddy / Nginx，终止 TLS 后转发到 `127.0.0.1:5080`：

- 保留 `Host`、`X-Forwarded-Proto`。
- SSE：关闭该路径的响应缓冲（Nginx `proxy_buffering off;`），超时大于 60s。
- 上传：`client_max_body_size 200m;` 与 `MIAOYAN_MAX_UPLOAD_MB` 一致。
- 不要把 `/healthz` 暴露为唯一的公网入口；健康检查不需要鉴权。

在懒猫上不需要自己配反向代理，网关负责 TLS 与登录。若应用直接暴露在公网，请至少设置
`MIAOYAN_WEB_PASSWORD` 或 `MIAOYAN_API_KEY`；两者都为空时任何人都能读写书库，这只适合
可信网络或已经由网关鉴权的懒猫环境。

## 懒猫微服

见 [../lazycat/README.md](../lazycat/README.md)。关键点：

- `package.yml.permissions.required` 必须有 `document.private`。
- 用 `services` + 显式 `entrypoint`，`run_as: "1000:1000"`。
- 书库自动定位到 `/lzcapp/documents/<uid>/MiaoYan`，状态到 `/lzcapp/var/miaoyan/<uid>`。
- 镜像必须位于 `registry.lazycat.cloud` 才能过审。

没有 Docker 但想先侧载试用时，可以运行 `./scripts/build-sideload-lpk.sh --copy-to-netdisk`：
它把框架依赖发布物装进 LPK 的 `contentdir`，使用官方 ASP.NET 运行时镜像，无需构建或推送
自定义镜像；该变体不包含 PlantUML/ffmpeg，且不能直接提交商店。详见
[../lazycat/README.md](../lazycat/README.md)。

上架前必须在真实懒猫容器里完成一次手工验收（本仓库的开发机没有容器运行时）：
安装 `.lpk`，确认 `document.private` 实际挂在 `/lzcapp/documents/<uid>`、不用密码即可从网关
进入、导入书库后能编辑和上传附件，再升级一次镜像确认笔记仍在。`scripts/verify-lazycat.sh`
只检查静态配置，不能替代这一步。

## 升级与回滚

```bash
# 备份（见 BACKUP.md），然后：
docker compose pull        # 或重新 build
docker compose up -d
```

- 笔记是普通文件，升级不会迁移或改写已有内容。
- SQLite schema 变化时，应用启动会自动迁移；迁移失败会保留旧库并报错，不会删除笔记。
- 回滚只需把镜像 tag 换回旧版本；旧库若无法识别新 schema，会提示重建索引（版本历史可能丢失）。

## 资源建议

| 场景 | CPU | 内存 | 磁盘 |
|---|---|---|---|
| 个人，数千笔记 | 1 核 | 512 MB | 笔记大小 + 1 GB |
| 家庭多人 | 2 核 | 1 GB | 同上 |
| 大型库（5 万笔记） | 2 核 | 2 GB | 预留索引/版本空间 |

PlantUML/ffmpeg 属于短时峰值；大量并发渲染时可限制预览并发（M3 起）。
