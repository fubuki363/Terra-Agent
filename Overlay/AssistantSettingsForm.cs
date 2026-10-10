namespace TerraWikiOverlay;

internal sealed class AssistantSettingsForm : Form
{
    private static readonly Color Background = Color.FromArgb(22, 26, 36);
    private static readonly Color Surface = Color.FromArgb(32, 38, 52);
    private static readonly Color Accent = Color.FromArgb(61, 203, 255);
    private static readonly Color Muted = Color.FromArgb(157, 170, 190);
    private readonly OverlayAskService _assistant;
    private readonly CheckBox _persistHistory = new();
    private readonly CheckBox _includeMemoryContext = new();
    private readonly CheckBox _includeHealth = new();
    private readonly CheckBox _includeMana = new();
    private readonly CheckBox _includeInventory = new();
    private readonly CheckBox _includeEquipment = new();
    private readonly CheckBox _includeWorldInfo = new();
    private readonly CheckBox _includeProgress = new();
    private readonly CheckBox _includeAchievements = new();
    private readonly CheckBox _enableStorageLocator = new();
    private readonly CheckBox _enableBiomeLocator = new();
    private readonly CheckBox _enableObjectLocator = new();
    private readonly NumericUpDown _contextTurns = new();
    private readonly NumericUpDown _contextBudget = new();
    private readonly NumericUpDown _retentionDays = new();
    private readonly Label _keyStatus = new();
    private readonly TextBox _keyInput = new();
    private readonly Label _usageStatus = new();
    private readonly TextBox _gameDataPreview = new();
    private readonly TabControl _tabs = new();

    public AssistantSettingsForm(OverlayAskService assistant, int initialTab = 0)
    {
        _assistant = assistant;
        Text = "Terra Agent 设置";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(590, 520);
        MinimumSize = new Size(560, 500);
        BackColor = Background;
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(16, 8);
        _tabs.TabPages.Add(BuildContextTab());
        _tabs.TabPages.Add(BuildKeyTab());
        _tabs.TabPages.Add(BuildUsageTab());
        _tabs.TabPages.Add(BuildGameDataTab());
        _tabs.SelectedIndex = Math.Clamp(initialTab, 0, _tabs.TabCount - 1);
        Controls.Add(_tabs);

        Panel footer = new() { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(12), BackColor = Surface };
        Button save = MakeButton("保存设置", 112);
        save.Dock = DockStyle.Right;
        save.Click += (_, _) => SaveAndClose();
        footer.Controls.Add(save);
        Controls.Add(footer);

        LoadValues();
        RefreshKeyStatus();
        RefreshUsage();
    }

    public void SelectTab(int tab)
    {
        _tabs.SelectedIndex = Math.Clamp(tab, 0, _tabs.TabCount - 1);
        if (tab == 1)
            RefreshKeyStatus();
        else if (tab == 2)
            RefreshUsage();
    }

    private TabPage BuildContextTab()
    {
        TabPage page = MakePage("上下文与历史");
        TableLayoutPanel grid = MakeGrid();
        page.Controls.Add(grid);

        _persistHistory.Text = "关闭程序后仍保留对话历史";
        _persistHistory.AutoSize = true;
        _persistHistory.ForeColor = Color.White;
        AddFullRow(grid, _persistHistory);
        AddNumericRow(grid, "携带的最近对话轮数", "0 表示每次都是独立问题。", _contextTurns, 0, 20, 1);
        AddNumericRow(grid, "上下文字符预算", "较大更连贯，也会增加 Token 消耗。", _contextBudget, 1000, 50000, 1000);
        AddNumericRow(grid, "历史保留天数", "过期的非当前会话会自动清理。", _retentionDays, 1, 365, 1);
        Label agentPolicy = MakeLabel(
            "Agent 固定启用深度思考，不设置单次输出或本地每日用量上限；请求和 Token 仅在“用量”页统计。模型思考、工具参数和结果会保存在会话轨迹中。",
            9f,
            FontStyle.Regular,
            Color.FromArgb(126, 218, 190));
        agentPolicy.AutoSize = true;
        agentPolicy.MaximumSize = new Size(520, 0);
        AddFullRow(grid, agentPolicy);
        return page;
    }

