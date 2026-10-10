# Terra Agent 悬浮助手

面向原版 Terraria 的独立 Windows 悬浮助手。无需 tModLoader；通过悬浮球在游戏中提问，
由 DeepSeek `deepseek-flash` 回答。助手可按需调用只读游戏状态、箱子物品搜索和群系定位技能，
并提供完整 Wiki 图鉴、多会话历史、Markdown 显示及用量统计。当前版本为 1.1.1。

右上角“定位”可直接查矿物、特定箱子、家具、制作站、植物、背景墙等本机游戏可放置物体，
以及生命果、生命水晶、世纪之花花苞或指定雕像；也可在对话中让 Agent 查找并导航。
正文支持精确拖选与选区复制，窗口可调整大小，运行中的请求可停止。

## 安装与启动

运行 [最新版悬浮窗程序](dist/TerraWikiOverlay-single/TerraWikiOverlay.exe)。首次启动会展开问答面板；
点击悬浮球可再次打开，或按 `Ctrl+Shift+T` 切换面板。建议将 Terraria 设为无边框窗口模式，
以便悬浮球保持可见。右上角“API Key”可导入 DeepSeek Key。

项目源码可用以下命令发布为单文件 Windows 程序：

```powershell
.\tools\Build-Overlay.ps1
```

完整使用说明和隐私边界见 [悬浮助手文档](Overlay/README.md)。本地 Key、设置、历史和缓存保存在
`%LOCALAPPDATA%\TerraWikiOverlay`，不会随程序更新删除。不要公开分享 Key。
