# Studio localization

KWZ Studio embeds 20 UTF-8 JSON catalogs from `Localization/`. English (`en.json`) is the fallback;
the English message IDs are stable keys, while the values are the displayed text. The catalogs cover
menus, tools, inspectors, dialogs, help, history labels, color descriptions, and editor error messages.
User-authored text, author names, filenames, project JSON fields, and library diagnostics are not translated.

`L.Initialize` selects the saved language or resolves the operating system UI culture. Specific Chinese
scripts take precedence over region: `zh-Hans` selects Simplified Chinese, `zh-Hant` Traditional Chinese;
Taiwan, Hong Kong, and Macao default to Traditional Chinese. Portuguese variants use Brazilian Portuguese.
Unknown system languages fall back to English. A malformed preference file falls back to the system language.

The Language menu lists native language names so users can find their language from any interface.
Changes apply immediately. Only the language code is saved, atomically, in
`%LOCALAPPDATA%/KWZ Studio/settings.json`; no project data is stored there. A failed write leaves the previous
language active and displays an error. UI formatting uses the selected culture without changing global
serialization culture or the project format.

Window-owned translation callbacks update controls without rebuilding the document or accumulating
event handlers. History stores message IDs and translates them at display time. Arabic changes the UI flow
direction; the canvas, palette indexes, transport controls, and timeline stay left-to-right to preserve
artwork coordinates and chronological navigation. UI text uses platform font fallback for the supported scripts.

## Updating translations

1. Add a stable English ID and value to `en.json`, then add the same key to every language catalog.
2. Use `L.T` for plain text and `L.F` for formatted text. In `MainWindow`, register static controls with
   `Localize`, `Label`, `Button`, or the menu helpers so they update during a language switch.
3. Preserve format placeholders exactly, including format specifiers such as `{0:0.#}` and `{1:0.00}`.
   Preserve actual shortcut letters and keys, file extensions, KWZ, and theme/package names.
4. Keep help paragraphs and line breaks readable. Menu underscores mark access keys; do not duplicate
   an access key among sibling menus. Use native UI terminology for frames, layers, objects, and projects.
5. Register a new locale in `L.Languages` with its native name. Catalogs are embedded automatically;
   language resources require rebuilding but do not need separate deployment files.

## Verification

Run the existing Studio smoke program after restoring/building its dependencies:

```powershell
dotnet run --project test/KwzEditor.Smoke -c Release -- .
```

Run this command from the library root. `KWZ_STUDIO_TEST_DIR` optionally places all disposable test data
in a task-owned scratch directory. Tests use a separate settings path and do not overwrite user preferences.

The checks require exact catalog coverage and matching format placeholders, render all 20 interfaces
through Avalonia.Headless and Skia, switch languages with unsaved text objects, and verify pixels,
selection, history, undo/redo, RTL coordinates, and preference persistence/failure handling. They regenerate
the README screenshots and write `localization-report.json` and `smoke-report.json` in this directory.
Rendering checks are automated; translation style can still benefit from native-speaker review.
