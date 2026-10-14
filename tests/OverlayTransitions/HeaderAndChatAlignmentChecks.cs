using System.Reflection;
using TerraWikiOverlay;

internal static class HeaderAndChatAlignmentChecks
{
    internal static void Run(Form form)
    {
        Control Field(string name) => (Control)form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (Control descendant in Descendants(child)) yield return descendant;
            }
        }
        void Layout(Control parent)
        {
            parent.PerformLayout();
            foreach (Control child in parent.Controls) Layout(child);
        }
        Button[] buttons = new[] { "历史", "定位", "在线图鉴", "设置" }
            .Select(text => Descendants(Field("_expandedPanel")).OfType<Button>().Single(button => button.Text == text)).ToArray();
        Font[] originalFonts = buttons.Select(button => button.Font).ToArray();
        foreach (float textScale in new[] { 1f, 1.25f, 1.5f, 2f })
        {
            List<Font> fonts = new();
            try
            {
                for (int index = 0; index < buttons.Length; index++)
                {
                    Font font = new(originalFonts[index].FontFamily, originalFonts[index].Size * textScale);
                    fonts.Add(font);
                    buttons[index].Font = font;
                }
                foreach (Size size in new[] { new Size(860, 600), new Size(1180, 820), new Size(1920, 1080) })
                {
                    form.Size = size;
                    Layout(form);
                    foreach (Button button in buttons)
                    {
                        Size text = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty,
                            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                        if (text.Width > button.ClientSize.Width - button.Padding.Horizontal
                            || text.Height > button.ClientSize.Height - button.Padding.Vertical)
                            throw new InvalidOperationException($"顶部按钮文字被截断：{button.Text}, scale={textScale}, text={text}, bounds={button.Bounds}。");
                        if (!button.Parent!.ClientRectangle.Contains(button.Bounds))
                            throw new InvalidOperationException("顶部按钮超出了工具栏。");
                    }
                    Control toolbar = buttons[0].Parent!;
                    if (!toolbar.Parent!.ClientRectangle.Contains(toolbar.Bounds))
                        throw new InvalidOperationException($"工具栏超出了标题区：scale={textScale}, window={form.ClientSize}, minimum={form.MinimumSize}, toolbar={toolbar.Bounds}, header={toolbar.Parent.ClientRectangle}。");
                    if (buttons.Select(button => button.Top).Distinct().Count() != 1
                        || buttons.Select(button => button.Height).Distinct().Count() != 1
                        || buttons.Select(button => button.Width).Distinct().Count() != 1)
                        throw new InvalidOperationException("顶部四个按钮没有统一行高和等宽对齐。");
                }
            }
            finally
            {
                for (int index = 0; index < buttons.Length; index++) buttons[index].Font = originalFonts[index];
                foreach (Font font in fonts) font.Dispose();
            }
        }
        MethodInfo create = form.GetType().GetMethod("CreateNewSession", BindingFlags.Instance | BindingFlags.NonPublic)!;
        create.Invoke(form, null);
        Control conversation = Field("_conversation");
        ScrollableControl stack = (ScrollableControl)conversation.GetType().GetField("_stack", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(conversation)!;
        int LocalLeft(Control control)
        {
            int left = 0;
            for (Control? ancestor = control; ancestor != form && ancestor is not null; ancestor = ancestor.Parent) left += ancestor.Left;
            return left;
        }
        foreach (Size size in new[] { new Size(860, 600), new Size(1180, 820), new Size(1920, 1080), new Size(1180, 820) })
        {
            form.Size = size;
            Layout(form);
            Application.DoEvents();
            Control card = stack.Controls.OfType<ConversationMessageCard>().First();
            if (Math.Abs(LocalLeft(card) - LocalLeft(Field("_input"))) > 3
                || card.Width < stack.ClientSize.Width - 45 || stack.HorizontalScroll.Visible)
                throw new InvalidOperationException($"新对话卡片没有跟随内容区对齐：size={size}, card={card.Bounds}, viewport={stack.ClientSize}。");
        }
        Button history = buttons[0];
        MethodInfo click = typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        for (int toggle = 0; toggle < 2; toggle++)
        {
            click.Invoke(history, new object[] { EventArgs.Empty });
            Layout(form);
            Application.DoEvents();
            Control card = stack.Controls.OfType<ConversationMessageCard>().First();
            if (Math.Abs(LocalLeft(card) - LocalLeft(Field("_input"))) > 3 || card.Width < stack.ClientSize.Width - 45)
                throw new InvalidOperationException("隐藏或恢复历史侧栏后，卡片没有跟随输入区对齐。");
        }
        Control selectedCard = stack.Controls.OfType<ConversationMessageCard>().First();
        RichTextBox textBox = (RichTextBox)selectedCard.GetType().GetField("_content", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(selectedCard)!;
        textBox.Select(4, 8);
        string selected = textBox.SelectedText;
        form.ClientSize = new Size(1920, 1080);
        Layout(form);
        form.ClientSize = new Size(1180, 820);
        Layout(form);
        if (textBox.SelectedText != selected || textBox.SelectionLength != 8)
            throw new InvalidOperationException("自适应布局改变了文字选区。");
        Console.WriteLine("OK alignment header=equal-width+single-line textScale=100/125/150/200 chat=left-aligned+full-width maximize=responsive");
    }
}
