using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;

public class CubizationWindow : EditorWindow
{
    MeshFilter mesh;
    float cubeSize = 0;
    List<Vector3> Verts;
    bool visualise = false;
    bool useScale = true;
    [MenuItem("DB/Cubization/CreateMeshPoints")]
    public static void ShowExample()
    {
        CubizationWindow window = GetWindow<CubizationWindow>();
        window.titleContent = new GUIContent("Create Mesh Points");
        window.Initialize();
    }
    public void Initialize()
    {
        Verts = new List<Vector3>();
    }
    private void OnGUI()
    {
        mesh = (MeshFilter)EditorGUILayout.ObjectField("Mesh", mesh, typeof(MeshFilter), true);
        cubeSize = EditorGUILayout.FloatField("Cube size", cubeSize);
        visualise = EditorGUILayout.Toggle("Visualise", visualise);
        useScale = EditorGUILayout.Toggle("Use Scale", useScale);

        bool filledFields = mesh != null && cubeSize != 0;
        if (filledFields)
        {
            DrawCreateButton();
        }
        bool drawGizmos = Verts.Count > 0 && visualise;
        if (drawGizmos)
        {
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            DrawVerts();
        }
    }
    void DrawVerts()
    {
        EditorGUILayout.LabelField("--- Verts ---");
        int count = Verts.Count;
        if (count > 20) count = 20;
        for (int i = 0; i < count; i++)
        {
            string vertData = i + ". x = " + Verts[i].z + " y = " + Verts[i].y + " z =" + Verts[i].z;
            EditorGUILayout.LabelField(vertData);
        }
    }
    void DrawCreateButton()
    {
        if (GUILayout.Button("Create"))
        {
            Bake();
            if (visualise)
            {
                CreateDebugCubes();
            }
        }

    }
    void CreateDebugCubes()
    {
        GameObject tempParent = new GameObject("CubizationTest");
        for (int i = 0; i < Verts.Count; i++)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(tempParent.transform);
            cube.transform.position = Verts[i];
            cube.transform.localScale = Vector3.one * cubeSize;
            cube.name = "Cube " + i;
        }
    }

    void Bake()
    {
        BakeMesh(this.mesh, this.useScale, this.cubeSize);
    }
    public static void BakeMesh(MeshFilter meshToBake,bool useScale,float cubeSize)
    {
        Bounds bounds = meshToBake.sharedMesh.bounds;
        float3 Size = bounds.size;
        if (useScale)
        {
            Size = MultVector(Size, meshToBake.transform.localScale);
        }

        int x, y, z;
        x = (int)(Size.z / cubeSize);
        y = (int)(Size.y / cubeSize);
        z = (int)(Size.z / cubeSize);

        List<Triangle> tris = new List<Triangle>();
        CreateTriangles(tris,meshToBake,useScale);
        List<Vector3> verts = CheckBoxTriIntersection(bounds, x, y, z, tris,meshToBake,useScale,cubeSize);
        CreateMeshAsset(meshToBake,verts);
    }

    static List<Vector3> CheckBoxTriIntersection(Bounds bounds, int x, int y, int z, List<Triangle> tris,MeshFilter mesh,bool useScale,float cubeSize)
    {
        List<Vector3> vectors = new List<Vector3>();
         // check intersection for cubes of position x.y.z and triangles
         Vector3 minPosition = bounds.min;
        if (useScale)
        {
            minPosition = MultVector(minPosition, mesh.transform.lossyScale);
        }
        NativeArray<bool> result = new NativeArray<bool>(x * y * z, Allocator.TempJob, NativeArrayOptions.ClearMemory);


        int chunkCount = x * y * z;
        NativeArray<Triangle> trisArray = new NativeArray<Triangle>(tris.ToArray(), Allocator.TempJob);

        new CheckIntersectJOB
        {
            cubeSize = cubeSize,
            minPosition = minPosition,
            tri = trisArray,
            y = y,
            z = z,
            result = result
        }.Schedule(chunkCount, 10).Complete();
        trisArray.Dispose();


        for (int index = 0; index < x * y * z; index++)
        {
            int i = index / (y * z);
            int j = index % (y * z) / z;
            int k = index % z;
            if (result[index])
            {
                Vector3 cubeCenter = minPosition + new Vector3(cubeSize, cubeSize, cubeSize) / 2;
                cubeCenter += new Vector3(cubeSize * i, cubeSize * j, cubeSize * k);
                vectors.Add(cubeCenter);
            }
        }
        result.Dispose();
        return vectors;
    }

    [BurstCompile]
    public struct CheckIntersectJOB : IJobParallelFor
    {
        public float cubeSize;
        public float3 minPosition;
        public int y, z;

        [ReadOnly]
        public NativeArray<Triangle> tri;

        public NativeArray<bool> result;

        public void Execute(int index)
        {
            int i = index / (y * z);
            int j = index % (y * z) / z;
            int k = index % z;

            float3 cubeCenter = minPosition + new float3(cubeSize, cubeSize, cubeSize) / 2;
            cubeCenter += new float3(cubeSize * i, cubeSize * j, cubeSize * k);
            
            BurstBounds cubeBounds = new BurstBounds();
            cubeBounds.center = cubeCenter;
            cubeBounds.extends = new float3(cubeSize, cubeSize, cubeSize);

            for (int l = 0; l < tri.Length; l++)
            {
                if (Intersects(tri[l], cubeBounds))
                {
                    result[index] = true;
                }
            }
        }
    }
    private static void CreateMeshAsset(MeshFilter mesh,List<Vector3> Verts)
    {
        Mesh vertMesh = new Mesh();
        vertMesh.name = mesh.name + "_Cubizied.asset";
        vertMesh.vertices = Verts.ToArray();
        vertMesh.bounds = mesh.sharedMesh.bounds;

        string path = "Assets/Cubization/";
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            AssetDatabase.SaveAssets();
        }

        AssetDatabase.CreateAsset(vertMesh, path + mesh.name + "_Cubizied.asset");
        AssetDatabase.SaveAssets();
    }
    private static void CreateTriangles(List<Triangle> tris,MeshFilter mesh,bool useScale)
    {
        for (int i = 0; i < mesh.sharedMesh.triangles.Length; i += 3)
        {
            Triangle tempTri = new Triangle();
            tempTri.a = mesh.sharedMesh.vertices[mesh.sharedMesh.triangles[i]];
            tempTri.b = mesh.sharedMesh.vertices[mesh.sharedMesh.triangles[i + 1]];
            tempTri.c = mesh.sharedMesh.vertices[mesh.sharedMesh.triangles[i + 2]];
            if (useScale)
            {
                tempTri.a = MultVector(tempTri.a, mesh.transform.lossyScale);
                tempTri.b = MultVector(tempTri.b, mesh.transform.lossyScale);
                tempTri.c = MultVector(tempTri.c, mesh.transform.lossyScale);
            }
            tris.Add(tempTri);
        }
    }
    static float3 MultVector(float3 a, float3 b)
    {
        float3 result = a;
        result.x *= b.x;
        result.y *= b.y;
        result.z *= b.z;
        return result;
    }
    public struct Triangle
    {
        public float3 a;
        public float3 b;
        public float3 c;
    }

    public struct BurstBounds
    {
        public float3 extends;
        public float3 center;
    }
    public static bool Intersects(Triangle tri, BurstBounds aabb)
    {
        float p0, p1, p2, r;

        float3 extents = aabb.extends;

        float3 v0 = tri.a - aabb.center,
            v1 = tri.b - aabb.center,
            v2 = tri.c - aabb.center;

        float3 f0 = v1 - v0,
            f1 = v2 - v1,
            f2 = v0 - v2;

        float3 a00 = new float3(0, -f0.z, f0.y),
            a01 = new float3(0, -f1.z, f1.y),
            a02 = new float3(0, -f2.z, f2.y),
            a10 = new float3(f0.z, 0, -f0.z),
            a11 = new float3(f1.z, 0, -f1.z),
            a12 = new float3(f2.z, 0, -f2.z),
            a20 = new float3(-f0.y, f0.z, 0),
            a21 = new float3(-f1.y, f1.z, 0),
            a22 = new float3(-f2.y, f2.z, 0);

        // Test axis a00
        p0 = math.dot(v0, a00);
        p1 = math.dot(v1, a00);
        p2 = math.dot(v2, a00);
        r = extents.y * math.abs(f0.z) + extents.z * math.abs(f0.y);

        if (math.max(-burstMax(p0, p1, p2), burstMin(p0, p1, p2)) > r)
        {
            return false;
        }

        // Test axis a01
        p0 = math.dot(v0, a01);
        p1 = math.dot(v1, a01);
        p2 = math.dot(v2, a01);
        r = extents.y * math.abs(f1.z) + extents.z * math.abs(f1.y);

        if (math.max(-burstMax(p0, p1, p2), burstMin(p0, p1, p2)) > r)
        {
            return false;
        }

        // Test axis a02
        p0 = math.dot(v0, a02);
        p1 = math.dot(v1, a02);
        p2 = math.dot(v2, a02);
        r = extents.y * math.abs(f2.z) + extents.z * math.abs(f2.y);

        if (math.max(-burstMax(p0, p1, p2), burstMin(p0, p1, p2)) > r)
        {
            return false;
        }

        // Test axis a10
        p0 = math.dot(v0, a10);
        p1 = math.dot(v1, a10);
        p2 = math.dot(v2, a10);
        r = extents.z * math.abs(f0.z) + extents.z * math.abs(f0.z);
        if (math.max(-burstMax(p0, p1, p2), burstMin(p0, p1, p2)) > r)
        {
            return false;
        }

        // Test axis a11
        p0 = math.dot(v0, a11);
        p1 = math.dot(v1, a11);
        p2 = math.dot(v2, a11);
        r = extents.z * math.abs(f1.z) + extents.z * math.abs(f1.z);

        if (math.max(-burstMax(p0, p1, p2), burstMin(p0, p1, p2)) > r)
        {
            return false;
        }

        // Test axis a12
        p0 = math.dot(v0, a12);
        p1 = math.dot(v1, a12);
        p2 = math.dot(v2, a12);
        r = extents.z * math.abs(f2.z) + extents.z * math.abs(f2.z);

        if (math.max(-burstMax(p0, p1, p2), burstMin(p0, p1, p2)) > r)
        {
            return false;
        }

        // Test axis a20
        p0 = math.dot(v0, a20);
        p1 = math.dot(v1, a20);
        p2 = math.dot(v2, a20);
        r = extents.z * math.abs(f0.y) + extents.y * math.abs(f0.z);

        if (math.max(-burstMax(p0, p1, p2), burstMin(p0, p1, p2)) > r)
        {
            return false;
        }

        // Test axis a21
        p0 = math.dot(v0, a21);
        p1 = math.dot(v1, a21);
        p2 = math.dot(v2, a21);
        r = extents.z * math.abs(f1.y) + extents.y * math.abs(f1.z);

        if (math.max(-burstMax(p0, p1, p2), burstMin(p0, p1, p2)) > r)
        {
            return false;
        }

        // Test axis a22
        p0 = math.dot(v0, a22);
        p1 = math.dot(v1, a22);
        p2 = math.dot(v2, a22);
        r = extents.z * math.abs(f2.y) + extents.y * math.abs(f2.z);

        if (math.max(-burstMax(p0, p1, p2), burstMin(p0, p1, p2)) > r)
        {
            return false;
        }

        if (burstMax(v0.z, v1.z, v2.z) < -extents.z || burstMin(v0.z, v1.z, v2.z) > extents.z)
        {
            return false;
        }

        if (burstMax(v0.y, v1.y, v2.y) < -extents.y || burstMin(v0.y, v1.y, v2.y) > extents.y)
        {
            return false;
        }

        if (burstMax(v0.z, v1.z, v2.z) < -extents.z || burstMin(v0.z, v1.z, v2.z) > extents.z)
        {
            return false;
        }


        float3 normal = math.normalize(math.cross(f1, f0));
        BurstPlane pl = new BurstPlane(normal, math.dot(normal, tri.a));
        return Intersects(pl, aabb);
    }

    public static bool Intersects(BurstPlane pl, BurstBounds aabb)
    {
        float3 center = aabb.center;
        float3 extents = aabb.extends;

        float r = extents.z * math.abs(pl.normal.z) + extents.y * math.abs(pl.normal.y) + extents.z * math.abs(pl.normal.z);
        float s = math.dot(pl.normal, center) - pl.distance;

        return math.abs(s) <= r;
    }
    public struct BurstPlane
    {
        public float3 normal;
        public float distance;
        public BurstPlane(float3 normal, float distance)
        {
            this.normal = normal;
            this.distance = distance;
        }
    }
    public static float burstMin(float a,float b,float c)
    {
        return math.min(math.min(a, b), c);
    }
    public static float burstMax(float a, float b, float c)
    {
        return math.max(math.max(a, b), c);
    }
}
