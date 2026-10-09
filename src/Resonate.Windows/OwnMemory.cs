namespace Resonate.Windows;

/// <summary>Resonate's own memory: handed back to Windows while nobody sees the window.</summary>
public static class OwnMemory
{
    /// <summary>Hands the pages Resonate is not using back to Windows; they page back in when needed.</summary>
    public static void Trim() => _ = Interop.Processes.EmptyWorkingSet(-1);
}
