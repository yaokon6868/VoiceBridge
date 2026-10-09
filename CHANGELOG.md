# Changelog

## 0.3.1 Beta — 2026-10-09

- 修复长回复在增量快照、前文修订及标点变化后漏读尾部或复读已提交内容的问题；保留既有短语边界和缓冲策略。
- 退役被替换的会话，避免旧来源迟到的文字重新抢回朗读；新的有效轮次仍可正常接管。
- 桌面采集按缓存树读取多段正文，保留短暂控件重建时的回复游标，减少错误打断。
- 保留原生单实例启动、重复打开设置、正常退出和输入路由检查等本机已测试行为。
- 打包现有可选 AEC 处理器；新配置默认关闭，保存的用户选择优先。未改 AEC 算法、免费模型或新增文字来源。
- 更新安装、单入口启停、字幕调整、版本回退和验收边界说明。

验证证据：197 项离线检查通过。本机一次 96 字、三段回复通过真实 Fish 合成及播放链路，用户确认三段完整听到且只读一次。该检查使用本地测试文字，不代表网页、普通 Codex、隔离 Codex 的长回复采集都已分别通过。

发布标签为 `v0.3.1-beta`；为保持已测试的配置与启动行为，程序内部仍显示 `0.3.1-candidate`，使用 `.voicebridge-next-candidate` 配置目录，扩展版本为 `0.3.1`。

已知限制：外放回声、真人语音打断和长时间稳定性尚未全面验收；大段语义重写超出当前去重保证。浏览器扩展仍需手动加载/重新加载，VB-CABLE 驱动不随包安装。没有签名安装器或商店发布。详见 [使用指南](docs/USER_GUIDE.md) 和 [AEC 说明](docs/AEC-031.md)。

需要回退时可使用 [0.3.0 Beta](https://github.com/yaokon6868/VoiceBridge/releases/tag/v0.3.0-beta)，退出新版本、安装旧包并重新加载旧包扩展；旧版使用独立的 `.voicebridge-next` 配置目录。

## 0.3.0 Beta — publication preparation

- Self-contained x64 packaging, per-user installer/uninstaller and stable shortcut.
- Repository-contained offline tests and Windows CI.
- Local API tokens, website-origin rejection and event validation.
- Extension automatic session renewal after application restart.
- Settings/tray styling, icon, popup close ordering and debounced position saving.
- DOM committed-identity deduplication, with later identical replies allowed.
- Documentation, MIT license, third-party notices and issue template.

Limitations: speaker AEC and broad Codex compatibility are experimental; manual
extension loading/reloading remains necessary. No signed installer or store release.
