using System.Drawing.Drawing2D;

namespace EasyClipboard;

// Icon tab. When active it is wrapped in the color of the page below: rounded on top,
// straight sides, then flaring out into the page edge from about 70% down.
internal sealed class WrapTab : Control
{
    private bool _active;
    private bool _hover;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Bitmap? Icon { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color ActiveColor { get; set; } = Color.Black;

    // First tab on the panel's left edge: straight left side, no flare.
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool FlushLeft { get => _flushLeft; set { _flushLeft = value; Width = BaseWidth + (int)_extra; } } // no left flare, so one foot narrower
    private bool _flushLeft;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => _active;
        set { _active = value; AccessibleDescription = value ? "Selected" : null; Invalidate(); Grow(); }
    }

    // Test: the active tab shows its name beside the icon, widening smoothly and pushing the others aside.
    private readonly System.Windows.Forms.Timer _grow = new() { Interval = 15 };
    private double _extra;
    private long _lastFrame;
    private int BaseWidth => LogicalToDeviceUnits(FlushLeft ? 37 : 42);
    private int IconBox => LogicalToDeviceUnits(32);
    private int TargetExtra => _active && !string.IsNullOrEmpty(Text) ? TextRenderer.MeasureText(Text, LabelFont).Width + LogicalToDeviceUnits(4) : 0;

    private void Grow()
    {
        if (!IsHandleCreated || !Visible || !SystemInformation.UIEffectsEnabled)
        {
            _extra = TargetExtra;
            Width = BaseWidth + (int)_extra;
            return;
        }
        _lastFrame = System.Diagnostics.Stopwatch.GetTimestamp();
        _grow.Start();
    }

    private void GrowStep()
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var seconds = System.Diagnostics.Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds;
        _lastFrame = now;
        var difference = TargetExtra - _extra;
        if (Math.Abs(difference) < 1) { _extra = TargetExtra; _grow.Stop(); }
        else _extra += difference * (1 - Math.Exp(-seconds / 0.06));
        Width = BaseWidth + (int)Math.Round(_extra);
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); _extra = TargetExtra; Width = BaseWidth + (int)_extra; }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); Grow(); }

    public WrapTab()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Size = new Size(42, 36); // icon body 32 wide plus a small flare each side
        Margin = Padding.Empty;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PageTab;
        _grow.Tick += (_, _) => GrowStep();
    }

    private int Foot => LogicalToDeviceUnits(5);
    private int BodyLeft => FlushLeft ? 0 : Foot;
    private int BodyRight => Width - Foot;
    public int IconTop => (Height - (Icon?.Height ?? 0)) / 2 + LogicalToDeviceUnits(1);
    // Square highlight centered on the icon (hover and keyboard focus).
    private Rectangle HoverBox
    {
        get
        {
            var side = (Icon?.Height ?? 0) + LogicalToDeviceUnits(10); // fits inside the narrower body
            return new Rectangle(IconCenter - side / 2, IconTop + (Icon?.Height ?? 0) / 2 - side / 2, side, side);
        }
    }

    private Font? _labelFont;
    private Font LabelFont => _labelFont ??= new Font(Font, FontStyle.Bold);
    protected override void OnFontChanged(EventArgs e) { _labelFont?.Dispose(); _labelFont = null; base.OnFontChanged(e); }
    private int IconCenter => BodyLeft + IconBox / 2;

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(Parent?.BackColor ?? BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float left = BodyLeft, right = BodyRight, top = LogicalToDeviceUnits(2), bottom = Height, flare = Height * .55f;
        var radius = LogicalToDeviceUnits(10);
        if (_active)
        {
            using var shape = new GraphicsPath();
            shape.AddArc(left, top, radius * 2, radius * 2, 180, 90);
            shape.AddArc(right - radius * 2, top, radius * 2, radius * 2, 270, 90);
            shape.AddLine(right, top + radius, right, flare);
            shape.AddBezier(right, flare, right, bottom, right, bottom, right + Foot, bottom);
            shape.AddLine(right + Foot, bottom, 0, bottom);
            if (FlushLeft) shape.AddLine(0, bottom, 0, top + radius);
            else shape.AddBezier(0, bottom, left, bottom, left, bottom, left, flare);
            shape.CloseFigure();
            using var fill = new SolidBrush(ActiveColor);
            graphics.FillPath(fill, shape);
        }
        else if (_hover && !SystemInformation.HighContrast)
        {
            using var hover = new GraphicsPath();
            hover.AddRoundedRectangle(HoverBox, new Size(radius * 2, radius * 2));
            using var fill = new SolidBrush(MainWindow.SelectionColor);
            graphics.FillPath(fill, hover);
        }
        if (Icon is { } icon)
            graphics.DrawImageUnscaled(icon, IconCenter - icon.Width / 2, IconTop);
        if (_extra >= 1 && Icon is { } shown)
        {
            // Name sits right of the icon; the widening body reveals it.
            var textLeft = IconCenter + shown.Width / 2 + LogicalToDeviceUnits(4);
            var clip = Rectangle.FromLTRB(textLeft, 0, BodyRight - LogicalToDeviceUnits(4), Height);
            if (clip.Width > 0)
            {
                graphics.SetClip(clip);
                TextRenderer.DrawText(graphics, Text, LabelFont, new Rectangle(textLeft, LogicalToDeviceUnits(2), TextRenderer.MeasureText(Text, LabelFont).Width + 2, Height - LogicalToDeviceUnits(2)),
                    ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                graphics.ResetClip();
            }
        }
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(HoverBox, -3, -3));
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { Icon?.Dispose(); _grow.Dispose(); _labelFont?.Dispose(); }
        base.Dispose(disposing);
    }
}
