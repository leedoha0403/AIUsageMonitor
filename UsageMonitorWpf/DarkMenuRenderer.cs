using System.Drawing;
using System.Windows.Forms;

namespace UsageMonitorWpf;

// Tray (WinForms) context menu colors that match the WPF theme.
public sealed class ThemedMenuRenderer(bool dark) : ToolStripProfessionalRenderer(new ThemedColors(dark))
{
    public Color Text { get; } = dark ? Color.FromArgb(244, 247, 245) : Color.FromArgb(21, 25, 29);

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Text;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        using var brush = new SolidBrush(Color.FromArgb(72, 194, 163));
        using var font = new Font("Segoe UI Symbol", 9f);
        e.Graphics.DrawString("✓", font, brush, e.ImageRectangle.X, e.ImageRectangle.Y - 1);
    }

    private sealed class ThemedColors(bool dark) : ProfessionalColorTable
    {
        private readonly Color _back = dark ? Color.FromArgb(23, 27, 29) : Color.White;
        private readonly Color _hover = dark ? Color.FromArgb(38, 46, 49) : Color.FromArgb(238, 242, 240);
        private readonly Color _line = dark ? Color.FromArgb(52, 62, 66) : Color.FromArgb(222, 226, 224);

        public override Color ToolStripDropDownBackground => _back;
        public override Color ImageMarginGradientBegin => _back;
        public override Color ImageMarginGradientMiddle => _back;
        public override Color ImageMarginGradientEnd => _back;
        public override Color MenuBorder => _line;
        public override Color MenuItemBorder => _hover;
        public override Color MenuItemSelected => _hover;
        public override Color MenuItemSelectedGradientBegin => _hover;
        public override Color MenuItemSelectedGradientEnd => _hover;
        public override Color SeparatorDark => _line;
        public override Color SeparatorLight => _back;
        public override Color CheckBackground => _back;
        public override Color CheckSelectedBackground => _hover;
        public override Color CheckPressedBackground => _hover;
    }
}
