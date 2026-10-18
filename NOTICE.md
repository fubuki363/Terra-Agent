# 第三方内容说明

Terra Agent 悬浮助手在运行时访问由 wiki.gg 托管的 Official Terraria Wiki，并在本地缓存与用户
问题最相关的页面片段。缓存内容不属于本项目代码。

Official Terraria Wiki 页面内容除非另有说明，采用
[Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International](https://creativecommons.org/licenses/by-nc-sa/4.0/)
许可。使用全图鉴打开 Wiki 条目时可查看原始页面；运行时检索结果不会作为独立来源列表显示在回答中。

本项目与 Re-Logic、Terraria、wiki.gg 或 DeepSeek 无隶属或背书关系。

1.3.0 的对话界面复用了 DeepSeek Harness 的主题变量，并将其消息、思考、工具调用及输入区的
展示规则适配到 WebView2。上游版本、对应源码路径及改动范围见
[来源说明](Overlay/Harness/SOURCES.md)，MIT 许可证见
[DeepSeek Harness 许可证](Overlay/Harness/vendor/deepseek-harness.LICENSE)。
Markdown 渲染使用本地打包的 marked 与 DOMPurify，各自许可证同在该 vendor 目录，发布时附带于 licenses 目录。
