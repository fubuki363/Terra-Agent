namespace TerraWikiOverlay;

internal sealed class OverlayMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly bool _dark;
    internal OverlayMenuRenderer(bool dark) : base(new Palette(dark)) { _dark = dark; RoundedEdges = true; }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = _dark ? Color.FromArgb(235, 238, 242) : Color.FromArgb(15, 17, 21);
        base.OnRenderItemText(e);
    }
    private sealed class Palette : ProfessionalColorTable
    {
        private readonly bool _dark;
        internal Palette(bool dark) { _dark = dark; UseSystemColors = false; }
        private Color Base => _dark ? Color.FromArgb(33, 33, 35) : Color.White;
        private Color Hover => _dark ? Color.FromArgb(53, 54, 56) : Color.FromArgb(235, 238, 242);
        public override Color ToolStripDropDownBackground => Base;
        public override Color ImageMarginGradientBegin => Base;
        public override Color ImageMarginGradientMiddle => Base;
        public override Color ImageMarginGradientEnd => Base;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemBorder => Hover;
        public override Color MenuBorder => _dark ? Color.FromArgb(67, 69, 74) : Color.FromArgb(225, 229, 238);
        public override Color SeparatorDark => MenuBorder;
        public override Color SeparatorLight => Base;
    }
}
