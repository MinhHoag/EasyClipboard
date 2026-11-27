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
    public bool FlushLeft { get => _flushLeft; set { _flushLeft = value; Width = value ? 42 : 48; } } // no left flare, so one foot narrower
    private bool _flushLeft;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => _active;
        set { _active = value; AccessibleDescription = value ? "Selected" : null; Invalidate(); }
    }

    public WrapTab()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Size = new Size(48, 36); // icon body 36 wide plus a small flare each side
        Margin = Padding.Empty;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PageTab;
    }

    private int Foot => LogicalToDeviceUnits(6);
    private int BodyLeft => FlushLeft ? 0 : Foot;
    private int BodyRight => Width - Foot;
    public int IconTop => (Height - (Icon?.Height ?? 0)) / 2 + LogicalToDeviceUnits(1);
    // Square highlight centered on the icon (hover and keyboard focus).
    private Rectangle HoverBox
    {
        get
        {
            var side = (Icon?.Height ?? 0) + LogicalToDeviceUnits(14);
            return new Rectangle((BodyLeft + BodyRight - side) / 2, IconTop + (Icon?.Height ?? 0) / 2 - side / 2, side, side);
        }
    }

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
            graphics.DrawImageUnscaled(icon, (BodyLeft + BodyRight - icon.Width) / 2, IconTop);
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
        if (disposing) Icon?.Dispose();
        base.Dispose(disposing);
    }
}
