using System.Reflection;
using TerraWikiAI.Services;
using TerraWikiOverlay;

internal static class HarnessWindowChecks
{
    internal static void Run(Form form)
    {
        Type type = form.GetType();
        object Field(string name) => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        object? Call(string name, params object[] values) => type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, values);
        void Layout(Control control) { control.PerformLayout(); foreach (Control child in control.Controls) Layout(child); }
        _ = form.Handle;
        foreach (Size size in new[] { new Size(860, 600), new Size(1180, 820), new Size(1920, 1080) })
        {
            form.ClientSize = size; Layout(form);
            Control host = (Control)Field("_contentHost"), chat = (Control)Field("_conversation");
            if (host.Width < size.Width - 12 || host.Height < size.Height - 12 || chat.Size != host.ClientSize)
                throw new Exception("Web chat did not fill the resized native host.");
        }
        Rectangle bounds = form.Bounds;
        Call("ToggleMaximize");
        if (form.Bounds != Screen.FromControl(form).WorkingArea) throw new Exception("Maximize failed.");
        Call("ToggleMaximize");
        if (form.Bounds != bounds) throw new Exception("Restore failed.");
        OverlayAskService assistant = (OverlayAskService)Field("_assistant");
        ConversationStore history = (ConversationStore)assistant.GetType().GetField("_history", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(assistant)!;
        history.AddTurn("测试第一段对话", "回答", Array.Empty<string>(), true, 30);
        string first = assistant.ActiveSessionId;
        type.GetField("_draft", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, "第一段草稿");
        Call("CreateNewSession");
        string second = assistant.ActiveSessionId;
        type.GetField("_draft", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, "第二段草稿");
        Call("SwitchSession", first);
        if ((string)Field("_draft") != "第一段草稿") throw new Exception("Switching sessions lost its draft.");
        Call("CreateNewSession");Call("CreateNewSession");
        if (assistant.ActiveSessionId != second || (string)Field("_draft") != "第二段草稿") throw new Exception("Reusing an empty session lost its draft.");
        HarnessChatView chatView = (HarnessChatView)Field("_conversation");
        chatView.Post(new { type = "history", turns = Enumerable.Range(0, 30).Select(index => new ConversationTurn
        { Question = "长对话 " + index, Answer = string.Join("\n\n", Enumerable.Repeat(new string('测', 100), 10)) }).ToArray() });
        Console.WriteLine("OK native-host responsive=fill maximize=restore drafts=per-session empty-session=reused queued-transcript=30");
    }
}
