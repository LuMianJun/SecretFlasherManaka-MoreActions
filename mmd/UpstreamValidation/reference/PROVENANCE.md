# Pinned upstream oracle sources

Repository: https://github.com/hobosore/UnityVMDPlayer

Commit: `2cee8abd56c68ad3e88bcf4086e33185aa5ca4e8`.

Downloaded from `https://raw.githubusercontent.com/hobosore/UnityVMDPlayer/<commit>/<filename>`. The three C# files are unmodified byte copies. `Program.cs` checks their SHA256 before executing the oracle:

| File | SHA256 |
| --- | --- |
| UnityVMDPlayer.cs | f9d7b7f59d983575eaa7c69960246c892f1d3d9ccdbb9c2855b1c4f856c11aaf |
| UnityVMDReader.cs | e6b60154bffb8178612b5d3d4111103568177f7d6acdf026cecc1889c7e97834 |
| UnityVMDQuaternionExtentions.cs | ff77d848fd72ebe306f662c740235c4696ff60bc17f1a394e950b9d8af10f88e |

The upstream MIT license is preserved in `LICENSE`, including its copyright notice. These are validation-only sources; no oracle source is linked into the plugin by this project.
