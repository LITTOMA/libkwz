using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using KwzStudio;
using KwzTestFixtures;
using System.Security.Cryptography;

internal static class SdPickerChecks
{
    public static void Run(MainWindow owner, string root, string scratch, Action<bool, string> check, Action pump)
    {
        string sd = Path.Combine(scratch, "picker-card"), app = Path.Combine(sd, "private", "Nintendo 3DS", "app", "JKZJ");
        string plain = Path.Combine(app, "000"), named = Path.Combine(app, "Sketches"), broken = Path.Combine(app, "Drafts");
        foreach (string path in new[] { plain, named, broken }) Directory.CreateDirectory(path);
        string icon = Path.Combine(named, "!.ico"); File.WriteAllBytes(icon, FolderIconFixture.Create());
        File.WriteAllBytes(Path.Combine(broken, "!.ico"), [1, 2, 3]);
        var dialog = new SdFolderDialog(sd) { FontFamily = owner.FontFamily };
        Task<string?> result = dialog.ShowDialog<string?>(owner);
        void Render() { dialog.UpdateLayout(); pump(); dialog.UpdateLayout(); }
        void Click(Control target)
        {
            Point point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), dialog)!.Value;
            dialog.MouseDown(point, MouseButton.Left); Render(); dialog.MouseUp(point, MouseButton.Left); Render();
        }
        ListBox Choices() => dialog.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "SdFolderChoices");
        Button Button(string name) => dialog.GetVisualDescendants().OfType<Button>().Single(c => c.Name == name);
        SdFolderChoice[] Folders() => Choices().Items.Cast<SdFolderChoice>().ToArray();
        string IconHash(Bitmap bitmap) { using var bytes = new MemoryStream(); bitmap.Save(bytes); return Convert.ToHexString(SHA256.HashData(bytes.ToArray())); }
        try
        {
            Render();
            check(Folders().Length == 3 && Folders().Any(f => f.Path == named), "SD picker includes named empty folders without a catalog");
            check(Folders().Single(f => f.Path == named).Icon?.PixelSize == new PixelSize(24, 24) && Folders().Where(f => f.Path != named).All(f => f.Icon == null), "SD picker decodes KIC icons and falls back for missing or damaged icons");
            var labels = dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
            check(labels.Contains("Sketches") && !labels.Any(t => t.Contains(sd)), "SD picker labels show folder names instead of absolute paths");
            check(dialog.GetVisualDescendants().OfType<Avalonia.Controls.Image>().Any(i => RenderOptions.GetBitmapInterpolationMode(i) == BitmapInterpolationMode.None), "Folder icons use nearest-neighbor scaling");
            Choices().SelectedItem = Folders().Single(f => f.Path == named); Render();
            string before = IconHash(Folders().Single(f => f.Path == named).Icon!);
            string added = Path.Combine(app, "New folder"); Directory.CreateDirectory(added);
            File.WriteAllBytes(icon, FolderIconFixture.Create(configure: m => m.LayerAFirstColorIndex = 2));
            Click(Button("RefreshSdFolders"));
            check(Folders().Any(f => f.Path == added) && (Choices().SelectedItem as SdFolderChoice)?.Path == named, "Refresh discovers new folders while preserving the selected destination");
            check(IconHash(Folders().Single(f => f.Path == named).Icon!) != before, "Refresh reloads a changed game icon even when folder names stay the same");
            Choices().SelectedItem = Folders().Single(f => f.Path == added); Directory.Delete(added);
            Click(Button("UseSdFolder"));
            check(!result.IsCompleted && Choices().SelectedIndex == -1 && !Button("UseSdFolder").IsEnabled, "Import rechecks a removed destination and never silently switches folders");
            Directory.CreateDirectory(added); Click(Button("RefreshSdFolders"));
            check(Choices().SelectedIndex == -1 && Folders().Any(f => f.Path == added), "Reappearing folders require an explicit selection after the destination disappeared");
            dialog.Close(null); pump();
            check(result.IsCompletedSuccessfully && result.Result == null && !Directory.EnumerateFiles(sd, "*.kwz", SearchOption.AllDirectories).Any(), "Canceling the destination dialog performs no SD import");
        }
        finally { if (dialog.IsVisible) dialog.Close(null); }

        string later = Path.Combine(app, "Later"); Directory.CreateDirectory(later);
        dialog = new SdFolderDialog(sd) { FontFamily = owner.FontFamily };
        result = dialog.ShowDialog<string?>(owner); Render();
        try
        {
            check(Folders().Any(f => f.Path == later), "Opening the next import rescans the SD root rather than reusing the previous destination list");
            Choices().SelectedItem = Folders().Single(f => f.Path == named); Render(); Click(Button("UseSdFolder"));
            check(result.IsCompletedSuccessfully && result.Result == named, "The icon chooser returns the explicitly selected destination");
        }
        finally { if (dialog.IsVisible) dialog.Close(null); }

        string previousLanguage = L.Current;
        foreach (var language in L.Languages)
        {
            L.SetLanguage(language.Code, persist: false);
            dialog = new SdFolderDialog(sd) { FontFamily = owner.FontFamily,
                FlowDirection = L.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
            result = dialog.ShowDialog<string?>(owner); Render();
            try
            {
                foreach (var button in dialog.GetVisualDescendants().OfType<Button>().Where(b => b.Name is "UseSdFolder" or "RefreshSdFolders"))
                {
                    var origin = button.TranslatePoint(default, dialog)!.Value;
                    var opposite = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), dialog)!.Value;
                    if (Math.Min(origin.X, opposite.X) < 0 || Math.Max(origin.X, opposite.X) > dialog.Width + 1 || Math.Max(origin.Y, opposite.Y) > dialog.Height + 1)
                        throw new InvalidDataException("Clipped SD picker action: " + language.Code);
                }
                using var capture = dialog.CaptureRenderedFrame();
                check(capture != null && capture.PixelSize.Width >= 640, $"{language.Code}: SD icon picker renders with visible localized actions");
                if (language.Code == "zh-Hans") capture!.Save(Path.Combine(root, "editor/sd-import-preview.png"));
                if (language.Code == "ar") capture!.Save(Path.Combine(scratch, "sd-picker-ar.png"));
            }
            finally { dialog.Close(null); pump(); }
        }
        L.SetLanguage(previousLanguage, persist: false); pump();
    }
}
