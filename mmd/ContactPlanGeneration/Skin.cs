using System.Text.Json;
using UnityEngine;

sealed class Skin
{
    readonly record struct Influence(int Bone, float Weight, Vector3 BindLocal);
    readonly record struct Vertex(int Id, Vector3 Source, Influence[] Influences);
    readonly Vertex[] vertices;
    readonly Dictionary<string, int[]> groups;
    readonly Matrix4x4[] matrices;
    readonly Vector3[] points;
    public string Name { get; }
    public string[] Paths { get; }

    public Skin(JsonElement input)
    {
        Name = input.GetProperty("mesh").GetString();
        Paths = input.GetProperty("bonePaths").EnumerateArray().Select(x => x.GetString()).ToArray();
        var bindPoses = input.GetProperty("bindMatrices").EnumerateArray().Select(Matrix).ToArray();
        vertices = input.GetProperty("vertices").EnumerateObject().Select(row =>
        {
            var value = row.Value;
            var p = new Vector3(value[0][0].GetSingle(), value[0][1].GetSingle(), value[0][2].GetSingle());
            var influences = value[1].EnumerateArray().Select(v =>
            {
                int bone = v[0].GetInt32();
                return new Influence(bone, v[1].GetSingle(), bindPoses[bone].MultiplyPoint3x4(p));
            }).ToArray();
            return new Vertex(int.Parse(row.Name), p, influences);
        }).ToArray();
        var index = vertices.Select((v, i) => (v.Id, i)).ToDictionary(p => p.Id, p => p.i);
        groups = input.GetProperty("groups").EnumerateObject().ToDictionary(row => row.Name,
            row => row.Value.EnumerateArray().Select(v => index[v.GetInt32()]).ToArray());
        matrices = new Matrix4x4[Paths.Length];
        points = new Vector3[vertices.Length];
    }

    public void UpdateMatrices(Transform[] bones)
    {
        for (int i = 0; i < matrices.Length; ++i)
            matrices[i] = bones[i].localToWorldMatrix;
        for (int i = 0; i < vertices.Length; ++i)
        {
            var p = Vector3.zero;
            foreach (var inf in vertices[i].Influences)
                p += matrices[inf.Bone].MultiplyPoint3x4(inf.BindLocal) * inf.Weight;
            if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z))
                throw new InvalidOperationException("Non-finite skinned vertex.");
            points[i] = p;
        }
    }

    public float ValidateBind(Matrix4x4 renderer) => vertices.Select((v, i) =>
        Vector3.Distance(renderer.MultiplyPoint3x4(v.Source), points[i])).Max();

    public Vector3 LowestPoint(string group)
    {
        var indices = groups[group];
        var point = points[indices[0]];
        foreach (int index in indices)
            if (points[index].y < point.y) point = points[index];
        return point;
    }

    static Matrix4x4 Matrix(JsonElement m)
    {
        float F(int i) => m[i].GetSingle();
        return new(new System.Numerics.Matrix4x4(F(0), F(4), F(8), F(12), F(1), F(5), F(9), F(13),
            F(2), F(6), F(10), F(14), F(3), F(7), F(11), F(15)));
    }
}
