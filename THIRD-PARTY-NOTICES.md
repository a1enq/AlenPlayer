# Third-party notices

AlenPlayer itself is licensed under the GNU General Public License v3.0 (see `LICENSE`).

The release archive also contains **`libmpv-2.dll`** — an unmodified build of the
[mpv](https://mpv.io) media player library made by the
[shinchiro/mpv-winbuild-cmake](https://github.com/shinchiro/mpv-winbuild-cmake) project.
AlenPlayer loads it at runtime and calls its public C API; the DLL is not changed in any way.

## Exact version used

| Component | Version | Source code |
|---|---|---|
| libmpv build | release `20261111`, file `mpv-dev-x86_64-20261111-git-bcde6b93cc.7z` | https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261111 |
| mpv | `v0.42.0-38-gbcde6b93c` | https://github.com/mpv-player/mpv/tree/bcde6b93cc |
| FFmpeg | `N-128766-gcd4e96eb3` | https://github.com/FFmpeg/FFmpeg/tree/cd4e96eb3 |

The build scripts that produce this DLL, including the exact versions of every bundled
library, are published in the shinchiro/mpv-winbuild-cmake repository linked above.
`build.ps1` in this repository downloads the same pinned file.

## License of libmpv-2.dll

This build of mpv is distributed under the **GNU General Public License, version 2 or later**
(it is built with GPL-licensed components of mpv and FFmpeg). AlenPlayer is distributed under
GPL-3.0, which is compatible with "GPL-2.0 or later".

## Main libraries included in the DLL

The DLL statically links many open-source libraries. The most important ones are:

| Library | License |
|---|---|
| [mpv](https://github.com/mpv-player/mpv) | GPL-2.0-or-later (parts LGPL-2.1-or-later) |
| [FFmpeg](https://ffmpeg.org) | GPL-2.0-or-later as built (LGPL-2.1-or-later core) |
| [libplacebo](https://code.videolan.org/videolan/libplacebo) | LGPL-2.1-or-later |
| [libass](https://github.com/libass/libass) | ISC |
| [FreeType](https://freetype.org) | FreeType License / GPL-2.0 |
| [HarfBuzz](https://github.com/harfbuzz/harfbuzz) | MIT |
| [FriBidi](https://github.com/fribidi/fribidi) | LGPL-2.1-or-later |
| [dav1d](https://code.videolan.org/videolan/dav1d) | BSD-2-Clause |
| [zlib](https://zlib.net) | zlib License |

The complete list depends on the build configuration and can be found in the
shinchiro/mpv-winbuild-cmake repository.

## Other components

- **.NET Framework 4.8** and **Windows** components are part of the operating system and are
  not distributed with AlenPlayer.
- All icons and graphics in AlenPlayer are drawn in code by the project itself.
