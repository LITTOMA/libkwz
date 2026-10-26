# Text object font

New text objects use **Fusion Pixel / 缝合像素体**, the 12 px proportional Simplified Chinese variant,
embedded directly in the Studio assembly. No system font installation is needed. The default text size
is 24 px (2× the native pixel size); 12, 24, 36, and other integer multiples of 12 give regular pixel scaling.
Text measurement, preview, project reopening, and KWZ export use the same embedded typeface.
Existing objects retain the `FontFamily` stored in their project, including Microsoft YaHei.
The bundled font affects text objects, not the application's UI font.

- Source: [TakWolf/fusion-pixel-font](https://github.com/TakWolf/fusion-pixel-font)
- Release: [2026.10.25](https://github.com/TakWolf/fusion-pixel-font/releases/tag/2026.10.25)
- File: `FusionPixel/fusion-pixel-12px-proportional-zh_hans.otf` (unmodified)
- Family: `Fusion Pixel 12px Prop zh-Hans`
- SHA-256: `2972782F05D7DD5F8414BC0215F69C181561172286B1F9823B5C279D9E5E6674`
- License: SIL Open Font License 1.1. Original `OFL.txt` and component notices under `LICENSES/`
  are included and copied to the application output directory.