    private TabPage BuildKeyTab()
    {
        TabPage page = MakePage("API Key");
        Panel content = new() { Dock = DockStyle.Fill, Padding = new Padding(22), BackColor = Background };
        page.Controls.Add(content);

        Label title = MakeLabel("DeepSeek API Key", 13f, FontStyle.Bold, Color.White);
        title.SetBounds(0, 8, 520, 32);
        content.Controls.Add(title);

        _keyStatus.ForeColor = Muted;
        _keyStatus.SetBounds(0, 48, 520, 58);
        _keyStatus.AutoEllipsis = true;
        content.Controls.Add(_keyStatus);

        _keyInput.UseSystemPasswordChar = true;
        _keyInput.PlaceholderText = "粘贴以 sk- 开头的 Key；已有 Key 不会在此回显";
        _keyInput.BackColor = Color.FromArgb(19, 23, 32);
        _keyInput.ForeColor = Color.White;
        _keyInput.BorderStyle = BorderStyle.FixedSingle;
        _keyInput.SetBounds(0, 118, 520, 34);
        content.Controls.Add(_keyInput);

        Button paste = MakeButton("从剪贴板粘贴", 132);
        paste.SetBounds(0, 170, 132, 36);
        paste.Click += (_, _) => PasteKey();
        content.Controls.Add(paste);

        Button save = MakeButton("保存到本机", 116, Accent, Color.FromArgb(5, 35, 48));
        save.SetBounds(142, 170, 116, 36);
        save.Click += (_, _) => SaveKey();
        content.Controls.Add(save);

        Button remove = MakeButton("删除本地 Key", 124);
        remove.SetBounds(268, 170, 124, 36);
        remove.Click += (_, _) => RemoveKey();
        content.Controls.Add(remove);

        Label note = MakeLabel(
            "Key 只保存在 %LOCALAPPDATA%\\TerraWikiOverlay\\key，不写入聊天历史，也不会打包进 EXE。环境变量中的 Key 优先级更高，删除按钮不会修改环境变量。",
            9f,
            FontStyle.Regular,
            Muted);
        note.SetBounds(0, 232, 520, 88);
        content.Controls.Add(note);
        return page;
    }

    private TabPage BuildUsageTab()
    {
        TabPage page = MakePage("用量");
        Panel content = new() { Dock = DockStyle.Fill, Padding = new Padding(22), BackColor = Background };
        page.Controls.Add(content);
        Label title = MakeLabel("本地用量统计", 13f, FontStyle.Bold, Color.White);
        title.SetBounds(0, 8, 520, 32);
        content.Controls.Add(title);
        _usageStatus.ForeColor = Color.FromArgb(210, 223, 240);
        _usageStatus.SetBounds(0, 54, 520, 120);
        content.Controls.Add(_usageStatus);
        Button refresh = MakeButton("刷新", 90);
        refresh.SetBounds(0, 190, 90, 36);
        refresh.Click += (_, _) => RefreshUsage();
        content.Controls.Add(refresh);
        Button reset = MakeButton("重置统计", 110);
        reset.SetBounds(100, 190, 110, 36);
        reset.Click += (_, _) => ResetUsage();
        content.Controls.Add(reset);
        Label note = MakeLabel("这里依据 DeepSeek 响应中的 usage 字段统计，不代表账户账单；实际计费以 DeepSeek 控制台为准。", 9f, FontStyle.Regular, Muted);
        note.SetBounds(0, 250, 520, 54);
        content.Controls.Add(note);
        return page;
    }

