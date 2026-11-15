"""Export the contact vertices from a local decrypted bundle."""

import argparse
import hashlib
import json
import math
from pathlib import Path

import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler


def matrix_values(pose):
    return [getattr(pose, f"e{r}{c}") for r in range(4) for c in range(4)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bundle", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    here = Path(__file__).resolve().parent
    catalog = json.loads((here / "contact-mesh.json").read_text(encoding="utf-8"))
    fixture = here.parent / "CompatibilityValidation" / "Fixtures" / "native-model-bind.json"
    bind = json.loads(fixture.read_text(encoding="utf-8-sig"))
    with args.bundle.open("rb") as reader:
        sha = hashlib.file_digest(reader, "sha256").hexdigest()
    if sha != catalog["bundleSha256"] or sha != bind["source"]["sha256"]:
        raise ValueError("Bundle does not match the contact indices and native binding fixture.")

    render, = [row for row in bind["renderers"] if row["path"] == catalog["mesh"]]
    environment = UnityPy.load(str(args.bundle))
    mesh, = [obj.read() for obj in environment.objects
             if obj.type.name == "Mesh" and obj.path_id == catalog["meshPathId"]]
    skin = MeshHandler(mesh)
    skin.process()
    count = catalog["sourceVertexCount"]
    matrices = [matrix_values(pose) for pose in mesh.m_BindPose]
    if (mesh.m_Name != catalog["mesh"] or
            any(len(column) != count for column in (skin.m_Vertices, skin.m_BoneIndices, skin.m_BoneWeights)) or
            matrices != [bone["matrix"] for bone in render["bones"]]):
        raise ValueError("Unexpected mesh layout or binding.")

    paths = [bone["path"] for bone in render["bones"]]
    groups = {name: [i for start, length in spans for i in range(start, start + length)]
              for name, spans in catalog["groups"].items()}
    vertices = {}
    for index in sorted(set().union(*groups.values())):
        influences = [(bone, weight) for bone, weight in
                      zip(skin.m_BoneIndices[index], skin.m_BoneWeights[index]) if weight > 0]
        if not math.isclose(sum(weight for _, weight in influences), 1, abs_tol=1e-6):
            raise ValueError(f"Invalid skin weights for vertex {index}.")
        if not all(0 <= bone < len(paths) for bone, _ in influences):
            raise ValueError(f"Invalid bone index for vertex {index}.")
        vertices[index] = [skin.m_Vertices[index], influences]

    result = dict(bundleSha256=sha, mesh=mesh.m_Name, meshPathId=catalog["meshPathId"],
                  sourceVertexCount=count, bonePaths=paths, bindMatrices=matrices,
                  groups=groups, vertices=vertices)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, separators=(",", ":"), allow_nan=False), encoding="utf-8")
    print(f"Exported {len(vertices)} contact vertices from {mesh.m_Name}.")


if __name__ == "__main__":
    main()
