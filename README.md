# Terra Agent 悬浮助手

面向原版 Terraria 的独立 Windows 悬浮助手。无需 tModLoader；通过悬浮球在游戏中提问，
由 DeepSeek `deepseek-flash` 回答。助手可按需调用只读游戏状态、箱子物品搜索和群系定位技能，
并提供在线 Wiki 浏览器、多会话历史、Markdown 显示及用量统计。当前版本为 1.2.2。

右上角“定位”提供世界物体、群系/结构、箱内物品三种直接搜索方式，无需调用 AI；支持全部可检测
群系，以及矿物、特定箱子、家具、药草、染料植物、大自然的恩赐、巨型龙蛋、友好巨石、生命果、
生命水晶、花苞、雕像等物体。目录结合本机物品样本、工具放置样式和自然拾取物映射，选中结果后即可导航。
“在线图鉴”直接渲染官方 Wiki 原网页，支持中英文切换、站内搜索、页内查找和浏览历史。
重复新建空对话会复用同一会话并保留草稿；窗口放大后输入区、按钮和内容视口会随尺寸自适应。

## 安装与启动

运行 [最新版悬浮窗程序](dist/TerraWikiOverlay-single/TerraWikiOverlay.exe)。首次启动会展开问答面板；
点击悬浮球可再次打开，或按 `Ctrl+Shift+T` 切换面板。建议将 Terraria 设为无边框窗口模式，
以便悬浮球保持可见。右上角“设置 → API Key”可导入 DeepSeek Key。
在线图鉴需要联网及 Microsoft Edge WebView2 Runtime；本机已安装即可直接使用，缺少时会给出安装入口。

项目源码可用以下命令发布为单文件 Windows 程序：

```powershell
.\tools\Build-Overlay.ps1
```

完整使用说明和隐私边界见 [悬浮助手文档](Overlay/README.md)。本地 Key、设置、历史和缓存保存在
`%LOCALAPPDATA%\TerraWikiOverlay`，不会随程序更新删除。不要公开分享 Key。
