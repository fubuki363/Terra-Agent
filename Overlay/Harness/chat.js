/* DeepSeek Harness presentation port: see SOURCES.md and vendor/deepseek-harness.LICENSE. */
"use strict";
const $ = (id) => document.getElementById(id);
const paths = {
  sidebar: "M8 3v18M3 3h18v18H3z",
  plus: "M12 5v14M5 12h14",
  search: "m20 20-5-5M17 10a7 7 0 1 1-14 0 7 7 0 0 1 14 0",
  book: "M12 5v15M3 4c4-1 6 0 9 2 3-2 5-3 9-2v15c-4-1-6 0-9 2-3-2-5-3-9-2z",
  locate:
    "M12 3v3m0 12v3M3 12h3m12 0h3M19 12a7 7 0 1 1-14 0 7 7 0 0 1 14 0M14 12a2 2 0 1 1-4 0 2 2 0 0 1 4 0",
  settings:
    "m9 3-1 3-3 1-2 4 2 2v3l4 3 3-1 3 1 4-3v-3l2-2-2-4-3-1-1-3zM15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0",
  activity: "M3 12h4l3-8 4 16 3-8h4",
  sun: "M12 2v2m0 16v2M2 12h2m16 0h2M5 5l1 1m12 12 1 1M5 19l1-1M18 6l1-1M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0",
  moon: "M20 15a8 8 0 0 1-11-11A9 9 0 1 0 20 15",
  monitor: "M3 4h18v13H3zM12 17v4m-4 0h8",
  more: "M5 12h.01M12 12h.01M19 12h.01",
  minus: "M5 12h14",
  maximize: "M5 5h14v14H5z",
  close: "m6 6 12 12M6 18 18 6",
  down: "M12 4v16m-6-6 6 6 6-6",
  up: "M12 20V4m-6 6 6-6 6 6",
  image: "M3 4h18v16H3zM3 16l5-5 5 5 3-3 5 5M16 8h.01",
  compass: "m16 8-3 5-5 3 3-5zM21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0",
  shield: "m12 3 8 3v6c0 4-4 7-8 9-4-2-8-5-8-9V6z",
  think:
    "M9 18h6m-6 3h6M8 15c-1-2-3-3-3-6a7 7 0 0 1 14 0c0 3-2 4-3 6M9 9l2 2 4-4",
  tool: "m14 6-3 3 4 4 3-3c2 5-3 8-6 5l-6 6-3-3 6-6c-3-3 0-8 5-6",
  check: "m5 12 4 4L19 6",
  alert: "m12 3 10 18H2zM12 9v5m0 3v.01",
  stop: "M7 7h10v10H7z",
  chevron: "m9 5 7 7-7 7",
  copy: "M8 8h12v13H8zM16 8V3H3v13h5",
  retry: "M4 10a8 8 0 1 1 1 8M4 4v6h6",
};
function icon(name) {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("fill", "none");
  svg.setAttribute("stroke", "currentColor");
  svg.setAttribute("stroke-width", "1.6");
  svg.setAttribute("stroke-linecap", "round");
  svg.setAttribute("stroke-linejoin", "round");
  svg.setAttribute("aria-hidden", "true");
  const path = document.createElementNS(svg.namespaceURI, "path");
  path.setAttribute("d", paths[name] || paths.tool);
  svg.append(path);
  return svg;
}
document
  .querySelectorAll("[data-icon]")
  .forEach((el) => el.append(icon(el.dataset.icon)));
