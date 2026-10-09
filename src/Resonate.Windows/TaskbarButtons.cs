using System.Runtime.InteropServices;

namespace Resonate.Windows;

/// <summary>
/// Taskbar controls, a built-in plugin: previous, play or pause, and next
/// buttons on the window's preview in the taskbar (ITaskbarList3's thumbnail
/// toolbar). Windows takes the buttons once per taskbar button and never
/// takes them back, so turning the plugin off hides them. A click reaches
/// the window as WM_COMMAND with <see cref="Clicked"/> in the high word of
/// wParam and the button in the low word. Call on the interface thread.
/// </summary>
public sealed unsafe partial class TaskbarButtons : IDisposable
{
    public const int PreviousId = 1;
    public const int PlayId = 2;
    public const int NextId = 3;

    /// <summary>THBN_CLICKED.</summary>
    public const int Clicked = 0x1800;

    private const uint ClsCtxInprocServer = 0x1;
    private const uint MaskIcon = 0x2;
    private const uint MaskTooltip = 0x4;
    private const uint MaskFlags = 0x8;
    private const uint FlagHidden = 0x8;

    private static readonly Guid ClsidTaskbarList = new("56FDF344-FD6D-11d0-958A-006097C9A090");
    private static readonly Guid IidTaskbarList3 = new("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf");

    private readonly nint _window;
    private readonly nint _previous;
    private readonly nint _play;
    private readonly nint _pause;
    private readonly nint _next;
    private nint _taskbar;
    private bool _added;

    /// <param name="window">The window whose taskbar button gets the buttons.</param>
    /// <param name="iconSize">The icons' size in pixels (16 at 100 % display scaling).</param>
    public TaskbarButtons(nint window, int iconSize)
    {
        _window = window;
        _previous = TaskbarIcons.Create(TaskbarIcon.Previous, iconSize);
        _play = TaskbarIcons.Create(TaskbarIcon.Play, iconSize);
        _pause = TaskbarIcons.Create(TaskbarIcon.Pause, iconSize);
        _next = TaskbarIcons.Create(TaskbarIcon.Next, iconSize);
    }

    /// <summary>The message Windows sends when the window's taskbar button is made again (Explorer restarted): the buttons are added again then.</summary>
    public static uint TaskbarButtonCreatedMessage { get; } = RegisterWindowMessageW("TaskbarButtonCreated");

    /// <summary>Shows the buttons (adding them the first time), with play or pause as the middle one; false when the taskbar refused.</summary>
    public bool Show(bool playing) => Send(playing, hidden: false);

    /// <summary>Hides the buttons (Windows keeps them until the window closes).</summary>
    public void Hide()
    {
        if (_added)
        {
            Send(playing: false, hidden: true);
        }
    }

    /// <summary>After the taskbar button was made again: the next <see cref="Show"/> adds the buttons anew.</summary>
    public void Forget()
    {
        Release();
        _added = false;
    }

    public void Dispose()
    {
        Release();
        foreach (var icon in (nint[])[_previous, _play, _pause, _next])
        {
            if (icon != 0)
            {
                DestroyIcon(icon);
            }
        }
    }

    private bool Send(bool playing, bool hidden)
    {
        if (_taskbar == 0)
        {
            var clsid = ClsidTaskbarList;
            var iid = IidTaskbarList3;
            if (CoCreateInstance(&clsid, 0, ClsCtxInprocServer, &iid, out var taskbar) < 0 || taskbar == 0)
            {
                return false;
            }

            _taskbar = taskbar;
            var init = (delegate* unmanaged[Stdcall]<nint, int>)Slot(_taskbar, 3);
            if (init(_taskbar) < 0)
            {
                Release();
                return false;
            }
        }

        var buttons = stackalloc ThumbButton[3];
        Fill(&buttons[0], PreviousId, _previous, "Previous", hidden);
        Fill(&buttons[1], PlayId, playing ? _pause : _play, playing ? "Pause" : "Play", hidden);
        Fill(&buttons[2], NextId, _next, "Next", hidden);

        // ThumbBarAddButtons once, then ThumbBarUpdateButtons.
        var call = (delegate* unmanaged[Stdcall]<nint, nint, uint, ThumbButton*, int>)Slot(_taskbar, _added ? 16 : 15);
        if (call(_taskbar, _window, 3, buttons) < 0)
        {
            return false;
        }

        _added = true;
        return true;
    }

