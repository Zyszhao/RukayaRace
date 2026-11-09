# Rukaya 双端发布工具

开发区保存本目录源码；统一工作区为 `E:\Rukaya Race OpenSource Branch`，GitHub 为 `Zyszhao/RukayaRace`，工坊为 `3833580701`。旧 Release 目录停止使用。完整规则见开发区 `设计文档/开发版与正式版同步规范.md`。

## 部署

1. 本目录同步到统一工作区 `tools/release/`；`.steamignore` 同时复制到统一工作区根目录。
2. 将 `Publish-Release.ps1`、`publish-dual.mjs`、`confirm-steam-upload.mjs` 与 `format-release-notes.mjs` 放在 `D:\SteamCMD` 同一目录。依赖现有 `tools/windows-steam-plugin.mjs`、`semantic-release-steam/`、`cygwin/`、Node.js、PowerShell 7、Git、gh。
3. 本机 `D:\SteamCMD\workshop.config.json` 中 `mods[0].path` 指向统一工作区，沿用 appId 294100、stable 工坊 ID 3833580701；不可提交登录缓存和凭据。
4. 工坊 `README.template.md` 在统一工作区根目录维护，沿用原工坊简介；它参与页面编译，但不进入运行包。

## 使用

```powershell
# 离线预演：实际暂存、打包、检查；完成后清理临时文件
D:\SteamCMD\Publish-Release.ps1
# 用户要求同步后，指定版本和玩家说明，完成两仓库推送即执行
D:\SteamCMD\Publish-Release.ps1 -Version v0.2.4 -NotesFile 'E:\更新日志\发布说明\v0.2.4.md' -PreserveSteamTags -Publish
# 单端发布 / 失败后重试
D:\SteamCMD\Publish-Release.ps1 -Version v0.2.4 -NotesFile 'E:\更新日志\发布说明\v0.2.4.md' -Target GitHub -PreserveSteamTags -Publish
D:\SteamCMD\Publish-Release.ps1 -Version v0.2.4 -NotesFile 'E:\更新日志\发布说明\v0.2.4.md' -Target Steam -PreserveSteamTags -Publish
```

正式发布前先完成开发区和统一工作区的相关提交、推送。统一工作区必须干净，运行文件须已入 Git，origin 对应分支须精确等于本地 HEAD。GitHub Release 自动关联该提交；已有版本按下文修订规则处理，禁止移动标签。带预发布后缀的版本标为 GitHub prerelease，但 Steam 仍按所配置分支目标更新 stable 工坊，因此测试版本不要对 Steam 执行正式发布。

SteamCMD 使用 `Login-Steam.ps1` 建立的登录缓存；默认用 `-PreserveSteamTags` 保留标签。确需更新标签时还需环境变量 `STEAM_REFRESH_TOKEN` 或 `STEAM_PASSWORD`。包装脚本按进程、用户级、机器级顺序读取凭据，不保存到文件。Steam Guard 可能仍需确认，不能保证完全无人值守。GitHub 使用 gh 当前账户。

GitHub 安装 ZIP 和 Steam 内容共用一个暂存目录，ZIP 根层直接包含 About、1.7、Content 等，可解压到一个 Mod 文件夹。单独提供清单记录提交与每个文件的 SHA-256。GitHub 自动 Source code 归档包含源码，这是预期行为。

`.steamignore` 是适配器实现的后写规则优先过滤，不是 SteamCMD 自带功能；不要把 Git 仓库根目录直接写进手工 VDF 的 contentfolder。允许新增运行根目录时须同时更新过滤规则和脚本白名单。

两端没有跨平台事务：GitHub → Steam 文件 → Steam 标签，后一步失败不回滚前一步；根据日志核对后单端重试。脚本仅对本机工具链和暂存内容做验证，未代替游戏测试、登录有效性与真实工坊所有权检查。

## 修订已有版本与保留工坊标签

用户明确要求修订已有 Release 时可加 `-UpdateExistingRelease`。须先取得本地对应标签；脚本核对标签与当前运行内容、生产源码完全一致，且远端标签仍指向原提交，才允许补充安装 ZIP、哈希清单和更新说明，始终不移动标签。新附件同名时上传会停止，不自动覆盖旧附件。

仅更新工坊文件并沿用现有标签时可加 `-PreserveSteamTags`，不调用额外 Steam CM 标签认证，也不在上传 VDF 中写 tags。存在多语言模板时拒绝使用此选项。发布后需核对工坊原标签仍在。

SteamCMD 上传成功判断兼容 stdout/stderr 与控制字符；无法识别成功文本时，再核对工坊远端更新记录的时间戳与本次完整更新说明，避免误判后重复上传。远端查询使用 PowerShell 网络栈以沿用 Windows 代理配置。
## 同步默认流程与日志格式

用户要求“同步”即包含内容同步、两仓库提交推送、GitHub Release 和 Steam 发布；缺版本号时先询问，不自动递增。用户明确限定不发布等范围时按其要求执行。维护工具或文档本身不触发发布。

NotesFile 使用开发区 `更新日志/发布说明/v版本号.md`，只包含面向玩家的简短中文正文，不放版本标题或交接记录。`format-release-notes.mjs` 自动清理可识别的首行旧版本标题并分别生成：

```text
Steam：
Rukaya Race v0.2.3
修复防空炮拦截首领空投仓导致任务卡住的问题。

GitHub 正文：
修复防空炮拦截首领空投仓导致任务卡住的问题。
```

GitHub 标题和标签仍包含版本号。Steam 远端更新核验使用包含版本首行的完整说明。预演会打印两端说明供检查，历史公开记录不因本地日志整理自动回写。玩家文风以开发区 `更新日志/README.md` 为准。