function node(tag, cls, text) {
  const el = document.createElement(tag);
  if (cls) el.className = cls;
  if (text !== undefined) el.textContent = text;
  return el;
}
function send(action, value) {
  window.chrome?.webview?.postMessage({ action, value });
}
const state = {
  sessions: [],
  active: "",
  busy: false,
  turns: new Map(),
  current: null,
  following: true,
  trajectory: false,
  draftSession: "",
};
let frame = 0;
const pending = new Map();
function flushStreams() {
  frame = 0;
  for (const stream of pending.values()) applyStream(stream);
  pending.clear();
  follow();
}
function queueStream(stream) {
  pending.set(`${stream.step}:${stream.attempt}:${stream.kind}`, stream);
  if (!frame) frame = requestAnimationFrame(flushStreams);
}
function copyButton(value, title = "复制") {
  const b = node("button", "icon");
  b.title = title;
  b.setAttribute("aria-label", title);
  b.append(icon("copy"));
  b.onclick = () => {
    send("copy", typeof value === "function" ? value() : value);
    b.replaceChildren(icon("check"));
    b.title = "已复制";
    setTimeout(() => {
      b.replaceChildren(icon("copy"));
      b.title = title;
    }, 1400);
  };
  return b;
}
function markdown(el, text) {
  // Never grant model text HTML, script, SVG, form or bridge capabilities.
  const html = marked.parse(text || "", { gfm: true, breaks: false });
  el.innerHTML = DOMPurify.sanitize(html, {
    USE_PROFILES: { html: true },
    FORBID_TAGS: [
      "img",
      "style",
      "form",
      "input",
      "button",
      "textarea",
      "video",
      "audio",
      "iframe",
      "object",
      "embed",
    ],
    FORBID_ATTR: ["style", "id", "name"],
    ALLOW_DATA_ATTR: false,
  });
  el.querySelectorAll("a").forEach((a) => {
    const href = a.getAttribute("href") || "";
    if (!/^https?:\/\//i.test(href)) {
      a.removeAttribute("href");
      return;
    }
    a.onclick = (e) => {
      e.preventDefault();
      send("openLink", href);
    };
    a.rel = "noreferrer";
  });
  el.querySelectorAll("pre").forEach((pre) => {
    const wrap = node("div", "code-wrap"),
      bar = node("div", "code-banner"),
      code = pre.querySelector("code");
    bar.append(
      node(
        "span",
        "",
        (code?.className || "").replace("language-", "") || "text",
      ),
    );
    const b = node("button", "", "复制代码");
    b.onclick = () => {
      send("copy", code?.textContent || pre.textContent);
      b.textContent = "已复制";
      setTimeout(() => (b.textContent = "复制代码"), 1400);
    };
    bar.append(b);
    pre.replaceWith(wrap);
    wrap.append(bar, pre);
  });
}
function formatJson(value) {
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value || "{}";
  }
}
function follow() {
  requestAnimationFrame(() => {
    if (state.following && !window.getSelection()?.toString())
      $("viewport").scrollTop = $("viewport").scrollHeight;
    updateLatest();
  });
}
function updateLatest() {
  const v = $("viewport");
  $("latest").hidden =
    v.scrollHeight - v.scrollTop - v.clientHeight < 80 ||
    document.body.classList.contains("empty");
}
$("viewport").addEventListener(
  "scroll",
  () => {
    const v = $("viewport");
    state.following = v.scrollHeight - v.scrollTop - v.clientHeight < 80;
    updateLatest();
  },
  { passive: true },
);
$("viewport").addEventListener(
  "wheel",
  () => {
    state.following = false;
  },
  { passive: true },
);
$("latest").onclick = () => {
  state.following = true;
  $("viewport").scrollTop = $("viewport").scrollHeight;
  updateLatest();
};
function newTurn(turn) {
  const root = node("article", "turn");
  root.dataset.turnId = turn.id;
  const user = node("div", "user-row"),
    stack = node("div", "user-stack"),
    actions = node("div", "message-actions");
  stack.append(node("div", "bubble", turn.question));
  actions.append(copyButton(turn.question));
  stack.append(actions);
  user.append(stack);
  const flow = node("div", "assistant-flow"),
    toggle = node("button", "turn-process"),
    process = node("div", "process"),
    answer = node("div", "answer"),
    body = node("div", "markdown");
  toggle.append(node("span", "", "正在处理"), icon("chevron"));
  toggle.lastChild.classList.add("chevron");
  toggle.setAttribute("aria-expanded", "true");
  toggle.onclick = () => {
    process.hidden = !process.hidden;
    toggle.setAttribute("aria-expanded", String(!process.hidden));
  };
  const a = node("div", "message-actions");
  const model = {
    root,
    process,
    toggle,
    answer,
    body,
    items: new Map(),
    step: 0,
    content: "",
    turn,
  };
  a.append(copyButton(() => model.content, "复制回答"));
  const retry = node("button", "icon");
  retry.title = "重新提问";
  retry.setAttribute("aria-label", "重新提问");
  retry.append(icon("retry"));
  retry.onclick = () => setDraft(turn.question);
  a.append(retry);
  answer.append(body, a);
  answer.hidden = true;
  const running = node("div", "running-status");
  running.append(node("span", "dot"), node("span", "", "正在思考…"));
  model.running = running;
  flow.append(toggle, process, answer, running);
  root.append(user, flow);
  $("timeline").append(root);
  state.turns.set(turn.id, model);
  return model;
}
function disclosure(model, key, title, glyph) {
  if (model.items.has(key)) return model.items.get(key);
  const el = node("details", "disclosure"),
    summary = node("summary"),
    lead = node("span", "lead"),
    g = node("span", "glyph"),
    arrow = node("span", "arrow");
  g.append(icon(glyph));
  arrow.append(icon("chevron"));
  lead.append(g, arrow);
  const label = node("span", "title", title),
    preview = node("span", "preview"),
    duration = node("span", "duration"),
    body = node("div", "disclosure-body");
  summary.append(lead, label, preview, duration);
  el.append(summary, body);
  model.process.append(el);
  el.ontoggle = () => {
    if (el.open) el.dataset.opened = "true";
    updateLatest();
  };
  const item = { el, lead: g, label, preview, duration, body };
  model.items.set(key, item);
  return item;
}
function showReasoning(model, trace, running) {
  const item = disclosure(
    model,
    `reasoning:${trace.step}:${trace.attempt || 1}`,
    "思考",
    "think",
  );
  item.el.dataset.kind = "reasoning";
  item.el.classList.toggle("running", running);
  let preview =
    (trace.content || "").split(/\r?\n/).find((x) => x.trim()) || "";
  if (running) {
    const lines = (trace.content || "").split(/\r?\n/);
    if (lines.length > 1)
      preview =
        lines
          .slice(0, -1)
          .filter((x) => x.trim())
          .at(-1) || preview;
  }
  item.preview.textContent = preview.replaceAll("**", "");
  item.body.className = "disclosure-body reasoning-body markdown";
  markdown(item.body, trace.content);
  item.el.title = `第 ${trace.step} 步${trace.attempt > 1 ? " · 重试 " + trace.attempt : ""}`;
}
function showAnswer(model, step, content) {
  model.step = step;
  model.content = content;
  model.answer.hidden = !content;
  markdown(model.body, content);
}
function toolKey(trace) {
  return (
    trace.callId || `legacy:${trace.step}:${trace.toolName}:${trace.arguments}`
  );
}
function showTool(model, trace, completed) {
  // A model step's commentary precedes its tool calls, separate from the final response.
  if (model.content && model.step === trace.step) {
    const commentary = node("div", "markdown commentary");
    markdown(commentary, model.content);
    model.process.append(commentary);
    model.content = "";
    model.answer.hidden = true;
  }
  const item = disclosure(
    model,
    "tool:" + toolKey(trace),
    trace.displayName || trace.toolName,
    "tool",
  );
  item.el.dataset.callId = toolKey(trace);
  item.el.dataset.kind = "tool";
  item.el.classList.toggle("running", !completed);
  item.el.classList.toggle("error", !!trace.isError);
  item.lead.replaceChildren(
    icon(completed ? (trace.isError ? "alert" : "check") : "tool"),
  );
  item.preview.textContent = completed
    ? trace.summary
    : compactArgs(trace.arguments);
  item.duration.textContent =
    completed && trace.durationMilliseconds
      ? `${(trace.durationMilliseconds / 1000).toFixed(1)}s`
      : "";
  const meta = node("div", "tool-meta");
  meta.append(
    node("code", "", trace.toolName),
    node("span", "", trace.callId || ""),
  );
  const args = node("pre", "", formatJson(trace.arguments));
  item.body.replaceChildren(
    meta,
    node("div", "tool-detail-label", "参数"),
    args,
  );
  item.body.classList.add("tool-body");
  if (completed)
    item.body.append(
      node("div", "tool-detail-label", trace.isError ? "错误" : "结果"),
      node("pre", "", trace.content || trace.summary),
    );
}
function compactArgs(value) {
  try {
    return Object.values(JSON.parse(value || "{}"))
      .map((v) => (typeof v === "string" ? v : JSON.stringify(v)))
      .join(" · ");
  } catch {
    return value;
  }
}
function applyTrace(model, trace) {
  if (trace.kind === "reasoning") showReasoning(model, trace, false);
  else if (trace.kind === "tool_call" || trace.kind === "tool_result")
    showTool(model, trace, trace.kind === "tool_result");
  else if (trace.kind === "assistant_message")
    showAnswer(model, trace.step, trace.content);
}
function settle(model, turn) {
  model.turn = turn;
  model.running.hidden = true;
  model.root.dataset.status = turn.status;
  for (const item of model.items.values()) {
    if (item.el.classList.contains("running")) {
      item.el.classList.remove("running");
      if (item.el.dataset.kind === "tool") {
        item.el.classList.add("cancelled");
        item.preview.textContent =
          turn.status === "completed" ? "未返回结果" : "已中断";
        item.lead.replaceChildren(icon("stop"));
      }
    }
  }
  const tools = [...model.items.values()].filter(
    (x) => x.el.dataset.kind === "tool",
  ).length;
  const time =
    turn.durationMilliseconds >= 1000
      ? `${Math.round(turn.durationMilliseconds / 1000)} 秒`
      : "完成";
  const label =
    turn.status === "cancelled"
      ? "已停止"
      : turn.status === "error"
        ? "执行失败"
        : `已处理 · ${time}`;
  model.toggle.firstChild.textContent =
    label + (tools ? ` · ${tools} 次工具调用` : "");
  model.toggle.hidden = model.process.childElementCount === 0;
  model.process.hidden = turn.status === "completed" && !state.trajectory;
  model.toggle.setAttribute("aria-expanded", String(!model.process.hidden));
  if (turn.answer) showAnswer(model, model.step, turn.answer);
  if (turn.notice) {
    const notice = node(
      "div",
      "turn-notice" + (turn.status === "error" ? " error" : ""),
      turn.notice,
    );
    model.root.append(notice);
  }
}
function renderTurn(turn) {
  const model = newTurn(turn);
  for (const trace of turn.agentTraces || []) applyTrace(model, trace);
  if (!(turn.agentTraces || []).length) {
    if (turn.reasoningContent)
      showReasoning(
        model,
        { step: 1, attempt: 1, content: turn.reasoningContent },
        false,
      );
    for (const [i, trace] of (turn.toolTraces || []).entries())
      showTool(
        model,
        { ...trace, step: 1, callId: "legacy-" + i, content: trace.summary },
        true,
      );
  }
  settle(model, turn);
  return model;
}
function applyStream(stream) {
  const model = state.current;
  if (!model) return;
  if (stream.kind === "reasoning") {
    showReasoning(model, { ...stream, content: stream.content }, true);
    model.running.lastChild.textContent = "正在思考…";
  } else {
    showAnswer(model, stream.step, stream.content);
    model.running.lastChild.textContent = "正在生成回答…";
  }
}
function renderSessions() {
  const q = $("session-search").value.trim().toLowerCase();
  $("sessions").replaceChildren();
  for (const session of state.sessions) {
    if (q && !session.title.toLowerCase().includes(q)) continue;
    const b = node(
      "button",
      "session" + (session.id === state.active ? " active" : ""),
      session.title,
    );
    b.disabled = state.busy;
    b.title = session.title;
    b.onclick = () => send("resume", session.id);
    $("sessions").append(b);
  }
  if (!document.body.dataset.page || document.body.dataset.page === "chat")
    $("session-title").textContent =
      state.sessions.find((s) => s.id === state.active)?.title || "新对话";
}
function setDraft(value) {
  $("input").value = value;
  resizeInput();
  send("draft", value);
  $("input").focus();
}
function resizeInput() {
  const input = $("input");
  input.style.height = "auto";
  input.style.height = Math.min(180, Math.max(40, input.scrollHeight)) + "px";
  $("counter").textContent =
    input.value.length > 1500 ? `${input.value.length} / 2000` : "";
  updateSend();
}
function updateSend() {
  const b = $("send");
  b.disabled = !state.busy && !$("input").value.trim();
  b.classList.toggle("stopping", state.busy);
  b.replaceChildren(icon(state.busy ? "stop" : "up"));
  b.title = state.busy ? "停止生成" : "发送消息";
  b.setAttribute("aria-label", b.title);
  document
    .querySelectorAll('[data-action="newSession"]')
    .forEach((x) => (x.disabled = state.busy));
}
function submit() {
  if (state.busy) {
    send("cancel");
    return;
  }
  const text = $("input").value.trim();
  if (!text || text.length > 2000) return;
  send("send", text);
}
$("input").addEventListener("input", () => {
  resizeInput();
  send("draft", $("input").value);
});
$("input").addEventListener("keydown", (e) => {
  if (e.key === "Enter" && !e.shiftKey && !e.isComposing) {
    e.preventDefault();
    if (!state.busy) submit();
  }
});
$("send").onclick = submit;
$("screenshot").onchange = () =>
  send("screenshot", $("screenshot").checked ? "true" : "false");
