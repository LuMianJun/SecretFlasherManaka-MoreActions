# Third-party references

## nanoem semi-standard bone behavior

- [Semi-standard bone plugin source](https://github.com/hkrn/nanoem/blob/main/emapp/plugins/ssb/ssb.cc) and [bone evaluation](https://github.com/hkrn/nanoem/blob/main/emapp/src/model/Bone.cc) were inspected to verify waist/shoulder cancellation, the post-IK D deformation chain, and user-rotation times inherited-rotation ordering.
- These were behavioral references for the retired local synthetic skeleton and retargeter. That implementation used the game toe joint as an adapted EX pivot; it is not part of the current UnityVMDPlayer-based body solve. The plugin does not embed nanoem's implementation or model assets.

## AnimReplacer

Project thread: [Secret Flasher Manaka Anim Replacer](https://f95zone.to/threads/secret-flasher-manaka-anim-replacer.299100/).

AssetBundle import, animation replacement and independent-pose playback reference the author-provided AnimReplacer source. Reviewed the user-provided anim.dll and source.zip for bundle layout, per-folder variants and AnimationClip naming conventions. This plugin imports bundles from its Actions directory, optionally accepts side-by-side JSON descriptors, and exposes additional poses through the native ring menu using an Avatar-driven PlayableGraph. Replacements match native AnimationClip names and use an animator override layer created at runtime from the live controller. The original IMGUI window, hotkey handler and controller-reclaim loop are not imported. Animation assets remain separately licensed and are not distributed in this repository or public packages. Native game localization keys and animation identifiers are referenced without distributing game assets or localization tables.

The user supplied this license with the original source:

```text
MIT License

Copyright (c) 2026 eifthbazvfn

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## UnityVMDPlayer

- Project: [hobosore/UnityVMDPlayer](https://github.com/hobosore/UnityVMDPlayer).
- Pinned revision: [`2cee8abd56c68ad3e88bcf4086e33185aa5ca4e8`](https://github.com/hobosore/UnityVMDPlayer/tree/2cee8abd56c68ad3e88bcf4086e33185aa5ca4e8).
- Adapted sources: `UnityVMDPlayer.cs`, `UnityVMDReader.cs` and `UnityVMDQuaternionExtentions.cs`. Source locations and hashes are recorded in `mmd/Plugin/Upstream/upstream-source.json`.
- MMD derives its body solver, reader and quaternion helpers from these sources under `mmd/Plugin/Upstream`. The game adapter adds independent control pivots, parent-first output, explicit zero foot positions and time-based IK switches. The pinned comparison path preserves the original behavior.
- The shared parser preserves duplicate bone keys and original IK frame/entry ordering. An explicit `UpstreamInputCompatibility` layer strips content after the first NUL in bone names and retains the existing full-width/half-width and common-spelling normalization. It supplies already-converted keys to the strict adapted reader without a second file read or coordinate conversion; this input adaptation does not replace the upstream sampling or body formulas. A managed `UpstreamAvatar` supplies the Humanoid references and initial skeleton data required by the extracted solver. Per-action A/T selection and nullable native toe controllers are integration adaptations.
- The body solve runs on a hidden hierarchy built from the game's bind data, native bone axes and supported body dimensions. The plugin retains game Animator ownership and transfers selected output through its native-pose lease. Rig preparation/cache lifecycle, expression layers, audio, camera, controller collision, cancellation and restoration remain local integration. A separate optional-track layer handles waist/cancellation, shoulder P/C, upper-body 3, eyes, twist, thumb numbering and D/EX grants. Active grants can change final poses; they do not replace the pinned standard solver routines.
- The loop-endpoint adapter obtains controller cycle displacement from the original final playable frame's saved n+1 pose using the same local-pose interpolation, then resets sampling state. Native neck, shoulder and leg-spacing customization participates in cached reference geometry.
- The fixed base-model morph table is a local adaptation of the upstream `MorphPlayer` concept. It uses verified path/name/index identifiers instead of hierarchy discovery or substring matching. Native expression overlay/restoration, customization protection and layout verification remain local. The table contains identifiers only, not model assets.
- `mmd/UpstreamValidation/reference` retains the pinned original sources for offline comparison against the path without game controls. `PlaybackParityValidation` measures native output against the original with extensions excluded or restored. The original MonoBehaviour, Animator enable/disable policy and UI are not used by the installed plugin. Game/Unity assemblies and model or animation assets are not distributed with these sources.

The upstream license is retained below for attribution and any adapted portions:

```text
MIT License

Copyright (c) 2019 hobosore

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Historical format/camera references and example assets

Local historical records identify three.js r160 `MMDLoader`, `MMDAnimationHelper` and `mmdparser` as references for VMD format, interpolation, coordinate conversion and camera behavior. The records do not claim whole-file source imports. WAVEFILE example motion/camera/audio and the retired default PMD originated from the three.js MMD examples.

The public repository and packages do not redistribute those historical example assets. Any media/model assets have their own terms; this notice does not relicense them. babylon-mmd, libmmd-for-unity and UnityMMDTools were discussed as alternatives, but were not integrated into this plugin.


## three.js r160 license

Source: https://github.com/mrdoob/three.js/blob/r160/LICENSE

```text
The MIT License

Copyright © 2010-2023 three.js authors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```


## mmd-parser license

Source: https://github.com/takahirox/mmd-parser/blob/master/LICENSE

```text
MIT License

Copyright (c) 2016 Takahiro

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Runtime infrastructure

BepInEx, Il2CppInterop and HarmonyX are supplied by the user's BepInEx installation. Their assemblies and game/Unity assemblies are not included in this repository or release package. See README for project links.
