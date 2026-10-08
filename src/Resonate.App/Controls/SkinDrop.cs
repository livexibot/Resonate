using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace Resonate.App.Controls;

/// <summary>A classic skin (.wsz) dropped on the classic or mini player is added and used, as Winamp did.</summary>
internal static class SkinDrop
{
    public static void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.Handled = true;
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Use this skin";
        }
    }

    public static async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.Handled = true;
        string? path = null;
        var deferral = e.GetDeferral();
        try
        {
            // Only the path is needed (SkinLibrary checks the file itself), so no item is cast to a file type.
            foreach (var item in await e.DataView.GetStorageItemsAsync())
            {
                var extension = Path.GetExtension(item.Path);
                if (extension.Equals(".wsz", StringComparison.OrdinalIgnoreCase) || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    path = item.Path;
                    break;
                }
            }
        }
        catch (COMException)
        {
            // Windows could not hand over what was dropped; nothing is added.
        }
        finally
        {
            deferral.Complete();
        }

        if (!string.IsNullOrEmpty(path))
        {
            await App.Services.Skins.ImportAsync(path);
        }
    }
}
