"""Compact the read-only original-bundle evidence; no Unity assets are written."""
import hashlib
import json
import pathlib
import sys

evidence = pathlib.Path(sys.argv[1])
bind_path = evidence / "native-bundle-binds.json"
transform_path = evidence / "native-bundle-transforms.json"
bind = json.loads(bind_path.read_text(encoding="utf-8-sig"))
transforms = json.loads(transform_path.read_text(encoding="utf-8-sig"))
armature = next(t for t in transforms if t["path"] == "Armature")
result = {
    "schema": 1,
    "source": {
        "bundle": "models.unity3d",
        "bytes": bind["source"]["bytes"],
        "sha256": bind["source"]["sha256"],
        "bindEvidenceSha256": hashlib.sha256(bind_path.read_bytes()).hexdigest(),
        "transformEvidenceSha256": hashlib.sha256(transform_path.read_bytes()).hexdigest(),
    },
    "modelLocal": bind["modelLocal"],
    "armature": {k: armature["local" + k.title()] for k in ["position", "rotation", "scale"]},
    "bones": [
        dict(path=b["path"], required=b["required"], bind=bool(b["bindRenderers"]),
             **(b["bindWorld"] if b["bindRenderers"] else {"local": b["prefabLocal"]}))
        for b in bind["bones"]
    ],
    "renderers": [
        dict(path=r["path"], **r["rendererTransform"],
             bones=[dict(path=b["path"], matrix=[v for row in b["bindPose"] for v in row]) for b in r["bones"]])
        for r in bind["renderers"]
    ],
}
output = pathlib.Path(__file__).with_name("native-model-bind.json")
output.write_text(json.dumps(result, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
print(f"{output.name}: {len(result['bones'])} paths, {sum(b['bind'] for b in result['bones'])} binds, {output.stat().st_size} bytes")