$("session-search").oninput = renderSessions;
document
  .querySelectorAll("[data-prompt]")
  .forEach((b) => (b.onclick = () => setDraft(b.dataset.prompt)));
document.querySelectorAll("[data-action]").forEach(
  (b) =>
    (b.onclick = () => {
      if (b.dataset.action === "sidebar")
        document.body.classList.toggle(
          matchMedia("(max-width:760px)").matches
            ? "sidebar-open"
            : "sidebar-hidden",
        );
      else send(b.dataset.action);
      $("menu").hidden = true;
    }),
);
$("titlebar").addEventListener("mousedown", (e) => {
  if (e.button === 0 && !e.target.closest("button")) send("drag");
});
$("titlebar").addEventListener("dblclick", (e) => {
  if (!e.target.closest("button")) send("maximize");
});
$("theme").onclick = () => {
  const mode = window.terraTheme.dark ? "light" : "dark";
  window.terraTheme.apply(mode);
  send("theme", mode);
};
$("trajectory").onclick = () => {
  state.trajectory = !state.trajectory;
  document.body.classList.toggle("trajectory", state.trajectory);
  $("trajectory").classList.toggle("active", state.trajectory);
  if (state.trajectory)
    for (const model of state.turns.values()) {
      model.process.hidden = false;
      model.toggle.setAttribute("aria-expanded", "true");
    }
  updateLatest();
};
$("more").onclick = () => {
  $("menu").hidden = !$("menu").hidden;
  $("more").setAttribute("aria-expanded", String(!$("menu").hidden));
};
document.addEventListener("click", (e) => {
  if (!e.target.closest("#menu,#more")) $("menu").hidden = true;
});
function confirmDelete(action, title, description) {
  if (state.busy) return;
  $("menu").hidden = true;
  $("confirm-title").textContent = title;
  $("confirm-description").textContent = description;
  $("confirm-ok").onclick = () => {
    $("confirm").close();
    send(action, state.active);
  };
  $("confirm").showModal();
}
$("delete-session").onclick = () =>
  confirmDelete(
    "deleteSession",
    "删除当前对话？",
    "这段对话及其调用记录会从本机移除。",
  );
