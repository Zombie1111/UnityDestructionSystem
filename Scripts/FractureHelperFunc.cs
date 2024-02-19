using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Unity.VisualScripting.FullSerializer;




#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Zombie1111_uDestruction
{
    public static class FractureHelperFunc
    {
        public static void SetVelocityAtPosition(Vector3 targetVelocity, Vector3 positionOfForce, Rigidbody rb)
        {
            //rb.AddForceAtPosition(rb.mass * (targetVelocity - rb.velocity) / Time.fixedDeltaTime, positionOfForce, ForceMode.Force);
            rb.AddForceAtPosition(targetVelocity - rb.velocity, positionOfForce, ForceMode.VelocityChange);
        }

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
            Vector3 center = matrix.MultiplyPoint(bounds.center);
            Vector3 size = matrix.MultiplyVector(bounds.size);

            return new Bounds(center, size);

            //Vector3[] vertices = bounds.GetVertices();
            //
            //for (int i = 0; i < vertices.Length; i++)
            //{
            //    vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            //}
            //
            //return vertices.ToBounds();
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
            for (int i = localPoss.Length - 1; i >= 0; i--)
            {
                //localPoss[i] = localTrans.TransformPoint(localPoss[i]);
                localPoss[i] = lTwMat.MultiplyPoint(localPoss[i]);
            }

            return localPoss;
        }

        public static Vector3[] ConvertDirectionsWithMatrix(Vector3[] localDirs, Matrix4x4 lTwMat)
        {
            for (int i = localDirs.Length - 1; i >= 0; i--)
            {
                localDirs[i] = lTwMat.MultiplyVector(localDirs[i]);
            }

            return localDirs;
        }

        public static Vector3[] ConvertPositionsWithMatrix_noScale(Vector3[] localposs, Matrix4x4 lTwMat)
        {
            Vector3 pos = lTwMat.GetPosition();
            for (int i = 0; i < localposs.Length; i++)
            {
                localposs[i] = (lTwMat.rotation * localposs[i]) + pos;
            }

            return localposs;
        }

        /// <summary>
        /// Adds the float to the list (low to high) and returns the index the item was inserted at
        /// </summary>
        /// <param name="list"></param>
        /// <param name="item"></param>
        /// <returns></returns>
        public static int InsertSorted(List<float> list, float item)
        {
            int index = list.BinarySearch(item);
            if (index < 0)
                index = ~index; // Bitwise complement to get the insert position

            if (list.Count > 1 && index == 0) index = 1;
            list.Insert(index, item);
            return index;
        }

        /// <summary>
        /// Returns the closest position index
        /// </summary>
        /// <param name="positions">The positions to check against</param>
        /// <param name="pos">Closest position to this</param>
        /// <param name="preExitTolerance">If point is closer than this, return it without checking rest</param>
        /// <returns></returns>
        public static int GetClosestPointInArray(Vector3[] positions, Vector3 pos, float preExitTolerance = 0.01f)
        {
            int bestI = 0;
            float bestD = float.MaxValue;
            float currentD;

            for (int i = 0; i < positions.Length; i += 1)
            {
                currentD = (pos - positions[i]).sqrMagnitude;

                if (currentD < bestD)
                {
                    bestD = currentD;
                    bestI = i;

                    if (currentD < preExitTolerance) break;
                }
            }

            return bestI;
        }

        public static Vector3 ClosestPointOnDisc(Vector3 point, Vector3 discCenter, Vector3 discNormal, float discRadius, float discRadiusSquared)
        {
            //Project the point onto the disc normal plane, discNormal wont be used after this so we can reuse it
            discNormal = point - Vector3.Dot(point - discCenter, discNormal) * discNormal;

            //Move the projected point into the disc radius
            if ((discNormal - discCenter).sqrMagnitude > discRadiusSquared) discNormal = discCenter + (discNormal - discCenter).normalized * discRadius;
            
            return discNormal;
        }

        public static Vector3 ClosestPointOnLine(Vector3 position, Vector3 linePosition, Vector3 lineDirection)
        {
            return linePosition + Mathf.Clamp01(Vector3.Dot(position - linePosition, lineDirection) / lineDirection.sqrMagnitude) * lineDirection;
        }

        public static Vector3 ClosestPointOnLine_doubleSided(Vector3 position, Vector3 linePosition, Vector3 lineDirection)
        {
            return linePosition + (Vector3.Dot(position - linePosition, lineDirection) / lineDirection.sqrMagnitude) * lineDirection;
        }

        /// <summary>
        /// Returns the closest triangel index on the mesh
        /// </summary>
        /// <param name="meshWorldVers">The mesh vertics in world space</param>
        /// <param name="meshTris">The mesh triangels</param>
        /// <param name="pos">Closest point to this</param>
        /// <param name="preExitTolerance">If point is closer than this, return it without checking rest</param>
        /// <returns></returns>
        public static int GetClosestPointOnMesh(Vector3[] meshWorldVers, int[] meshTris, Vector3 pos, float preExitTolerance = 0.01f)
        {
            int bestI = 0;
            float bestD = float.MaxValue;
            float currentD;

            for (int i = 0; i < meshTris.Length; i += 3)
            {
                currentD = (pos - ClosestPointOnTriangle(meshWorldVers[meshTris[i]], meshWorldVers[meshTris[i + 1]], meshWorldVers[meshTris[i + 2]], pos)).sqrMagnitude;

                if (currentD < bestD)
                {
                    bestD = currentD;
                    bestI = i;

                    if (currentD < preExitTolerance) break;
                }
            }

            return bestI;
        }

        /// <summary>
        /// Returns the closest triangel index on the mesh
        /// </summary>
        /// <param name="meshWorldVers">The mesh vertics in world space</param>
        /// <param name="meshTris">The mesh triangels</param>
        /// <param name="pos">Closest triangel to these</param>
        /// <param name="preExitTolerance">If point is closer than this, return it without checking rest</param>
        /// <returns></returns>
        public static int GetClosestTriOnMesh(Vector3[] meshWorldVers, int[] meshTris, Vector3[] poss, float preExitTolerance = 0.01f)
        //public static int GetClosestTriOnMesh(Vector3[] meshWorldVers, int[] meshVerIds, int[] meshTris, Vector3[] poss, int id, float preExitTolerance = 0.01f)
        {
            int bestI = 0;
            float bestD = float.MaxValue;
            float currentD;

            for (int i = 0; i < meshTris.Length; i += 3)
            {
                //if (Vector3.Dot(meshWorldNors[meshTris[i]].normalized, nor.normalized) < 0.5f) continue;
                //Debug.DrawLine(meshWorldVers[meshTris[i]], meshWorldVers[meshTris[i]] + (meshWorldNors[meshTris[i]].normalized * 0.01f), Color.red, 10.0f);
                //Debug.DrawLine(poss[0], poss[0] + (nor * 0.01f), Color.yellow, 10.0f);

                //if (meshVerIds[meshTris[i]] != id) continue;

                currentD = 0.0f;
                for (int ii = 0; ii < poss.Length; ii += 1) currentD += (poss[ii] - ClosestPointOnTriangle(meshWorldVers[meshTris[i]], meshWorldVers[meshTris[i + 1]], meshWorldVers[meshTris[i + 2]], poss[ii])).sqrMagnitude;

                if (currentD < bestD)
                {
                    bestD = currentD;
                    bestI = i;

                    if (currentD < preExitTolerance) break;
                }
            }

            return bestI;
        }

        public static Mesh MergeVerticesInMesh(Mesh originalMesh)
        {
            var verts = originalMesh.vertices;
            var normals = originalMesh.normals;
            var uvs = originalMesh.uv;
            Dictionary<Vector3, int> duplicateHashTable = new Dictionary<Vector3, int>();
            List<int> newVerts = new List<int>();
            int[] map = new int[verts.Length];

            //create mapping and find duplicates, dictionaries are like hashtables, mean fast
            for (int i = 0; i < verts.Length; i++)
            {
                if (!duplicateHashTable.ContainsKey(verts[i]))
                {
                    duplicateHashTable.Add(verts[i], newVerts.Count);
                    map[i] = newVerts.Count;
                    newVerts.Add(i);
                }
                else
                {
                    map[i] = duplicateHashTable[verts[i]];
                }
            }

            //create new vertices
            var verts2 = new Vector3[newVerts.Count];
            var normals2 = new Vector3[newVerts.Count];
            var uvs2 = new Vector2[newVerts.Count];
            for (int i = 0; i < newVerts.Count; i++)
            {
                int a = newVerts[i];
                verts2[i] = verts[a];
                normals2[i] = normals[a];
                uvs2[i] = uvs[a];
            }

            //map the triangle to the new vertices
            int subMeshCount = originalMesh.subMeshCount;
            for (int submeshIndex = 0; submeshIndex < subMeshCount; submeshIndex++)
            {
                var tris = originalMesh.GetTriangles(submeshIndex);
                for (int i = 0; i < tris.Length; i++)
                {
                    tris[i] = map[tris[i]];
                }
                originalMesh.SetTriangles(tris, submeshIndex);
            }

            originalMesh.SetVertices(verts2);
            originalMesh.SetNormals(normals2);
            originalMesh.uv = uvs2;

            return originalMesh;
        }

        public static void MergeSimilarVectors(ref List<Vector3> vectors, float tolerance = 0.001f)
        {
            for (int i = 0; i < vectors.Count; i += 1)
            {
                for (int ii = i + 1; ii < vectors.Count; ii += 1)
                {
                    if ((vectors[i] - vectors[ii]).sqrMagnitude > tolerance) continue;

                    vectors.RemoveAt(ii);
                }
            }
        }

        /// <summary>
        /// Returns 1 if dir2 point in the same direction as dir1, Returns -1 if dir2 point in the opposite direction as dir2
        /// </summary>
        /// <param name="dir1">Should be normalized</param>
        /// <param name="dir2">Should be normalized</param>
        /// <returns></returns>
        public static float GetDirectionSimularity(Vector3 dir1, Vector3 dir2)
        {
            return Vector3.Dot(dir1, dir2);
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
        public static List<Mesh> SplitMeshInTwo(HashSet<int> vertexIndexesToSplit, Mesh orginalMesh, bool doBones, FractureThis fT)
        {
            int[] tris = orginalMesh.triangles;
            Vector3[] verts = orginalMesh.vertices;
            Vector3[] nors = orginalMesh.normals;
            Vector2[] uvs = orginalMesh.uv;
            BoneWeight[] bones = doBones ? orginalMesh.boneWeights : new BoneWeight[verts.Length];
            Color[] cols = orginalMesh.colors;

            // Verify mesh properties and handle mismatches if necessary
            if (uvs.Length != verts.Length)
            {
                Debug.LogWarning("The uvs for the mesh " + orginalMesh.name + " may not be valid");
                uvs = new Vector2[verts.Length];
            }

            if (nors.Length != verts.Length)
            {
                Debug.LogError("The mesh " + orginalMesh.name + " normal count does not match its vertex count");
                return null;
            }

            List<Vector3> splitVerA = new List<Vector3>(verts.Length);
            List<Vector3> splitNorA = new List<Vector3>(nors.Length);
            List<Vector2> splitUvsA = new List<Vector2>(uvs.Length);
            List<int> splitTriA = new List<int>(tris.Length);
            List<BoneWeight> splitBonA = new List<BoneWeight>(bones.Length);
            List<Color> splitColsA = new List<Color>(cols.Length);

            List<Vector3> splitVerB = new List<Vector3>(verts.Length);
            List<Vector3> splitNorB = new List<Vector3>(nors.Length);
            List<Vector2> splitUvsB = new List<Vector2>(uvs.Length);
            List<int> splitTriB = new List<int>(tris.Length);
            List<BoneWeight> splitBonB = new List<BoneWeight>(bones.Length);
            List<Color> splitColsB = new List<Color>(cols.Length);

            Dictionary<Vector3, int> vertexIndexMapA = new Dictionary<Vector3, int>();
            Dictionary<Vector3, int> vertexIndexMapB = new Dictionary<Vector3, int>();

            for (int i = 0; i < tris.Length; i += 3)
            {
                int indexA = tris[i];
                int indexB = tris[i + 1];
                int indexC = tris[i + 2];

                bool splitA = vertexIndexesToSplit.Contains(indexA) || vertexIndexesToSplit.Contains(indexB) || vertexIndexesToSplit.Contains(indexC);

                ProcessTriangle(indexA, indexB, indexC, splitA);
            }

            Mesh newMA = CreateMesh(splitVerA, splitNorA, splitUvsA, splitColsA, splitBonA, splitTriA);
            Mesh newMB = CreateMesh(splitVerB, splitNorB, splitUvsB, splitColsB, splitBonB, splitTriB);

            return new List<Mesh> { newMA, newMB };

            void ProcessTriangle(int indexA, int indexB, int indexC, bool splitA)
            {
                int newIndexA = GetIndexOfVertex(indexA, splitA, vertexIndexMapA, verts, nors, uvs, cols, bones, splitVerA, splitNorA, splitUvsA, splitColsA, splitBonA);
                int newIndexB = GetIndexOfVertex(indexB, splitA, vertexIndexMapA, verts, nors, uvs, cols, bones, splitVerA, splitNorA, splitUvsA, splitColsA, splitBonA);
                int newIndexC = GetIndexOfVertex(indexC, splitA, vertexIndexMapA, verts, nors, uvs, cols, bones, splitVerA, splitNorA, splitUvsA, splitColsA, splitBonA);

                if (splitA)
                {
                    splitTriA.Add(newIndexA);
                    splitTriA.Add(newIndexB);
                    splitTriA.Add(newIndexC);
                }
                else
                {
                    splitTriB.Add(newIndexA);
                    splitTriB.Add(newIndexB);
                    splitTriB.Add(newIndexC);
                }
            }

            int GetIndexOfVertex(int index, bool splitA, Dictionary<Vector3, int> vertexIndexMap, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, Color[] colors, BoneWeight[] boneWeights, List<Vector3> splitVertices, List<Vector3> splitNormals, List<Vector2> splitUVs, List<Color> splitColors, List<BoneWeight> splitBoneWeights)
            {
                Vector3 vertex = vertices[index];
                Vector3 normal = normals[index];
                Vector2 uv = uvs[index];
                Color color = colors[index];
                BoneWeight boneWeight = boneWeights[index];

                Dictionary<Vector3, int> vertexIndexMapTarget = splitA ? vertexIndexMapA : vertexIndexMapB;
                List<Vector3> splitVerticesTarget = splitA ? splitVerA : splitVerB;
                List<Vector3> splitNormalsTarget = splitA ? splitNorA : splitNorB;
                List<Vector2> splitUVsTarget = splitA ? splitUvsA : splitUvsB;
                List<Color> splitColorsTarget = splitA ? splitColsA : splitColsB;
                List<BoneWeight> splitBoneWeightsTarget = splitA ? splitBonA : splitBonB;

                if (vertexIndexMapTarget.TryGetValue(vertex, out int existingIndex))
                {
                    return existingIndex;
                }
                else
                {
                    int newIndex = splitVerticesTarget.Count;
                    vertexIndexMapTarget[vertex] = newIndex;

                    splitVerticesTarget.Add(vertex);
                    splitNormalsTarget.Add(normal);
                    splitUVsTarget.Add(uv);
                    splitColorsTarget.Add(color);
                    splitBoneWeightsTarget.Add(boneWeight);

                    return newIndex;
                }
            }

            Mesh CreateMesh(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<Color> colors, List<BoneWeight> boneWeights, List<int> triangles)
            {
                Mesh mesh = new Mesh
                {
                    vertices = vertices.ToArray(),
                    normals = normals.ToArray(),
                    uv = uvs.ToArray(),
                    triangles = triangles.ToArray()
                };

                if (colors.Count > 0)
                    mesh.colors = colors.ToArray();

                if (boneWeights.Count > 0)
                    mesh.boneWeights = boneWeights.ToArray();

                mesh.RecalculateBounds();
                mesh.RecalculateTangents();

                return mesh;
            }
        }

        /// <summary>
        /// Returns all vertics connected to the given vertexIndex
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="vertexIndex"></param>
        /// <param name="verDisTol">All vertics within this radius will count as the same vertex</param>
        /// <returns></returns>
        public static HashSet<int> GetConnectedVertics(Mesh mesh, int vertexIndex, float verDisTol = 0.0001f)
        {
            //setup
            HashSet<int> conV = new() { vertexIndex };
            Vector3[] vers = mesh.vertices;
            int[] tris = mesh.triangles;
            int trisL = tris.Length / 3;
            List<int> trisToSearch = new();
            HashSet<int> usedFaces = new();
            GetAllTrisAtPos(vers[vertexIndex]);

            //get all connected
            for (int i = 0; i < trisToSearch.Count; i++)
            {
                if (conV.Add(tris[trisToSearch[i]]) == true) GetAllTrisAtPos(vers[tris[trisToSearch[i]]]);
                if (conV.Add(tris[trisToSearch[i] + 1]) == true) GetAllTrisAtPos(vers[tris[trisToSearch[i] + 1]]);
                if (conV.Add(tris[trisToSearch[i] + 2]) == true) GetAllTrisAtPos(vers[tris[trisToSearch[i] + 2]]);
            }

            Debug.Log(trisToSearch.Count);
            return conV;
            
            void GetAllTrisAtPos(Vector3 pos)
            {
                //for (int i = 0; i < trisL; i++)
                Parallel.For(0, trisL, i =>
                {
                    int tI = i * 3;

                    //if (usedFaces.Contains(tI) == false)
                    //{
                        if ((vers[tris[tI]] - pos).sqrMagnitude < verDisTol
                  || (vers[tris[tI + 1]] - pos).sqrMagnitude < verDisTol
                  || (vers[tris[tI + 2]] - pos).sqrMagnitude < verDisTol)
                        {
                            lock (trisToSearch)
                            {
                                if (usedFaces.Add(tI) == true) trisToSearch.Add(tI);
                            }
                        }
                    //}
                });
            }
        }

        /// <summary>
        /// Returns all vertics that is close to the given position. (Always excluding vertex index vIndexToIgnore)
        /// </summary>
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

        /// <summary>
        /// Returns all vertics that is close to the given position and has the same id. (Always excluding vertex index vIndexToIgnore)
        /// </summary>
        /// <param name="pos"></param>
        /// <param name="verDisTol"></param>
        /// <param name="vIndexToIgnore"></param>
        /// <param name="verticsIds">Must have the same lenght as vertics</param>
        /// <returns></returns>
        public static List<int> GetAllVertexIndexesAtPos_id(Vector3[] vertics, int[] verticsIds, Vector3 pos, int id, float verDisTol = 0.0001f, int vIndexToIgnore = -1)
        {
            List<int> vAtPos = new();

            for (int i = 0; i < vertics.Length; i += 1)
            {
                if ((vertics[i] - pos).magnitude < verDisTol && i != vIndexToIgnore && verticsIds[i] == id)
                {
                    vAtPos.Add(i);
                }
            }

            return vAtPos;
        }

        /// <summary>
        /// Adds all group ids that exists in the given color array to the groupIds hashset, does not include links
        /// </summary>
        public static void Gd_getIdsFromColors(Color[] colors, ref HashSet<List<float>> groupIds)
        {
            foreach (Color col in colors)
            {
                bool canAdd = true;

                foreach (List<float> id in groupIds)
                {
                    if (Gd_isIdInColor(id, col) == true)
                    {
                        canAdd = false;
                        break;
                    }
                }

                if (canAdd == false) continue;

                groupIds.Add(Gd_getIdFromColor(col));
            }
        }

        /// <summary>
        /// Returns the id from the color, does not include links, returns null if base id
        /// </summary>
        public static List<float> Gd_getIdFromColor(Color color)
        {
            if (color.r <= 0.5f) return null;
            List<float> gIds = new()
            {
                color.r
            };

            if (color.g <= 0.5f) return gIds;
            gIds.Add(color.g);

            if (color.b <= 0.5f) return gIds;
            gIds.Add(color.b);

            if (color.a <= 0.5f) return gIds;
            gIds.Add(color.a);

            return gIds;
        }

        /// <summary>
        /// Returns true if the given group id matches the id in the color, does not include links
        /// </summary>
        public static bool Gd_isIdInColor(List<float> id, Color color)
        {
            if (color.r <= 0.5f) return id == null;
            if (id == null) return false;
            if (id.Contains(color.r) == false) return false;

            if (color.g <= 0.5f) return id.Count == 1;
            if (id.Contains(color.g) == false) return false;

            if (color.b <= 0.5f) return id.Count == 2;
            if (id.Contains(color.b) == false) return false;

            if (color.a <= 0.5f) return id.Count == 3;
            return id.Count == 4 && id.Contains(color.a);
        }

        /// <summary>
        /// Returns the index of all vertices that has the given id
        /// </summary>
        public static HashSet<int> Gd_getAllVerticesInId(Color[] verColors, List<float> id)
        {
            HashSet<int> verInId = new();
            int verL = verColors.Length;

            for (int i = 0; i < verL; i++)
            {
                if (Gd_isIdInColor(id, verColors[i]) == true) verInId.Add(i);
            }

            return verInId;
        }

        public static Mesh ConvertMeshWithMatrix(Mesh mesh, Matrix4x4 lTwMatrix)
        {
            //mesh.SetVertices(ConvertPositionsWithMatrix(mesh.vertices, lTwMatrix));
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
            //List<BoneWeight> combinedBones = new List<BoneWeight>();
            int vertexOffset = 0;

            for (int i = 0; i < fracMeshes.Count; i += 1)
            {
                Mesh mesh = fracMeshes[i];

                // Get the vertices, normals, and UVs from the small mesh
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Vector2[] uvs = mesh.uv;
                //BoneWeight[] bones = mesh.boneWeights;

                //link combined vertices to fracture part
                for (int ii = 0; ii < vertices.Length; ii += 1)
                {
                    fracParts[i].rendLinkVerIndexes.Add(combinedVertices.Count + ii);
                }

                // Append the vertex data to the combined lists
                combinedVertices.AddRange(vertices);
                combinedNormals.AddRange(normals);
                combinedUVs.AddRange(uvs);
                //combinedBones.AddRange(bones);

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
            //combinedMesh.boneWeights = combinedBones.ToArray(); //if no 0 we get error, (Must implement og bones to fractured mesh bones)
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

        public static Vector3 ClosestPointOnTriangle(Vector3 pointA, Vector3 pointB, Vector3 pointC, Vector3 queryPoint)
        {
            // Calculate triangle normal
            Vector3 triNorm = Vector3.Cross(pointA - pointB, pointA - pointC);

            // Calculate the projection of the query point onto the triangle plane
            Plane triPlane = new(pointA, triNorm);
            triPlane.Set3Points(pointA, pointB, pointC);
            Vector3 projectedPoint = triPlane.ClosestPointOnPlane(queryPoint);

            // Check if the projected point is inside the triangle
            if (IsPointInTriangle(projectedPoint, pointA, pointB, pointC))
            {
                return projectedPoint;
            }
            else
            {
                // If not, find the closest point on each triangle edge
                Vector3 closestOnAB = ClosestPointOnSegment(pointA, pointB, queryPoint);
                Vector3 closestOnBC = ClosestPointOnSegment(pointB, pointC, queryPoint);
                Vector3 closestOnCA = ClosestPointOnSegment(pointC, pointA, queryPoint);

                // Find the closest point among these three
                float distAB = Vector3.Distance(queryPoint, closestOnAB);
                float distBC = Vector3.Distance(queryPoint, closestOnBC);
                float distCA = Vector3.Distance(queryPoint, closestOnCA);

                if (distAB < distBC && distAB < distCA)
                    return closestOnAB;
                else if (distBC < distCA)
                    return closestOnBC;
                else
                    return closestOnCA;
            }


            bool IsPointInTriangle(Vector3 point, Vector3 A, Vector3 B, Vector3 C)
            {
                // Check if the point is inside the triangle using barycentric coordinates
                float alpha = ((B - C).y * (point.x - C.x) + (C - B).x * (point.y - C.y)) /
                              ((B - C).y * (A.x - C.x) + (C - B).x * (A.y - C.y));

                float beta = ((C - A).y * (point.x - C.x) + (A - C).x * (point.y - C.y)) /
                             ((B - C).y * (A.x - C.x) + (C - B).x * (A.y - C.y));

                float gamma = 1.0f - alpha - beta;

                return alpha > 0 && beta > 0 && gamma > 0;
            }

            Vector3 ClosestPointOnSegment(Vector3 start, Vector3 end, Vector3 queryPoint)
            {
                // Find the closest point on a line segment
                Vector3 direction = end - start;
                float t = Mathf.Clamp01(Vector3.Dot(queryPoint - start, direction) / direction.sqrMagnitude);
                return start + t * direction;
            }
        }


        /// <summary>
        /// Performs a linecast for all positions between all positions
        /// </summary>
        /// <param name="poss"></param>
        /// <returns></returns>
        public static HashSet<Collider> LinecastsBetweenPositions(List<Vector3> poss, PhysicsScene phyScene)
        {
            RaycastHit[] rHits = new RaycastHit[5];
            int rHitCount;
            HashSet<Collider> cHits = new();
            Vector3 rayDir;

            for (int i = 0; i < poss.Count; i++)
            {
                for (int ii = 0; ii < poss.Count; ii++)
                {
                    if (ii == i) continue;

                    rayDir = poss[i] - poss[ii];
                    rHitCount = phyScene.Raycast(poss[ii], rayDir.normalized, rHits, rayDir.magnitude, Physics.AllLayers, QueryTriggerInteraction.Ignore);

                    for (int rI = 0; rI < rHitCount; rI++)
                    {
                        if (rHits[rI].collider != null) cHits.Add(rHits[rI].collider);
                    }
                    //Physics.Linecast(poss[i], poss[ii], out nHit);
                    //if (nHit.collider != null) hits.Add(nHit.collider);
                }
            }

            return cHits;
        }

        // Function to find the most similar triangle in the mesh
        public static int FindMostSimilarTriangle(Mesh mesh, Vector3[] worldTriangle)
        {
            int triangleCount = mesh.triangles.Length / 3;
            int mostSimilarTriangleIndex = -1;
            float minDifference = float.MaxValue;

            for (int i = 0; i < triangleCount; i++)
            {
                Vector3[] meshTriangle = GetTriangleVertices(mesh, i);

                // Calculate some similarity metric (e.g., distance, orientation, shape)
                float difference = CalculateTriangleDifference(worldTriangle, meshTriangle);

                // Update the most similar triangle if the current one is more similar
                if (difference < minDifference)
                {
                    minDifference = difference;
                    mostSimilarTriangleIndex = i;
                }
            }

            return mostSimilarTriangleIndex;
        }

        // Function to get the vertices of a triangle in the mesh
        public static Vector3[] GetTriangleVertices(Mesh mesh, int triangleIndex)
        {
            int startIndex = triangleIndex * 3;
            Vector3[] vertices = new Vector3[3];

            for (int i = 0; i < 3; i++)
            {
                int vertexIndex = mesh.triangles[startIndex + i];
                vertices[i] = mesh.vertices[vertexIndex];
            }

            return vertices;
        }

        // Function to calculate the difference between two triangles
        public static float CalculateTriangleDifference(Vector3[] triangle1, Vector3[] triangle2)
        {
            // Implement your similarity metric here
            // Example: calculate the distance between corresponding vertices
            float difference = 0;

            for (int i = 0; i < 3; i++)
            {
                difference += Vector3.Distance(triangle1[i], triangle2[i]);
            }

            return difference;
        }

        public  static int FindTriangleIndexWithVertex(int[] triangles, int vertexIndex)
        {
            for (int i = 0; i < triangles.Length; i += 3)
            {
                if (triangles[i] == vertexIndex || triangles[i + 1] == vertexIndex || triangles[i + 2] == vertexIndex)
                {
                    return i; // Return the triangle index
                }
            }

            return -1; // Vertex is not part of any triangle
        }

        public static Collider CopyColliderToTransform(Collider ogCol, Transform targetTrans)
        {
            // Instantiate a new collider of the same type
            Collider newCollider = null;

            if (ogCol is MeshCollider ogMeshCol)
            {
                MeshCollider newMeshCol = targetTrans.gameObject.AddComponent<MeshCollider>();

                // Copy properties from the original collider to the new collider
                newMeshCol.convex = ogMeshCol.convex;
                newMeshCol.cookingOptions = ogMeshCol.cookingOptions;
                newMeshCol.sharedMesh = new() { vertices = FractureHelperFunc.ConvertPositionsWithMatrix(FractureHelperFunc.ConvertPositionsWithMatrix(ogMeshCol.sharedMesh.vertices, ogCol.transform.localToWorldMatrix), targetTrans.worldToLocalMatrix) };
                newCollider = newMeshCol;
            }
            else if (ogCol is BoxCollider originalBoxCollider)
            {
                BoxCollider newBoxCollider = targetTrans.gameObject.AddComponent<BoxCollider>();

                // Copy properties from the original collider to the new collider
                newBoxCollider.center = originalBoxCollider.center;
                newBoxCollider.size = originalBoxCollider.size;
                newCollider = newBoxCollider;
            }
            else if (ogCol is SphereCollider originalSphereCollider)
            {
                SphereCollider newSphereCollider = targetTrans.gameObject.AddComponent<SphereCollider>();

                // Copy properties from the original collider to the new collider
                newSphereCollider.center = originalSphereCollider.center;
                newSphereCollider.radius = originalSphereCollider.radius;
                newCollider = newSphereCollider;
            }
            else if (ogCol is CapsuleCollider originalCapsuleCollider)
            {
                CapsuleCollider newCapsuleCollider = targetTrans.gameObject.AddComponent<CapsuleCollider>();

                // Copy properties from the original collider to the new collider
                newCapsuleCollider.center = originalCapsuleCollider.center;
                newCapsuleCollider.radius = originalCapsuleCollider.radius;
                newCapsuleCollider.height = originalCapsuleCollider.height;
                newCapsuleCollider.direction = originalCapsuleCollider.direction;
                newCollider = newCapsuleCollider;
            }

            newCollider.contactOffset = ogCol.contactOffset;
            newCollider.isTrigger = ogCol.isTrigger;
            newCollider.sharedMaterial = ogCol.sharedMaterial;
            newCollider.hasModifiableContacts = ogCol.hasModifiableContacts;
            return newCollider;
        }

        /// <summary>
        /// Modifies to given collider mesh/size/radius based on the given positions as good as possible
        /// </summary>
        /// <param name="col"></param>
        /// <param name="possLocal"></param>
        public static void SetColliderFromFromPoints(Collider col, Vector3[] possLocal)
        {
            Transform colTrans = col.transform;

            if (col is MeshCollider mCol)
            {
                mCol.sharedMesh.SetVertices(possLocal, 0, possLocal.Length,
                      UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices
                | UnityEngine.Rendering.MeshUpdateFlags.DontResetBoneBounds
                | UnityEngine.Rendering.MeshUpdateFlags.DontNotifyMeshUsers
                | UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
            }
            else if (col is BoxCollider bCol)
            {
                bCol.center = FractureHelperFunc.GetGeometricCenterOfPositions(possLocal);
                possLocal = FractureHelperFunc.ConvertPositionsWithMatrix(possLocal, colTrans.localToWorldMatrix);

                Vector3 extents = Vector3.one * 0.001f;
                float cDis;
                Vector3 tPos = bCol.bounds.center;
                Vector3 tFor = colTrans.forward;
                Vector3 tSide = colTrans.right;
                Vector3 tUp = colTrans.up;

                foreach (Vector3 wPos in possLocal)
                {
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLine_doubleSided(wPos, tPos, tFor), tPos);
                    if (cDis > extents.z) extents.z = cDis;
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLine_doubleSided(wPos, tPos, tSide), tPos);
                    if (cDis > extents.x) extents.x = cDis;
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLine_doubleSided(wPos, tPos, tUp), tPos);
                    if (cDis > extents.y) extents.y = cDis;
                }

                bCol.size = extents * 2.0f;
            }
            else if (col is SphereCollider sCol)
            {
                sCol.center = FractureHelperFunc.GetGeometricCenterOfPositions(possLocal);
                possLocal = FractureHelperFunc.ConvertPositionsWithMatrix(possLocal, colTrans.localToWorldMatrix);

                Vector3 extents = Vector3.one * 0.001f;
                float cDis;
                Vector3 tPos = sCol.bounds.center;
                Vector3 tFor = colTrans.forward;
                Vector3 tSide = colTrans.right;
                Vector3 tUp = colTrans.up;

                foreach (Vector3 wPos in possLocal)
                {
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLine_doubleSided(wPos, tPos, tFor), tPos);
                    if (cDis > extents.z) extents.z = cDis;
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLine_doubleSided(wPos, tPos, tSide), tPos);
                    if (cDis > extents.x) extents.x = cDis;
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLine_doubleSided(wPos, tPos, tUp), tPos);
                    if (cDis > extents.y) extents.y = cDis;
                }

                extents.Scale(colTrans.localToWorldMatrix.lossyScale);
                sCol.radius = Mathf.Max(extents.x, extents.y, extents.z);
            }
            else if (col is CapsuleCollider cCol)
            {
                cCol.center = FractureHelperFunc.GetGeometricCenterOfPositions(possLocal);
                possLocal = FractureHelperFunc.ConvertPositionsWithMatrix(possLocal, colTrans.localToWorldMatrix);

                Vector3 extents = Vector3.one * 0.001f;
                float cDis;
                Vector3 tPos = cCol.bounds.center;
                Vector3 tFor = colTrans.forward;
                Vector3 tSide = colTrans.right;
                Vector3 tUp = colTrans.up;

                foreach (Vector3 wPos in possLocal)
                {
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLine_doubleSided(wPos, tPos, tFor), tPos);
                    if (cDis > extents.z) extents.z = cDis;
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLine_doubleSided(wPos, tPos, tSide), tPos);
                    if (cDis > extents.x) extents.x = cDis;
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLine_doubleSided(wPos, tPos, tUp), tPos);
                    if (cDis > extents.y) extents.y = cDis;
                }

                if (extents.x > extents.y && extents.x > extents.z)
                {
                    // X-axis is the longest
                    cCol.direction = 0;
                    cCol.height = extents.x * 2.0f;
                    cCol.radius = Mathf.Max(extents.y, extents.z);
                }
                else if (extents.y > extents.x && extents.y > extents.z)
                {
                    // Y-axis is the longest
                    cCol.direction = 1;
                    cCol.height = extents.y * 2.0f;
                    cCol.radius = Mathf.Max(extents.x, extents.z);
                }
                else
                {
                    // Z-axis is the longest (or all axes are equal)
                    cCol.direction = 2;
                    cCol.height = extents.z * 2.0f;
                    cCol.radius = Mathf.Max(extents.x, extents.y);
                }
            }

            if (col.enabled == false) return;
            col.enabled = false;
            col.enabled = true;
        }

        /// <summary>
        /// Returns the geometric/(not average) center of given positions
        /// </summary>
        /// <param name="positions"></param>
        /// <returns></returns>
        public static Vector3 GetGeometricCenterOfPositions(Vector3[] positions)
        {
            Vector3 min = positions[0];
            Vector3 max = positions[0];

            // Find the minimum and maximum coordinates along each axis
            for (int i = 1; i < positions.Length; i++)
            {
                min = Vector3.Min(min, positions[i]);
                max = Vector3.Max(max, positions[i]);
            }

            // Calculate the geometric center as the midpoint of the bounding box
            return (min + max) * 0.5f;
        }

        /// <summary>
        /// Subtracts the given vector lenght by amount
        /// </summary>
        /// <param name="vector"></param>
        /// <param name="amount"></param>
        /// <returns></returns>
        public static Vector3 SubtractMagnitude(Vector3 vector, float amount)
        {
            if (vector.magnitude <= amount) return Vector3.zero;
            return vector.normalized * (vector.magnitude - amount);
        }

        /// <summary>
        /// Returns true if transToLookFor is a parent of transToSearchFrom (Includes indirect parents like transform.parent.parent)
        /// </summary>
        /// <param name="transToLookFor"></param>
        /// <param name="transToSearchFrom"></param>
        /// <returns></returns>
        public static bool GetIfTransformIsAnyParent(Transform transToLookFor, Transform transToSearchFrom)
        {
            if (transToLookFor == transToSearchFrom) return true;

            while (transToSearchFrom.parent != null)
            {
                transToSearchFrom = transToSearchFrom.parent;
                if (transToSearchFrom == transToLookFor) return true;
            }

            return false;
        }

        /// <summary>
        /// Assigns each axis of vecA with vecB if the same vecB axis is lower
        /// </summary>
        public static void GetEachAxisMin(ref Vector3 vecA, Vector3 vecB)
        {
            if (vecA.x > vecB.x) vecA.x = vecB.x;
            if (vecA.y > vecB.y) vecA.y = vecB.y;
            if (vecA.z > vecB.z) vecA.z = vecB.z;
        }

        /// <summary>
        /// Assigns each axis of vecA with vecB if the same vecB axis is higher
        /// </summary>
        public static void GetEachAxisMax(ref Vector3 vecA, Vector3 vecB)
        {
            if (vecA.x < vecB.x) vecA.x = vecB.x;
            if (vecA.y < vecB.y) vecA.y = vecB.y;
            if (vecA.z < vecB.z) vecA.z = vecB.z;
        }

        public class HashSetComparer : IComparer<List<float>>
        {
            public int Compare(List<float> x, List<float> y)
            {
                if (x == null && y == null)
                {
                    return 0; // Both are null, consider them equal
                }
                else if (x == null)
                {
                    return -1; // x is null, consider it less than y
                }
                else if (y == null)
                {
                    return 1; // y is null, consider it greater than x
                }

                // Compare the total values
                return x.Sum().CompareTo(y.Sum());
            }
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

        public static void Debug_drawDisc(Vector3 discCenter, Vector3 discNormal, float discRadius, int segments)
        {
            // Calculate two vectors perpendicular to the normal
            Vector3 from = Vector3.Cross(discNormal, Vector3.up).normalized;
            Vector3 to = Vector3.Cross(discNormal, from).normalized;

            // Calculate the world position of the disc center
            Vector3 worldCenter = discCenter;

            // Draw the disc circumference using line segments
            for (int i = 0; i < segments; i++)
            {
                float angle = i * 2 * Mathf.PI / segments;
                Vector3 start = worldCenter + discRadius * Mathf.Cos(angle) * from + discRadius * Mathf.Sin(angle) * to;
                angle = (i + 1) * 2 * Mathf.PI / segments;
                Vector3 end = worldCenter + discRadius * Mathf.Cos(angle) * from + discRadius * Mathf.Sin(angle) * to;
                Debug.DrawLine(start, end, Color.red, 0.5f);
            }
        }

        public static void Debug_drawBox(Vector3 position, float size, Color color, float duration = 0.1f)
        {
            Vector3 halfSize = 0.5f * size * Vector3.one;

            // Calculate the corners of the box
            Vector3[] corners = new Vector3[8];
            corners[0] = position + new Vector3(-halfSize.x, -halfSize.y, -halfSize.z);
            corners[1] = position + new Vector3(halfSize.x, -halfSize.y, -halfSize.z);
            corners[2] = position + new Vector3(halfSize.x, -halfSize.y, halfSize.z);
            corners[3] = position + new Vector3(-halfSize.x, -halfSize.y, halfSize.z);
            corners[4] = position + new Vector3(-halfSize.x, halfSize.y, -halfSize.z);
            corners[5] = position + new Vector3(halfSize.x, halfSize.y, -halfSize.z);
            corners[6] = position + new Vector3(halfSize.x, halfSize.y, halfSize.z);
            corners[7] = position + new Vector3(-halfSize.x, halfSize.y, halfSize.z);

            // Draw lines between corners to form the box
            Debug.DrawLine(corners[0], corners[1], color, duration);
            Debug.DrawLine(corners[1], corners[2], color, duration);
            Debug.DrawLine(corners[2], corners[3], color, duration);
            Debug.DrawLine(corners[3], corners[0], color, duration);

            Debug.DrawLine(corners[4], corners[5], color, duration);
            Debug.DrawLine(corners[5], corners[6], color, duration);
            Debug.DrawLine(corners[6], corners[7], color, duration);
            Debug.DrawLine(corners[7], corners[4], color, duration);

            Debug.DrawLine(corners[0], corners[4], color, duration);
            Debug.DrawLine(corners[1], corners[5], color, duration);
            Debug.DrawLine(corners[2], corners[6], color, duration);
            Debug.DrawLine(corners[3], corners[7], color, duration);
        }
#endif
    }
}