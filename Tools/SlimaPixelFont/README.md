# Slima Pixel UI font sources

Build: `python build_font.py` with dependencies from `requirements.txt`.
The bundled Codex Python also works after installing FontTools in the workspace's `.utmp/fontdeps` directory.

Original Latin capitals, lowercase letters and digits live in `build_font.py`.
Chinese and remaining characters derive from the pinned Fusion Pixel 12px zh_hans BDF in `source/`.
Four styles are exported as TrueType files under `Assets/_Game/UI/Resources/SlimaUI/Fonts/`.
All font sources, generated font files and this builder are provided under OFL-1.1; preserve the notices in `source/OFL.txt` and `source/LICENSES/`.

See `Docs/Slima像素字体.md` for design, attribution, UI integration and preview.
