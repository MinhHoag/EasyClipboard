namespace EasyClipboard;

// One stop on the tour: what to highlight, what to say, and optional extra content (e.g. the Shortcut values).
internal sealed record TourStep(string Title, string Body, Func<Control[]> Targets, Func<Control>? Extra = null);

// Guided tour: dims the window except a cut-out around the current target, with a card beside it.
internal sealed class Tour : IDisposable
{
    private readonly Form _owner;
    private readonly IReadOnlyList<TourStep> _steps;
    private readonly Form _shade;
    private readonly Form _card;
    private readonly Label _title = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
    private readonly Label _body = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
    private readonly Label _progress = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Panel _extraHost = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
    private readonly Button _back, _next;
    private readonly CloseButton _close = new() { AccessibleName = "Close tour" };
    private Control _stack = null!;
    private int _index;
    private bool _closed;
    public event Action? Finished;

    public Tour(Form owner, IReadOnlyList<TourStep> steps, Func<string, Button> makeButton)
    {
        _owner = owner;
        _steps = steps;
        _shade = new Form
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            BackColor = Color.Black, Opacity = 0.6, Owner = owner
        };
        _card = new Form
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            BackColor = owner.BackColor, ForeColor = MainWindow.TextColor, Font = owner.Font, Owner = _shade,
            KeyPreview = true
        };
        _title.Font = new Font(owner.Font.FontFamily, owner.Font.Size * 1.15f, FontStyle.Bold);
        _body.MaximumSize = new Size(owner.LogicalToDeviceUnits(300), 0);
        _progress.ForeColor = Program.LightTheme ? Color.FromArgb(105, 105, 105) : Color.FromArgb(150, 150, 150);
        _back = makeButton("Back");
        _next = makeButton("Next");
        _back.Click += (_, _) => Show(_index - 1);
        _next.Click += (_, _) => { if (_index == _steps.Count - 1) Close(); else Show(_index + 1); };
        _close.Click += (_, _) => Close();
        var buttons = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
        foreach (var width in new[] { SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize })
            buttons.ColumnStyles.Add(new ColumnStyle(width, 100));
        buttons.Controls.Add(_progress, 0, 0);
        buttons.Controls.Add(_back, 2, 0);
        buttons.Controls.Add(_next, 3, 0);
        _next.Margin = _next.Margin with { Right = 0 };
        var stack = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        stack.Controls.AddRange([_title, _body, _extraHost, buttons]);
        stack.Layout += (_, _) => buttons.Width = Math.Max(Math.Max(buttons.PreferredSize.Width, _body.Width), _extraHost.Visible ? _extraHost.Width : 0);
        _stack = stack;
        // Message-box spacing; the card is sized by hand in Show() so the X can sit in its corner.
        stack.Location = new Point(owner.LogicalToDeviceUnits(24), owner.LogicalToDeviceUnits(20));
        foreach (var button in new[] { _back, _next })
        {
            button.MinimumSize = new Size(owner.LogicalToDeviceUnits(72), owner.LogicalToDeviceUnits(30));
            button.Margin = new Padding(owner.LogicalToDeviceUnits(6), 0, 0, 0);
        }
        _card.Controls.Add(stack);
        _card.Controls.Add(_close);
        _card.AcceptButton = _next;
        _card.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        // Windows 11 draws smooth, anti-aliased rounded corners for us (ignored on older Windows).
        _card.HandleCreated += (_, _) =>
        {
            var round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(_card.Handle, 33, ref round, sizeof(int));
            // Visible border on the dimmed background (COLORREF is 0x00BBGGRR).
            var border = Program.LightTheme ? 0x00B4B4B4 : 0x00585858;
            DwmSetWindowAttribute(_card.Handle, 34, ref border, sizeof(int));
        };
        _shade.Click += (_, _) => _card.Activate();
        _owner.LocationChanged += OnOwnerMoved;
        _owner.SizeChanged += OnOwnerMoved;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    // Small red X drawn in code (no font glyph), with a soft hover square.
    private sealed class CloseButton : Control
    {
        private bool _hover;
        public CloseButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            Size = new Size(26, 26);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(Parent?.BackColor ?? BackColor);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (_hover || (Focused && ShowFocusCues))
            {
                using var shape = new System.Drawing.Drawing2D.GraphicsPath();
                shape.AddRoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), new Size(LogicalToDeviceUnits(8), LogicalToDeviceUnits(8)));
                using var fill = new SolidBrush(MainWindow.SelectionColor);
                graphics.FillPath(fill, shape);
            }
            var red = Program.LightTheme ? Color.FromArgb(196, 58, 52) : Color.FromArgb(230, 92, 86);
            using var pen = new Pen(red, LogicalToDeviceUnits(2)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            float inset = Width * .32f;
            graphics.DrawLine(pen, inset, inset, Width - inset, Height - inset);
            graphics.DrawLine(pen, Width - inset, inset, inset, Height - inset);
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
    }

    public void Start()
    {
        _shade.Show(_owner);
        _card.Show(_shade);
        Show(0);
    }

    private void OnOwnerMoved(object? sender, EventArgs e)
    {
        if (_owner.WindowState == FormWindowState.Minimized) { Close(); return; }
        Place();
    }

    private void Show(int index)
    {
        if (_closed || index < 0 || index >= _steps.Count) return;
        _index = index;
        var step = _steps[index];
        _title.Text = step.Title;
        _body.Text = step.Body;
        foreach (Control old in _extraHost.Controls) old.Dispose();
        _extraHost.Controls.Clear();
        if (step.Extra?.Invoke() is { } extra) _extraHost.Controls.Add(extra);
        _extraHost.Visible = _extraHost.Controls.Count > 0;
        _progress.Text = $"{index + 1} of {_steps.Count}";
        _back.Enabled = index > 0;
        _next.Text = index == _steps.Count - 1 ? "Done" : "Next";
        // Size the card around its content, then pin the X to the top-right corner.
        _stack.PerformLayout();
        var content = _stack.PreferredSize;
        _card.ClientSize = new Size(_stack.Left + content.Width + _owner.LogicalToDeviceUnits(24), _stack.Top + content.Height + _owner.LogicalToDeviceUnits(18));
        _close.Location = new Point(_card.ClientSize.Width - _close.Width - _owner.LogicalToDeviceUnits(8), _owner.LogicalToDeviceUnits(8));
        _close.BringToFront();
        Place();
        _card.Activate();
        _next.Focus();
    }

    // Cut a rounded hole in the shade around the targets and park the card beside them.
    private void Place()
    {
        if (_closed) return;
        var window = _owner.Bounds;
        _shade.Bounds = window;
        var targets = _steps[_index].Targets().Where(target => target.Visible && target.IsHandleCreated).ToArray();
        var hole = Rectangle.Empty;
        foreach (var target in targets)
        {
            var screen = target.RectangleToScreen(target.ClientRectangle);
            hole = hole.IsEmpty ? screen : Rectangle.Union(hole, screen);
        }
        var region = new Region(new Rectangle(Point.Empty, window.Size));
        if (!hole.IsEmpty)
        {
            hole.Inflate(8, 8);
            hole.Offset(-window.X, -window.Y);
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddRoundedRectangle(hole, new Size(20, 20));
            region.Exclude(path);
            hole.Offset(window.X, window.Y);
        }
        var oldRegion = _shade.Region;
        _shade.Region = region;
        oldRegion?.Dispose();

        var size = _card.Size;
        var gap = 14;
        Point location;
        if (hole.IsEmpty) location = new Point(window.X + (window.Width - size.Width) / 2, window.Y + (window.Height - size.Height) / 2);
        else if (hole.Bottom + gap + size.Height <= window.Bottom) location = new Point(hole.Left, hole.Bottom + gap);
        else if (hole.Top - gap - size.Height >= window.Top) location = new Point(hole.Left, hole.Top - gap - size.Height);
        else location = new Point(window.X + (window.Width - size.Width) / 2, window.Y + (window.Height - size.Height) / 2);
        location.X = Math.Clamp(location.X, window.Left + 12, Math.Max(window.Left + 12, window.Right - size.Width - 12));
        _card.Location = location;
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _owner.LocationChanged -= OnOwnerMoved;
        _owner.SizeChanged -= OnOwnerMoved;
        _card.Close();
        _shade.Close();
        Finished?.Invoke();
        if (_owner.Visible) _owner.Activate();
    }

    public void Dispose()
    {
        Close();
        _title.Font.Dispose();
        _card.Dispose();
        _shade.Dispose();
    }
}
