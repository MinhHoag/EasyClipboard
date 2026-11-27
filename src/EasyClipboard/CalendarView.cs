namespace EasyClipboard;

// Week of saved clips: one column per day, newest on top, each clip a tab-labelled preview card.
// Narrow windows show only the selected day. Previews load in the background, only for visible cards.
internal sealed class CalendarView : Control
{
    private readonly Func<Guid, ClipItem?> _load;
    private readonly VScrollBar _scroll = new() { Dock = DockStyle.Right, SmallChange = 40 };
    private readonly Dictionary<Guid, object?> _previews = []; // Bitmap, string, or null when unreadable
    private readonly HashSet<Guid> _loading = [];
    private List<HistoryEntry> _entries = [];
    private DateTime _weekStart = StartOfWeek(DateTime.Today);
    private DateTime _selected = DateTime.Today;
    private List<(HistoryEntry Entry, Rectangle Tab, Rectangle Body, int Overflow)> _cards = [];
    private readonly Dictionary<Guid, int> _textOffsets = []; // scroll position inside long text cards
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 15 };
    private double _target;
    private long _lastFrame;

    public event Action<HistoryEntry>? Activated;
    public event Action? WeekChanged;

    internal static Color Page => SystemInformation.HighContrast ? SystemColors.Window : Program.LightTheme ? Color.White : Color.Black;
    private static Color Card => SystemInformation.HighContrast ? SystemColors.Control : Program.LightTheme ? Color.FromArgb(234, 234, 234) : Color.FromArgb(30, 30, 30);
    private static Color CardBorder => SystemInformation.HighContrast ? SystemColors.WindowText : Program.LightTheme ? Color.FromArgb(200, 200, 200) : Color.FromArgb(62, 62, 62);
    private static Color Stripe => SystemInformation.HighContrast ? SystemColors.WindowText : Program.LightTheme ? Color.FromArgb(225, 225, 225) : Color.FromArgb(42, 42, 42);
    private static Color Muted => SystemInformation.HighContrast ? SystemColors.GrayText : Program.LightTheme ? Color.FromArgb(105, 105, 105) : Color.FromArgb(150, 150, 150);
    private static Color Ink => SystemInformation.HighContrast ? SystemColors.WindowText : Program.LightTheme ? Color.FromArgb(20, 20, 20) : Color.White;

    public CalendarView(Func<Guid, ClipItem?> load)
    {
        _load = load;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Page;
        ForeColor = Ink;
        AccessibleName = "Clipboard calendar";
        AccessibleRole = AccessibleRole.Grouping;
        Controls.Add(_scroll);
        _scroll.ValueChanged += (_, _) => Invalidate();
        _scroll.Scroll += (_, _) => { _animation.Stop(); _target = _scroll.Value; };
        _animation.Tick += (_, _) => AdvanceScroll();
    }

    public DateTime WeekStart => _weekStart;

    public void SetEntries(List<HistoryEntry> entries)
    {
        _entries = entries;
        var keep = entries.Select(entry => entry.Id).ToHashSet();
        foreach (var id in _previews.Keys.Where(id => !keep.Contains(id)).ToList()) Forget(id);
        Invalidate();
    }

    public void ShowWeek(DateTime anyDay, bool selectDay = false)
    {
        _weekStart = StartOfWeek(anyDay.Date);
        _selected = selectDay ? anyDay.Date : _weekStart.AddDays(_selected.DayOfWeek - _weekStart.DayOfWeek);
        _scroll.Value = 0;
        _target = 0;
        _textOffsets.Clear();
        // Only this week's previews stay in memory.
        foreach (var id in _previews.Keys.ToList()) Forget(id);
        WeekChanged?.Invoke();
        Invalidate();
    }

    private static DateTime StartOfWeek(DateTime day) => day.AddDays(-(int)day.DayOfWeek);
    private int Px(int logical) => LogicalToDeviceUnits(logical);
    // Solid block behind the week bar and day strip, distinct from the page.
    internal static Color HeaderColor => SystemInformation.HighContrast ? SystemColors.Control : Program.LightTheme ? Color.FromArgb(236, 236, 236) : Color.FromArgb(22, 22, 22);
    private static Color SelectedColumn => SystemInformation.HighContrast ? SystemColors.Window : Program.LightTheme ? Color.FromArgb(248, 248, 248) : Color.FromArgb(14, 14, 14);
    private Font NumberFont => _numberFont ??= new Font(Font.FontFamily, Font.Size * 1.3f, FontStyle.Bold);
    private Font SmallFont => _smallFont ??= new Font(Font.FontFamily, Font.Size * 0.85f);
    private Font? _numberFont, _smallFont;
    private int Circle => NumberFont.Height + Px(8);
    private int HeaderHeight => Px(8) + SmallFont.Height + Px(4) + Circle + Px(4) + SmallFont.Height + Px(10);
    private int ContentWidth => ClientSize.Width - (_scroll.Visible ? _scroll.Width : 0);
    // Narrow windows keep all seven day headers but show cards for the selected day only.
    private bool SingleDay => ContentWidth / 7 < Px(110);
    private DateTime[] Week => Enumerable.Range(0, 7).Select(offset => _weekStart.AddDays(offset)).ToArray();
    private DateTime[] CardDays => SingleDay ? [_selected] : Week;

    protected override void OnFontChanged(EventArgs e)
    {
        _numberFont?.Dispose(); _numberFont = null;
        _smallFont?.Dispose(); _smallFont = null;
        base.OnFontChanged(e);
    }

    private List<HistoryEntry> EntriesOn(DateTime day) =>
        _entries.Where(entry => entry.ReceivedAt.LocalDateTime.Date == day).ToList(); // already newest first

    private void LayoutCards(Graphics graphics)
    {
        _cards = [];
        var days = CardDays;
        var columnWidth = ContentWidth / days.Length;
        var pad = Px(8);
        var tabHeight = SmallFont.Height + Px(8);
        var tallest = 0;
        for (var column = 0; column < days.Length; column++)
        {
            var x = column * columnWidth + pad;
            var width = Math.Max(Px(40), columnWidth - pad * 2);
            var y = pad + Px(4);
            foreach (var entry in EntriesOn(days[column]))
            {
                _previews.TryGetValue(entry.Id, out var preview);
                var overflow = 0;
                int bodyHeight;
                if (preview is string text)
                {
                    var full = TextRenderer.MeasureText(graphics, text, Font, new Size(width - pad * 2, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height;
                    var shown = Math.Min(full, Font.Height * 8);
                    overflow = full - shown;
                    bodyHeight = shown + pad * 2;
                }
                else bodyHeight = preview is Bitmap image
                    ? Math.Min(width * image.Height / Math.Max(1, image.Width), Math.Max(Px(120), Height * 6 / 10))
                    : entry.IsImage ? width * 56 / 100 : Font.Height * 2 + pad * 2;
                var tabWidth = Math.Min(width - Px(10), TextRenderer.MeasureText(graphics, TabText(entry), SmallFont).Width + Px(16));
                _cards.Add((entry, new Rectangle(x, y, tabWidth, tabHeight), new Rectangle(x, y + tabHeight, width, bodyHeight), overflow));
                y += tabHeight + bodyHeight + Px(14);
            }
            tallest = Math.Max(tallest, y);
        }
        var visible = Math.Max(1, ClientSize.Height - HeaderHeight);
        _scroll.Maximum = Math.Max(0, tallest);
        _scroll.LargeChange = visible;
        _scroll.Visible = tallest > visible;
        if (_scroll.Value > Math.Max(0, tallest - visible)) _scroll.Value = Math.Max(0, tallest - visible);
    }

    private static string TabText(HistoryEntry entry) =>
        $"{entry.ReceivedAt.LocalDateTime:t} · {(entry.IsImage ? "Photo" : "Text")}";

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(Page);
        LayoutCards(graphics);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var week = Week;
        var headerWidth = ContentWidth / 7;
        using var stripe = new Pen(Stripe);

        if (!SingleDay)
        {
            using var tint = new SolidBrush(SelectedColumn);
            graphics.FillRectangle(tint, (int)(_selected - _weekStart).TotalDays * headerWidth, HeaderHeight, headerWidth, ClientSize.Height);
            for (var column = 1; column < 7; column++) graphics.DrawLine(stripe, column * headerWidth, HeaderHeight, column * headerWidth, ClientSize.Height);
        }

        // Cards first; the solid day strip is painted over them so nothing scrolls through it.
        var center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        var shift = HeaderHeight - _scroll.Value;
        using var card = new SolidBrush(Card);
        using var outline = new Pen(CardBorder);
        foreach (var (entry, tab, body, _) in _cards)
        {
            var tabOnScreen = tab with { Y = tab.Y + shift };
            var bodyOnScreen = body with { Y = body.Y + shift };
            if (bodyOnScreen.Bottom < HeaderHeight || tabOnScreen.Top > ClientSize.Height) continue;
            using var shape = Tabbed(tabOnScreen, bodyOnScreen);
            graphics.FillPath(card, shape);
            TextRenderer.DrawText(graphics, TabText(entry), SmallFont, tabOnScreen with { Y = tabOnScreen.Y + Px(1) }, Muted,
                center | TextFormatFlags.EndEllipsis);
            // Keep previews inside the rounded card (TextRenderer only honors this with PreserveGraphicsClipping).
            graphics.SetClip(shape);
            DrawPreview(graphics, entry, bodyOnScreen, shape);
            graphics.ResetClip();
            graphics.DrawPath(outline, shape); // thin edge separates light photos from the page
        }

        using (var strip = new SolidBrush(HeaderColor)) graphics.FillRectangle(strip, 0, 0, ClientSize.Width, HeaderHeight);
        for (var column = 0; column < 7; column++)
        {
            var day = week[column];
            var x = column * headerWidth;
            var count = EntriesOn(day).Count;
            var y = Px(8);
            TextRenderer.DrawText(graphics, day.ToString("ddd"), SmallFont, new Rectangle(x, y, headerWidth, SmallFont.Height), Muted, center);
            y += SmallFont.Height + Px(4);
            var circle = new Rectangle(x + (headerWidth - Circle) / 2, y, Circle, Circle);
            var isToday = day == DateTime.Today;
            if (isToday)
            {
                using var fill = new SolidBrush(Ink);
                graphics.FillEllipse(fill, circle);
            }
            else if (day == _selected)
            {
                using var ring = new Pen(Muted, Px(1));
                graphics.DrawEllipse(ring, Rectangle.Inflate(circle, -1, -1));
            }
            TextRenderer.DrawText(graphics, day.Day.ToString(), NumberFont, circle, isToday ? HeaderColor : Ink, center);
            y += Circle + Px(4);
            TextRenderer.DrawText(graphics, count == 0 ? "" : count == 1 ? "1 item" : $"{count} items", SmallFont,
                new Rectangle(x, y, headerWidth, SmallFont.Height), Muted, center);
            if (column > 0) graphics.DrawLine(stripe, x, Px(10), x, HeaderHeight - Px(10));
        }
        if (CardDays.All(day => EntriesOn(day).Count == 0))
            TextRenderer.DrawText(graphics, SingleDay ? "No copies saved on this day." : "No copies saved on these days.", Font,
                new Rectangle(0, HeaderHeight + Px(24), ContentWidth, Font.Height * 2), Muted, TextFormatFlags.HorizontalCenter);
        base.OnPaint(e);
    }

    // Folder tab on the card's top-left, joined to the card with a concave curve.
    private System.Drawing.Drawing2D.GraphicsPath Tabbed(Rectangle tab, Rectangle body)
    {
        var r = Px(6) * 2;  // tab corner diameter
        var big = Px(10) * 2; // card corner diameter
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddLine(body.X, body.Bottom - big / 2, tab.X, tab.Y + r / 2);
        path.AddArc(tab.X, tab.Y, r, r, 180, 90);
        path.AddArc(tab.Right - r, tab.Y, r, r, 270, 90);
        path.AddArc(tab.Right, body.Top - r, r, r, 180, -90);
        path.AddArc(body.Right - big, body.Top, big, big, 270, 90);
        path.AddArc(body.Right - big, body.Bottom - big, big, big, 0, 90);
        path.AddArc(body.X, body.Bottom - big, big, big, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void DrawPreview(Graphics graphics, HistoryEntry entry, Rectangle body, System.Drawing.Drawing2D.GraphicsPath shape)
    {
        var inner = Rectangle.Inflate(body, -Px(8), -Px(8));
        if (!_previews.TryGetValue(entry.Id, out var preview))
        {
            RequestPreview(entry.Id);
            TextRenderer.DrawText(graphics, entry.IsImage ? "Loading preview" : entry.Label, Font, inner, Muted,
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
            return;
        }
        switch (preview)
        {
            case Bitmap image:
                var height = Math.Min(body.Height, body.Width * image.Height / Math.Max(1, image.Width));
                var width = height * image.Width / Math.Max(1, image.Height);
                var target = new Rectangle(body.X + (body.Width - width) / 2, body.Y, width, height);
                // Fill the card shape with the photo so its rounded corners stay anti-aliased.
                using (var photo = new TextureBrush(image, System.Drawing.Drawing2D.WrapMode.Clamp))
                {
                    photo.TranslateTransform(target.X, target.Y);
                    photo.ScaleTransform((float)target.Width / image.Width, (float)target.Height / image.Height);
                    graphics.SetClip(target, System.Drawing.Drawing2D.CombineMode.Replace);
                    graphics.FillPath(photo, shape);
                }
                break;
            case string text:
                // Long text scrolls inside its card (mouse wheel while hovering), so draw it whole and clip.
                _textOffsets.TryGetValue(entry.Id, out var offset);
                graphics.SetClip(inner, System.Drawing.Drawing2D.CombineMode.Intersect);
                TextRenderer.DrawText(graphics, text, Font, new Rectangle(inner.X, inner.Y - offset, inner.Width, short.MaxValue), Ink,
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
                break;
            default:
                TextRenderer.DrawText(graphics, "Preview unavailable", Font, inner, Muted, TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
                break;
        }
    }

    private void RequestPreview(Guid id)
    {
        if (!_loading.Add(id)) return;
        var week = _weekStart;
        var maxWidth = Math.Max(Px(400), ContentWidth);
        Task.Run(() =>
        {
            object? result = null;
            try
            {
                var item = _load(id);
                if (item?.Png is { } png)
                {
                    using var stream = new MemoryStream(png);
                    using var image = Image.FromStream(stream);
                    // Display copy only; Copy Again still uses the original PNG.
                    var scale = Math.Min(1.0, (double)maxWidth / image.Width);
                    result = new Bitmap(image, Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
                }
                else result = item?.Text;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
                System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or
                System.Runtime.InteropServices.ExternalException or OutOfMemoryException) { }
            if (IsDisposed || !IsHandleCreated) { (result as IDisposable)?.Dispose(); return; }
            BeginInvoke(() =>
            {
                _loading.Remove(id);
                if (week != _weekStart || IsDisposed) { (result as IDisposable)?.Dispose(); return; }
                _previews[id] = result;
                Invalidate();
            });
        });
    }

    private void Forget(Guid id)
    {
        if (_previews.Remove(id, out var preview)) (preview as IDisposable)?.Dispose();
    }

    private HistoryEntry? CardAt(Point point) => CardInfoAt(point)?.Entry;

    private (HistoryEntry Entry, Rectangle Tab, Rectangle Body, int Overflow)? CardInfoAt(Point point)
    {
        if (point.Y < HeaderHeight) return null;
        var content = point with { Y = point.Y - HeaderHeight + _scroll.Value };
        foreach (var card in _cards)
            if (card.Body.Contains(content) || card.Tab.Contains(content)) return card;
        return null;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left || e.Y >= HeaderHeight) return;
        var column = Math.Clamp(e.X / Math.Max(1, ContentWidth / 7), 0, 6);
        _selected = _weekStart.AddDays(column);
        _scroll.Value = 0;
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (CardAt(e.Location) is { } entry) Activated?.Invoke(entry);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = CardAt(e.Location) is null ? Cursors.Default : Cursors.Hand;
    }

    private int MaxScroll => Math.Max(0, _scroll.Maximum - _scroll.LargeChange + 1);

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
        var step = e.Delta * Font.Height * 3 / 120;
        if (CardInfoAt(e.Location) is { Overflow: > 0 } card)
        {
            _textOffsets.TryGetValue(card.Entry.Id, out var offset);
            var next = Math.Clamp(offset - step, 0, card.Overflow);
            if (next != offset) { _textOffsets[card.Entry.Id] = next; Invalidate(); return; }
            // At the card's top or bottom: fall through and scroll the page.
        }
        if (!_scroll.Visible) return;
        _target = Math.Clamp((_animation.Enabled ? _target : _scroll.Value) - step, 0, MaxScroll);
        if (!SystemInformation.UIEffectsEnabled) { _scroll.Value = (int)_target; return; }
        if (_animation.Enabled) return;
        _lastFrame = System.Diagnostics.Stopwatch.GetTimestamp();
        _animation.Start();
    }

    // Eased scrolling, same feel as the history list.
    private void AdvanceScroll()
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var seconds = System.Diagnostics.Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds;
        _lastFrame = now;
        _target = Math.Clamp(_target, 0, MaxScroll);
        var difference = _target - _scroll.Value;
        if (Math.Abs(difference) <= 1) { _scroll.Value = (int)Math.Round(_target); _animation.Stop(); return; }
        var move = difference * (1 - Math.Exp(-seconds / 0.06));
        _scroll.Value = Math.Clamp(_scroll.Value + (int)(move > 0 ? Math.Ceiling(move) : Math.Floor(move)), 0, MaxScroll);
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is not (Keys.Left or Keys.Right)) return;
        var next = _selected.AddDays(e.KeyCode == Keys.Left ? -1 : 1);
        if (next < _weekStart || next >= _weekStart.AddDays(7)) ShowWeek(next, selectDay: true);
        else { _selected = next; Invalidate(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animation.Dispose();
            foreach (var id in _previews.Keys.ToList()) Forget(id);
            _numberFont?.Dispose();
            _smallFont?.Dispose();
        }
        base.Dispose(disposing);
    }
}
