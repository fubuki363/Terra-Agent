# Terra Agent 悬浮助手

面向原版 Terraria 的独立 Windows 悬浮助手。无需 tModLoader；通过悬浮球在游戏中提问，
由 DeepSeek `deepseek-flash` 回答。助手可按需调用只读游戏状态、箱子物品搜索和群系定位技能，
并提供在线 Wiki 浏览器、多会话历史、Markdown 显示及用量统计。当前版本为 1.4.0。

1.3.0 按 DeepSeek Harness 官方源码重做对话界面与交互：流式回答、可展开的思考、同一行更新的
工具调用、完整参数与结果、调用轨迹、浅色/深色主题，以及能保留中断内容的历史记录。
界面使用官方主题变量，源码出处与适配范围见 [来源说明](Overlay/Harness/SOURCES.md)。

1.4.0 将设置、API Key、用量、游戏权限、查找与导航、常用速查和图鉴工具栏统一到同一套界面。
在“设置 → 常规 → 外观”选择浅色、夜间或跟随系统；所有页面、图鉴浏览器、悬浮球和菜单同步切换，
下次启动会恢复选择。右上角太阳/月亮按钮也可快速切换。

左下角“查找与导航”提供世界物体、群系/结构、箱内物品三种搜索方式，无需调用 AI。
“Terraria 在线图鉴”直接显示官方 Wiki 网页，支持中英文切换、站内搜索和浏览历史。
聊天中的资料检索与游戏工具统一由 Agent 按需调用，完成后可展开处理过程查看依据。

## 安装与启动

运行 [1.4.0 悬浮窗程序](dist/TerraWikiOverlay-1.4.0/TerraWikiOverlay.exe)。旧版正在运行时，先从托盘退出旧版。
首次启动会展开问答面板；
点击悬浮球可再次打开，或按 `Ctrl+Shift+T` 切换面板。建议将 Terraria 设为无边框窗口模式，
以便悬浮球保持可见。左下角“设置 → API Key”可导入 DeepSeek Key。
在线图鉴需要联网及 Microsoft Edge WebView2 Runtime；本机已安装即可直接使用，缺少时会给出安装入口。

项目源码可用以下命令发布为单文件 Windows 程序：

```powershell
.\tools\Build-Overlay.ps1
```

完整使用说明和隐私边界见 [悬浮助手文档](Overlay/README.md)。本地 Key、设置、历史和缓存保存在
`%LOCALAPPDATA%\TerraWikiOverlay`，不会随程序更新删除。不要公开分享 Key。
