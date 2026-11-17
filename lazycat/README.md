# 懒猫微服打包与上架

本目录是 LPK v2 的打包根目录：

| 文件 | 作用 |
|---|---|
| `package.yml` | 包 ID、版本、名称/描述、多语言、作者、许可证、`document.private` 权限 |
| `lzc-manifest.yml` | 运行结构：入口路由、`services.miaoyan`、`run_as`、环境变量、健康检查、HTML no-cache 注入 |
| `lzc-build.yml` | 正式打包配置（引用 `registry.lazycat.cloud` 镜像） |
| `lzc-build.embed.yml` | 离线备用：从 `../Dockerfile` 构建并内嵌镜像 |
| `icon.png` | 512×512 PNG，需小于 200 KB |

## 为什么是 `services` 而不是 `application.image`

懒猫的 `application.image` 内置 app 服务不会执行镜像的 `ENTRYPOINT`。本应用需要入口脚本处理
独立部署时的 `/data` 初始化，因此使用普通 service，并显式写出：

```yaml
entrypoint: /usr/local/bin/entrypoint.sh
# command 必须是字符串，不能写成 YAML 列表（manifest schema: *string）；
# 写成列表会在安装阶段直接失败。
command: dotnet MiaoYan.Web.dll
```

路由用短服务名（官方模板的写法）：

```yaml
routes:
  - /=http://miaoyan:5080/
```

## 用户与数据隔离

- 网关鉴权后注入受信任的 `X-HC-User-ID`。应用只用它选择书库，不信任客户端伪造的同名头
  （网关会清理后重设）。
- 私有文稿目录：`/lzcapp/documents/<uid>/MiaoYan`，需要 `document.private`。
  manifest 声明 `multi_instance: true`：每位用户一个容器，多实例下应用只会看到当前
  用户的 UID 子目录，`/lzcapp/var` 也各自独立，不会串库。
- 状态目录：`/lzcapp/var/miaoyan/<uid 或哈希>`（SQLite 索引、版本历史、缓存）。
  `/lzcapp/var` 由平台保证持久化，删除只损失索引，笔记不受影响。
- `run_as: "1000:1000"` 让 `/lzcapp` 持久目录的 owner 映射到容器用户，无需 `setup_script`、
  `chown` 或额外的 bind。要求 `lzcos >= 1.6.0`。
- 容器启动时如果发现 `/lzcapp/pkg` 存在却没有 `/lzcapp/documents`，会直接报错退出，
  避免把笔记写进随容器销毁的 rootfs。

## 构建与上架流程

### 1. 构建镜像（amd64 + arm64）

```bash
docker buildx build --platform linux/amd64,linux/arm64 \
  -t <your-dockerhub>/miaoyan-web:1.0.0 --push .
```

镜像必须公网可拉取。`copy-image` 是在懒猫服务端执行 `docker pull`，本地镜像无法直接复制。

> .NET 10 的 `aspnet:10.0` 基础镜像可能已经占用 GID/UID 1000。Dockerfile 用
> `getent group 1000 || groupadd ...`、`getent passwd 1000 || useradd ...` 幂等创建，
> 否则 buildx 会报 `groupadd: GID '1000' already exists` 并中断。
仓库里的 `.github/workflows/release-image.yml` 会在推送 `v*` tag 时构建
`linux/amd64,linux/arm64` 并推到 GHCR；把 GHCR 包设为 public 后，再用
`prepare-store-image.sh ghcr.io/<owner>/miaoyan-web:1.0.0` 复制到懒猫仓库。

### 2. 复制到懒猫官方仓库

```bash
./scripts/prepare-store-image.sh <your-dockerhub>/miaoyan-web:1.0.0
```

脚本执行 `lzc-cli appstore copy-image`，从输出里取出
`registry.lazycat.cloud/<community-username>/...:<image-id>`，并写回
`lzc-manifest.yml` 的 `services.miaoyan.image`，最后跑一遍预检。

> 上架审核要求 LPK 中的镜像位于 `registry.lazycat.cloud`。如果提交时仍是
> `registry.lazycat.cloud/miaoyan/miaoyan-web:...` 占位符，审核人员会装不上。

### 3. 构建 LPK

```bash
./scripts/package-lpk.sh
# 或：cd lazycat && npx --yes @lazycatcloud/lzc-cli@2.0.9 project build . -f lzc-build.yml
```

产物为 `lazycat/*.lpk`。首次构建前可单独运行 `./scripts/verify-lazycat.sh` 做静态检查；
发布前（已执行 `prepare-store-image.sh`）必须不用参数、严格通过。CI 在镜像占位符尚未替换时
使用 `--allow-placeholder`，其余检查仍然会失败中断。

### 无 Docker 侧载包（自用）

开发机没有 Docker 时，不必构建自定义镜像：把框架依赖发布物作为 `contentdir`，配合官方
ASP.NET 运行时镜像即可打包。清单是 `lzc-manifest.sideload.yml`，应用文件安装后位于
`/lzcapp/pkg/content`。

```bash
./scripts/build-sideload-lpk.sh --copy-to-netdisk
```