    private static void Fill(ThumbButton* button, int id, nint icon, string tip, bool hidden)
    {
        button->Mask = MaskIcon | MaskTooltip | MaskFlags;
        button->Id = (uint)id;
        button->Icon = icon;
        button->Flags = hidden ? FlagHidden : 0;
        var length = Math.Min(tip.Length, 259);
        for (var i = 0; i < length; i++)
        {
            button->Tip[i] = tip[i];
        }

        button->Tip[length] = '\0';
    }

    private static nint Slot(nint instance, int index) => (*(nint**)instance)[index];

    private void Release()
    {
        if (_taskbar != 0)
        {
            var release = (delegate* unmanaged[Stdcall]<nint, uint>)Slot(_taskbar, 2);
            release(_taskbar);
            _taskbar = 0;
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, out nint instance);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    /// <summary>THUMBBUTTON.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbButton
    {
        public uint Mask;
        public uint Id;
        public uint Bitmap;
        public nint Icon;
        public fixed char Tip[260];
        public uint Flags;
    }
}

/// <summary>The taskbar buttons' pictures.</summary>
public enum TaskbarIcon
{
    Previous,
    Play,
    Pause,
    Next,
}

/// <summary>
/// The taskbar buttons' icons, drawn here in white with soft edges (four by
/// four samples a pixel), since the thumbnail toolbar only takes icons.
/// </summary>
public static unsafe partial class TaskbarIcons
{
    /// <summary>The icon's pixels, premultiplied BGRA from the top left (0 where nothing is drawn).</summary>
    public static uint[] Draw(TaskbarIcon kind, int size)
    {
        var pixels = new uint[size * size];
        const int Samples = 4;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var covered = 0;
                for (var sy = 0; sy < Samples; sy++)
                {
                    for (var sx = 0; sx < Samples; sx++)
                    {
                        var u = (x + ((sx + 0.5) / Samples)) / size;
                        var v = (y + ((sy + 0.5) / Samples)) / size;
                        if (Covers(kind, u, v))
                        {
                            covered++;
                        }
                    }
                }

                var alpha = (uint)Math.Round(255.0 * covered / (Samples * Samples));
                pixels[(y * size) + x] = (alpha << 24) | (alpha << 16) | (alpha << 8) | alpha;
            }
        }

        return pixels;
    }

    /// <summary>An icon Windows can show (destroy it with DestroyIcon), or 0.</summary>
    public static nint Create(TaskbarIcon kind, int size)
    {
        size = Math.Clamp(size, 12, 64);
        var pixels = Draw(kind, size);
        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = size,
            Height = -size,
            Planes = 1,
            BitCount = 32,
        };
        var color = CreateDIBSection(0, &header, 0, out var bits, 0, 0);
        if (color == 0)
        {
            return 0;
        }

        fixed (uint* source = pixels)
        {
            Buffer.MemoryCopy(source, (void*)bits, pixels.Length * 4L, pixels.Length * 4L);
        }

        var mask = CreateBitmap(size, size, 1, 1, null);
        var info = new IconInfo { IsIcon = 1, Mask = mask, Color = color };
        var icon = CreateIconIndirect(&info);
        DeleteObject(color);
        DeleteObject(mask);
        return icon;
    }

    /// <summary>Whether the point (0 to 1 across and down) is inside the shape.</summary>
    private static bool Covers(TaskbarIcon kind, double u, double v)
    {
        // Shapes in the middle 70 % of the square.
        const double Top = 0.18;
        const double Bottom = 0.82;
        if (v < Top || v > Bottom)
        {
            return false;
        }

        var t = (v - Top) / (Bottom - Top);
        var half = 0.5 - Math.Abs(t - 0.5);
        return kind switch
        {
            TaskbarIcon.Play => u >= 0.26 && u <= 0.26 + (1.2 * half * 0.92),
            TaskbarIcon.Pause => (u >= 0.24 && u <= 0.42) || (u >= 0.58 && u <= 0.76),
            TaskbarIcon.Next => (u >= 0.16 && u <= 0.16 + (1.12 * half)) || (u >= 0.72 && u <= 0.84),
            _ => (u >= 0.16 && u <= 0.28) || (u <= 0.84 && u >= 0.84 - (1.12 * half)),
        };
    }

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint dc, BitmapInfoHeader* info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateBitmap(int width, int height, uint planes, uint bitCount, void* bits);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);

    [LibraryImport("user32.dll")]
    private static partial nint CreateIconIndirect(IconInfo* info);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public uint HotspotX;
        public uint HotspotY;
        public nint Mask;
        public nint Color;
    }
}
