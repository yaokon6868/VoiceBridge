# VoiceBridge

**让 ChatGPT Voice 用你喜欢的音色回答，同时显示实时悬浮字幕。**

我经常用 ChatGPT 语音聊天，希望它能用自己喜欢的音色回答，也希望讲话时能看到字幕，于是做了这个项目。

目前已经实现：

- 实时悬浮字幕，字号、透明度和位置可调。
- 接入 Fish Audio，按逗号和短语连续朗读。
- 固定只请求 `s2.1-pro-free`，需要自己的 Fish API Key；实际配额和可用性由 Fish 账号及政策决定。
- 提供 Windows 安装包和桌面启动入口。
- 不采集整个电脑的系统音频。

当前测试版为 **0.3.1 Beta**。本轮修复长回复漏读、复读和旧会话迟到文字打断播放的问题，仍保留 Beta 标识。

如果你也经常使用 ChatGPT Voice，欢迎试用、反馈问题，或者参与改进。喜欢这个方向，也欢迎给项目一个 ⭐。

[下载 0.3.1 Beta](https://github.com/yaokon6868/VoiceBridge/releases/tag/v0.3.1-beta) · [详细使用说明](docs/USER_GUIDE.md) · [使用场景与支持范围](docs/USE_CASES.md) · [反馈问题](https://github.com/yaokon6868/VoiceBridge/issues)

本机检查中，96 字、三段回复已由用户确认完整听到且只读一次；197 项离线检查通过。
这次播放检查不等于网页、普通 Codex、隔离 Codex 的长回复都已验收。外放回声、语音打断和长时间稳定性仍需分别测试。
发布包保留已测试程序的内部 `0.3.1-candidate` 标识与候选配置目录；GitHub 标签为 `v0.3.1-beta`，两者指同一份发布程序。

## 先选对版本

| 版本 | 获取方式 | 适合谁 |
|---|---|---|
| **0.3.1 Beta** | [当前发布页](https://github.com/yaokon6868/VoiceBridge/releases/tag/v0.3.1-beta)中的 Windows ZIP | 体验本轮朗读修复；首次建议戴耳机，AEC 仍为可选实验功能 |
| **0.3.0 Beta** | [旧版发布页](https://github.com/yaokon6868/VoiceBridge/releases/tag/v0.3.0-beta) | 需要回退时使用；不含实验 AEC 处理器 |

下载包和网页扩展应来自同一版本。源码更新不等于本机程序自动升级；回退步骤见 [使用指南](docs/USER_GUIDE.md#回退到-030-beta)。
本项目与 OpenAI、Fish Audio 没有官方隶属关系。

## 可以拿来做什么

- **个性化 AI 语音聊天**：用选好的 Fish 音色听 ChatGPT Voice 回答。
- **边听边看**：通过置顶字幕核对名称、术语或长回复中的重点。
- **学习与语言练习**：向 ChatGPT 提问、练习对话，同时看助手回复字幕。
- **开发时听助手回复**：尝试普通版、隔离版 Codex 桌面采集；需分别验证。

当前核心是“助手文字 → 字幕与指定音色朗读”，不是把原始音频直接改音色。
微信、QQ、B站视频、任意文章朗读和麦克风直接变声尚未正式接入。
各场景的操作、边界及后续扩展方向见 [使用场景说明](docs/USE_CASES.md)。

## 功能

- 助手增量文本同时送到实时字幕和按逗号、短语缓冲的流式 TTS。
- 固定 `s2.1-pro-free`，不会自动回退到付费模型。
- 字幕置顶、不抢焦点、自动换行、拖动缩放、鼠标穿透、保存位置和外观。
- 文字与背景不透明度独立调节，托盘提供预览、停止朗读和连接重试。
- 会话隔离、打断清空、重新渲染去重、合成故障解除网页静音。
- 0.3.1 默认按“监听 Codex”设置开启桌面采集，也可从托盘手动接入；兼容性仍属于 Beta 功能。
- 不监听整个 Windows 系统音频。

## 安装（无需编程）

需要 Windows 11 x64、Chrome、可使用 ChatGPT Voice 的账号、麦克风和声音输出设备。
首次体验建议使用耳机。其他系统、ARM64 原生运行和其他浏览器尚未列入已验证支持。

1. 下载 Releases 的 `VoiceBridge-Windows-x64.zip`，**完整解压**。不要把 Source code 或 `VoiceBridge-Source.zip` 当作安装包。
2. 双击 `Install.cmd`，安装到 `%LOCALAPPDATA%\VoiceBridge`，不需要管理员权限。
3. 桌面出现 **启动换声工具**，以后只需双击这个入口。
4. Chrome 打开 `chrome://extensions`，开启开发者模式，选择“加载已解压的扩展程序”，
   选择 `%LOCALAPPDATA%\VoiceBridge\extension`，只启用一个 VoiceBridge 扩展。
5. 双击右下角声波托盘图标，填写自己的 Fish API Key 和 Voice / Reference ID，勾选“启用 Fish Audio 流式朗读”，保存。
6. 首次使用保持“外放回声处理”关闭，使用耳机和真实麦克风。刷新 ChatGPT 网页，开启语音，说一句新话，核对字幕和所选 Fish 音色。

Windows x64 发布包自带 .NET 运行时，无需另外安装 .NET。
浏览器扩展目前通过开发者模式安装，**不会自动安装到 Chrome**。
Fish 网站/PWA 不必启动；程序直接连接 Fish WebSocket 服务。
模型和账号条件以 [Fish 官方说明](https://fish.audio/blog/s2-1-pro-free-api/)为准。本工具只请求 `s2.1-pro-free`，不自动改用付费模型；不承诺无限额度、永久免费或服务始终可用。

不知道去哪里拿 Key、音色 ID，或找不到程序？请看 [逐步使用指南](docs/USER_GUIDE.md)，
其中包含日常启停、字幕调整、两套 Codex 接入、升级和按症状排查。

可选开机启动：执行 `Install.cmd -StartWithWindows`。
重新执行普通 `Install.cmd` 会关闭本安装创建的开机启动项。

## 使用与排查

| 问题 | 检查方法 |
|---|---|
| 找不到程序 | 双击桌面“启动换声工具”；程序在右下角托盘运行 |
| 没有字幕 | 扩展弹窗检查心跳，点“重新接入”，再开启语音 |
| 讲话没有文字 | 在 `chrome://settings/content/microphone` 选择真实麦克风，重开语音 |
| CABLE Output 收不到音 | 先恢复真实麦克风并关闭 AEC；仅在处理器和实际输入路由核验后使用虚拟输入 |
| 有字幕无换声 | 检查朗读开关、密钥、音色 ID，从托盘重试 Fish |
| 外放自问自答 | 尚未完成回声处理，目前耳机更适合稳定使用 |
| 显示字幕预览 | 预览不是实时采集成功的证据 |

0.3.1 在启动时按保存的“监听 Codex”设置开启采集；0.3.0 则需托盘“接入普通版和隔离版 Codex”或启动参数 `--desktop`。
拖动字幕前选择“调整字幕位置”，调整后在设置中重新打开鼠标穿透。退出请使用托盘“退出 VoiceBridge”；关闭设置不会结束后台程序。

## 升级与卸载

升级：下载、解压新 ZIP，运行 `Install.cmd`，在 Chrome 扩展管理页重新加载扩展，
刷新 ChatGPT。应用和扩展应一起升级。
0.3.1 配置保存在 `%USERPROFILE%\.voicebridge-next-candidate\settings.json`；0.3.0 使用 `%USERPROFILE%\.voicebridge-next\settings.json`。两份配置分别保留，旧版配置不会因为启动新版而自动迁移。
API Key 使用当前 Windows 用户 DPAPI 加密，不能直接复制到另一台电脑使用。

卸载：默认安装运行安装目录中的 `Uninstall.cmd`，再手动移除 Chrome 扩展。
自定义安装位置的卸载限制见 [详细指南](docs/USER_GUIDE.md)。
卸载保留个人设置；需要彻底清除时先核对要移除的版本，再删除对应配置文件夹。
旧预览用户升级前应退出旧程序、停用旧扩展和旧开机启动项。

## 从源码构建

需要 Windows 11、.NET 8 SDK、Node.js 22 或更新版本；包含 AEC 的发布包另外需要 Python 3.12 构建环境。

```powershell
dotnet build VoiceBridge.csproj -c Release
./scripts/Check.ps1
./scripts/BuildAec.ps1 -Python python
./scripts/Package.ps1 -IncludeAec
```

`Check.ps1` 使用本机模拟服务和假播放器，不调用 Fish，也不需要真实密钥。
上述打包命令会先运行离线检查，再输出自带运行时及 AEC 处理器的 x64 ZIP 与 SHA-256 文件；虚拟音频驱动需另行安装。
应用桥接仅监听本机端口 17892；启用 AEC 后另有本机处理器端口 17896。构建与检查不会完成真人语音验收。
组织管理策略可能限制脚本或程序安装。

## 隐私与限制

助手文字发送到 Fish Audio 合成；ChatGPT 的收音由 ChatGPT 自己处理。
本机桥接使用每次启动更新的临时令牌，扩展自动连接；普通网页不能直接调用。
该机制不防御同一 Windows 用户的恶意程序或恶意扩展，见 [SECURITY.md](SECURITY.md)。
诊断只记录有界事件元数据和设备状态，不保存聊天全文、音频或明文密钥。
设备名称和时间也可能包含个人信息，报告问题前请检查。

ChatGPT 页面或 Fish 接口变化可能影响兼容性。故障可以解除网页静音，
但不能追回已错过的官方语音。0.3.0 包不包含实验 AEC 环境，
0.3.1 Beta 包带处理器，新配置默认关闭 AEC；所有版本均不捆绑虚拟音频驱动。
启用方式和实际输入核验见 [AEC 使用步骤](docs/USER_GUIDE.md#10-031-beta-实验-aec)。
不要宣传本版已实现可靠的外放全双工回声消除。

## 贡献与许可

参见 [CONTRIBUTING.md](CONTRIBUTING.md)、[ARCHITECTURE.md](ARCHITECTURE.md)
与 [CHANGELOG.md](CHANGELOG.md)。项目使用 [MIT License](LICENSE)，
第三方组件见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
