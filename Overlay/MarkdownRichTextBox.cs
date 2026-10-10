using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace TerraWikiOverlay;

internal sealed partial class MarkdownRichTextBox : RichTextBox
{
    private const int WmMouseWheel = 0x020A;
    private static readonly Color BodyColor = Color.FromArgb(226, 232, 242);
    private static readonly Color CodeColor = Color.FromArgb(190, 238, 255);
    private static readonly Color CodeBackground = Color.FromArgb(19, 28, 42);
    private static readonly Color QuoteColor = Color.FromArgb(162, 182, 207);
    private static readonly Color LinkColor = Color.FromArgb(93, 202, 255);
    private readonly Font _bodyFont;
    private readonly Font _boldFont;
    private readonly Font _italicFont;
    private readonly Font _codeFont;
    private readonly Font _headingFont;
    private readonly Font _subheadingFont;
    private readonly Font _timestampFont;
    private readonly Font _linkFont;
    private bool _selectingCharacters;
    private int _selectionAnchor;
    private int _pressedIndex;
    private bool _selectionMoved;

    public event Action<int>? WheelRequested;

    public MarkdownRichTextBox()
    {
        _bodyFont = new Font("Microsoft YaHei UI", 10.25f);
        _boldFont = new Font(_bodyFont, FontStyle.Bold);
        _italicFont = new Font(_bodyFont, FontStyle.Italic);
        _codeFont = new Font("Cascadia Mono", 9.5f);
        _headingFont = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
        _subheadingFont = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold);
        _timestampFont = new Font(_bodyFont.FontFamily, 8f);
        _linkFont = new Font(_bodyFont, FontStyle.Bold | FontStyle.Underline);
        Font = _bodyFont;
        DetectUrls = true;
        ReadOnly = true;
        BorderStyle = BorderStyle.None;
        AutoWordSelection = false;
        HideSelection = false;
        ShortcutsEnabled = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // ECO_AUTOWORDSELECTION can remain set in native RichEdit despite the
        // managed property. Clear it explicitly; single drags select characters.
        SendMessage(Handle, 0x044D, (IntPtr)3, (IntPtr)(-2));
    }

    public bool CopySelection() => SelectionLength > 0 && OverlayClipboard.TryCopy(SelectedText);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Control && e.KeyCode is Keys.C or Keys.Insert)
        {
            CopySelection();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0201) // WM_LBUTTONDOWN
        {
            Point point = MousePoint(message.LParam);
            int index = GetCharIndexFromPosition(point);
            Focus();
            _pressedIndex = index;
            _selectionAnchor = (ModifierKeys & Keys.Shift) != 0 ? SelectionStart : index;
            _selectionMoved = false;
            _selectingCharacters = true;
            Capture = true;
            Select(Math.Min(_selectionAnchor, index), Math.Abs(index - _selectionAnchor));
            message.Result = IntPtr.Zero;
            return;
        }
        if (message.Msg == 0x0200 && _selectingCharacters)
        {
            int index = GetCharIndexFromPosition(MousePoint(message.LParam));
            _selectionMoved |= index != _pressedIndex;
            Select(Math.Min(_selectionAnchor, index), Math.Abs(index - _selectionAnchor));
            message.Result = IntPtr.Zero;
            return;
        }
        if (message.Msg == 0x0202 && _selectingCharacters)
        {
            _selectingCharacters = false;
            Capture = false;
            if (!_selectionMoved && SelectionLength == 0)
            {
                foreach (Match url in Regex.Matches(Text, @"https?://[^\s)]+"))
                {
                    if (_pressedIndex >= url.Index && _pressedIndex < url.Index + url.Length)
                        OnLinkClicked(new LinkClickedEventArgs(url.Value));
                }
            }
            message.Result = IntPtr.Zero;
            return;
        }
        if (message.Msg == 0x0203) // Avoid native Chinese paragraph-sized double-click selection.
        {
            _selectingCharacters = false;
            Capture = false;
            int index = GetCharIndexFromPosition(MousePoint(message.LParam));
            string text = Text;
            if (index < text.Length)
            {
                int start = index, end = index + 1;
                bool LatinWord(char value) => value < 128 && (char.IsLetterOrDigit(value) || value is '_' or '-');
                if (LatinWord(text[index]))
                {
                    while (start > 0 && LatinWord(text[start - 1])) start--;
                    while (end < text.Length && LatinWord(text[end])) end++;
                }
                else if (char.IsHighSurrogate(text[index]) && end < text.Length && char.IsLowSurrogate(text[end])) end++;
                Select(start, end - start);
            }
            message.Result = IntPtr.Zero;
            return;
        }
        if (message.Msg == WmMouseWheel && WheelRequested is not null)
        {
            int delta = (short)((message.WParam.ToInt64() >> 16) & 0xFFFF);
            WheelRequested(delta);
            message.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref message);
    }

    private static Point MousePoint(IntPtr value) => new((short)(value.ToInt64() & 0xFFFF),
        (short)((value.ToInt64() >> 16) & 0xFFFF));

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

    public void AppendMessage(string sender, string markdown, Color senderColor, DateTime? timestamp = null)
    {
        SelectionStart = TextLength;
        SelectionIndent = 0;
        SelectionHangingIndent = 0;
        SelectionColor = senderColor;
        SelectionBackColor = BackColor;
        SelectionFont = _boldFont;
        AppendText(sender);

        SelectionColor = Color.FromArgb(116, 130, 151);
        SelectionFont = _timestampFont;
        AppendText("  " + (timestamp ?? DateTime.Now).ToLocalTime().ToString("HH:mm"));
        AppendText(Environment.NewLine);

        RenderMarkdown(markdown.Trim());
        AppendText(Environment.NewLine + Environment.NewLine);
        SelectionStart = TextLength;
        ScrollToCaret();
    }

    public void RenderMarkdown(string markdown)
    {
        string normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        bool inCodeFence = false;
        string[] lines = normalized.Split('\n');
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence)
            {
                AppendStyled(line.Length == 0 ? " " : line, _codeFont, CodeColor, CodeBackground);
                AppendText(Environment.NewLine);
                continue;
            }
            if (line.Contains('|') && lineIndex + 1 < lines.Length && TableSeparatorRegex().IsMatch(lines[lineIndex + 1]))
            {
                string[] headings = TableCells(line);
                lineIndex += 2;
                while (lineIndex < lines.Length && lines[lineIndex].Contains('|') && !string.IsNullOrWhiteSpace(lines[lineIndex]))
                {
                    string[] cells = TableCells(lines[lineIndex]);
                    for (int cell = 0; cell < cells.Length; cell++)
                    {
                        if (cell > 0) AppendStyled("   ·   ", _bodyFont, QuoteColor, BackColor);
                        if (cell < headings.Length) AppendStyled(headings[cell] + "：", _boldFont, BodyColor, BackColor);
                        AppendInline(cells[cell], _bodyFont, BodyColor);
                    }
                    AppendText(Environment.NewLine + Environment.NewLine);
                    lineIndex++;
                }
                lineIndex--;
                continue;
            }

            if (HorizontalRuleRegex().IsMatch(line))
            {
                AppendStyled("────────────────────────", _bodyFont, Color.FromArgb(69, 81, 103), BackColor);
                AppendText(Environment.NewLine);
                continue;
            }

            Match heading = HeadingRegex().Match(line);
            if (heading.Success)
            {
                Font font = heading.Groups[1].Length == 1 ? _headingFont : _subheadingFont;
                AppendInline(heading.Groups[2].Value, font, Color.White);
                AppendText(Environment.NewLine);
                continue;
            }

            Match unordered = UnorderedListRegex().Match(line);
            Match ordered = OrderedListRegex().Match(line);
            if (unordered.Success || ordered.Success)
            {
                SelectionIndent = 18;
                SelectionHangingIndent = -14;
                string prefix = unordered.Success ? "• " : ordered.Groups[1].Value + ". ";
                string content = unordered.Success ? unordered.Groups[1].Value : ordered.Groups[2].Value;
                AppendStyled(prefix, _boldFont, Color.FromArgb(102, 214, 255), BackColor);
                AppendInline(content, _bodyFont, BodyColor);
                AppendText(Environment.NewLine);
                SelectionIndent = 0;
                SelectionHangingIndent = 0;
                continue;
            }

            Match quote = QuoteRegex().Match(line);
            if (quote.Success)
            {
                SelectionIndent = 16;
                AppendStyled("▌ ", _boldFont, Color.FromArgb(86, 175, 220), BackColor);
                AppendInline(quote.Groups[1].Value, _italicFont, QuoteColor);
                AppendText(Environment.NewLine);
                SelectionIndent = 0;
                continue;
            }

            AppendInline(line, _bodyFont, BodyColor);
            AppendText(Environment.NewLine);
        }
    }

    private static string[] TableCells(string line) => Regex.Split(line.Trim().Trim('|'), @"(?<!\\)\|")
        .Select(value => value.Trim().Replace("\\|", "|", StringComparison.Ordinal)).ToArray();

    private void AppendInline(string text, Font baseFont, Color baseColor)
    {
        int index = 0;
        foreach (Match match in InlineTokenRegex().Matches(text))
        {
            if (match.Index > index)
                AppendStyled(text[index..match.Index], baseFont, baseColor, BackColor);

            string token = match.Value;
            if (token.StartsWith("**", StringComparison.Ordinal) || token.StartsWith("__", StringComparison.Ordinal))
                AppendStyled(token[2..^2], _boldFont, baseColor, BackColor);
            else if (token.StartsWith('`'))
                AppendStyled(token[1..^1], _codeFont, CodeColor, CodeBackground);
            else if (token.StartsWith('['))
            {
                Match link = MarkdownLinkRegex().Match(token);
                AppendStyled(link.Groups[1].Value, _boldFont, LinkColor, BackColor, underline: true);
                AppendStyled(" (" + link.Groups[2].Value + ")", _bodyFont, LinkColor, BackColor);
            }
            else
                AppendStyled(token[1..^1], _italicFont, baseColor, BackColor);
            index = match.Index + match.Length;
        }

        if (index < text.Length)
            AppendStyled(text[index..], baseFont, baseColor, BackColor);
    }

    private void AppendStyled(string text, Font font, Color foreground, Color background, bool underline = false)
    {
        SelectionStart = TextLength;
        SelectionLength = 0;
        SelectionFont = underline ? _linkFont : font;
        SelectionColor = foreground;
        SelectionBackColor = background;
        AppendText(text);
        SelectionBackColor = BackColor;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bodyFont.Dispose();
            _boldFont.Dispose();
            _italicFont.Dispose();
            _codeFont.Dispose();
            _headingFont.Dispose();
            _subheadingFont.Dispose();
            _timestampFont.Dispose();
            _linkFont.Dispose();
        }
        base.Dispose(disposing);
    }

    [GeneratedRegex(@"^\s*(#{1,4})\s+(.+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\s*[-+*]\s+(.+)$")]
    private static partial Regex UnorderedListRegex();

    [GeneratedRegex(@"^\s*(\d+)[.)]\s+(.+)$")]
    private static partial Regex OrderedListRegex();

    [GeneratedRegex(@"^\s*>\s?(.*)$")]
    private static partial Regex QuoteRegex();

    [GeneratedRegex(@"^\s*((-{3,})|(\*{3,})|(_{3,}))\s*$")]
    private static partial Regex HorizontalRuleRegex();

    [GeneratedRegex(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$")]
    private static partial Regex TableSeparatorRegex();

    [GeneratedRegex(@"(\*\*.+?\*\*|__.+?__|`[^`]+`|\[[^\]]+\]\(https?://[^\s)]+\)|(?<!\*)\*[^*\r\n]+\*(?!\*)|(?<!_)_[^_\r\n]+_(?!_))")]
    private static partial Regex InlineTokenRegex();

    [GeneratedRegex(@"^\[([^\]]+)\]\((https?://[^\s)]+)\)$")]
    private static partial Regex MarkdownLinkRegex();
}