    private TabPage BuildGameDataTab()
    {
        TabPage page = MakePage("游戏数据");
        Panel content = new() { Dock = DockStyle.Fill, Padding = new Padding(22), BackColor = Background };
        page.Controls.Add(content);
        Label title = MakeLabel("Agent 游戏状态技能权限", 13f, FontStyle.Bold, Color.White);
        title.SetBounds(0, 8, 520, 32);
        content.Controls.Add(title);

        ConfigureGameDataCheckBox(_includeHealth, "血量", 0, 46);
        ConfigureGameDataCheckBox(_includeMana, "魔力", 125, 46);
        ConfigureGameDataCheckBox(_includeInventory, "背包内容", 250, 46);
        ConfigureGameDataCheckBox(_includeEquipment, "装备 / 饰品", 375, 46);
        ConfigureGameDataCheckBox(_includeWorldInfo, "世界信息", 0, 76);
        ConfigureGameDataCheckBox(_includeProgress, "进度信息", 170, 76);
        ConfigureGameDataCheckBox(_includeAchievements, "成就信息", 340, 76);
        content.Controls.AddRange(new Control[]
        {
            _includeHealth, _includeMana, _includeInventory, _includeEquipment,
            _includeWorldInfo, _includeProgress, _includeAchievements
        });

        _includeMemoryContext.Text = "启用 read_game_state 技能（默认开启、只读、由 Agent 按需调用）";
        _includeMemoryContext.AutoSize = true;
        _includeMemoryContext.ForeColor = Color.FromArgb(255, 205, 128);
        _includeMemoryContext.SetBounds(0, 106, 520, 28);
        content.Controls.Add(_includeMemoryContext);

        _enableStorageLocator.Text = "允许 Agent 搜索世界箱子和个人储物，并启动物品导航";
        _enableStorageLocator.AutoSize = true;
        _enableStorageLocator.ForeColor = Color.FromArgb(126, 218, 190);
        _enableStorageLocator.SetBounds(0, 134, 520, 26);
        content.Controls.Add(_enableStorageLocator);

        _enableBiomeLocator.Text = "允许 Agent 扫描并导航到可检测的群系、迷你群系和剑冢";
        _enableBiomeLocator.AutoSize = true;
        _enableBiomeLocator.ForeColor = Color.FromArgb(126, 218, 190);
        _enableBiomeLocator.SetBounds(0, 160, 520, 26);
        content.Controls.Add(_enableBiomeLocator);

        _enableObjectLocator.Text = "允许 Agent 定位世界物体（矿物、箱子、家具等）";
        _enableObjectLocator.AutoSize = true;
        _enableObjectLocator.ForeColor = Color.FromArgb(126, 218, 190);
        _enableObjectLocator.SetBounds(0, 186, 520, 26);
        content.Controls.Add(_enableObjectLocator);
        Label note = MakeLabel("勾选表示授权，Agent 按问题选择技能。物体命中后可跟随悬浮球箭头；预览只检查状态技能。", 8.7f, FontStyle.Regular, Muted);
        note.SetBounds(0, 216, 520, 40);
        content.Controls.Add(note);
        _gameDataPreview.Multiline = true;
        _gameDataPreview.ReadOnly = true;
        _gameDataPreview.ScrollBars = ScrollBars.Vertical;
        _gameDataPreview.BackColor = Color.FromArgb(19, 23, 32);
        _gameDataPreview.ForeColor = Color.FromArgb(210, 223, 240);
        _gameDataPreview.BorderStyle = BorderStyle.FixedSingle;
        _gameDataPreview.SetBounds(0, 258, 520, 119);
        content.Controls.Add(_gameDataPreview);
        Button refresh = MakeButton("刷新预览", 112, Accent, Color.FromArgb(5, 35, 48));
        refresh.SetBounds(0, 390, 112, 36);
        refresh.Click += async (_, _) => await RefreshGameDataAsync(refresh);
        content.Controls.Add(refresh);
        return page;
    }

    private async Task RefreshGameDataAsync(Button button)
    {
        button.Enabled = false;
        button.Text = "读取中…";
        _gameDataPreview.Text = _includeMemoryContext.Checked
            ? "正在只读解析 Terraria 实时内存；失败时不会读取存档……"
            : "实时内存读取已关闭；不会读取存档。";
        try
        {
            GameContextSelection selection = GetGameContextSelection();
            bool enabled = _includeMemoryContext.Checked;
            string preview = await Task.Run(() => _assistant.GetGameContextPreview(selection, enabled));
            if (!IsDisposed && !Disposing) _gameDataPreview.Text = preview;
        }
        catch (Exception exception)
        {
            if (!IsDisposed && !Disposing) _gameDataPreview.Text = "读取失败：" + exception.Message;
        }
        finally
        {
            if (!button.IsDisposed) { button.Text = "刷新预览"; button.Enabled = true; }
        }
    }

    private void LoadValues()
    {
        OverlaySettings settings = _assistant.Settings;
        _persistHistory.Checked = settings.PersistHistory;
        _includeMemoryContext.Checked = settings.IncludeMemoryContext;
        _includeHealth.Checked = settings.IncludeHealth;
        _includeMana.Checked = settings.IncludeMana;
        _includeInventory.Checked = settings.IncludeInventory;
        _includeEquipment.Checked = settings.IncludeEquipment;
        _includeWorldInfo.Checked = settings.IncludeWorldInfo;
        _includeProgress.Checked = settings.IncludeProgress;
        _includeAchievements.Checked = settings.IncludeAchievements;
        _enableStorageLocator.Checked = settings.EnableStorageLocator;
        _enableBiomeLocator.Checked = settings.EnableBiomeLocator;
        _enableObjectLocator.Checked = settings.EnableObjectLocator;
        _contextTurns.Value = settings.ContextTurns;
        _contextBudget.Value = settings.ContextCharacterBudget;
        _retentionDays.Value = settings.HistoryRetentionDays;
    }

