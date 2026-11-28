namespace EasyClipboard;

internal sealed class HistoryList : ScrollableControl
{
    private readonly List<HistoryEntry> _entries = [];
    // Display rows: a group heading (Entry -1) or an entry index. _rowOf maps an entry index to its row.
    private readonly List<(string? Heading, int Entry)> _rows = [];
    private readonly List<int> _rowOf = [];
    private readonly Dictionary<string, int> _whenWidth = []; // time column width per group, so short times sit close
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 15 };
    private int _selectedIndex = -1;
    private double _targetOffset;
    private long _lastFrame;
    private bool _settingOffset;

    public event EventHandler? SelectedIndexChanged;
    public HistoryEntry? SelectedItem => _selectedIndex >= 0 ? _entries[_selectedIndex] : null;
    private int RowHeight => Font.Height + (int)Math.Round(10 * DeviceDpi / 96.0);
    private int Offset => -AutoScrollPosition.Y;
    private int MaximumOffset => Math.Max(0, _rows.Count * RowHeight - ClientSize.Height);
    private int RowOf(int entry) => entry >= 0 && entry < _rowOf.Count ? _rowOf[entry] : 0;

    private void BuildRows()
    {
        _rows.Clear();
        _rowOf.Clear();
        string? group = null;
        for (var index = 0; index < _entries.Count; index++)
        {
            var next = _entries[index].Group;
            if (next != group) _rows.Add((group = next, -1));
            _rowOf.Add(_rows.Count);
            _rows.Add((null, index));
        }
        var gap = (int)Math.Round(10 * DeviceDpi / 96.0);
        _whenWidth.Clear();
        var widestTime = TextRenderer.MeasureText(new DateTime(2000, 1, 1, 12, 59, 0).ToString("t"), Font).Width + gap; // Today and Yesterday line up
        foreach (var entry in _entries)
            _whenWidth[entry.Group] = Math.Max(_whenWidth.GetValueOrDefault(entry.Group, widestTime), TextRenderer.MeasureText(entry.When, Font).Width + gap);
        AutoScrollMinSize = new Size(0, _rows.Count * RowHeight);
    }

    public HistoryList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        AutoScroll = true;
        TabStop = true;
        BackColor = MainWindow.Surface;
        ForeColor = MainWindow.TextColor;
        AccessibleName = "Saved clipboard history";
        AccessibleRole = AccessibleRole.List;
        _animation.Tick += (_, _) => AdvanceScroll();
    }

    public void SetEntries(IReadOnlyList<HistoryEntry> entries)
    {
        var selectedId = SelectedItem?.Id;
        var topRow = Math.Min(Offset / RowHeight, _rows.Count - 1);
        var topEntry = topRow >= 0 ? _rows.Skip(topRow).Select(row => row.Entry).FirstOrDefault(entry => entry >= 0, -1) : -1;
        var topId = topEntry >= 0 ? _entries[topEntry].Id : (Guid?)null;
        var withinRow = Offset % RowHeight;
        var wasAtTop = Offset == 0;
        _animation.Stop();
        _entries.Clear();
        _entries.AddRange(entries);
        _selectedIndex = _entries.FindIndex(entry => entry.Id == selectedId);
        if (_selectedIndex < 0 && _entries.Count > 0) _selectedIndex = 0;
        BuildRows();
        var newTop = _entries.FindIndex(entry => entry.Id == topId);
        SetOffset(wasAtTop ? 0 : newTop >= 0 ? RowOf(newTop) * RowHeight + withinRow : Offset);
        _targetOffset = Offset;
        Invalidate();
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        AccessibilityNotifyClients(AccessibleEvents.Reorder, -1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var first = Offset / RowHeight;
        var last = Math.Min(_rows.Count, (Offset + ClientSize.Height + RowHeight - 1) / RowHeight);
        var highContrast = SystemInformation.HighContrast;
        var selection = highContrast ? SystemColors.Highlight : MainWindow.SelectionColor;
        var selectionText = highContrast ? SystemColors.HighlightText : MainWindow.TextColor;
        using var selectionBrush = new SolidBrush(selection);
        for (var row = first; row < last; row++)
        {
            var bounds = new Rectangle(0, row * RowHeight - Offset, ClientSize.Width, RowHeight);
            if (_rows[row].Heading is { } heading)
            {
                // Group heading: bold, full text color, with a hairline running to the edge (like File Explorer).
                using var bold = new Font(Font, FontStyle.Bold);
                var textBounds = Rectangle.Inflate(bounds, -4, 0) with { Y = bounds.Y + RowHeight / 5 };
                TextRenderer.DrawText(e.Graphics, heading, bold, textBounds, ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                var lineLeft = textBounds.X + TextRenderer.MeasureText(heading, bold).Width + 4;
                var lineY = textBounds.Y + textBounds.Height / 2;
                using var hairline = new Pen(highContrast ? ForeColor : Color.FromArgb((ForeColor.R + BackColor.R * 3) / 4, (ForeColor.G + BackColor.G * 3) / 4, (ForeColor.B + BackColor.B * 3) / 4));
                if (lineLeft < bounds.Right - 8) e.Graphics.DrawLine(hairline, lineLeft, lineY, bounds.Right - 8, lineY);
                continue;
            }
            var index = _rows[row].Entry;
            var selected = index == _selectedIndex;
            if (selected)
            {
                if (highContrast || bounds.Width < 10)
                    e.Graphics.FillRectangle(selectionBrush, bounds);
                else
                {
                    using var outline = new System.Drawing.Drawing2D.GraphicsPath();
                    outline.AddRoundedRectangle(bounds, new Size(10, 10));
                    var smoothing = e.Graphics.SmoothingMode;
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.FillPath(selectionBrush, outline);
                    e.Graphics.SmoothingMode = smoothing;
                }
            }
            // Day and time in a dimmer fixed column, so the labels line up and get the room.
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            var text = selected ? selectionText : ForeColor;
            var dim = highContrast ? text : Color.FromArgb((text.R * 2 + BackColor.R) / 3, (text.G * 2 + BackColor.G) / 3, (text.B * 2 + BackColor.B) / 3);
            var column = _whenWidth.GetValueOrDefault(_entries[index].Group);
            var inner = Rectangle.Inflate(bounds, -4, 0);
            TextRenderer.DrawText(e.Graphics, _entries[index].When, Font, inner with { Width = column }, dim, flags);
            TextRenderer.DrawText(e.Graphics, _entries[index].Label, Font,
                inner with { X = inner.X + column, Width = Math.Max(0, inner.Width - column) }, text, flags);
            if (selected && Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -2, -2),
                selectionText, selection);
        }
        base.OnPaint(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
        var lines = SystemInformation.MouseWheelScrollLines;
        if (lines == 0) return;
        var distance = lines < 0 ? ClientSize.Height : lines * RowHeight;
        ScrollTo((_animation.Enabled ? _targetOffset : Offset) - e.Delta / 120.0 * distance);
    }

    private void ScrollTo(double offset)
    {
        _targetOffset = Math.Clamp(offset, 0, MaximumOffset);
        if (!SystemInformation.UIEffectsEnabled)
        {
            SetOffset((int)Math.Round(_targetOffset));
            return;
        }
        if (_animation.Enabled) return;
        _lastFrame = System.Diagnostics.Stopwatch.GetTimestamp();
        _animation.Start();
    }

    private void AdvanceScroll()
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var seconds = System.Diagnostics.Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds;
        _lastFrame = now;
        _targetOffset = Math.Clamp(_targetOffset, 0, MaximumOffset);
        var difference = _targetOffset - Offset;
        if (Math.Abs(difference) <= 1)
        {
            SetOffset((int)Math.Round(_targetOffset));
            _animation.Stop();
            return;
        }
        var step = difference * (1 - Math.Exp(-seconds / 0.045));
        SetOffset(Offset + (int)(step > 0 ? Math.Ceiling(step) : Math.Floor(step)));
    }

    private void SetOffset(int offset)
    {
        _settingOffset = true;
        try { AutoScrollPosition = new Point(0, Math.Clamp(offset, 0, MaximumOffset)); }
        finally { _settingOffset = false; }
        Invalidate();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        if (!_settingOffset)
        {
            _animation.Stop();
            _targetOffset = Offset;
        }
        base.OnScroll(se);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        var clicked = (Offset + e.Y) / RowHeight;
        if (clicked >= 0 && clicked < _rows.Count && _rows[clicked].Entry >= 0) SelectEntry(_rows[clicked].Entry);
    }

    public void Select(Guid id)
    {
        var index = _entries.FindIndex(entry => entry.Id == id);
        if (index < 0) return;
        SelectEntry(index);
        var row = RowOf(index);
        if (row * RowHeight < Offset || (row + 1) * RowHeight > Offset + ClientSize.Height)
            SetOffset(row * RowHeight - ClientSize.Height / 3);
        _targetOffset = Offset;
    }

    private void SelectEntry(int index)
    {
        if (index < 0 || index >= _entries.Count || index == _selectedIndex) return;
        _selectedIndex = index;
        Invalidate();
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        AccessibilityNotifyClients(AccessibleEvents.Selection, index);
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is
        Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var page = Math.Max(1, ClientSize.Height / RowHeight);
        var next = e.KeyCode switch
        {
            Keys.Up => _selectedIndex - 1,
            Keys.Down => _selectedIndex + 1,
            Keys.Home => 0,
            Keys.End => _entries.Count - 1,
            Keys.PageUp => _selectedIndex - page,
            Keys.PageDown => _selectedIndex + page,
            _ => (int?)null
        };
        if (next is { } index && _entries.Count > 0)
        {
            SelectEntry(Math.Clamp(index, 0, _entries.Count - 1));
            var top = RowOf(_selectedIndex) * RowHeight;
            // Keep the group heading above the first item in view too.
            if (_rows[RowOf(_selectedIndex) - 1].Entry < 0) top -= RowHeight;
            if (top < Offset) ScrollTo(top);
            else if (top + RowHeight > Offset + ClientSize.Height) ScrollTo(top + RowHeight - ClientSize.Height);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        BuildRows();
        Invalidate();
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new HistoryAccessibility(this);
    private sealed class HistoryAccessibility(HistoryList owner) : ControlAccessibleObject(owner)
    {
        public override int GetChildCount() => owner._entries.Count;
        public override AccessibleObject? GetChild(int index) => index >= 0 && index < owner._entries.Count
            ? new EntryAccessibility(owner, this, owner._entries[index]) : null;
        public override AccessibleObject? GetSelected() => GetChild(owner._selectedIndex);
        public override AccessibleObject? GetFocused() => owner.Focused ? GetSelected() : null;
    }

    private sealed class EntryAccessibility(HistoryList owner, AccessibleObject parent, HistoryEntry entry) : AccessibleObject
    {
        private int Index => owner._entries.FindIndex(item => item.Id == entry.Id);
        public override string? Name { get => entry.ToString(); set { } }
        public override AccessibleRole Role => AccessibleRole.ListItem;
        public override AccessibleObject? Parent => parent;
        public override Rectangle Bounds => owner.RectangleToScreen(new Rectangle(0,
            owner.RowOf(Index) * owner.RowHeight - owner.Offset, owner.ClientSize.Width, owner.RowHeight));
        public override AccessibleStates State => AccessibleStates.Selectable | AccessibleStates.Focusable |
            (Index < 0 ? AccessibleStates.Unavailable : AccessibleStates.None) |
            (owner._selectedIndex == Index ? AccessibleStates.Selected : AccessibleStates.None) |
            (owner.Focused && owner._selectedIndex == Index ? AccessibleStates.Focused : AccessibleStates.None) |
            (!Bounds.IntersectsWith(owner.RectangleToScreen(owner.ClientRectangle)) ? AccessibleStates.Offscreen : AccessibleStates.None);
        public override string? DefaultAction => "Select";
        public override void DoDefaultAction() => owner.SelectEntry(Index);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animation.Dispose();
        base.Dispose(disposing);
    }
}
