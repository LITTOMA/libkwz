using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LibKwz;
using System.Text.Json;

namespace KwzStudio;

public sealed partial class MainWindow
{
    private bool _sdImporting, _sdLoaded;
    private string? _sdRoot;
    private static string SdSettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KWZ Studio", "sd-import.json");
    private sealed record SdSettings(string Root);

    private async Task<bool> ChooseSdCard()
    {
        Stop();
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        { Title = L.T("Choose the SD card root"), AllowMultiple = false });
        string? selected = folders.FirstOrDefault()?.TryGetLocalPath();
        if (selected == null) return false;
        if (!KwzSdCard.IsSdRoot(selected))
        {
            await Info("Choose SD card…", "Select the SD card root containing private/Nintendo 3DS/app, not an animation folder.");
            return false;
        }
        _sdRoot = Path.GetFullPath(selected); _sdLoaded = true;
        SaveSdRoot();
        _status.Text = L.F("SD card: {0}", _sdRoot);
        return true;
    }

    private void SaveSdRoot()
    {
        // Local preference only: never stored in projects or exported animations.
        string temporary = SdSettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SdSettingsPath)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new SdSettings(_sdRoot!)));
            File.Move(temporary, SdSettingsPath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Usable for this session. */ }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private async Task ImportToSdCard()
    {
        if (_sdImporting) return;
        Stop();
        if (!_sdLoaded)
        {
            _sdLoaded = true;
            try
            {
                using var settings = JsonDocument.Parse(File.ReadAllBytes(SdSettingsPath));
                if (settings.RootElement.TryGetProperty("Root", out var root)) _sdRoot = root.GetString();
                else if (settings.RootElement.TryGetProperty("Folder", out var folder) && folder.GetString() is {} oldFolder)
                {
                    // Migrate only the user's previously explicit choice; never search other cards.
                    _sdRoot = KwzSdCard.GetSdRoot(oldFolder);
                    if (_sdRoot != null) SaveSdRoot();
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException) { }
        }
        if ((_sdRoot == null || !KwzSdCard.IsSdRoot(_sdRoot)) && !await ChooseSdCard()) return;
        // Choose a destination on every import, freshly scanned from the selected card.
        var dialog = new SdFolderDialog(_sdRoot!) { FlowDirection = FlowDirection, FontFamily = FontFamily };
        string? destination = await dialog.ShowDialog<string?>(this);
        if (destination == null) return;
        using var flattened = Document.CreateFlattenedNote();
        var content = (Control)Content!;
        _sdImporting = true; content.IsEnabled = false;
        _status.Text = L.T("Importing to SD card…");
        try
        {
            var result = await Task.Run(() => KwzSdCard.Import(destination, flattened));
            _status.Text = L.F("Imported to SD card: {0}", result.AnimationPath);
        }
        finally { _sdImporting = false; content.IsEnabled = true; }
    }
}