    private void SaveAndClose()
    {
        OverlaySettings settings = _assistant.Settings;
        settings.PersistHistory = _persistHistory.Checked;
        settings.IncludeSaveContext = GetGameContextSelection().HasAny;
        settings.IncludeMemoryContext = _includeMemoryContext.Checked;
        settings.IncludeHealth = _includeHealth.Checked;
        settings.IncludeMana = _includeMana.Checked;
        settings.IncludeInventory = _includeInventory.Checked;
        settings.IncludeEquipment = _includeEquipment.Checked;
        settings.IncludeWorldInfo = _includeWorldInfo.Checked;
        settings.IncludeProgress = _includeProgress.Checked;
        settings.IncludeAchievements = _includeAchievements.Checked;
        settings.EnableStorageLocator = _enableStorageLocator.Checked;
        settings.EnableBiomeLocator = _enableBiomeLocator.Checked;
        settings.EnableObjectLocator = _enableObjectLocator.Checked;
        settings.ContextTurns = (int)_contextTurns.Value;
        settings.ContextCharacterBudget = (int)_contextBudget.Value;
        settings.HistoryRetentionDays = (int)_retentionDays.Value;
        _assistant.SaveSettings();
        DialogResult = DialogResult.OK;
        Close();
    }

    private GameContextSelection GetGameContextSelection() => new(
        _includeHealth.Checked,
        _includeMana.Checked,
        _includeInventory.Checked,
        _includeEquipment.Checked,
        _includeWorldInfo.Checked,
        _includeProgress.Checked,
        _includeAchievements.Checked);

    private static void ConfigureGameDataCheckBox(CheckBox checkBox, string text, int x, int y)
    {
        checkBox.Text = text;
        checkBox.AutoSize = true;
        checkBox.ForeColor = Color.White;
        checkBox.SetBounds(x, y, 160, 26);
    }

    private void PasteKey()
    {
        try
        {
            _keyInput.Text = Clipboard.GetText().Trim();
            _keyInput.SelectAll();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, "无法读取剪贴板：" + exception.Message, "API Key", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SaveKey()
    {
        if (_assistant.SaveApiKey(_keyInput.Text, out string message))
        {
            _keyInput.Clear();
            RefreshKeyStatus();
            MessageBox.Show(this, message, "API Key", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(this, message, "API Key", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RemoveKey()
    {
        if (MessageBox.Show(this, "确定删除悬浮助手本地保存的 API Key？", "API Key", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        _assistant.RemoveLocalKey(out string message);
        RefreshKeyStatus();
        MessageBox.Show(this, message, "API Key", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ResetUsage()
    {
        if (MessageBox.Show(this, "确定重置本机记录的用量统计？", "用量", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _assistant.ResetUsage();
        RefreshUsage();
    }

    private void RefreshKeyStatus() => _keyStatus.Text = _assistant.KeyStatus;

    private void RefreshUsage() => _usageStatus.Text = _assistant.UsageSummary;

    private static TabPage MakePage(string title) => new(title) { BackColor = Background, ForeColor = Color.White };

    private static TableLayoutPanel MakeGrid()
    {
        TableLayoutPanel grid = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            AutoScroll = true,
            BackColor = Background
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32f));
        return grid;
    }

    private static void AddNumericRow(TableLayoutPanel grid, string title, string hint, NumericUpDown control, int minimum, int maximum, int increment)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        Label label = MakeLabel(title + Environment.NewLine + hint, 9f, FontStyle.Regular, Color.White);
        label.Dock = DockStyle.Fill;
        grid.Controls.Add(label, 0, row);
        control.Minimum = minimum;
        control.Maximum = maximum;
        control.Increment = increment;
        control.ThousandsSeparator = true;
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(8, 8, 0, 14);
        grid.Controls.Add(control, 1, row);
    }

    private static void AddFullRow(TableLayoutPanel grid, Control control)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 43f));
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 8, 0, 6);
        grid.Controls.Add(control, 0, row);
        grid.SetColumnSpan(control, 2);
    }

    private static Label MakeLabel(string text, float size, FontStyle style, Color color) => new()
    {
        Text = text,
        Font = new Font("Microsoft YaHei UI", size, style),
        ForeColor = color,
        AutoSize = false
    };

    private static Button MakeButton(string text, int width, Color? backColor = null, Color? foreColor = null)
    {
        Button button = new()
        {
            Text = text,
            Width = width,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor ?? Surface,
            ForeColor = foreColor ?? Color.White,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(65, 76, 96);
        return button;
    }
}
