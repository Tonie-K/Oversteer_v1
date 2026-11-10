using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Builds one continuous raised edge barrier and mesh collider along a road edge.</summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public sealed class RoadBoundaryGenerator : MonoBehaviour
{
    private Mesh boundaryMesh;
    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock propertyBlock;
    private int side;

    public int Side => side;
    public RoadSegment Segment => GetComponentInParent<RoadSegment>();

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
        Vector3 endForward, float roadWidth, int side, float thickness, float height,
        float surfaceOffset, float textureMetersPerTile)
    {
        if (centerline == null || centerline.Count < 2) return;
        this.side = side < 0 ? -1 : 1;
        EnsureMesh();

        int pointCount = centerline.Count;
        var vertices = new Vector3[pointCount * 4];
        var uv = new Vector2[vertices.Length];
        var triangles = new List<int>((pointCount - 1) * 24 + 12);
        float distance = 0f;
        float halfThickness = thickness * 0.5f;

        for (int i = 0; i < pointCount; i++)
        {
            Vector3 tangent = GetTangent(centerline, i, startForward, endForward);
            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
            Vector3 edge = centerline[i] + right * (this.side * roadWidth * 0.5f);
            Vector3 innerBottom = edge + Vector3.up * surfaceOffset - right * (this.side * halfThickness);
            Vector3 outerBottom = edge + Vector3.up * surfaceOffset + right * (this.side * halfThickness);
            int vertex = i * 4;
            vertices[vertex] = transform.InverseTransformPoint(innerBottom);
            vertices[vertex + 1] = transform.InverseTransformPoint(outerBottom);
            vertices[vertex + 2] = transform.InverseTransformPoint(innerBottom + Vector3.up * height);
            vertices[vertex + 3] = transform.InverseTransformPoint(outerBottom + Vector3.up * height);

            if (i > 0) distance += Vector3.Distance(centerline[i - 1], centerline[i]);
            float along = distance / Mathf.Max(1f, textureMetersPerTile);
            uv[vertex] = new Vector2(0f, along);
            uv[vertex + 1] = new Vector2(1f, along);
            uv[vertex + 2] = new Vector2(0f, along);
            uv[vertex + 3] = new Vector2(1f, along);

            if (i == pointCount - 1) continue;
            int next = vertex + 4;

            // Top face.
            AddQuad(triangles, vertex + 2, next + 2, vertex + 3, next + 3);
            // Road-facing inner wall.
            AddQuad(triangles, vertex, next, vertex + 2, next + 2);
            // Outer wall.
            AddQuad(triangles, vertex + 1, vertex + 3, next + 1, next + 3);
        }

        // Close the first and last ends. Neighboring segments still share identical edge points.
        AddQuad(triangles, 0, 1, 2, 3);
        int last = (pointCount - 1) * 4;
        AddQuad(triangles, last + 1, last, last + 3, last + 2);

        boundaryMesh.Clear();
        boundaryMesh.indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        boundaryMesh.vertices = vertices;
        boundaryMesh.uv = uv;
        boundaryMesh.SetTriangles(triangles, 0, true);
        boundaryMesh.RecalculateNormals();
        boundaryMesh.RecalculateBounds();
        meshFilter.sharedMesh = boundaryMesh;
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = boundaryMesh;
    }

    private static void AddQuad(List<int> triangles, int a, int b, int c, int d)
    {
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
        triangles.Add(c); triangles.Add(b); triangles.Add(d);
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
        if (boundaryMesh != null) return;
        boundaryMesh = new Mesh { name = "Pooled Road Boundary" };
        boundaryMesh.MarkDynamic();
        meshFilter.sharedMesh = boundaryMesh;
    }

    private void OnDestroy()
    {
        if (boundaryMesh == null) return;
        if (Application.isPlaying) Destroy(boundaryMesh);
        else DestroyImmediate(boundaryMesh);
    }
}
