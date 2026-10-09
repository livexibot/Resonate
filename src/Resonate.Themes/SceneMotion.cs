using System.Globalization;

namespace Resonate.Themes;

/// <summary>
/// How the moving parts of a special look's scenery move (the app's
/// <c>SceneArt</c>): a striped sun that breathes, a grid that races towards
/// you, neon signs that flicker, searchlights that sweep, cars that cross a
/// bridge. Each moving element of the scenery carries its motion as its
/// <c>Tag</c>, written by <c>tools/scenes</c>: one or more parts separated
/// by <c>;</c>, each a kind and its values (<c>twinkle p=8 ph=0.3 lo=0.2</c>),
/// plus <c>o=</c>, the element's opacity, which the app sets itself. Every
/// part repeats a whole number of times per <see cref="SceneWeather.LoopSeconds"/>
/// (its period <c>p</c>, in seconds, is rounded to fit), so the scene's
/// clock starts again with no jump. <see cref="Parse"/> turns a tag into
/// compositor expressions on the scene's clock (<see cref="Clock"/>.Time),
/// and the pose the element rests in while nothing moves.
/// </summary>
public static class SceneMotion
{
    /// <summary>The name the expressions give the scene clock's property set.</summary>
    public const string Clock = "c";

    // How sharply a flicker or a glitch switches, in parts of a period.
    private const double Edge = 400;

    private static readonly Dictionary<string, Kind> Kinds = new(StringComparer.Ordinal)
    {
        // Brightens and dims smoothly, between lo and full.
        ["twinkle"] = new(["p", "ph", "lo"], Rest: 1),

        // Flashes on for the first w of each period: a beacon.
        ["blink"] = new(["p", "ph", "w"], Rest: 1),

        // Mostly on, failing twice in quick succession at the start of each period, down to lo: a tired neon tube.
        ["flicker"] = new(["p", "ph", "w", "lo"], Rest: 1),

        // To and fro along (dx, dy).
        ["sway"] = new(["p", "ph", "dx", "dy"], Rest: 1),

        // Round an ellipse of rx by ry.
        ["orbit"] = new(["p", "ph", "rx", "ry"], Rest: 1),

        // Along (dx, dy) and back to the start at once: for a pattern that repeats every (dx, dy).
        ["scroll"] = new(["p", "ph", "dx", "dy"], Rest: 1),

        // Along (dx, dy), fading in and out over the first and last w: across the sky and gone.
        ["travel"] = new(["p", "ph", "dx", "dy", "w"], Rest: 0),

        // Along (dx, dy) in the first w of each period, brightening and fading: a shooting star.
        ["shoot"] = new(["p", "ph", "dx", "dy", "w"], Rest: 0),

        // A line on the ground coming towards you, from the horizon at top to bottom, thicker as it nears; y0 is where it is drawn, far how much farther the horizon's end is than the bottom's.
        ["approach"] = new(["p", "ph", "top", "bottom", "y0", "far", "fade"], Rest: 1),

        // Turns round (cx, cy), dir 1 or -1.
        ["spin"] = new(["p", "ph", "dir", "cx", "cy"], Rest: 1),

        // Turns a degrees each way round (cx, cy): a searchlight.
        ["swing"] = new(["p", "ph", "a", "cx", "cy"], Rest: 1),

        // Grows and shrinks by s (or sx and sy) round (cx, cy).
        ["pulse"] = new(["p", "ph", "s", "sx", "sy", "cx", "cy"], Rest: 1),

        // Jumps dx to each side a few times in the first w of each period: a glitch.
        ["glitch"] = new(["p", "ph", "w", "dx"], Rest: 1),
    };

    /// <summary>Every kind of motion, by name.</summary>
    public static IReadOnlyCollection<string> KindNames => Kinds.Keys;

