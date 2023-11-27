using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Zombie1111_uDestruction
{
    public static class FractureHelperFunc
    {
        public static Color SetAlpha(this Color color, float value)
        {
            return new Color(color.r, color.g, color.b, value);
        }

        public static ISet<T> ToSet<T>(this IEnumerable<T> iEnumerable)
        {
            return new HashSet<T>(iEnumerable);
        }

        public static T GetOrAddComponent<T>(this Component c) where T : Component
        {
            return c.gameObject.GetOrAddComponent<T>();
        }

        public static Component GetOrAddComponent(this GameObject go, Type componentType)
        {
            var result = go.GetComponent(componentType);
            return result == null ? go.AddComponent(componentType) : result;
        }

        public static T GetOrAddComponent<T>(this GameObject go) where T : Component
        {
            return GetOrAddComponent(go, typeof(T)) as T;
        }

        public static Vector3 SetX(this Vector3 vector3, float x)
        {
            return new Vector3(x, vector3.y, vector3.z);
        }

        public static Vector3 SetY(this Vector3 vector3, float y)
        {
            return new Vector3(vector3.x, y, vector3.z);
        }

        public static Vector3 SetZ(this Vector3 vector3, float z)
        {
            return new Vector3(vector3.x, vector3.y, z);
        }

        public static Vector3 Multiply(this Vector3 vectorA, Vector3 vectorB)
        {
            return Vector3.Scale(vectorA, vectorB);
        }

        public static Vector3 Abs(this Vector3 vector)
        {
            var x = Mathf.Abs(vector.x);
            var y = Mathf.Abs(vector.y);
            var z = Mathf.Abs(vector.z);
            return new Vector3(x, y, z);
        }

        private static float SignedVolumeOfTriangle(Vector3 p1, Vector3 p2, Vector3 p3)
        {
            float v321 = p3.x * p2.y * p1.z;
            float v231 = p2.x * p3.y * p1.z;
            float v312 = p3.x * p1.y * p2.z;
            float v132 = p1.x * p3.y * p2.z;
            float v213 = p2.x * p1.y * p3.z;
            float v123 = p1.x * p2.y * p3.z;
            return (1.0f / 6.0f) * (-v321 + v231 + v312 - v132 - v213 + v123);
        }

        public static float Volume(this Mesh mesh)
        {
            float volume = 0;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var p1 = vertices[triangles[i + 0]];
                var p2 = vertices[triangles[i + 1]];
                var p3 = vertices[triangles[i + 2]];
                volume += SignedVolumeOfTriangle(p1, p2, p3);
            }
            return Mathf.Abs(volume);
        }

        public static Vector3[] GetVertices(this Bounds bounds) => new[]
        {
            new Vector3(bounds.center.x, bounds.center.y, bounds.center.z) + new Vector3(-bounds.extents.x, -bounds.extents.y, -bounds.extents.z),
            new Vector3(bounds.center.x, bounds.center.y, bounds.center.z) + new Vector3(bounds.extents.x, -bounds.extents.y, -bounds.extents.z),
            new Vector3(bounds.center.x, bounds.center.y, bounds.center.z) + new Vector3(-bounds.extents.x, -bounds.extents.y, bounds.extents.z),
            new Vector3(bounds.center.x, bounds.center.y, bounds.center.z) + new Vector3(bounds.extents.x, -bounds.extents.y, bounds.extents.z),
            new Vector3(bounds.center.x, bounds.center.y, bounds.center.z) + new Vector3(-bounds.extents.x, bounds.extents.y, -bounds.extents.z),
            new Vector3(bounds.center.x, bounds.center.y, bounds.center.z) + new Vector3(bounds.extents.x, bounds.extents.y, -bounds.extents.z),
            new Vector3(bounds.center.x, bounds.center.y, bounds.center.z) + new Vector3(-bounds.extents.x, bounds.extents.y, bounds.extents.z),
            new Vector3(bounds.center.x, bounds.center.y, bounds.center.z) + new Vector3(bounds.extents.x, bounds.extents.y, bounds.extents.z),
        };

        public static Vector3 Min(this Vector3 vectorA, Vector3 vectorB)
        {
            return Vector3.Min(vectorA, vectorB);
        }

        public static Vector3 Max(this Vector3 vectorA, Vector3 vectorB)
        {
            return Vector3.Max(vectorA, vectorB);
        }

        public static Bounds ToBounds(this Vector3[] vertices)
        {
            var min = Vector3.one * float.MaxValue;
            var max = Vector3.one * float.MinValue;

            for (int i = 0; i < vertices.Length; i++)
            {
                min = vertices[i].Min(min);
                max = vertices[i].Max(max);
            }

            return new Bounds((max - min) / 2 + min, max - min);
        }

        public static Bounds ToBounds(this IEnumerable<Vector3> vertices)
        {
            return vertices.ToArray().ToBounds();
        }

        public static Vector3 TransformPoint(this Transform t, Vector3 position, Transform dest)
        {
            var world = t.TransformPoint(position);
            return dest.InverseTransformPoint(world);
        }

        public static Bounds TransformBounds(this Transform from, Transform to, Bounds bounds)
        {
            return bounds.GetVertices()
                .Select(bv => from.transform.TransformPoint(bv, to.transform))
                .ToBounds();
        }

        public static Bounds ConvertBoundsWithMatrix(Bounds bounds, Matrix4x4 matrix)
        {
            Vector3[] vertices = bounds.GetVertices();

            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            }

            return vertices.ToBounds();
        }

        public static Bounds GetCompositeMeshBounds(Mesh[] meshes)
        {
            var bounds = meshes
                .Select(mesh =>
                {
                    return mesh.bounds;
                })
                .Where(b => b.size != Vector3.zero)
                .ToArray();

            if (bounds.Length == 0)
                return new Bounds();

            if (bounds.Length == 1)
                return bounds[0];

            var compositeBounds = bounds[0];

            for (var i = 1; i < bounds.Length; i++)
            {
                compositeBounds.Encapsulate(bounds[i]);
            }

            return compositeBounds;
        }

        public static Vector3[] ConvertPositionsWithMatrix(Vector3[] localPoss, Matrix4x4 lTwMat)
        {
            for (int i = localPoss.Length - 1; i >= 0; i -= 1)
            {
                //localPoss[i] = localTrans.TransformPoint(localPoss[i]);
                localPoss[i] = lTwMat.MultiplyPoint(localPoss[i]);
            }

            return localPoss;
        }

        public static Vector3[] ConvertDirectionsWithMatrix(Vector3[] localDirs, Matrix4x4 lTwMat)
        {
            for (int i = localDirs.Length - 1; i >= 0; i -= 1)
            {
                localDirs[i] = lTwMat.MultiplyVector(localDirs[i]);
            }

            return localDirs;
        }

        public static Vector3[] ConvertPositionsToWorldspace_noScale(Vector3[] localposs, Transform trans)
        {
            for (int i = 0; i < localposs.Length; i++)
            {
                localposs[i] = (trans.rotation * localposs[i]) + trans.position;
            }

            return localposs;
        }

        /// <summary>
        /// Returns true if the mesh is valid for fracturing
        /// </summary>
        /// <param name="mesh"></param>
        /// <returns></returns>
        public static bool IsMeshValid(Mesh mesh, float EPSILON = 0.0001f)
        {
            if (mesh == null)
            {
                return false;
            }

            Vector3[] vertices = mesh.vertices;
            int trisCount = mesh.triangles.Length;
            if (vertices == null || vertices.Length < 4 || trisCount < 7 || trisCount % 3 != 0)
            {
                return false;
            }

            return FindInitialHullIndices(mesh.vertices.ToList());

            bool FindInitialHullIndices(List<Vector3> points)
            {
                var count = points.Count;

                for (int i0 = 0; i0 < count - 3; i0++)
                {
                    for (int i1 = i0 + 1; i1 < count - 2; i1++)
                    {
                        var p0 = points[i0];
                        var p1 = points[i1];

                        if (AreCoincident(p0, p1)) continue;

                        for (int i2 = i1 + 1; i2 < count - 1; i2++)
                        {
                            var p2 = points[i2];

                            if (AreCollinear(p0, p1, p2)) continue;

                            for (int i3 = i2 + 1; i3 < count - 0; i3++)
                            {
                                var p3 = points[i3];

                                if (AreCoplanar(p0, p1, p2, p3)) continue;

                                return true;
                            }
                        }
                    }
                }

                return false;
            }

            bool AreCollinear(Vector3 a, Vector3 b, Vector3 c)
            {
                return Cross(c - a, c - b).magnitude <= EPSILON;
            }

            Vector3 Cross(Vector3 a, Vector3 b)
            {
                return new Vector3(
                    a.y * b.z - a.z * b.y,
                    a.z * b.x - a.x * b.z,
                    a.x * b.y - a.y * b.x);
            }

            bool AreCoplanar(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                var n1 = Cross(c - a, c - b);
                var n2 = Cross(d - a, d - b);

                var m1 = n1.magnitude;
                var m2 = n2.magnitude;

                return m1 <= EPSILON
                    || m2 <= EPSILON
                    || AreCollinear(Vector3.zero,
                        (1.0f / m1) * n1,
                        (1.0f / m2) * n2);
            }

            bool AreCoincident(Vector3 a, Vector3 b)
            {
                return (a - b).magnitude <= EPSILON;
            }

            //return true;
        }

        /// <summary>
        /// Returns 2 meshes, [0] is the one containing vertexIndexesToSplit. May remove tris at split edges if some tris vertics are in vertexIndexesToSplit and some not
        /// </summary>
        /// <param name="vertexIndexesToSplit"></param>
        /// <param name="originalMesh"></param>
        /// <returns></returns>
        public static List<Mesh> SplitMeshInTwo(HashSet<int> vertexIndexesToSplit, Mesh orginalMesh, bool doBones)
        {
            //setup
            int[] tris = orginalMesh.triangles;
            Vector3[] verts = orginalMesh.vertices;
            Vector3[] nors = orginalMesh.normals;
            Vector2[] uvs = orginalMesh.uv;
            BoneWeight[] bones = doBones == true ? orginalMesh.boneWeights : new BoneWeight[verts.Length];

            List<Vector3> splitVerA = new List<Vector3>();
            List<Vector3> splitNorA = new List<Vector3>();
            List<Vector2> splitUvsA = new List<Vector2>();
            List<int> splitTriA = new List<int>();
            List<BoneWeight> splitBonA = new List<BoneWeight>();

            List<Vector3> splitVerB = new List<Vector3>();
            List<Vector3> splitNorB = new List<Vector3>();
            List<Vector2> splitUvsB = new List<Vector2>();
            List<int> splitTriB = new List<int>();
            List<BoneWeight> splitBonB = new List<BoneWeight>();

            //split faces/mesh
            for (int i = 0; i < tris.Length; i += 3)
            {
                if (vertexIndexesToSplit.Contains(tris[i]) == true || vertexIndexesToSplit.Contains(tris[i + 1]) == true || vertexIndexesToSplit.Contains(tris[i + 2]) == true)
                {
                    splitTriA.Add(GetIndexOfVertex(verts[tris[i]], nors[tris[i]], uvs[tris[i]], bones[tris[i]], true));
                    splitTriA.Add(GetIndexOfVertex(verts[tris[i + 1]], nors[tris[i + 1]], uvs[tris[i + 1]], bones[tris[i + 1]], true));
                    splitTriA.Add(GetIndexOfVertex(verts[tris[i + 2]], nors[tris[i + 2]], uvs[tris[i + 2]], bones[tris[i + 2]], true));
                }
                else
                {
                    splitTriB.Add(GetIndexOfVertex(verts[tris[i]], nors[tris[i]], uvs[tris[i]], bones[tris[i]], false));
                    splitTriB.Add(GetIndexOfVertex(verts[tris[i + 1]], nors[tris[i + 1]], uvs[tris[i + 1]], bones[tris[i + 1]], false));
                    splitTriB.Add(GetIndexOfVertex(verts[tris[i + 2]], nors[tris[i + 2]], uvs[tris[i + 2]], bones[tris[i + 2]], false));
                }
            }

            //return the new meshes
            Mesh newMA = new Mesh();
            newMA.vertices = splitVerA.ToArray();
            newMA.normals = splitNorA.ToArray();
            newMA.uv = splitUvsA.ToArray();
            if (doBones == true) newMA.boneWeights = splitBonA.ToArray();
            newMA.triangles = splitTriA.ToArray();

            Mesh newMB = new Mesh();
            newMB.vertices = splitVerB.ToArray();
            newMB.normals = splitNorB.ToArray();
            newMB.uv = splitUvsB.ToArray();
            if (doBones == true) newMB.boneWeights = splitBonB.ToArray();
            newMB.triangles = splitTriB.ToArray();

            return new List<Mesh>() { newMA, newMB };


            int GetIndexOfVertex(Vector3 vPos, Vector3 vNormal, Vector2 vUv, BoneWeight vBone, bool checkSplitA)
            {
                if (checkSplitA == true)
                {
                    for (int i = 0; i < splitVerA.Count; i += 1)
                    {
                        //if already contains index return it
                        if (splitVerA[i] == vPos && splitNorA[i] == vNormal) return i;
                    }

                    //if not contain index, create new
                    splitVerA.Add(vPos);
                    splitNorA.Add(vNormal);
                    splitUvsA.Add(vUv);
                    if (doBones == true) splitBonA.Add(vBone);
                    return splitVerA.Count - 1;
                }
                else
                {
                    for (int i = 0; i < splitVerB.Count; i += 1)
                    {
                        //if already contains index return it
                        if (splitVerB[i] == vPos && splitNorB[i] == vNormal) return i;
                    }

                    //if not contain index, create new
                    splitVerB.Add(vPos);
                    splitNorB.Add(vNormal);
                    splitUvsB.Add(vUv);
                    if (doBones == true) splitBonB.Add(vBone);
                    return splitVerB.Count - 1;
                }
            }
        }

        /// <summary>
        /// Returns all vertics connected to the given vertexIndex
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="vertexIndex"></param>
        /// <param name="verDisTol">All vertics within this radius will count as the same vertex</param>
        /// <returns></returns>
        public static HashSet<int> GetConnectedVertexIndexes(Mesh mesh, int vertexIndex, float verDisTol = 0.0001f)
        {
            //setup
            HashSet<int> conV = new HashSet<int>();
            Vector3[] vers = mesh.vertices;
            int[] tris = mesh.triangles;
            List<int> trisToSearch = GetAllTrisAtPos(vers[vertexIndex]);

            //get all connected
            for (int i = 0; i < trisToSearch.Count; i += 1)
            {
                if (conV.Add(tris[trisToSearch[i]]) == true) trisToSearch.AddRange(GetAllTrisAtPos(vers[tris[trisToSearch[i]]]));
                if (conV.Add(tris[trisToSearch[i] + 1]) == true) trisToSearch.AddRange(GetAllTrisAtPos(vers[tris[trisToSearch[i] + 1]]));
                if (conV.Add(tris[trisToSearch[i] + 2]) == true) trisToSearch.AddRange(GetAllTrisAtPos(vers[tris[trisToSearch[i] + 2]]));
            }

            return conV;

            List<int> GetAllTrisAtPos(Vector3 pos)
            {
                List<int> trisAtPos = new List<int>();
                for (int i = 0; i < tris.Length; i += 3)
                {
                    //if (Vector3.Distance(vers[tris[i]], pos) < verDisTol || Vector3.Distance(vers[tris[i + 1]], pos) < verDisTol || Vector3.Distance(vers[tris[i + 2]], pos) < verDisTol) trisAtPos.Add(i);
                    if (Vector3.SqrMagnitude(vers[tris[i]] - pos) < verDisTol || Vector3.SqrMagnitude(vers[tris[i + 1]] - pos) < verDisTol || Vector3.SqrMagnitude(vers[tris[i + 2]] - pos) < verDisTol) trisAtPos.Add(i);
                }

                return trisAtPos;
            }
        }

        /// <summary>
        /// Returns all vertics that is close to the given position. (Always excluding vertex index vIndexToIgnore)
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="pos"></param>
        /// <param name="verDisTol"></param>
        /// <param name="vIndexToIgnore"></param>
        /// <returns></returns>
        public static List<int> GetAllVertexIndexesAtPos(Vector3[] vertics, Vector3 pos, float verDisTol = 0.0001f, int vIndexToIgnore = -1)
        {
            List<int> vAtPos = new();

            for (int i = 0; i < vertics.Length; i += 1)
            {
                if ((vertics[i] - pos).sqrMagnitude < verDisTol && i != vIndexToIgnore)
                {
                    vAtPos.Add(i);
                }
            }

            return vAtPos;
        }

        public static Mesh ConvertMeshWithMatrix(Mesh mesh, Matrix4x4 lTwMatrix)
        {
            mesh.SetVertices(ConvertPositionsWithMatrix(mesh.vertices, lTwMatrix));
            mesh.SetNormals(ConvertDirectionsWithMatrix(mesh.normals, lTwMatrix));
            return mesh;
        }

        /// <summary>
        /// Returns a float for each mesh that is each mesh size compared to each other. All floats added = 1.0f
        /// </summary>
        /// <param name="meshes"></param>
        /// <param name="useMeshBounds">If true, Mesh.bounds is used to get scales (Faster but less accurate)</param>
        /// <returns></returns>
        public static List<float> GetPerMeshScale(List<Mesh> meshes, bool useMeshBounds = false)
        {
            List<float> meshVolumes = new List<float>();
            float totalVolume = 0.0f;
            for (int i = 0; i < meshes.Count; i += 1)
            {
                if (useMeshBounds == false) meshVolumes.Add(meshes[i].Volume());
                else meshVolumes.Add(GetBoundingBoxVolume(meshes[i].bounds));
                totalVolume += meshVolumes[i];
            }

            float perMCost = 1.0f / totalVolume;
            for (int i = 0; i < meshes.Count; i += 1)
            {
                meshVolumes[i] = perMCost * meshVolumes[i];
            }

            return meshVolumes;
        }

        public static float GetBoundingBoxVolume(Bounds bounds)
        {
            // Calculate the volume using the size of the bounds
            float volume = bounds.size.x * bounds.size.y * bounds.size.z;

            return volume;
        }

        /// <summary>
        /// Combines the meshes and updates fracParts rendVertexIndexes list
        /// </summary>
        /// <param name="fracMeshes">Must have same lenght as fracParts</param>
        /// <param name="fracParts"></param>
        /// <returns></returns>
        public static Mesh CombineMeshes(List<Mesh> fracMeshes, ref FractureThis.FracParts[] fracParts)
        {
            List<Vector3> combinedVertices = new List<Vector3>();
            List<Vector3> combinedNormals = new List<Vector3>();
            List<Vector2> combinedUVs = new List<Vector2>();
            List<int> combinedTrianglesA = new List<int>();
            List<int> combinedTrianglesB = new List<int>();
            List<BoneWeight> combinedBones = new List<BoneWeight>();
            int vertexOffset = 0;

            for (int i = 0; i < fracMeshes.Count; i += 1)
            {
                Mesh mesh = fracMeshes[i];

                // Get the vertices, normals, and UVs from the small mesh
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Vector2[] uvs = mesh.uv;
                BoneWeight[] bones = mesh.boneWeights;

                //link combined vertices to fracture part
                for (int ii = 0; ii < vertices.Length; ii += 1)
                {
                    fracParts[i].rendVertexIndexes.Add(combinedVertices.Count + ii);
                }

                // Append the vertex data to the combined lists
                combinedVertices.AddRange(vertices);
                combinedNormals.AddRange(normals);
                combinedUVs.AddRange(uvs);
                combinedBones.AddRange(bones);

                int[] trianglesA = mesh.GetTriangles(0);
                int[] trianglesB = new int[0];
                if (mesh.subMeshCount > 1) trianglesB = mesh.GetTriangles(1);

                // Append the triangle data to the combined list, adjusting the indices with the vertex offset
                for (int j = 0; j < trianglesA.Length; j++)
                {
                    combinedTrianglesA.Add(trianglesA[j] + vertexOffset);
                }

                for (int j = 0; j < trianglesB.Length; j++)
                {
                    combinedTrianglesB.Add(trianglesB[j] + vertexOffset);
                }

                // Update the vertex offset for the next small mesh
                vertexOffset += vertices.Length;
            }

            Mesh combinedMesh = new Mesh();
            combinedMesh.vertices = combinedVertices.ToArray();
            combinedMesh.normals = combinedNormals.ToArray();
            combinedMesh.uv = combinedUVs.ToArray();
            combinedMesh.subMeshCount = 2;
            combinedMesh.SetTriangles(combinedTrianglesA.ToArray(), 0);
            combinedMesh.SetTriangles(combinedTrianglesB.ToArray(), 1);
            combinedMesh.boneWeights = combinedBones.ToArray();
            return combinedMesh;
        }

        public static Vector3 GetMedianPosition(Vector3[] positions)
        {
            Vector3 midPos = Vector3.zero;
            for (int i = 0; i < positions.Length; i += 1)
            {
                midPos += positions[i];
            }

            return midPos / positions.Length;
        }

        /// <summary>
        /// Performs a linecast for all positions between all positions
        /// </summary>
        /// <param name="poss"></param>
        /// <returns></returns>
        public static HashSet<Collider> LinecastsBetweenPositions(List<Vector3> poss)
        {
            RaycastHit nHit;
            HashSet<Collider> hits = new();
            for (int i = 1; i < poss.Count; i += 1)
            {
                for (int ii = 1; ii < poss.Count; ii += 1)
                {
                    if (ii == i) continue;

                    Physics.Linecast(poss[ii], poss[i], out nHit);
                    if (nHit.collider != null) hits.Add(nHit.collider);
                    //Physics.Linecast(poss[i], poss[ii], out nHit);
                    //if (nHit.collider != null) hits.Add(nHit.collider);
                }
            }

            return hits;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Draw line between all vertics in the worldspace mesh. 
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="drawNormals"></param>
        public static void Debug_drawMesh(Mesh mesh, bool drawNormals = false, float durration = 0.1f)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            // Draw lines between triangle vertices
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 v0 = vertices[triangles[i]];
                Vector3 v1 = vertices[triangles[i + 1]];
                Vector3 v2 = vertices[triangles[i + 2]];

                Debug.DrawLine(v0, v1, Color.red, durration);
                Debug.DrawLine(v1, v2, Color.green, durration);
                Debug.DrawLine(v2, v0, Color.blue, durration);
            }

            // Draw normals from each vertex
            if (drawNormals)
            {
                Vector3[] normals = mesh.normals;

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 vertex = vertices[i];
                    Vector3 normal = normals[i];

                    // Adjust the length of the normal line as needed
                    float normalLength = 1f;
                    Vector3 endPos = vertex + normal * normalLength;

                    Debug.DrawLine(vertex, endPos, Color.yellow, durration);
                }
            }
        }
    }
#endif
}