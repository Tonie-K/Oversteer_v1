using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Builds the visible, collidable ribbon surface for a sampled road centerline.</summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public sealed class RoadMeshGenerator : MonoBehaviour
{
    private Mesh roadMesh;
    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock propertyBlock;

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();
        meshRenderer = GetComponent<MeshRenderer>();
        EnsureMesh();
    }

    public void SetMaterial(Material material, Color tint)
    {
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        if (material != null) meshRenderer.sharedMaterial = material;
        propertyBlock ??= new MaterialPropertyBlock();
        propertyBlock.SetColor("_BaseColor", tint);
        propertyBlock.SetColor("_Color", tint);
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    public void Build(IReadOnlyList<Vector3> centerline, Vector3 startForward,
        Vector3 endForward, float width, float surfaceOffset, float textureMetersPerTile)
    {
        if (centerline == null || centerline.Count < 2) return;
        EnsureMesh();

        int pointCount = centerline.Count;
        var vertices = new Vector3[pointCount * 2];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[(pointCount - 1) * 6];
        float distance = 0f;

        for (int i = 0; i < pointCount; i++)
        {
            Vector3 tangent = GetTangent(centerline, i, startForward, endForward);
            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
            Vector3 center = centerline[i] + Vector3.up * surfaceOffset;
            vertices[i * 2] = transform.InverseTransformPoint(center - right * (width * 0.5f));
            vertices[i * 2 + 1] = transform.InverseTransformPoint(center + right * (width * 0.5f));

            if (i > 0) distance += Vector3.Distance(centerline[i - 1], centerline[i]);
            float v = distance / Mathf.Max(1f, textureMetersPerTile);
            uv[i * 2] = new Vector2(0f, v);
            uv[i * 2 + 1] = new Vector2(1f, v);

            if (i == pointCount - 1) continue;
            int triangleIndex = i * 6;
            int a = i * 2;
            int b = (i + 1) * 2;
            triangles[triangleIndex] = a;
            triangles[triangleIndex + 1] = b;
            triangles[triangleIndex + 2] = a + 1;
            triangles[triangleIndex + 3] = a + 1;
            triangles[triangleIndex + 4] = b;
            triangles[triangleIndex + 5] = b + 1;
        }

        roadMesh.Clear();
        roadMesh.indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        roadMesh.vertices = vertices;
        roadMesh.uv = uv;
        roadMesh.triangles = triangles;
        roadMesh.RecalculateNormals();
        roadMesh.RecalculateBounds();
        meshFilter.sharedMesh = roadMesh;
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = roadMesh;
    }

    private static Vector3 GetTangent(IReadOnlyList<Vector3> points, int index,
        Vector3 startForward, Vector3 endForward)
    {
        if (index == 0) return startForward.normalized;
        if (index == points.Count - 1) return endForward.normalized;
        Vector3 tangent = points[index + 1] - points[index - 1];
        tangent.y = 0f;
        return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : startForward.normalized;
    }

    private void EnsureMesh()
    {
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (meshCollider == null) meshCollider = GetComponent<MeshCollider>();
        if (roadMesh != null) return;
        roadMesh = new Mesh { name = "Pooled Road Surface" };
        roadMesh.MarkDynamic();
        meshFilter.sharedMesh = roadMesh;
    }

    private void OnDestroy()
    {
        if (roadMesh == null) return;
        if (Application.isPlaying) Destroy(roadMesh);
        else DestroyImmediate(roadMesh);
    }
}
