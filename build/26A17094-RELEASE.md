# 26A17094：中文资源与新版更新规则

从 26A17091 的界面基础继续开发，保留 26A17092 的主题修复、双运行时兼容和旧版更新入口。

## 资源中文显示

模组、资源包、整合包、光影、地图及前置依赖共用显示层。原始项目数据和下载身份保持不变。
名称优先已有中文、常用译名，再调用免费机器翻译；连写词可以拆分重试。
简介优先 MCIM 提供的中文译文（严格匹配项目编号和当前原文），再调用 MyMemory。

- 翻译服务只接收公开项目名称/简介/编号，不使用资源下载接口的授权头。
- 请求并发最多 4，单次网络请求最多 8 秒；每个项目有 45 秒总预算。
- 相同原文并发请求合并；取消一个调用方不会取消其他调用方的翻译。
- 成功字段以原文摘要为键保存于 `BHL/cache/resource-translations-v1`，作者改名/改简介后自动失效。
- 英文、错误响应和失败提示不会保存为成功译文。断网、限额或无可用译名时显示中文状态，可在详情页重试。
- 这些状态不是中文译名。机器翻译不能保证所有新造词都能翻译，也不能保证与社区惯用译名一致。
- 实测刚上传资源中，`PotatoGlow` 可通过拆分得到中文；`ZXoptifabric` 仍可能无可用中文名称。
- 项目版本号、实际文件名、加载器和来源名称属于技术标识，保持原值。

提供方说明：[MCIM](https://github.com/mcmod-info-mirror/mcim-translate)、[MyMemory API](https://mymemory.translated.net/doc/spec.php)。
实时抽样工具：`dotnet run --project build/ResourceTranslationProbe -- .tmp/translation-probe`；此命令会联网并消耗免费额度，不属于常规自动测试。

## 更新弹窗与身份

弹窗显示版本名称、简介、更新内容、完整版本/维护补丁类型。正文有独立滚动区域，操作按钮始终位于正文外。
`summary` 缺省时从旧 `releaseNotes` 首行提取简介；二者均缺省时显示明确提示。

新客户端先读取 `update/{channel}/latest-v2.json`，不可用时兼容 `latest.json`。
原版 91 继续只读取 schema 1 的 `latest.json`，不能把该文件直接改成 schema 2。
91 用户须先完成一次升级，才会有新窗口和新的检测规则。

Schema 2 必填的发布字段：

| 字段 | 用途 |
|---|---|
| `versionName` | 任意显示名称，例如 `春季特别版` 或 `94 维护补丁一`，不参与大小比较 |
| `releaseId` | 发布的唯一固定标识，只允许英文字母、数字、点、下划线和短横线 |
| `releaseSequence` | 发布顺序，必须大于上一正式发布，独立于显示名称；迁移起点为 648114324 |
| `releaseType` | `full` 或 `patch` |
| `baseReleaseIds` | `patch` 必填，列出补丁实际支持的已安装发布标识 |
| `assets[0].delivery` | `self-update` 表示实现启动器替换协议的 EXE；`installer` 表示普通安装包 |

包的大小、SHA-256、官方 URL、平台及架构验证继续生效。相同发布标识和不更高的发布顺序不会重复提示。
不适用的补丁和普通安装包引导用户到发布页面，绝不传入自替换流程。
构建下一版时必须同步 App 项目的 `ReleaseId`、`ReleaseSequence` 元数据与发布清单；显示名称不再需要与顺序数值对应。

## 构建与验证

```powershell
dotnet build Launcher.sln
dotnet test Launcher.sln
dotnet test Launcher.CompatibilityTests/Launcher.CompatibilityTests.csproj
./build/package-94-release.ps1
./build/test-91-installer.ps1 -VersionName 26A17094
./build/test-92-update.ps1 -VersionName 26A17094 -PackageDirectory './交付文件/26A17094更新补丁'
```

产物位于项目 `交付文件/26A17094统一安装包` 和 `交付文件/26A17094更新补丁`。原桌面上的启动器历史产物也归档至 `交付文件`；后续打包默认输出到该目录。
打包脚本仅生成本地文件，不上传、不变更在线更新清单。
兼容包仍采用 .NET 6；实际 Windows 7 环境需要单独验证。

91 外观校验仅放行此前修复及本次的字符串、资源详情重试入口、更新弹窗；其余原版界面文件继续校验。