产物 `lazycat/cloud.lazycat.app.miaoyanweb-<version>.lpk` 会复制到懒猫网盘根目录
`/lzcapp/document/`，在懒猫客户端里点击即可侧载安装。这个变体：

- 不需要 Docker、不需要 `copy-image`，镜像地址是 `mcr.microsoft.com/dotnet/aspnet:10.0`；
- **PlantUML 不可用**（基础镜像没有 JRE），也没有内置 ffmpeg/OCR/Whisper；Mermaid、Markmap、
  KaTeX 和 PPT 仍在浏览器端正常工作；
- 默认不能直接提交商店（镜像不在 `registry.lazycat.cloud`）。如果暂时没有 Docker，
  也可以用 `appstore copy-image mcr.microsoft.com/dotnet/aspnet:10.0` 把运行时镜像
  复制到懒猫仓库后提交；但 content 是 linux-x64，且没有 PlantUML/ffmpeg，正式上架
  仍建议走上面的 Dockerfile + `prepare-store-image.sh` 流程。

### 4. 本机安装测试

```bash
npx --yes @lazycatcloud/lzc-cli@2.0.9 project deploy . -f lzc-build.yml
```

安装后从启动器打开「妙言 Web」。控制台日志应出现：

```text
Vault mode: lazycat; documents=/lzcapp/documents; state=/lzcapp/var/miaoyan
```

`/healthz` 的 `vault.mode` 应为 `lazycat`，`userCount` 至少为 1。

## 上架检查清单

- [ ] `package.yml`：合法包名、semver 版本、中英文 `locales`、作者、许可证、图标。
- [ ] `permissions.required` 只包含真正需要的 `document.private`，没有多余的高危权限。
- [ ] manifest 声明 `multi_instance: true`（按用户读文档/存状态的应用必须）。
- [ ] 所有镜像已用 `appstore copy-image` 复制到 `registry.lazycat.cloud`，manifest 已改写。
- [ ] 安装后 5 分钟内健康；`health_check.test_url` 或容器 healthcheck 通过。
- [ ] 免登录：经懒猫网关访问不出现应用自己的登录页（当前直接信任网关）。
- [ ] 数据持久化：重启、升级、卸载重装后 `/lzcapp/documents/<uid>/MiaoYan` 仍在。
- [ ] 多用户：两位用户分别打开，各看到各自的书库；升级后不串库。
- [ ] 截图、商店描述、更新日志准备好；`version` 与镜像 tag 对应。
- [ ] `./scripts/verify-lazycat.sh` 通过。

## 商店资料（开发者中心）

- 图标：PNG，≤200 KB，512×512（当前 `icon.png` 已满足）。
- 桌面截图：≥2、≤8 张，16:9，每边 1080–7680 px。
- 手机截图：声明「支持移动端」时 ≥3、≤8 张，16:9，每边 320–3840 px。
- 中英文条目**分别**上传截图并勾选平台；切换语言后截图区会归零，漏传会报
  「本地化应用信息不完整」。
- 本项目以 tw93/MiaoYan 的功能和交互为基准，**不要勾“原创开发”**；`source_author`
  填 `tw93`，`source` 地址填 <https://github.com/tw93/MiaoYan>，并给出本仓库的公开源码地址。
- `package.yml` 的 `name` / `description` / `locales` 会带出并锁定商店名称和简介；
  改文案要改 `package.yml` 重新上传版本。
- 发布前先把本仓库推到公开 GitHub（`package.yml` 的 `homepage` 指向
  <https://github.com/yigiaogiao/miaoyan-web>），否则商店 `source` 字段没有有效地址。

## 离线内嵌镜像（不推荐上架用）

如果暂时没有公共 registry，可以内嵌镜像：

1. 把 `lzc-manifest.yml` 中 `services.miaoyan.image` 改成 `embed:miaoyan`。
2. 执行 `npx --yes @lazycatcloud/lzc-cli@2.0.9 project build . -f lzc-build.embed.yml`。

LPK 会包含完整镜像（数百 MB）。构建机需要 Docker，且升级包会明显变大；正式上架仍建议走
`copy-image`。

## 常见问题

| 现象 | 原因 / 处理 |
|---|---|
| 启动即退出，日志提示没有 `/lzcapp/documents` | `document.private` 未生效。确认写在 `package.yml.permissions.required`，重新安装。 |
| 访问返回 401 `vault-unavailable` | 请求没有经过懒猫网关，缺少 `X-HC-User-ID`。请从启动器/客户端打开；API-key 场景可设 `MIAOYAN_DEFAULT_UID`。 |
| 多人设备上看到别人的书库 | 不应该发生。检查是否设置了 `MIAOYAN_LIBRARY` 把所有人固定到同一目录。 |
| 首页升级后仍是旧版 | 确认 `miaoyan-revalidate-html` 注入仍在最终 `manifest.yml` 中。 |
| 镜像拉取失败 | 用 `appstore copy-image` 重新复制，确认 tag 使用复制后返回的 image-id。 |
| 容器文件权限错误 | 确认 service 使用 `run_as: "1000:1000"`，不要同时设置 `user` 或 `setup_script`。 |