$("clear-history").onclick = () =>
  confirmDelete(
    "clearHistory",
    "清空全部历史？",
    "所有对话和调用记录都会从本机移除。",
  );
$("confirm-cancel").onclick = () => $("confirm").close();
document.addEventListener("keydown", (e) => {
  if (e.ctrlKey && e.key.toLowerCase() === "n") {
    e.preventDefault();
    send("newSession");
  }
  if (e.ctrlKey && e.key.toLowerCase() === "l") {
    e.preventDefault();
    if (window.terraFeatures?.page && window.terraFeatures.page !== "chat")
      window.terraFeatures.focus();
    else $("input").focus();
  }
  if (e.key === "Escape" && !$("confirm").open) {
    e.preventDefault();
    send(state.busy ? "cancel" : "collapse");
  }
});
function receive(message) {
  if (window.terraFeatures?.receive(message)) return;
  switch (message.type) {
    case "theme": {
      const dark = window.terraTheme.apply(message.mode);
      send("themeResolved", dark ? "dark" : "light");
      break;
    }
    case "sessions":
      state.sessions = message.sessions;
      state.active = message.active;
      renderSessions();
      break;
    case "history":
      pending.clear();
      state.turns.clear();
      state.current = null;
      $("timeline").replaceChildren();
      for (const turn of message.turns) renderTurn(turn);
      document.body.classList.toggle("empty", message.turns.length === 0);
      state.following = true;
      follow();
      break;
    case "begin": {
      pending.clear();
      document.body.classList.remove("empty");
      $("request-notice").hidden = true;
      state.current = newTurn(message.turn);
      state.following = true;
      $("input").value = "";
      resizeInput();
      follow();
      break;
    }
    case "stream":
      queueStream(message.stream);
      break;
    case "trace":
      if (state.current) {
        flushStreams();
        applyTrace(state.current, message.trace);
        state.current.running.lastChild.textContent =
          message.trace.kind === "tool_call"
            ? "正在调用 " +
              (message.trace.displayName || message.trace.toolName) +
              "…"
            : "正在思考…";
        follow();
      }
      break;
    case "status":
      if (state.current)
        state.current.running.lastChild.textContent = message.value;
      break;
    case "finish": {
      flushStreams();
      if (state.current) {
        const old = state.current;
        old.root.remove();
        state.turns.delete(old.turn.id);
        const turn = renderTurn(message.turn);
        state.current = null;
        state.turns.set(message.turn.id, turn);
      }
      follow();
      break;
    }
    case "notice":
      $("request-notice").textContent = message.value;
      $("request-notice").hidden = !message.value;
      break;
    case "busy":
      state.busy = message.value;
      updateSend();
      renderSessions();
      break;
    case "draft":
      $("input").value = message.value;
      resizeInput();
      break;
    case "focus":
      if (window.terraFeatures?.page && window.terraFeatures.page !== "chat")
        window.terraFeatures.focus();
      else $("input").focus();
      break;
    case "info":
      $("connection").textContent = message.hasKey
        ? "本地技能按需调用"
        : "请在设置中配置 API Key";
      $("connection").title = message.status;
      $("usage").textContent = message.usage;
      $("key-dot").classList.toggle("missing", !message.hasKey);
      break;
    case "navigation":
      $("navigation").hidden = !message.active;
      $("navigation-text").textContent = message.value;
      document.querySelector('[data-action="nextTarget"]').hidden =
        message.count < 2;
      break;
    default:
      if (!window.terraFeatures)
        (window.terraFeatureQueue ||= []).push(message);
      break;
  }
}
window.terraChat = { receive };
window.chrome?.webview?.addEventListener("message", (event) =>
  receive(event.data),
);
document.body.classList.add("empty");
resizeInput();
send("ready");
send("themeResolved", window.terraTheme.dark ? "dark" : "light");
window.addEventListener("terra-appearance", (event) => {
  $("theme").replaceChildren(icon(event.detail.dark ? "moon" : "sun"));
  if (event.detail.mode === "system")
    send("themeResolved", event.detail.dark ? "dark" : "light");
});
