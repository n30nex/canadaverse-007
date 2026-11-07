# Graphics foundation and actual extension points

Audit base: Project Midas 1.0.2b / `9062f9f120b7f64d39e5db59500ed50e8e7a6686`. Paths below are relative to `F:\007\source\Project-Midas`. This audit describes implemented code, not an assumption that the empty `Mods` directory is an asset pipeline.

## Installed settings

The run log identifies NVIDIA GeForce GTX 1060 6GB and an Acer XF240H at **1920×1080, 144 Hz**. OpenGL 4.6 was available. `F:\007\Project-Midas\midas.ini` selects fullscreen, 1920×1080, widescreen, 90° horizontal widescreen FOV, 144 FPS cap, VSync, 4× MSAA, trilinear filtering, 16× anisotropy, and retained room cache mode 2. Low-resolution rendering and N64 AA emulation are off. Filtering of 2D art is off so UI glyphs stay crisp. Unlock All, unlimited explosions, steady scope and Fast Pause are off; head roll and normal distance fading remain on.

Upstream's borderless fullscreen code requests a drawable one pixel taller than the display (captures are 1920×1081). The configured desktop resolution is still native 1920×1080. Menus intentionally retain their original framing; gameplay is widescreen.

Dam's short automated traversal was around 142–144 rendered FPS in sampled release log intervals. Loading and shader/capture/frame-pacing hitches occurred; this does not guarantee every level is locked at 144. No overclocking or driver settings were changed. The source build was separately rendered and tested.

`src/game/frametiming.c` implements sub-field motion: float-scaled movement/camera integration can run at the configured higher rate, while discrete logic remains gated on whole 60 Hz fields. Thus this is **not merely render interpolation**. The preview fixes several high-rate behavior bugs but full game parity is not established by a short Dam test. Test movement, damage, AI and timers at 60/120/144 before changing this system.

## Existing code versus work to implement

| Area | Existing implementation and useful entry points | Work still needed |
| --- | --- | --- |
| Texture replacement | `port/src/port_textures.c`, `portTexReplaceRom` (~772), `portTexContextSet` (~874); `src/game/image.c` calls the replacement hook. `Data/Textures/Base/t02618.bmp` replaces texture number 2618. `Options/` overrides the options-menu context. | The replacement is encoded back into the original texture size/format/palette/mip layout. Larger images are downsampled. This is **not an HD texture-pack loader**. Add a renderer-side texture lookup and GPU-resident replacement path to retain higher resolution. |
| Named menu textures | Same file accepts 24/32-bit BMP files; named images use RGBA16, 1-bit alpha and a maximum 64×64 size. | PNG/DDS/KTX support, high-resolution UI layout and alpha quality require new code. No drop-in PNG directory support was found. |
| Shaders | `port/fast3d/gfx_cc.cpp/.h` decodes combiner state; `gfx_opengl.cpp`, `gfx_opengl_create_and_load_new_shader` (~337), generates GLSL in C++. | A reloadable external shader/postprocessing API needs implementation. `Data/shaders_base.txt`/`shaders.txt` contain shader IDs for warm-up/cache behavior, not editable GLSL effects. |
| Lighting | `gfx_pc.cpp`, `gfx_sp_vertex` (~3381), handles N64 vertex/lighting state. The retained OpenGL path in `gfx_opengl.cpp` (~2155–2178) supplies ambient plus two directional lights through `uLight[5]`; `gfx_roomcache.inl` retains draws. Game shading also comes from room/model data and `src/game/bg.c`. | Physical materials, normal/roughness maps, shadow maps, ambient occlusion and dynamic point-light attenuation are absent from this path. Add material/light metadata and preserve the original combiner as fallback. Cache and immediate paths must agree. |
| Models | `port/src/romfile_model.c`, `romfileModelConvert` (~376), converts big-endian ROM models to 32-bit host layouts. `src/game/model.c`, model-loading code, skeleton metadata and the `scripts/generate_*_model_c.py` tools expose the actual pipeline. | No general glTF/OBJ runtime replacement loader or skeleton retargeting interface was found. A new render-model layer should map stable file IDs to replacement geometry, skeletons and materials; collision/hitboxes stay independently versioned. |
| Rooms and sky | `port/src/romfile_bg.c`, `src/game/bg.c`, `src/game/sky.c`, plus `port/fast3d/gfx_roomcache.inl`. Existing fog, sky, transparent sorting and room/part caching are implemented. | Per-room authored lighting, new geometry, lightmaps and atmosphere effects require loaders/data formats and cache invalidation. Preserve original room visibility and collision. |
| Postprocessing | `gfx_opengl.cpp` already has framebuffer creation/resolution/MSAA plumbing. `gfx_sdl2.cpp` owns presentation. | No user-facing bloom/SSAO/tonemapping effect stack was found. Add an explicit final composition pass and GUI controls; avoid applying effects to the UI unintentionally. |
| Mod packaging | ROM loading and file conversion are in `romdata.c` and `romfile_*.c`. `Mods/README.txt` states that this build loads the stock cart only. | A manifest, dependency/order rules, asset validation, loader and mod-version hashes still need implementation. An empty Mods directory is not evidence that these exist. |

## Suggested implementation sequence

1. Establish fixed input recordings and screenshots at Dam, Facility and a translucent/fog-heavy scene; compare 60/144 Hz behavior. Keep 4× MSAA as the GTX 1060 baseline.
2. Implement renderer-side HD textures indexed by original texture number, with original dimensions/UV scale separate from GPU image dimensions. Enforce VRAM/cache limits, invalidate replaced cache entries, generate mipmaps, and fall back cleanly to the ROM. Start with newly authored test artwork, not a redistributed texture dump.
3. Add a small, optional final color/tonemapping pass. Then add normal/material metadata and a limited dynamic-light experiment. Measure GPU time and visual correctness in both retained and immediate render paths; keep all effects switchable.
4. Add replacement render models with explicit skeleton/animation mappings. Keep gameplay geometry, collision and hitboxes unchanged for a visual-only pack. Any changes affecting visibility, collision, timing or simulation belong in the simulation-mod compatibility hash.
5. Define a mod manifest and content-addressed package format only after the loader's real constraints are known. No arbitrary script execution or unvalidated absolute paths in an asset manifest.

No HD pack, replacement models, ray tracing or new lighting effects have been installed in this task. The completed work is the graphics configuration, editable source, tested build and a map of concrete implementation points.
