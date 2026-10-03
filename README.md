# VoiceBridge

**给 ChatGPT Voice 换声音、加实时字幕的 Windows 工具。**

我经常用 ChatGPT 语音聊天，希望它能用自己喜欢的音色回答，也希望讲话时能看到字幕，于是做了这个项目。

目前已经实现：

- 实时悬浮字幕，字号、透明度和位置可调。
- 接入 Fish Audio，按逗号和短语连续朗读。
- 使用当前免费的 `s2.1-pro-free` 模型，需要自己的 API Key。
- 提供 Windows 安装包和桌面启动入口。
- 不采集整个电脑的系统音频。

现在开放 **0.3.0 Beta**。核心网页换声功能已经跑通，外放回声和部分兼容性还在完善。

如果你也经常使用 ChatGPT Voice，欢迎试用、反馈问题，或者参与改进。喜欢这个方向，也欢迎给项目一个 ⭐。

[下载测试版](https://github.com/yaokon6868/VoiceBridge/releases/tag/v0.3.0-beta) · [反馈问题](https://github.com/yaokon6868/VoiceBridge/issues)

**当前为 0.3.0 Beta。** 已在维护者电脑上验证网页采集和换声；外放回声消除、
两套 Codex 的完整兼容性和长时间稳定性仍需测试。本项目与 OpenAI、Fish Audio
没有官方隶属关系。

## 功能

- 助手增量文本同时送到实时字幕和按逗号、短语缓冲的流式 TTS。
- 固定 `s2.1-pro-free`，不会自动回退到付费模型。
- 字幕置顶、不抢焦点、自动换行、拖动缩放、鼠标穿透、保存位置和外观。
- 文字与背景不透明度独立调节，托盘提供预览、停止朗读和连接重试。
- 会话隔离、打断清空、重新渲染去重、合成故障解除网页静音。
- 可从托盘启动 Codex 桌面采集，仍属于 Beta 功能。
- 不监听整个 Windows 系统音频。

## 安装（无需编程）

1. 下载 Releases 的 `VoiceBridge-Windows-x64.zip`，**完整解压**。
2. 双击 `Install.cmd`，安装到 `%LOCALAPPDATA%\VoiceBridge`，不需要管理员权限。
3. 桌面出现 **启动换声工具**，以后只需双击这个入口。
4. Chrome 打开 `chrome://extensions`，开启开发者模式，选择“加载已解压的扩展程序”，
   选择 `%LOCALAPPDATA%\VoiceBridge\extension`，只启用一个 VoiceBridge 扩展。
5. 双击右下角声波托盘图标，填写自己的 Fish API Key 和 Voice / Reference ID，保存。
6. 刷新 ChatGPT 网页，开启语音，说一句新话。扩展弹窗分别显示连接和语音状态。

Windows x64 发布包自带 .NET 运行时，无需另外安装 .NET。
浏览器扩展目前通过开发者模式安装，**不会自动安装到 Chrome**。
Fish 网站/PWA 不必启动；程序直接连接 Fish WebSocket 服务。
免费 API 仍需要账号密钥，可用性以 [Fish 官方说明](https://fish.audio/blog/s2-1-pro-free-api/)
为准，不承诺永久免费。

可选开机启动：执行 `Install.cmd -StartWithWindows`。
重新执行普通 `Install.cmd` 会关闭本安装创建的开机启动项。

## 使用与排查

| 问题 | 检查方法 |
|---|---|
| 找不到程序 | 双击桌面“启动换声工具”；程序在右下角托盘运行 |
| 没有字幕 | 扩展弹窗检查心跳，点“重新接入”，再开启语音 |
| 讲话没有文字 | 在 `chrome://settings/content/microphone` 选择真实麦克风，重开语音 |
| CABLE Output 收不到音 | 公共版不含虚拟麦克风处理器，切回真实麦克风 |
| 有字幕无换声 | 检查朗读开关、密钥、音色 ID，从托盘重试 Fish |
| 外放自问自答 | 尚未完成回声处理，目前耳机更适合稳定使用 |
| 显示字幕预览 | 预览不是实时采集成功的证据 |

“监听 Codex”表示允许桌面采集；默认仍需托盘“接入普通版和隔离版 Codex”
或启动参数 `--desktop` 启动。拖动字幕前选择“调整字幕位置”，调整后在设置中
重新打开鼠标穿透。

## 升级与卸载

升级：下载、解压新 ZIP，运行 `Install.cmd`，在 Chrome 扩展管理页重新加载扩展，
刷新 ChatGPT。应用和扩展应一起升级。
配置保存在 `%USERPROFILE%\.voicebridge-next\settings.json`，升级保留。
API Key 使用当前 Windows 用户 DPAPI 加密，不能直接复制到另一台电脑使用。

卸载：运行安装目录中的 `Uninstall.cmd`，再手动移除 Chrome 扩展。
卸载保留个人设置；需要彻底清除时自行删除上述配置文件夹。
旧预览用户升级前应退出旧程序、停用旧扩展和旧开机启动项。

## 从源码构建

需要 Windows 11、.NET 8 SDK、Node.js 22 或更新版本。

```powershell
dotnet build VoiceBridge.csproj -c Release
./scripts/Check.ps1
./scripts/Package.ps1
```

`Check.ps1` 使用本机模拟服务和假播放器，不调用 Fish，也不需要真实密钥。
`Package.ps1` 输出自带运行时的 x64 ZIP 与 SHA-256 文件。
测试端口为 17893、17895；应用仅监听本机端口 17892。
组织管理策略可能限制脚本或程序安装。

## 隐私与限制

助手文字发送到 Fish Audio 合成；ChatGPT 的收音由 ChatGPT 自己处理。
本机桥接使用每次启动更新的临时令牌，扩展自动连接；普通网页不能直接调用。
该机制不防御同一 Windows 用户的恶意程序或恶意扩展，见 [SECURITY.md](SECURITY.md)。
诊断只记录有界事件元数据和设备状态，不保存聊天全文、音频或明文密钥。
设备名称和时间也可能包含个人信息，报告问题前请检查。

ChatGPT 页面或 Fish 接口变化可能影响兼容性。故障可以解除网页静音，
但不能追回已错过的官方语音。公共包不包含实验 AEC 环境和虚拟音频驱动。
不要宣传本版已实现可靠的外放全双工回声消除。

## 贡献与许可

参见 [CONTRIBUTING.md](CONTRIBUTING.md)、[ARCHITECTURE.md](ARCHITECTURE.md)
与 [CHANGELOG.md](CHANGELOG.md)。项目使用 [MIT License](LICENSE)，
第三方组件见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
