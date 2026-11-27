using System.Drawing.Drawing2D;

namespace EasyClipboard;

// Decorative window edges, drawn procedurally in the empty side and bottom margins.
internal static class EdgeArt
{
    public static readonly string[] Styles = ["None", "Vines", "Blossom"];
    public static string Label(string style) => style switch
    {
        "Vines" => "Vines and leaves",
        "Blossom" => "Wood and cherry blossom",
        _ => "None"
    };

    // Draws in the left, bottom and right margins; the top of the window is already busy.
    // sideTop: where the side art starts, so it stays clear of the busy header.
    public static void Draw(Graphics graphics, Rectangle outer, Padding margins, string style, int sideTop = 0)
    {
        if (style is not ("Vines" or "Blossom") || SystemInformation.HighContrast) return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var light = Program.LightTheme;
        // "Inward" points from the window edge into its margin band.
        var inset = Rectangle.Inflate(outer, -1, -1);
        (PointF From, PointF To, PointF Inward, int Band)[] edges =
        [
            (new(inset.Left, inset.Top + sideTop), new(inset.Left, inset.Bottom), new(1, 0), margins.Left),
            (new(inset.Left, inset.Bottom), new(inset.Right, inset.Bottom), new(0, -1), margins.Bottom),
            (new(inset.Right, inset.Bottom), new(inset.Right, inset.Top + sideTop), new(-1, 0), margins.Right),
        ];
        var random = new Random(7); // fixed seed: the same edge every repaint
        foreach (var (from, to, inward, band) in edges)
        {
            if (band < 6) continue;
            if (style == "Vines") Vine(graphics, from, to, inward, band, light, random);
            else Branch(graphics, from, to, inward, band, light, random);
        }
    }

    private static PointF Along(PointF from, PointF to, PointF inward, float t, float depth)
    {
        var length = Distance(from, to);
        var dx = (to.X - from.X) / length;
        var dy = (to.Y - from.Y) / length;
        return new(from.X + dx * t + inward.X * depth, from.Y + dy * t + inward.Y * depth);
    }

    private static float Distance(PointF a, PointF b) => MathF.Max(1, MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)));

    private static void Vine(Graphics graphics, PointF from, PointF to, PointF inward, int band, bool light, Random random)
    {
        var length = Distance(from, to);
        var stem = light ? Color.FromArgb(86, 120, 70) : Color.FromArgb(98, 140, 84);
        var leaf = light ? Color.FromArgb(104, 150, 82) : Color.FromArgb(84, 128, 70);
        var points = new List<PointF>();
        for (float t = 0; t <= length; t += 4)
            points.Add(Along(from, to, inward, t, band * .5f + MathF.Sin(t / 22f) * band * .22f));
        using var pen = new Pen(stem, MathF.Max(1.2f, band * .1f)) { LineJoin = LineJoin.Round };
        if (points.Count > 1) graphics.DrawLines(pen, points.ToArray());
        using var brush = new SolidBrush(leaf);
        var angle = MathF.Atan2(to.Y - from.Y, to.X - from.X) * 180 / MathF.PI;
        var side = 1;
        for (float t = 10; t < length - 6; t += 22 + random.Next(0, 10))
        {
            var at = Along(from, to, inward, t, band * .5f + MathF.Sin(t / 22f) * band * .22f);
            var state = graphics.Save();
            graphics.TranslateTransform(at.X, at.Y);
            graphics.RotateTransform(angle + side * (40 + random.Next(0, 25)));
            graphics.FillEllipse(brush, 0, -band * .12f, band * .48f, band * .24f);
            graphics.Restore(state);
            side = -side;
        }
    }

    private static void Branch(Graphics graphics, PointF from, PointF to, PointF inward, int band, bool light, Random random)
    {
        var length = Distance(from, to);
        var wood = light ? Color.FromArgb(118, 86, 60) : Color.FromArgb(128, 96, 70);
        var petal = light ? Color.FromArgb(240, 168, 190) : Color.FromArgb(232, 160, 184);
        var heart = light ? Color.FromArgb(196, 84, 118) : Color.FromArgb(206, 96, 128);
        var points = new List<PointF>();
        for (float t = 0; t <= length; t += 6)
            points.Add(Along(from, to, inward, t, band * .32f + MathF.Sin(t / 47f) * band * .1f + (float)(random.NextDouble() - .5) * band * .05f));
        using var pen = new Pen(wood, MathF.Max(1.2f, band * .11f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (points.Count > 1) graphics.DrawLines(pen, points.ToArray());
        using var twig = new Pen(wood, MathF.Max(1, band * .08f)) { EndCap = LineCap.Round };
        using var petals = new SolidBrush(petal);
        using var centre = new SolidBrush(heart);
        var bottom = inward.Y != 0; // the long bottom edge gets about half the flowers of the sides
        for (float t = 12; t < length - 8; t += (bottom ? 44 + random.Next(0, 30) : 38 + random.Next(0, 26)))
        {
            var root = Along(from, to, inward, t, band * .32f + MathF.Sin(t / 47f) * band * .1f);
            var bloom = Along(from, to, inward, t + band * .3f, band * (random.Next(2) == 0 ? .18f : .6f));
            graphics.DrawLine(twig, root, bloom);
            var size = band * (random.Next(3) == 0 ? .16f : .22f); // some buds, some open flowers
            for (var p = 0; p < 5; p++)
            {
                var a = p * MathF.Tau / 5 + t;
                graphics.FillEllipse(petals, bloom.X + MathF.Cos(a) * size - size * .7f, bloom.Y + MathF.Sin(a) * size - size * .7f, size * 1.4f, size * 1.4f);
            }
            graphics.FillEllipse(centre, bloom.X - size * .35f, bloom.Y - size * .35f, size * .7f, size * .7f);
        }
        // Loose petals caught mid-fall, scattered inward from the branch (static).
        using var fallen = new SolidBrush(Color.FromArgb(200, petal));
        var angle = MathF.Atan2(to.Y - from.Y, to.X - from.X) * 180 / MathF.PI;
        for (float t = 8; t < length - 4; t += (bottom ? 30 + random.Next(0, 24) : 26 + random.Next(0, 20)))
        {
            var at = Along(from, to, inward, t, band * (.05f + (float)random.NextDouble() * .3f));
            var state = graphics.Save();
            graphics.TranslateTransform(at.X, at.Y);
            graphics.RotateTransform(angle + random.Next(0, 360));
            graphics.FillEllipse(fallen, -band * .09f, -band * .05f, band * .18f, band * .1f);
            graphics.Restore(state);
        }
    }
}
