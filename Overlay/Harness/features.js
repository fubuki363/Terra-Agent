// Settings, locator, reference and Wiki chrome share the conversation shell.
(() => {
  const host = $("feature-view");
  const model = {
    page: "chat",
    settings: null,
    reference: [],
    biomes: [],
    kind: "objects",
    queries: {},
    results: [],
    selected: -1,
    searching: false,
    referenceCategory: "全部",
    wiki: {},
    wikiVisible: false,
  };
  const titles = {
    settings: "设置",
    locator: "查找与导航",
    reference: "常用速查",
    wiki: "Terraria 在线图鉴",
  };
  const permissionNames = [
    ["includeHealth", "血量"],
    ["includeMana", "魔力"],
    ["includeInventory", "背包内容"],
    ["includeEquipment", "装备与饰品"],
    ["includeWorldInfo", "世界信息"],
    ["includeProgress", "进度信息"],
    ["includeAchievements", "成就信息"],
  ];
  function button(text, action, cls = "") {
    const b = node("button", "ui-button " + cls, text);
    b.type = "button";
    if (action) b.onclick = action;
    return b;
  }
  function header(title, description) {
    const h = node("div", "feature-header"),
      text = node("div");
    text.append(node("h1", "", title));
    if (description) text.append(node("p", "", description));
    h.append(
      text,
      button("返回对话", () => send("chat"), "back-chat"),
    );
    return h;
  }
  function tabBar(labels, selected, change) {
    const tabs = node("div", "feature-tabs");
    tabs.setAttribute("role", "tablist");
    labels.forEach(([value, label]) => {
      const b = node("button", value === selected ? "active" : "", label);
      b.setAttribute("role", "tab");
      b.setAttribute("aria-selected", String(value === selected));
      b.onclick = () => change(value);
      b.dataset.tab = value;
      tabs.append(b);
    });
    return tabs;
  }
  function activateTabs(tabs, selected) {
    tabs.querySelectorAll("[data-tab]").forEach((b) => {
      const active = b.dataset.tab === selected;
      b.classList.toggle("active", active);
      b.setAttribute("aria-selected", String(active));
    });
  }
  function input(id, placeholder, type = "text") {
    const field = node("input", "field-input");
    field.id = id;
    field.type = type;
    field.placeholder = placeholder;
    field.setAttribute("aria-label", placeholder);
    return field;
  }
  function card(title) {
    const c = node("section", "settings-card");
    c.append(node("h2", "", title));
    return c;
  }
  function row(label, hint, control) {
    const r = node("label", "setting-row"),
      text = node("span", "setting-label");
    text.append(node("strong", "", label));
    if (hint) text.append(node("small", "", hint));
    r.append(text, control);
    return r;
  }
  function settingSwitch(key) {
    const field = input("setting-" + key, key, "checkbox");
    field.className = "switch";
    field.dataset.setting = key;
    return field;
  }
  function settingNumber(key, min, max, step = 1) {
    const field = input("setting-" + key, key, "number");
    field.min = min;
    field.max = max;
    field.step = step;
    field.dataset.setting = key;
    return field;
  }
  function currentSettings() {
    const values = {};
    host
      .querySelectorAll("[data-setting]")
      .forEach(
        (field) =>
          (values[field.dataset.setting] =
            field.type === "checkbox" ? field.checked : Number(field.value)),
      );
    return values;
  }
  function confirm(action, title, description) {
    $("confirm-title").textContent = title;
    $("confirm-description").textContent = description;
    $("confirm-ok").textContent = "确认";
    $("confirm-ok").onclick = () => {
      $("confirm").close();
      send(action);
      reportWikiBounds();
    };
    $("confirm").showModal();
    reportWikiBounds();
  }
  function setAppearance(mode, notify = true) {
    const dark = window.terraTheme.apply(mode);
    if (notify) send("theme", mode);
    updateThemeChoices();
    return dark;
  }
  function updateThemeChoices() {
    host
      .querySelectorAll("[data-theme-mode]")
      .forEach((b) =>
        b.setAttribute(
          "aria-checked",
          String(b.dataset.themeMode === window.terraTheme.mode),
        ),
      );
  }
  function renderSettings(tab = 0) {
    host.replaceChildren(
      header("设置", "管理外观、对话上下文与本地游戏权限。"),
    );
    const scroll = node("div", "feature-scroll"),
      content = node("div", "feature-content"),
      panels = new Map();
    const labels = [
      ["general", "常规"],
      ["key", "API Key"],
      ["usage", "用量"],
      ["game", "游戏数据"],
    ];
    const initial = labels[Math.min(3, Math.max(0, tab))][0];
    const tabs = tabBar(labels, initial, (value) => {
      activateTabs(tabs, value);
      panels.forEach((panel, key) => (panel.hidden = key !== value));
      scroll.scrollTop = 0;
    });
    content.append(tabs);
    for (const [key] of labels) {
      const panel = node("div", "settings-panel");
      panel.dataset.panel = key;
      panel.hidden = key !== initial;
      panels.set(key, panel);
      content.append(panel);
    }
    const appearance = card("外观"),
      choices = node("div", "theme-choices");
    choices.setAttribute("role", "radiogroup");
    choices.setAttribute("aria-label", "界面风格");
    for (const [mode, text, glyph] of [
      ["light", "浅色风格", "sun"],
      ["dark", "夜间风格", "moon"],
      ["system", "跟随系统", "monitor"],
    ]) {
      const b = node("button", "theme-choice");
      b.dataset.themeMode = mode;
      b.setAttribute("role", "radio");
      b.append(icon(glyph), node("span", "", text));
      const check = icon("check");
      check.classList.add("choice-check");
      b.append(check);
      b.onclick = () => setAppearance(mode);
      choices.append(b);
    }
    appearance.append(
      choices,
      node(
        "p",
        "",
        "所有功能页面同步切换；跟随系统会随 Windows 的颜色设置自动变化。",
      ),
    );
    panels.get("general").append(appearance);
    const context = card("上下文与历史");
    context.append(
      row(
        "保留对话历史",
        "关闭程序后仍可继续之前的对话。",
        settingSwitch("persistHistory"),
      ),
      row(
        "最近对话轮数",
        "0 表示每次独立提问。",
        settingNumber("contextTurns", 0, 20),
      ),
      row(
        "上下文字符预算",
        "更多上下文会增加 Token 用量。",
        settingNumber("contextCharacterBudget", 1000, 50000, 1000),
      ),
      row(
        "历史保留天数",
        "自动清理过期的非当前会话。",
        settingNumber("historyRetentionDays", 1, 365),
      ),
    );
    panels
      .get("general")
      .append(
        context,
        node(
          "p",
          "field-hint",
          "深度思考始终开启，思考、工具参数与结果会保存在会话轨迹中。",
        ),
      );
    const key = card("DeepSeek API Key"),
      keyStatus = node("div", "key-status");
    keyStatus.id = "settings-key-status";
    const keyField = node("div", "key-field"),
      secret = input("settings-key-input", "粘贴以 sk- 开头的 Key", "password");
    secret.autocomplete = "off";
    secret.spellcheck = false;
    keyField.append(
      secret,
      button("粘贴", () => send("pasteKey")),
    );
    const keyActions = node("div", "button-row");
    keyActions.append(
      button("保存到本机", () => send("saveKey", secret.value), "primary"),
      button(
        "删除本地 Key",
        () =>
          confirm(
            "removeKey",
            "删除本地 Key？",
            "环境变量中的 Key 不会被修改。",
          ),
        "danger",
      ),
    );
    const keyMessage = node("div", "feature-status");
    keyMessage.id = "feature-status-key";
    key.append(
      keyStatus,
      keyField,
      keyActions,
      node(
        "p",
        "",
        "已有 Key 不会回显，也不会写入聊天历史。环境变量中的 Key 优先使用。",
      ),
      keyMessage,
    );
    panels.get("key").append(key);
    const usage = card("本地用量统计"),
      usageText = node("div", "usage-summary");
    usageText.id = "settings-usage";
    const usageActions = node("div", "button-row");
    usageActions.append(
      button("刷新统计", () => send("settingsRefresh")),
      button("重置统计", () =>
        confirm(
          "resetUsage",
          "重置用量统计？",
          "只重置本机统计，不影响 DeepSeek 账户账单。",
        ),
      ),
    );
    const usageStatus = node("div", "feature-status");
    usageStatus.id = "feature-status-usage";
    usage.append(
      usageText,
      usageActions,
      node(
        "p",
        "",
        "用量来自模型响应中的 usage 字段，实际计费以 DeepSeek 控制台为准。",
      ),
      usageStatus,
    );
    panels.get("usage").append(usage);
    const live = card("游戏数据读取");
    const runtimeStatus = node("p", "feature-status", model.gameStatus || "正在识别游戏…");
    runtimeStatus.id = "game-runtime-status";
    live.append(runtimeStatus, button("刷新识别", () => send("settingsRefresh")));
    live.append(
      row(
        "启用实时读取",
        "自动识别 Terraria / tModLoader，无需安装配套 Mod；由 Agent 按问题决定是否只读访问。",
        settingSwitch("includeMemoryContext"),
      ),
    );
    const permissions = node("div", "permission-grid");
    permissionNames.forEach(([key, label]) =>
      permissions.append(row(label, "", settingSwitch(key))),
    );
    live.append(permissions);
    panels.get("game").append(live);
    const location = card("定位与导航权限");
    location.append(
      row(
        "世界物体",
        "矿物、植物、宝箱、家具及自然拾取物。",
        settingSwitch("enableObjectLocator"),
      ),
      row(
        "群系与结构",
        "世界层、主要群系、迷你群系和剑冢。",
        settingSwitch("enableBiomeLocator"),
      ),
      row(
        "箱子与个人储物",
        "搜索物品，并为有坐标的结果启动导航。",
        settingSwitch("enableStorageLocator"),
      ),
    );
    panels.get("game").append(location);
    const preview = card("实时状态预览"),
      previewText = node(
        "pre",
        "preview-block",
        "点击读取预览，仅检查上方选中的状态类别。",
      );
    previewText.id = "game-preview";
    const previewButton = button("读取预览", () =>
      send("previewGame", currentSettings()),
    );
    previewButton.id = "game-preview-button";
    preview.append(previewButton, previewText);
    panels.get("game").append(preview);
    scroll.append(content);
    const footer = node("div", "feature-footer"),
      status = node("span", "feature-status");
    status.id = "feature-status-settings";
    footer.append(
      status,
      button(
        "保存设置",
        () => {
          send("settingsSave", currentSettings());
        },
        "primary",
      ),
    );
    host.append(scroll, footer);
    fillSettings();
    updateThemeChoices();
  }
  function fillSettings() {
    if (!model.settings) return;
    host.querySelectorAll("[data-setting]").forEach((field) => {
      const value = model.settings[field.dataset.setting];
      if (field.type === "checkbox") field.checked = !!value;
      else field.value = value;
    });
    if ($("settings-key-status"))
      $("settings-key-status").textContent = model.keyStatus || "";
    if ($("settings-usage"))
      $("settings-usage").textContent = model.usage || "";
  }
  function searchHint() {
    return model.kind === "biomes"
      ? `提供全部 ${model.biomes.length} 类可检测群系、世界层与结构，也可输入别名。`
      : model.kind === "storage"
        ? "搜索世界箱子及个人储物，显示数量、槽位和排列提示。"
        : "输入矿物、植物、宝箱、家具或自然拾取物名称。";
  }
  function renderLocator() {
    host.replaceChildren(
      header("查找与导航", "直接读取游戏数据，不消耗 AI 用量。"),
    );
    const scroll = node("div", "feature-scroll"),
      content = node("div", "feature-content");
    const tabs = tabBar(
      [
        ["objects", "世界物体"],
        ["biomes", "群系 / 结构"],
        ["storage", "箱内物品"],
      ],
      model.kind,
      (kind) => {
        if (model.searching) return;
        model.queries[model.kind] = $("locator-query").value;
        model.kind = kind;
        model.results = [];
        model.selected = -1;
        renderLocator();
      },
    );
    content.append(tabs);
    const toolbar = node("div", "feature-toolbar"),
      query = input("locator-query", "输入名称或选择下方示例");
    query.value = model.queries[model.kind] || "";
    query.maxLength = 200;
    query.oninput = () => (model.queries[model.kind] = query.value);
    query.onkeydown = (e) => {
      if (e.key === "Enter" && !e.isComposing) {
        e.preventDefault();
        if (!model.searching) startSearch();
      }
    };
    toolbar.append(query);
    if (model.kind === "biomes") {
      const select = node("select", "field-select");
      select.id = "biome-presets";
      select.setAttribute("aria-label", "全部可检测群系");
      select.append(new Option("全部可检测群系", ""));
      model.biomes.forEach((name) => select.append(new Option(name, name)));
      select.onchange = () => {
        if (select.value) {
          query.value = select.value;
          model.queries[model.kind] = query.value;
          query.focus();
        }
      };
      toolbar.append(select);
    }
    const search = button(
      model.searching ? "取消查找" : "开始查找",
      () => (model.searching ? send("cancelSearch") : startSearch()),
      "primary",
    );
    search.id = "locator-search";
    toolbar.append(search);
    content.append(toolbar);
    const shortcuts = node("div", "search-examples");
    const examples =
      model.kind === "biomes"
        ? ["微光湖", "地下丛林", "猩红之地", "附魔剑冢", "浮空岛"]
        : model.kind === "storage"
          ? ["魔镜", "回忆药水", "铁锭", "木材", "生命水晶"]
          : ["大自然的恩赐", "巨型龙蛋", "友好巨石", "植物", "矿物"];
    examples.forEach((name) => {
      const b = node("button", "", name);
      b.onclick = () => {
        query.value = name;
        model.queries[model.kind] = name;
        query.focus();
      };
      shortcuts.append(b);
    });
    content.append(shortcuts);
    const personal = node("label", "personal-check"),
      check = input("locator-personal", "包含个人储物", "checkbox");
    check.checked = model.personal !== false;
    check.onchange = () => (model.personal = check.checked);
    personal.append(
      check,
      node("span", "", "包含猪猪储蓄罐、保险箱、护卫熔炉与虚空保险库"),
    );
    personal.hidden = model.kind !== "storage";
    content.append(personal);
    const status = node("div", "feature-status", searchHint());
    status.id = "feature-status-locator";
    status.setAttribute("role", "status");
    content.append(status);
    const wrap = node("div", "results-wrap"),
      table = node("table", "data-table");
    table.id = "locator-table";
    const head = node("thead"),
      tr = node("tr");
    ["目标 / 数量", "位置", "方位", "距离（格）", "图格坐标"].forEach((title) =>
      tr.append(node("th", "", title)),
    );
    head.append(tr);
    table.append(head, node("tbody"));
    wrap.append(table);
    content.append(wrap);
    const detail = node(
      "p",
      "result-detail",
      "选中结果查看位置细节；双击有世界坐标的目标即可导航。",
    );
    detail.id = "locator-detail";
    content.append(detail);
    scroll.append(content);
    host.append(scroll);
    const footer = node("div", "feature-footer");
    footer.append(node("span", "feature-status", "个人储物没有固定世界位置。"));
    const navigate = button("导航到所选", navigateSelected, "primary");
    navigate.id = "locator-navigate";
    navigate.disabled = true;
    footer.append(
      button("停止导航", () => send("stopNavigation")),
      navigate,
    );
    host.append(footer);
    renderResults();
    setSearching(model.searching);
  }
  function startSearch() {
    const query = $("locator-query").value.trim();
    model.queries[model.kind] = query;
    send("worldSearch", {
      kind: model.kind,
      query,
      personal: $("locator-personal").checked,
    });
  }
  function setSearching(busy) {
    model.searching = busy;
    if (model.page !== "locator") return;
    $("locator-search").textContent = busy ? "取消查找" : "开始查找";
    host
      .querySelectorAll(
        ".feature-tabs button,.search-examples button,#locator-query,#biome-presets,#locator-personal",
      )
      .forEach((el) => (el.disabled = busy));
    updateSelected();
  }
  function renderResults() {
    const body = $("locator-table")?.querySelector("tbody");
    if (!body) return;
    body.replaceChildren();
    if (!model.results.length) {
      const tr = node("tr", "empty-row"),
        cell = node(
          "td",
          "",
          model.searching ? "正在查找…" : "查找结果会显示在这里",
        );
      cell.colSpan = 5;
      tr.append(cell);
      body.append(tr);
    }
    model.results.forEach((hit, index) => {
      const tr = node("tr", index === model.selected ? "selected" : "");
      tr.tabIndex = 0;
      tr.setAttribute("aria-selected", String(index === model.selected));
      [
        hit.name,
        hit.place,
        hit.direction,
        hit.distanceTiles == null
          ? "—"
          : Math.round(hit.distanceTiles).toString(),
        hit.tileX == null ? "个人储物" : `${hit.tileX}, ${hit.tileY}`,
      ].forEach((text, i) =>
        tr.append(node("td", i > 2 ? "number" : "", text)),
      );
      const select = () => {
        model.selected = index;
        updateSelected();
      };
      tr.onclick = select;
      tr.ondblclick = () => {
        select();
        navigateSelected();
      };
      tr.onkeydown = (e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          select();
        }
      };
      body.append(tr);
    });
    updateSelected();
  }
  function updateSelected() {
    if (model.page !== "locator" || !$("locator-navigate")) return;
    const selected = model.results[model.selected];
    $("locator-navigate").disabled = model.searching || !selected?.canNavigate;
    $("locator-detail").textContent =
      selected?.detail ||
      "选中结果查看位置细节；双击有世界坐标的目标即可导航。";
    $("locator-table")
      .querySelectorAll("tbody tr")
      .forEach((tr, i) => {
        tr.classList.toggle("selected", i === model.selected);
        tr.setAttribute("aria-selected", String(i === model.selected));
      });
  }
  function navigateSelected() {
    if (!model.searching && model.results[model.selected]?.canNavigate)
      send("navigateResult", model.selected);
  }
  function renderReference() {
    host.replaceChildren(
      header("常用速查", "精选原版药水配方与关键掉落，离线可用。"),
    );
    const scroll = node("div", "feature-scroll"),
      content = node("div", "feature-content");
    const tabs = tabBar(
      [
        ["全部", "全部"],
        ["药水", "药水"],
        ["掉落", "掉落"],
      ],
      model.referenceCategory,
      (category) => {
        model.referenceCategory = category;
        activateTabs(tabs, category);
        updateReference();
      },
    );
    content.append(tabs);
    const toolbar = node("div", "feature-toolbar"),
      search = input("reference-query", "搜索药水、材料、怪物或掉落物");
    search.value = model.referenceQuery || "";
    search.oninput = () => {
      model.referenceQuery = search.value;
      updateReference();
    };
    toolbar.append(search);
    content.append(toolbar);
    const wrap = node("div", "results-wrap"),
      table = node("table", "data-table");
    table.id = "reference-table";
    const head = node("thead"),
      tr = node("tr");
    ["条目", "材料 / 出现条件", "效果 / 主要掉落"].forEach((text) =>
      tr.append(node("th", "", text)),
    );
    head.append(tr);
    table.append(head, node("tbody"));
    wrap.append(table);
    content.append(wrap);
    const detail = node("section", "settings-card reference-detail");
    detail.id = "reference-detail";
    content.append(detail);
    scroll.append(content);
    host.append(scroll);
    updateReference();
  }
  function updateReference() {
    const query = (model.referenceQuery || "")
      .trim()
      .toLowerCase()
      .split(/\s+/)
      .filter(Boolean);
    const entries = model.reference.filter(
      (entry) =>
        (model.referenceCategory === "全部" ||
          entry.category === model.referenceCategory) &&
        query.every((part) =>
          Object.values(entry).join(" ").toLowerCase().includes(part),
        ),
    );
    const body = $("reference-table").querySelector("tbody");
    body.replaceChildren();
    entries.forEach((entry) => {
      const tr = node("tr");
      tr.tabIndex = 0;
      const name = node("td", "", entry.name);
      name.append(node("small", "", entry.stage));
      tr.append(
        name,
        node("td", "", entry.requirement),
        node("td", "", entry.result),
      );
      tr.onclick = () => showReferenceDetail(entry, tr);
      tr.ondblclick = () => askReference(entry);
      tr.onkeydown = (e) => {
        if (e.key === "Enter") showReferenceDetail(entry, tr);
      };
      body.append(tr);
    });
    if (entries.length) showReferenceDetail(entries[0], body.firstChild);
    else {
      const tr = node("tr", "empty-row"),
        td = node("td", "", "没有匹配条目，试试其他关键词。");
      td.colSpan = 3;
      tr.append(td);
      body.append(tr);
      $("reference-detail").replaceChildren(
        node(
          "p",
          "field-hint",
          "常用速查未收录的内容，可以返回对话让 Agent 检索。",
        ),
      );
    }
  }
  function showReferenceDetail(entry, row) {
    $("reference-table")
      .querySelectorAll("tbody tr")
      .forEach((tr) => tr.classList.toggle("selected", tr === row));
    const detail = $("reference-detail"),
      actions = node("div", "button-row");
    detail.replaceChildren(
      node("h2", "", entry.name + " · " + entry.category),
      node(
        "div",
        "detail-content",
        `${entry.category === "药水" ? "材料" : "出现条件"}：${entry.requirement}\n${entry.category === "药水" ? "效果" : "主要掉落"}：${entry.result}\n说明：${entry.note}`,
      ),
    );
    actions.append(
      node("span", "field-hint", `共 ${model.reference.length} 条精选内容`),
      button("继续问 Agent", () => askReference(entry), "primary"),
    );
    detail.append(actions);
  }
  function askReference(entry) {
    send(
      "featureAsk",
      entry.category === "药水"
        ? `请详细说明${entry.name}的制作方法、材料获取方式，以及什么时候值得使用。`
        : `请详细说明${entry.name}的主要掉落、生成条件和高效获取方法。`,
    );
  }
  function renderWiki() {
    const page = node("div", "wiki-page");
    page.append(header("Terraria 在线图鉴", "官方原网页 · 不消耗 AI 用量"));
    const toolbar = node("div", "feature-toolbar");
    for (const [command, title, glyph] of [
      ["wikiBack", "后退", "chevron"],
      ["wikiForward", "前进", "chevron"],
      ["wikiReload", "刷新或停止", "retry"],
      ["wikiHome", "首页", "book"],
    ]) {
      const b = button("", () => send(command));
      b.id = command;
      b.title = title;
      b.setAttribute("aria-label", title);
      const svg = icon(glyph);
      if (command === "wikiBack") svg.style.transform = "rotate(180deg)";
      b.append(svg);
      toolbar.append(b);
    }
    const address = node("form", "wiki-address"),
      field = input("wiki-address", "搜索 Wiki 或输入官方地址");
    field.value = model.wiki.url || "";
    address.onsubmit = (e) => {
      e.preventDefault();
      send("wikiNavigate", field.value);
    };
    const go = button("搜索");
    go.type = "submit";
    address.append(field, go);
    toolbar.append(address);
    const language = node("select", "field-select");
    language.id = "wiki-language";
    language.setAttribute("aria-label", "图鉴语言");
    language.append(new Option("简体中文", "zh"), new Option("English", "en"));
    language.value = model.wiki.english ? "en" : "zh";
    language.onchange = () => send("wikiLanguage", language.value);
    toolbar.append(language);
    page.append(toolbar);
    const categories = node("div", "search-examples");
    [
      ["物品", "Items"],
      ["药水", "Potions"],
      ["敌怪", "Enemies"],
      ["NPC", "NPCs"],
      ["Boss", "Bosses"],
      ["制作", "Crafting"],
    ].forEach(([zh, en]) => {
      const b = node("button", "", zh);
      b.onclick = () =>
        send(
          "wikiNavigate",
          `https://terraria.wiki.gg/${model.wiki.english ? "" : "zh/"}wiki/${encodeURIComponent(model.wiki.english ? en : zh)}`,
        );
      categories.append(b);
    });
    const ask = button("问 Agent", () => send("wikiAsk"));
    ask.id = "wiki-ask";
    categories.append(ask);
    page.append(categories);
    const region = node("div", "wiki-region");
    region.id = "wiki-viewport";
    const placeholder = node("div", "wiki-placeholder");
    placeholder.append(
      node("span", "", "正在准备在线 Wiki 浏览器…"),
      button("刷新重试", () => send("wikiReload")),
    );
    const install = button("安装 WebView2", () =>
      send(
        "openLink",
        "https://developer.microsoft.com/microsoft-edge/webview2/",
      ),
    );
    install.id = "wiki-install";
    install.hidden = true;
    placeholder.append(install);
    region.append(placeholder);
    page.append(region);
    const footer = node("div", "wiki-footer");
    footer.id = "wiki-status";
    page.append(footer);
    host.replaceChildren(page);
    updateWiki();
    wikiObserver?.disconnect();
    wikiObserver = new ResizeObserver(reportWikiBounds);
    wikiObserver.observe(region);
    reportWikiBounds();
  }
  function updateWiki() {
    if (model.page !== "wiki" || !$("wiki-status")) return;
    $("wiki-status").textContent =
      model.wiki.status || "需要联网 · Ctrl+L 地址栏 · Ctrl+F 页内查找";
    $("wiki-status").title = $("wiki-status").textContent;
    if (document.activeElement !== $("wiki-address"))
      $("wiki-address").value = model.wiki.url || "";
    $("wiki-language").value = model.wiki.english ? "en" : "zh";
    $("wikiBack").disabled = !model.wiki.canBack;
    $("wikiForward").disabled = !model.wiki.canForward;
    $("wiki-ask").disabled = !model.wiki.canAsk;
    $("wiki-install").hidden = !model.wiki.failed;
    $("wikiReload").replaceChildren(
      icon(model.wiki.loading ? "close" : "retry"),
    );
    const placeholder = $("wiki-viewport").querySelector(
      ".wiki-placeholder span",
    );
    placeholder.textContent = model.wiki.failed
      ? model.wiki.status
      : "正在加载官方 Wiki 原网页…";
    reportWikiBounds();
  }
  let boundsFrame = 0;
  let wikiObserver;
  function reportWikiBounds() {
    if (boundsFrame) return;
    boundsFrame = requestAnimationFrame(() => {
      boundsFrame = 0;
      const region = $("wiki-viewport");
      if (model.page !== "wiki" || !region) return;
      const rect = region.getBoundingClientRect();
      const visible =
        !!(model.wiki.ready || model.wiki.loading) &&
        !$("confirm").open &&
        $("menu").hidden &&
        !document.body.classList.contains("sidebar-open");
      send("wikiBounds", {
        x: rect.x + 1,
        y: rect.y + 1,
        width: Math.max(0, rect.width - 2),
        height: Math.max(0, rect.height - 2),
        scale: devicePixelRatio,
        visible,
      });
    });
  }
  function route(page, tab = 0) {
    wikiObserver?.disconnect();
    model.page = page;
    document.body.dataset.page = page;
    host.hidden = page === "chat";
    document.body.classList.remove("sidebar-open");
    document
      .querySelectorAll(".nav-button[data-action]")
      .forEach((b) => b.classList.toggle("active", b.dataset.action === page));
    if (page === "chat") {
      host.replaceChildren();
      renderSessions();
      follow();
    } else {
      $("session-title").textContent = titles[page] || "Terra Agent";
      if (page === "settings") renderSettings(tab);
      else if (page === "locator") renderLocator();
      else if (page === "reference") renderReference();
      else if (page === "wiki") renderWiki();
    }
  }
  function receive(message) {
    switch (message.type) {
      case "featureData":
        model.biomes = message.biomes;
        model.reference = message.reference;
        if (model.page === "reference") renderReference();
        return true;
      case "route":
        route(message.page, message.tab);
        return true;
      case "settingsData":
        model.settings = message.settings;
        model.gameStatus = message.gameStatus;
        if ($("game-runtime-status")) $("game-runtime-status").textContent = model.gameStatus || "未检测到游戏客户端";
        model.keyStatus = message.keyStatus;
        model.usage = message.usage;
        fillSettings();
        $("key-dot").classList.toggle("missing", !message.hasKey);
        return true;
      case "featureStatus": {
        const status = $("feature-status-" + message.target);
        if (status) status.textContent = message.value;
        return true;
      }
      case "keyPaste":
        if ($("settings-key-input")) {
          $("settings-key-input").value = message.value;
          $("settings-key-input").focus();
        }
        return true;
      case "keySaved":
        if (message.success && $("settings-key-input"))
          $("settings-key-input").value = "";
        return true;
      case "gamePreview":
        if ($("game-preview")) {
          $("game-preview").textContent = message.value;
          $("game-preview-button").disabled = message.busy;
          $("game-preview-button").textContent = message.busy
            ? "读取中…"
            : "读取预览";
        }
        return true;
      case "searchState":
        setSearching(message.busy);
        if (message.busy) {
          model.results = [];
          model.selected = -1;
          renderResults();
        }
        if (message.status && $("feature-status-locator"))
          $("feature-status-locator").textContent = message.status;
        return true;
      case "searchResults":
        model.results = message.rows;
        model.selected = message.rows.length ? 0 : -1;
        if ($("feature-status-locator"))
          $("feature-status-locator").textContent = message.summary;
        renderResults();
        return true;
      case "wikiState":
        model.wiki = message;
        updateWiki();
        return true;
      case "featureFocus":
        focus();
        return true;
      default:
        return false;
    }
  }
  function focus() {
    const field =
      model.page === "locator"
        ? $("locator-query")
        : model.page === "reference"
          ? $("reference-query")
          : model.page === "wiki"
            ? $("wiki-address")
            : null;
    if (field) {
      field.focus();
      field.select();
    } else if (model.page === "chat") $("input").focus();
  }
  window.addEventListener("terra-appearance", () => {
    updateThemeChoices();
    reportWikiBounds();
  });
  window.addEventListener("resize", reportWikiBounds);
  new MutationObserver(reportWikiBounds).observe(document.body, {
    attributes: true,
    attributeFilter: ["class"],
  });
  new MutationObserver(reportWikiBounds).observe($("menu"), {
    attributes: true,
    attributeFilter: ["hidden"],
  });
  $("confirm").addEventListener("close", reportWikiBounds);
  window.terraFeatures = {
    receive,
    route,
    focus,
    reportWikiBounds,
    get page() {
      return model.page;
    },
  };
  for (const message of window.terraFeatureQueue || []) receive(message);
  window.terraFeatureQueue = [];
})();
