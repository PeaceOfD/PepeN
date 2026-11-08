using Avalonia.Platform.Storage;
using v2rayN.Desktop.Manager;
using v2rayN.Desktop.Views;

namespace v2rayN.Desktop.Common;

internal class UI
{
    private static readonly string caption = Global.AppName;

    public static async Task<ButtonResult> ShowYesNo(string msg)
    {
        var owner = WindowDialog.TryGetOwnerWindow();
        var box = new MessageBoxDialog(caption, msg);
        var result = await box.ShowDialog<ButtonResult>(owner);
        return result == ButtonResult.Yes ? ButtonResult.Yes : ButtonResult.No;
    }

    // PattN: a question with buttons of its own, over the window in front (the Aether editor's missing-keys question, the
    // preset fields' replace question): the index of the button clicked, -1 when it was closed otherwise.
    public static async Task<int> ShowChoice(string? title, string msg, IReadOnlyList<string> buttons, int cancelIndex)
    {
        var owner = WindowDialog.TryGetOwnerWindow();
        var dialog = new ChoiceDialog(title ?? caption, msg, buttons, cancelIndex);
        var result = await dialog.ShowDialog<int?>(owner);
        return result ?? -1;
    }

    public static async Task<string?> OpenFileDialog(FilePickerFileType? filter)
    {
        var sp = GetStorageProvider();
        if (sp is null)
        {
            return null;
        }

        // Start async operation to open the dialog.
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = filter is null ? [FilePickerFileTypes.All, FilePickerFileTypes.ImagePng] : [filter]
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task<string?> SaveFileDialog(string filter)
    {
        var sp = GetStorageProvider();
        if (sp is null)
        {
            return null;
        }

        // Start async operation to open the dialog.
        var files = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
        });

        return files?.TryGetLocalPath();
    }

    private static IStorageProvider? GetStorageProvider()
    {
        var owner = WindowDialog.TryGetOwnerWindow();
        var topLevel = TopLevel.GetTopLevel(owner);
        return topLevel?.StorageProvider;
    }
}