    /// <summary>
    /// The motion in <paramref name="tag"/>: an expression for each property
    /// it moves (null where it moves none), the point it turns and grows
    /// round, and the opacity it rests at.
    /// </summary>
    /// <exception cref="FormatException">An unknown kind or value, a missing period, or a number that does not read.</exception>
    public static Motion Parse(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        var opacity = 1.0;
        var centre = ((double X, double Y)?)null;
        var x = new List<string>();
        var y = new List<string>();
        var fades = new List<string>();
        var scaleX = new List<string>();
        var scaleY = new List<string>();
        var turns = new List<string>();
        var rest = 1.0;
        var parts = 0;
        foreach (var text in tag.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var word in words.Skip(1))
            {
                var (key, value) = Value(word);
                if (key == "o")
                {
                    opacity = Math.Clamp(value, 0, 1);
                }
                else
                {
                    values[key] = value;
                }
            }

            if (!Kinds.TryGetValue(words[0], out var kind))
            {
                if (words[0].StartsWith("o=", StringComparison.Ordinal) && values.Count == 0)
                {
                    // Only the opacity.
                    continue;
                }

                throw new FormatException($"'{words[0]}' is no kind of motion.");
            }

            foreach (var key in values.Keys.Where(k => !kind.Keys.Contains(k)))
            {
                throw new FormatException($"'{words[0]}' takes no '{key}'.");
            }

            if (!values.TryGetValue("p", out var period) || period <= 0)
            {
                throw new FormatException($"'{words[0]}' needs a period above 0 (p=).");
            }

            parts++;
            rest = Math.Min(rest, kind.Rest);
            if (values.ContainsKey("cx") || values.ContainsKey("cy"))
            {
                var c = (values.GetValueOrDefault("cx"), values.GetValueOrDefault("cy"));
                if (centre is { } other && other != c)
                {
                    throw new FormatException("Every part of one motion turns and grows round the same point.");
                }

                centre = c;
            }

            Add(words[0], values, x, y, fades, scaleX, scaleY, turns);
        }

        if (parts == 0)
        {
            throw new FormatException("A motion needs at least one kind.");
        }

