# DeepSeek Harness presentation port

Upstream: https://github.com/deepseek-ai/deepseek-harness

Pinned revision: `687f282e8625c6b8d11ff58211a90524be2e4317` (retrieved 2026-10-18).

`vendor/base.css` and `vendor/design-platform.css` are unmodified upstream theme files from `packages/client/ui-theme/src/styles/`. The upstream MIT license is included as `vendor/deepseek-harness.LICENSE` and embedded in the application.

The local `chat.css` and `chat.js` adapt the following upstream presentation and interaction rules to the existing C# application:

| Upstream source | Local behavior |
| --- | --- |
| `packages/client/ui-chat/src/client/chat/MessageItem.module.css` | Right-aligned user bubble (20px radius, 10px/16px padding, 70.2% column cap); unboxed assistant prose |
| `packages/client/ui-chat/src/client/chat/ChatView.module.css` | Shared centered 748px conversation axis, 6px process spacing and 12px answer spacing |
| `packages/client/ui-chat/src/client/chat/ReasoningRow.tsx` and `.module.css` | Independent collapsed reasoning; live paragraph preview; complete expandable Markdown |
| `packages/client/ui-chat/src/client/chat/TurnProcessNodeView.tsx` | Settled turn duration, interrupted/error state and process disclosure |
| `packages/client/ui-tool/src/client/tool/ToolCallTree.tsx` | A stable row per call ID, updated from running to result; independent parameter/result disclosure |
| `packages/client/ui-conversation/src/client/skeleton/InputBar.module.css` | Theme input surface, 28px composer radius, text growth and bottom toolbar |
| `packages/client/ui-chat/src/client/chat/use-scroll-follow.ts` | Independent reader scroll intent, bottom follow and a return-to-latest affordance |

This is a WinForms/WebView2 presentation port and C# conversation-loop adapter, not a bundled Cordis/Node.js runtime. Terra Agent keeps its game-specific tool registry, permission settings, memory readers, navigation, model configuration, history format and Wiki browser. Upstream shell execution, plugin loading, MCP, subagents, workspace management and its other general-purpose agent facilities are not exposed by this application. The official logo/product identity is not reused.

In 1.4.0 all feature chrome (settings, API key, usage, game permissions, direct locator, curated reference and Wiki navigation) uses the same local WebView and theme tokens. The earlier native feature forms/tables and native chat renderer have been removed. `ThemeMode` is persisted with settings as `light`, `dark` or `system`; the pre-paint boot script and native host resolve the same preference. System color changes propagate through `prefers-color-scheme`. The floating ball, native context menus and the isolated Wiki document follow the resolved appearance. Wiki navigation controls remain in the local shell; only the external page viewport is overlaid by a separate WebView. Its bounds and popup visibility are synchronized with the local layout, and it has no local host bridge. The Wiki's `view-dark`/`view-light` classes and a removable document stylesheet adapt its page and tables without changing images or article content.

The host rebuilds completed assistant/tool message pairs from saved traces for subsequent model requests. Pending calls never enter replay. SSE fragments are assembled by tool-call index before dispatch; tool calls are executed only after a valid stream completion. Reasoning, commentary, call IDs, arguments, results and duration use the same ordered trace records for live projection and history. Stream snapshots are periodically checkpointed; stop and error paths persist the final received snapshot.

Markdown dependencies are vendored locally from the npm registry and embedded; no CDN or Node installation is required at runtime:

- `marked@18.1.1`, `lib/marked.umd.js`, MIT, `vendor/marked.LICENSE` (https://github.com/markedjs/marked).
- `dompurify@3.4.17`, `dist/purify.min.js`, Apache-2.0 OR MPL-2.0, `vendor/dompurify.LICENSE` and `vendor/dompurify.LICENSE-MPL` (https://github.com/cure53/DOMPurify).

Model Markdown is sanitized before insertion. Tool results and arguments are inserted as text. The embedded UI has no network connection capability, host objects or external page navigation. Only its exact local origin can send bridge commands. The Wiki WebView uses a separate profile with no local bridge.
