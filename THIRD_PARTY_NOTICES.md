# Third-party references

## UnityVMDPlayer

- Project: https://github.com/hobosore/UnityVMDPlayer
- Reviewed revision: `2cee8abd56c68ad3e88bcf4086e33185aa5ca4e8`
- Source: `UnityVMDPlayer.cs`, notably `CenterAnimation`, parent-of-all position handling, and `DefaultBoneAmplifier = 0.08f`.
- Additional reference from 0.9.4: `UnityVMDReader.cs` bone interpolation control layout, using channel blocks at offsets 0/16/32/48 and controls at block offsets 0/4/8/12. The existing Bezier evaluation and quaternion sampling implementation remain local.
- Use: algorithmic reference for composing MMD center/groove/parent displacements and coordinate direction. The local plugin retains its own sparse VMD solver and derives root displacement from the solved hierarchy. It does not embed the entire UnityVMDPlayer component or its Animator ownership. Controller collision, cancellation, asynchronous loading, rig caching and game integration are local adaptations.

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