        static string? Sum(List<string> terms) => terms.Count == 0 ? null : string.Join(" + ", terms);
        static string? Product(List<string> factors) => factors.Count == 0 ? null : string.Join(" * ", factors);
        string? translation = x.Count + y.Count == 0 ? null : $"Vector3({Sum(x) ?? "0"}, {Sum(y) ?? "0"}, 0)";
        string? scale = scaleX.Count + scaleY.Count == 0 ? null : $"Vector3({Product(scaleX) ?? "1"}, {Product(scaleY) ?? "1"}, 1)";
        var fade = fades.Count == 0 ? null : $"{N(opacity)} * {Product(fades)}";
        var (cx, cy) = centre ?? (0, 0);
        return new Motion(translation, fade, scale, Sum(turns), cx, cy, opacity, opacity * rest);
    }

    /// <summary>
    /// How many times a second something with a period of
    /// <paramref name="seconds"/> repeats: the nearest that repeats a whole
    /// number of times per loop, at least once.
    /// </summary>
    public static double Frequency(double seconds) =>
        Math.Max(1, Math.Round(SceneWeather.LoopSeconds / seconds)) / SceneWeather.LoopSeconds;

    private static void Add(
        string kind,
        Dictionary<string, double> v,
        List<string> x,
        List<string> y,
        List<string> fades,
        List<string> scaleX,
        List<string> scaleY,
        List<string> turns)
    {
        double Get(string key, double otherwise = 0) => v.GetValueOrDefault(key, otherwise);
        var frequency = Frequency(v["p"]);
        var phase = Get("ph");

        // How far through its period, 0 to 1, and the same as an angle for Sin and Cos.
        var u = $"Mod({Clock}.Time * {N(frequency)} + {N(phase)}, 1)";
        var angle = $"{Clock}.Time * {N(Math.Tau * frequency)} + {N(Math.Tau * phase)}";
        var w = Get("w", kind switch
        {
            "blink" => 0.06,
            "flicker" => 0.02,
            "travel" => 0.08,
            "shoot" => 0.05,
            _ => 0.02,
        });
        switch (kind)
        {
            case "twinkle":
                var lo = Get("lo", 0.3);
                fades.Add($"({N(lo)} + {N((1 - lo) / 2)} * (1 + Sin({angle})))");
                break;
            case "blink":
                fades.Add($"Sin({N(Math.PI)} * Clamp({u} / {N(w)}, 0, 1))");
                break;
            case "flicker":
                var low = Get("lo", 0.15);
                var first = $"Clamp(({N(w)} - {u}) * {N(Edge)}, 0, 1)";
                var second = $"Clamp(({N(3 * w)} - {u}) * {N(Edge)}, 0, 1) * Clamp(({u} - {N(2 * w)}) * {N(Edge)}, 0, 1)";
                fades.Add($"(1 - {N(1 - low)} * Max({first}, {second}))");
                break;
            case "sway":
                AddTerm(x, Get("dx"), $"Sin({angle})");
                AddTerm(y, Get("dy"), $"Sin({angle})");
                break;
            case "orbit":
                AddTerm(x, Get("rx"), $"Sin({angle})");
                AddTerm(y, Get("ry"), $"Cos({angle})");
                break;
            case "scroll":
                AddTerm(x, Get("dx"), u);
                AddTerm(y, Get("dy"), u);
                break;
            case "travel":
                AddTerm(x, Get("dx"), u);
                AddTerm(y, Get("dy"), u);
                fades.Add($"Clamp({u} / {N(w)}, 0, 1) * Clamp((1 - {u}) / {N(w)}, 0, 1)");
                break;
            case "shoot":
                var q = $"Clamp({u} / {N(w)}, 0, 1)";
                AddTerm(x, Get("dx"), q);
                AddTerm(y, Get("dy"), q);
                fades.Add($"Sin({N(Math.PI)} * {q})");
                break;
            case "approach":
                var top = Get("top");
                var bottom = Get("bottom");
                var y0 = Get("y0");
                var far = Math.Max(1.01, Get("far", 12));
                if (y0 <= top || bottom <= top)
                {
                    throw new FormatException("'approach' needs top < y0 and top < bottom.");
                }

                // Lines equally far apart on the ground: the bottom's distance 1, the horizon's end far; y follows 1 / distance.
                var line = $"({N(top)} + {N(bottom - top)} / (1 + {N(far - 1)} * (1 - {u})))";
                y.Add($"{line} - {N(y0)}");
                scaleY.Add($"({line} - {N(top)}) / {N(y0 - top)}");
                fades.Add($"Clamp(({line} - {N(top)}) / {N(Math.Max(1, Get("fade", 40)))}, 0, 1)");
                break;
            case "spin":
                turns.Add($"{N(360 * (Get("dir", 1) < 0 ? -1 : 1))} * {u}");
                break;
            case "swing":
                AddTerm(turns, Get("a"), $"Sin({angle})");
                break;
            case "pulse":
                var sx = Get("sx", Get("s"));
                var sy = Get("sy", Get("s"));
                if (sx != 0)
                {
                    scaleX.Add($"(1 + {N(sx)} * Sin({angle}))");
                }

                if (sy != 0)
                {
                    scaleY.Add($"(1 + {N(sy)} * Sin({angle}))");
                }

                break;
            case "glitch":
                // Three jumps each way in its window, still the rest of the period.
                var on = $"Clamp(({N(w)} - {u}) * {N(Edge)}, 0, 1)";
                AddTerm(x, Get("dx", 6), $"{on} * (Mod(Floor({u} / {N(w / 6)}), 2) * 2 - 1)");
                break;
        }
    }

    private static void AddTerm(List<string> terms, double amount, string expression)
    {
        if (amount != 0)
        {
            terms.Add($"{N(amount)} * {expression}");
        }
    }

    private static (string Key, double Value) Value(string word)
    {
        var at = word.IndexOf('=', StringComparison.Ordinal);
        if (at <= 0 || !double.TryParse(word.AsSpan(at + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
        {
            throw new FormatException($"'{word}' is not a value (key=number).");
        }

        return (word[..at], value);
    }

    // Negative numbers in brackets, so "a - -b" never appears.
    private static string N(double value) => value < 0 ? $"({VisualizerShapes.Number(value)})" : VisualizerShapes.Number(value);

    /// <summary>What a kind takes, and the share of its opacity it rests at while nothing moves.</summary>
    private sealed record Kind(string[] Keys, double Rest);

    /// <summary>
    /// A moving element's motion (see <see cref="Parse"/>): expressions for
    /// its visual's <c>Translation</c> (a Vector3), <c>Opacity</c>,
    /// <c>Scale</c> (a Vector3) and <c>RotationAngleInDegrees</c>, each null
    /// when it does not move; the <c>CenterPoint</c> it turns and grows round;
    /// its opacity; and the opacity it rests at while nothing moves (at its
    /// place as drawn, unturned, its own size).
    /// </summary>
    public sealed record Motion(
        string? Translation,
        string? Opacity,
        string? Scale,
        string? Rotation,
        double CentreX,
        double CentreY,
        double BaseOpacity,
        double RestOpacity);
}
