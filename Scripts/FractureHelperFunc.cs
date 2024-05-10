using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Unity.VisualScripting.FullSerializer;
using UnityEngine.Rendering;
using g3;
using OpenCover.Framework.Model;
using Unity.Collections;
using Unity.VisualScripting;
using System.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Experimental.Rendering;
using Unity.Burst;












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
        /// By using the add, set and remove functions to modify items.
        /// You can have a type(indexValues) sorted by a weights(sortValues) and still access them by weight order or by index.
        /// Reading is besically as fast as reading a single list, writing is slower but faster than writing and then using .sort() to sort the list.
        /// All indexValues must be unique but you can have identical sortValues
        /// </summary>
        public struct NativeSortedIndexes
        {
            /// <summary>
            /// [indexValue] = the sortValue that indexValue has
            /// </summary>
            public NativeHashMap<int, float> indexValueToSortValue;
            
            /// <summary>
            /// [0] = the lowest sortValue that exists (The floats are always sorted from lowest to highest)
            /// </summary>
            public NativeList<float> sortIndexToSortValue;

            /// <summary>
            /// [0] = the indexValue that has the lowest sortValue (Sorted from lowest to highest sortValue, sorted by sortIndexToSortValue)
            /// </summary>
            public NativeList<int> sortIndexToIndexValue;

            /// <summary>
            /// Adds the sort-index pair and returns the sortIndex it was added to,
            /// its highly recommended that you make sure indexValueToSortValue does not already contain the given indexValue!
            /// </summary>
            public int Add(float sortValue, int indexValue)
            {
                int sortIndex = GetNewSortIndex(sortValue);
                
                if (sortIndex >= sortIndexToSortValue.Length)
                {
                    sortIndexToSortValue.Add(sortValue);
                    sortIndexToIndexValue.Add(indexValue);
                }
                else
                {
                    sortIndexToSortValue.InsertRangeWithBeginEnd(sortIndex, sortIndex + 1);
                    sortIndexToSortValue[sortIndex] = sortValue;
                    sortIndexToIndexValue.InsertRangeWithBeginEnd(sortIndex, sortIndex + 1);
                    sortIndexToIndexValue[sortIndex] = indexValue;
                }

                indexValueToSortValue.Add(indexValue, sortValue);
                return sortIndex;
            }

            /// <summary>
            /// Sets the sortValue of the given indexValue or adds a new sort-index pair if indexValue does not exist
            /// </summary>
            public void SetOrAdd(float newSortValue, int indexValue)
            {
                int sortIndex = GetSortIndex(indexValue);
                if (sortIndex < 0)
                {
                    //add
                    Add(newSortValue, indexValue);
                    return;
                }

                //set, just copy from SetAt()
                int newSortIndex = GetNewSortIndex(newSortValue);

                indexValueToSortValue[indexValue] = newSortValue;
                if (newSortIndex == sortIndex)
                {
                    sortIndexToSortValue[sortIndex] = newSortValue;
                    return;
                }

                sortIndexToSortValue.RemoveAt(sortIndex);
                sortIndexToIndexValue.RemoveAt(sortIndex);

                if (newSortIndex > sortIndex)
                {
                    // If moving to a higher index, the new index should be reduced by 1
                    newSortIndex--;
                }

                if (newSortIndex >= sortIndexToSortValue.Length)
                {
                    sortIndexToSortValue.Add(newSortValue);
                    sortIndexToIndexValue.Add(indexValue);
                }
                else
                {
                    sortIndexToSortValue.InsertRangeWithBeginEnd(newSortIndex, newSortIndex + 1);
                    sortIndexToSortValue[newSortIndex] = newSortValue;
                    sortIndexToIndexValue.InsertRangeWithBeginEnd(newSortIndex, newSortIndex + 1);
                    sortIndexToIndexValue[newSortIndex] = indexValue;
                }
            }

            /// <summary>
            /// Sets the sortValue, will cause exception if indexValue or sortIndex does not exist
            /// </summary>
            public void SetAt(float newSortValue, int indexValue, int sortIndex)
            {
                int newSortIndex = GetNewSortIndex(newSortValue);

                indexValueToSortValue[indexValue] = newSortValue;
                if (newSortIndex == sortIndex)
                {
                    sortIndexToSortValue[sortIndex] = newSortValue;
                    return;
                }

                sortIndexToSortValue.RemoveAt(sortIndex);
                sortIndexToIndexValue.RemoveAt(sortIndex);

                if (newSortIndex > sortIndex)
                {
                    // If moving to a higher index, the new index should be reduced by 1
                    newSortIndex--;
                }

                if (newSortValue >= sortIndexToSortValue.Length)
                {
                    sortIndexToSortValue.Add(newSortValue);
                    sortIndexToIndexValue.Add(indexValue);
                }
                else
                {
                    sortIndexToSortValue.InsertRangeWithBeginEnd(newSortIndex, newSortIndex + 1);
                    sortIndexToSortValue[newSortIndex] = newSortValue;
                    sortIndexToIndexValue.InsertRangeWithBeginEnd(newSortIndex, newSortIndex + 1);
                    sortIndexToIndexValue[newSortIndex] = indexValue;
                }
            }

            /// <summary>
            /// Removes a sort-index pair that has the given indexValue
            /// </summary>
            public void Remove(int indexValue)
            {
                int sortIndex = GetSortIndex(indexValue);
                if (sortIndex < 0)
                {
                    return;
                }

                sortIndexToSortValue.RemoveAt(sortIndex);
                sortIndexToIndexValue.RemoveAt(sortIndex);
                indexValueToSortValue.Remove(indexValue);
            }

            /// <summary>
            /// Removes the sort-index pair, will cause exception if indexValue or sortIndex does not exist
            /// </summary>
            public void RemoveAt(int indexValue, int sortIndex)
            {
                sortIndexToSortValue.RemoveAt(sortIndex);
                sortIndexToIndexValue.RemoveAt(sortIndex);
                indexValueToSortValue.Remove(indexValue);
            }

            /// <summary>
            /// Returns the sortIndex the given indexValue has, returns -1 if indexValue does not exist
            /// </summary>
            public readonly int GetSortIndex(int indexValue)
            {
                if (indexValueToSortValue.TryGetValue(indexValue, out float oldSortValue) == false)
                {
                    return -1;
                }

                //In theory we should be able to just search for indexValue in sortIndexToIndexValue directly but that caused issues when I tested it ealier??
                int sortIndex = GetNewSortIndex(oldSortValue);
                if (sortIndex > 0) sortIndex--;

                while (sortIndex < sortIndexToIndexValue.Length)
                {
                    if (sortIndexToIndexValue[sortIndex] == indexValue)
                    {
                        return sortIndex;
                    }

                    sortIndex++;
                }

                Debug.LogError("Expected indexValue to be found, will pretend it does not exist! Since we know indexValueToSortValue contains indexValue," +
                    " that means either BinarySearch or sortIndexToIndexValue is incorrect. Should we implement a fallback method?");
                return -1;
            }

            /// <summary>
            /// Returns the sortIndex sortValue should be added to, to maintain a sorted list
            /// </summary>
            public readonly int GetNewSortIndex(float sortValue)
            {
                int sortIndex = sortIndexToSortValue.BinarySearch(sortValue);
                if (sortIndex < 0) return ~sortIndex; // Bitwise complement to get the insert position
                return sortIndex;
            }

            public void Clear()
            {
                sortIndexToIndexValue.Clear();
                sortIndexToSortValue.Clear();
                indexValueToSortValue.Clear();
            }

            public NativeSortedIndexes(Allocator allocator, int initialCapacity = 4)
            {
                indexValueToSortValue = new NativeHashMap<int, float>(initialCapacity, allocator);
                sortIndexToIndexValue = new NativeList<int>(initialCapacity, allocator);
                sortIndexToSortValue = new NativeList<float>(initialCapacity, allocator);
            }
        }

        /// <summary>
        /// Adds the float to the list (low to high) and returns the index the item was inserted at
        /// </summary>
        public static int InsertSorted(ref List<float> list, float item)
        {
            int index = list.BinarySearch(item);
            if (index < 0)
                index = ~index; // Bitwise complement to get the insert position

            if (list.Count > 1 && index == 0) index = 1;
            list.Insert(index, item);
            return index;
        }

        /// <summary>
        /// Moves the item at oldIndex to newIndex (removeAt->Insert)
        /// </summary>
        public static void MoveItem<T>(ref List<T> list, int oldIndex, int newIndex)
        {
            if (oldIndex == newIndex) return;
            
            T item = list[oldIndex];
            list.RemoveAt(oldIndex);

            if (newIndex > oldIndex)
            {
                // If moving to a higher index, the new index should be reduced by 1
                newIndex--;
            }

            list.Insert(newIndex, item);
        }

        public static List<T> NativeListToList<T>(NativeList<T> nativeList) where T : unmanaged
        {
            // Create a new List<T> to hold the converted elements
            List<T> list = new(nativeList.Length);

            // Iterate through the NativeList and add each element to the List
            for (int i = 0; i < nativeList.Length; i++)
            {
                list.Add(nativeList[i]);
            }

            // Return the converted List
            return list;
        }

        public static Dictionary<TKey, TValue> NativeHashMapToDictorary<TKey, TValue>(NativeHashMap<TKey, TValue> nativeHashMap)
       where TKey : unmanaged, System.IEquatable<TKey>
       where TValue : unmanaged
        {
            // Create a new Dictionary<TKey, TValue> to hold the converted elements
            Dictionary<TKey, TValue> dictionary = new(nativeHashMap.Count);

            foreach (var pair in nativeHashMap)
            {
                dictionary.Add(pair.Key, pair.Value);
            }

            // Return the converted Dictionary
            return dictionary;
        }

        public static List<T> NativeArrayToList<T>(NativeArray<T> nativeArray) where T : unmanaged
        {
            // Create a new List<T> to hold the converted elements
            List<T> list = new(nativeArray.Length);

            // Iterate through the NativeArray and add each element to the List
            for (int i = 0; i < nativeArray.Length; i++)
            {
                list.Add(nativeArray[i]);
            }

            // Return the converted List
            return list;
        }

        /// <summary>
        /// Removes or Adds elements to the given list so its count matches desiredLength
        /// </summary>
        public static void SetListLenght<T>(ref List<T> list, int desiredLength)
        {
            if (desiredLength < 0) return;

            int currentLength = list.Count;
            if (currentLength == desiredLength) return;

            if (desiredLength > currentLength)
            {
                //Add items
                int elementsToAdd = desiredLength - currentLength;
                for (int i = 0; i < elementsToAdd; i++)
                {
                    list.Add(default);
                }
            }
            else
            {
                //Remove items
                list.RemoveRange(desiredLength, currentLength - desiredLength);
            }
        }

        public static void SetWholeNativeArray<T>(ref NativeArray<T> nativeFA, T newValue) where T : unmanaged
        {
            int aCount = nativeFA.Length;
            for (int i = 0; i < aCount; i++)
            {
                nativeFA[i] = newValue;
            }
        }

        /// <summary>
        /// Returns true if boundsA = boundsB (Size and position wize)
        /// </summary>
        public static bool AreBoundsArrayEqual(Bounds[] boundsA, Bounds[] boundsB)
        {
            if (boundsA.Length != boundsB.Length) return false;

            for (int i = 0; i < boundsA.Length; i++)
            {
                // Check if both bounds have the same center and size
                if (boundsA[i].center != boundsB[i].center || boundsA[i].size != boundsB[i].size) return false;
            }

            return true;
        }

        /// <summary>
        /// Returns the closest position index
        /// </summary>
        /// <param name="positions">The positions to check against</param>
        /// <param name="pos">Closest position to this</param>
        /// <param name="preExitTolerance">If point is closer than this, return it without checking rest</param>
        /// <returns></returns>
        public static int GetClosestPointInArray(Vector3[] positions, Vector3 pos)
        {
            int bestI = 0;
            float bestD = float.MaxValue;
            float currentD;

            for (int i = 0; i < positions.Length; i++)
            {
                currentD = (pos - positions[i]).sqrMagnitude;

                if (currentD < bestD)
                {
                    bestD = currentD;
                    bestI = i;

                    //if (currentD < preExitTolerance) break;
                }
            }

            return bestI;
        }

        /// <summary>
        /// Returns the closest position on the given disc (Flat circle)
        /// </summary>
        public static Vector3 ClosestPointOnDisc(Vector3 point, Vector3 discCenter, Vector3 discNormal, float discRadius, float discRadiusSquared)
        {
            //Project the point onto the disc normal plane, discNormal wont be used after this so we can reuse it
            discNormal = point - Vector3.Dot(point - discCenter, discNormal) * discNormal;

            //Move the projected point into the disc radius
            if ((discNormal - discCenter).sqrMagnitude > discRadiusSquared) discNormal = discCenter + (discNormal - discCenter).normalized * discRadius;

            return discNormal;
        }

        /// <summary>
        /// Returns the closest position to point on a line between start and end
        /// </summary>
        public static Vector3 ClosestPointOnLine(Vector3 lineStart, Vector3 lineEnd, Vector3 point)
        {
            Vector3 lineDirection = lineEnd - lineStart;
            float lineLength = lineDirection.magnitude;
            lineDirection.Normalize();

            // Project the point onto the line
            float dotProduct = Vector3.Dot(point - lineStart, lineDirection);
            dotProduct = Mathf.Clamp(dotProduct, 0f, lineLength); // Ensure point is within the segment
            return lineStart + dotProduct * lineDirection;
        }

        /// <summary>
        /// Returns the closest position to point on the given line
        /// </summary>
        public static Vector3 ClosestPointOnLineInfinit(Vector3 point, Vector3 linePosition, Vector3 lineDirection)
        {
            return linePosition + (Vector3.Dot(point - linePosition, lineDirection) / lineDirection.sqrMagnitude) * lineDirection;
        }

        /// <summary>
        /// Returns the closest triangel index on the mesh
        /// </summary>
        /// <param name="meshWorldVers">The mesh vertics in world space</param>
        /// <param name="meshTris">The mesh triangels</param>
        /// <param name="pos">Closest triangel to these</param>
        /// <param name="preExitTolerance">If point is closer than this, return it without checking rest</param>
        /// <returns></returns>
        public static int GetClosestTriOnMesh(Vector3[] meshWorldVers, int[] meshTris, Vector3[] poss, float worldScale = 1.0f)
        {
            int bestI = 0;
            float bestD = float.MaxValue;
            float disT;
            float disP;
            float worldScaleDis = worldScale * 0.00001f;
            worldScaleDis *= worldScaleDis;

            for (int i = 0; i < meshTris.Length; i += 3)
            {

                //if (Vector3.Dot(meshWorldNors[meshTris[i]].normalized, nor.normalized) < 0.5f) continue;

                //if (meshVerIds[meshTris[i]] != id) continue;
                //if distance to tris is < 2x the distance to plane, We can use plane distance
                disT = 0.0f;
                disP = 0.0f;
                for (int ii = 0; ii < poss.Length; ii++)
                {
                    disT += (poss[ii] - ClosestPointOnTriangle(meshWorldVers[meshTris[i]], meshWorldVers[meshTris[i + 1]], meshWorldVers[meshTris[i + 2]], poss[ii])).sqrMagnitude;
                    disP += (poss[ii] - ClosestPointOnPlaneInfinit(meshWorldVers[meshTris[i]],
                        Vector3.Cross(meshWorldVers[meshTris[i]] - meshWorldVers[meshTris[i + 1]], meshWorldVers[meshTris[i]] - meshWorldVers[meshTris[i + 2]]), poss[ii])).sqrMagnitude;
                }

                //if (disT < disP * 3.0f) disT = disP;
                if (disP < worldScaleDis) disT /= 9.0f;//The odds of this being true for "incorrect faces" and false for "correct faces" is besically 0%,
                                                       //so it should never cause a problem. Since disT is squared 9.0f = 3.0f
                                                       //disT = disP;

                //if (disT < bestD)
                if (disT < bestD)
                {
                    //bestD = disT;
                    bestD = disT;
                    bestI = i;
                    if (bestD == 0.0f) break;

                    //if (currentD < preExitTolerance) break;
                }
            }

            return bestI;
        }

        /// <summary>
        /// Returns the closest position to pos on the given mesh
        /// </summary>
        public static Vector3 ClosestPointOnMesh(Vector3[] meshWorldVers, int[] meshTris, Vector3 pos)
        {
            float bestD = float.MaxValue;
            Vector3 closePos = pos;
            float disT;
            Vector3 posT;

            for (int i = 0; i < meshTris.Length; i += 3)
            {
                posT = ClosestPointOnTriangle(meshWorldVers[meshTris[i]], meshWorldVers[meshTris[i + 1]], meshWorldVers[meshTris[i + 2]], pos);
                disT = (pos - posT).sqrMagnitude;

                if (disT < bestD)
                {
                    bestD = disT;
                    closePos = posT;
                }
            }

            return closePos;
        }

        public static Mesh MergeVerticesInMesh(Mesh originalMesh)
        {
            var verts = originalMesh.vertices;
            var normals = originalMesh.normals;
            var boneWe = originalMesh.boneWeights;
            var uvs = originalMesh.uv;
            bool hasUvs = uvs.Length > 0;
            bool hasBoneWe = boneWe.Length > 0;

            Dictionary<Vector3, int> duplicateHashTable = new();
            List<int> newVerts = new();
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
            var uvs2 = new Vector2[hasUvs == true ? newVerts.Count : 0];
            var boneWe2 = new BoneWeight[hasBoneWe == true ? newVerts.Count : 0];

            for (int i = 0; i < newVerts.Count; i++)
            {
                int a = newVerts[i];
                verts2[i] = verts[a];
                normals2[i] = normals[a];
                if (hasUvs == true) uvs2[i] = uvs[a];
                if (hasBoneWe == true) boneWe2[i] = boneWe[a];
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
            originalMesh.boneWeights = boneWe2;

            return originalMesh;
        }

        /// <summary>
        /// Removes all vectors from the vectors list that is similar enough to vectors[X]
        /// </summary>
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
        /// Returns 1 if dir2 point in the same direction as dir1, Returns -1 if dir2 point in the opposite direction as dir2. Always return 1 if dir1 or dir2 is zero
        /// </summary>
        /// <param name="dir1">Should be normalized</param>
        /// <param name="dir2">Should be normalized</param>
        public static float GetDirectionSimularity(Vector3 dir1, Vector3 dir2)
        {
            if (dir1.sqrMagnitude < 0.00001f || dir2.sqrMagnitude < 0.00001f) return 1.0f;
            return Vector3.Dot(dir1, dir2);
        }

        /// <summary>
        /// Returns true if the mesh is valid for fracturing
        /// </summary>
        public static bool IsMeshValid(Mesh mesh, bool checkIfValidHull = false, float EPSILON = 0.0001f)
        {
            if (mesh == null)
            {
                return false;
            }

            int trisCount = mesh.triangles.Length;
            if (mesh.vertexCount < 4 || trisCount < 7 || trisCount % 3 != 0)
            {
                return false;
            }

            if (checkIfValidHull == false) return true;
            return HasValidHull(mesh.vertices.ToList());

            bool HasValidHull(List<Vector3> points)
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
        }

        /// <summary>
        /// Returns 2 meshes, [0] is the one containing vertexIndexesToSplit. May remove tris at split edges if some tris vertics are in vertexIndexesToSplit and some not
        /// </summary>
        public static List<FractureThis.FracSource> SplitMeshInTwo(HashSet<int> vertexIndexesToSplit, FractureThis.FracSource orginalMeshD, bool doBones)
        {
            //get ogMesh tris, vers....
            Mesh oMesh = orginalMeshD.meshW;
            int[] oTris = oMesh.triangles;
            Vector3[] oVerts = oMesh.vertices;
            Vector3[] oNors = oMesh.normals;
            Vector2[] oUvs = oMesh.uv;
            BoneWeight[] oBones = doBones ? oMesh.boneWeights : new BoneWeight[oVerts.Length];
            Color[] oCols = oMesh.colors;

            //get what submesh each og triangel has
            int otL = oTris.Length;
            int[] ogTrisSubMeshI = new int[oTris.Length];//What submesh index a given vertex has in the ogMesh

            for (int sI = 0; sI < oMesh.subMeshCount; sI++)
            {
                SubMeshDescriptor subMesh = oMesh.GetSubMesh(sI);
                int indexEnd = (subMesh.indexStart / 3) + (subMesh.indexCount / 3);

                for (int tI = (subMesh.indexStart / 3); tI < indexEnd; tI++)
                {
                    ogTrisSubMeshI[tI] = sI;
                }
            }

            // Verify mesh properties and handle mismatches if necessary
            if (oUvs.Length != oVerts.Length)
            {
                Debug.LogWarning("The uvs for the mesh " + oMesh.name + " may not be valid");
                oUvs = new Vector2[oVerts.Length];
            }

            if (oNors.Length != oVerts.Length)
            {
                Debug.LogError("The mesh " + oMesh.name + " normal count does not match its vertex count");
                return null;
            }

            //create lists to assign the splitted mesh data to
            List<Vector3> splitVerA = new(oVerts.Length);
            List<Vector3> splitNorA = new(oNors.Length);
            List<Vector2> splitUvsA = new(oUvs.Length);
            List<int> splitTriA = new(otL);
            List<BoneWeight> splitBonA = new(oBones.Length);
            List<Color> splitColsA = new(oCols.Length);
            List<int> splitLinkA = new();

            List<Vector3> splitVerB = new(oVerts.Length);
            List<Vector3> splitNorB = new(oNors.Length);
            List<Vector2> splitUvsB = new(oUvs.Length);
            List<int> splitTriB = new(otL);
            List<BoneWeight> splitBonB = new(oBones.Length);
            List<Color> splitColsB = new(oCols.Length);
            List<int> splitLinkB = new();

            Dictionary<Vector3, int> vertexIndexMapA = new();
            Dictionary<Vector3, int> vertexIndexMapB = new();

            //######Everything works above this
            //split the mesh
            for (int i = 0; i < oTris.Length; i += 3)
            {
                int vIndexA = oTris[i];
                int vIndexB = oTris[i + 1];
                int vIndexC = oTris[i + 2];

                bool splitA = vertexIndexesToSplit.Contains(vIndexA) || vertexIndexesToSplit.Contains(vIndexB) || vertexIndexesToSplit.Contains(vIndexC);

                ProcessTriangle(vIndexA, vIndexB, vIndexC, splitA, i);
            }

            FractureThis.FracSource newMA = CreateMesh(splitVerA, splitNorA, splitUvsA, splitColsA, splitBonA, splitTriA, splitLinkA);
            FractureThis.FracSource newMB = CreateMesh(splitVerB, splitNorB, splitUvsB, splitColsB, splitBonB, splitTriB, splitLinkB);

            return new List<FractureThis.FracSource> { newMA, newMB };

            void ProcessTriangle(int vIndexA, int vIndexB, int vIndexC, bool splitA, int ogTrisI)
            {
                int newIndexA = GetIndexOfVertex(vIndexA, splitA, oVerts, oNors, oUvs, oCols, oBones);
                int newIndexB = GetIndexOfVertex(vIndexB, splitA, oVerts, oNors, oUvs, oCols, oBones);
                int newIndexC = GetIndexOfVertex(vIndexC, splitA, oVerts, oNors, oUvs, oCols, oBones);

                if (splitA)
                {
                    splitTriA.Add(newIndexA);
                    splitTriA.Add(newIndexB);
                    splitTriA.Add(newIndexC);
                    splitLinkA.Add(ogTrisI);
                }
                else
                {
                    splitTriB.Add(newIndexA);
                    splitTriB.Add(newIndexB);
                    splitTriB.Add(newIndexC);
                    splitLinkB.Add(ogTrisI);
                }
            }

            int GetIndexOfVertex(int vIndex, bool splitA, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, Color[] colors, BoneWeight[] boneWeights)
            {
                Vector3 vertex = vertices[vIndex];
                Vector3 normal = normals[vIndex];
                Vector2 uv = uvs[vIndex];
                Color color = colors[vIndex];
                BoneWeight boneWeight = boneWeights[vIndex];

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

            FractureThis.FracSource CreateMesh(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<Color> colors, List<BoneWeight> boneWeights, List<int> triangles, List<int> splitTrisOgTris)
            {
                Mesh mesh = new()
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

                if (triangles.Count == 0) return new() { meshW = mesh, mMats = new() };

                //set submesh                
                Dictionary<int, int> usedOgSubI = new();
                List<List<int>> newSubTrisI = new();
                List<Material> newSubMats = new();
                int stI, ogSubI, subI;

                for (int otI = 0; otI < splitTrisOgTris.Count; otI++)
                {
                    stI = otI * 3;
                    ogSubI = ogTrisSubMeshI[splitTrisOgTris[otI] / 3];
                    usedOgSubI.TryAdd(ogSubI, newSubTrisI.Count);
                    subI = usedOgSubI[ogSubI];

                    if (subI == newSubTrisI.Count)
                    {
                        newSubMats.Add(orginalMeshD.mMats[ogSubI]);
                        newSubTrisI.Add(new() { triangles[stI], triangles[stI + 1], triangles[stI + 2] });
                    }
                    else
                    {
                        newSubTrisI[subI].Add(triangles[stI]);
                        newSubTrisI[subI].Add(triangles[stI + 1]);
                        newSubTrisI[subI].Add(triangles[stI + 2]);
                    }
                }

                mesh.subMeshCount = newSubTrisI.Count;

                for (int i = 0; i < newSubTrisI.Count; i++)
                {
                    mesh.SetTriangles(newSubTrisI[i], i);
                }

                mesh.RecalculateBounds();
                mesh.RecalculateTangents();

                return new() { meshW = mesh, mMats = newSubMats };
            }
        }

        /// <summary>
        /// Returns a new instance of the given mesh but all submeshes are merged into one
        /// </summary>
        public static Mesh MergeSubMeshes(Mesh mesh)
        {
            return new() {
                vertices = mesh.vertices,
                triangles = mesh.triangles,
                normals = mesh.normals,
                bindposes = mesh.bindposes,
                boneWeights = mesh.boneWeights,
                uv = mesh.uv,
                colors = mesh.colors,
                bounds = mesh.bounds,
                tangents = mesh.tangents
            };
        }

        /// <summary>
        /// Returns all vertics connected to the given vertexIndex
        /// </summary>
        /// <param name="verDisTol">All vertics within this radius will count as the same vertex</param>
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
        /// <param name="verticsIds">Must have the same lenght as vertics</param>
        public static List<int> GetAllVertexIndexesAtPos_id(Vector3[] vertics, int[] verticsIds, Vector3 pos, int id, float verDisTol = 0.0001f)
        {
            List<int> vAtPos = new();

            for (int i = 0; i < vertics.Length; i += 1)
            {
                if ((vertics[i] - pos).magnitude < verDisTol && verticsIds[i] == id)
                {
                    vAtPos.Add(i);
                }
            }

            return vAtPos;
        }

        /// <summary>
        /// Adds all group ids that exists in the given color array to the groupIds hashset
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
        /// Returns the id from the color, returns null if base id
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

        public static void GetLinksFromColors(HashSet<Color> colors, ref HashSet<float> gLinks)
        {
            foreach (Color col in colors)
            {
                foreach (float link in Gd_getLinksFromColor(col))
                {
                    gLinks.Add(link);
                }
            }
        }

        public static List<float> Gd_getLinksFromColor(Color color)
        {
            List<float> gLinks = new();
            if (color.r <= 0.5f && color.r > 0.0f) gLinks.Add(color.r);
            if (color.g <= 0.5f && color.g > 0.0f) gLinks.Add(color.g);
            if (color.b <= 0.5f && color.b > 0.0f) gLinks.Add(color.b);
            if (color.a <= 0.5f && color.a > 0.0f) gLinks.Add(color.a);

            return gLinks;
        }

        /// <summary>
        /// Returns true if the given group id matches the id in the color
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
        /// Returns true if colorA is linked with colorB (They can be connected)
        /// </summary>
        public static bool Gd_isColorLinkedWithColor(Color colorA, Color colorB)
        {
            //get if connected with id
            List<float> colAId = Gd_getIdFromColor(colorA);
            List<float> colBId = Gd_getIdFromColor(colorB);
            bool aIsPrim = colAId == null || (colBId != null && colAId.Count < colBId.Count);
            if (CheckPrimIdWithSecId(aIsPrim == true ? colAId : colBId, aIsPrim == false ? colAId : colBId) == true) return true;

            //get if connected with links
            HashSet<float> colBs = new() { colorB.r, colorB.g, colorB.b, colorB.a };
            if (colorA.r <= 0.5f && colorA.r > 0.0f && colBs.Contains(colorA.r) == true) return true;
            if (colorA.g <= 0.5f && colorA.g > 0.0f && colBs.Contains(colorA.g) == true) return true;
            if (colorA.b <= 0.5f && colorA.b > 0.0f && colBs.Contains(colorA.b) == true) return true;
            if (colorA.a <= 0.5f && colorA.a > 0.0f && colBs.Contains(colorA.a) == true) return true;
            return false;

            static bool CheckPrimIdWithSecId(List<float> primIds, List<float> secIds)
            {
                if (primIds == null) return secIds == null;

                foreach (float primId in primIds)
                {
                    if (secIds.Contains(primId) == false) return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Returns true if partA is linked with partB (They can be connected)
        /// </summary>
        public static bool Gd_isPartLinkedWithPart(FractureThis.FracPart partA, FractureThis.FracPart partB)
        {
            //get if connected with id
            List<float> partAId = partA.groupId;
            List<float> partBId = partB.groupId;
            bool aIsPrim = partAId == null || (partBId != null && partAId.Count < partBId.Count);
            if (CheckPrimIdWithSecId(aIsPrim == true ? partAId : partBId, aIsPrim == false ? partAId : partBId) == true) return true;

            //get if connected with links
            foreach (float gLink in partA.groupLinks)
            {
                if (partB.groupLinks.Contains(gLink) == true) return true;
            }

            return false;

            static bool CheckPrimIdWithSecId(List<float> primIds, List<float> secIds)
            {
                if (primIds == null) return secIds == null;

                foreach (float primId in primIds)
                {
                    if (secIds.Contains(primId) == false) return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Returns the index of all vertices that has the given id
        /// </summary>
        public static HashSet<int> Gd_getAllVerticesInId(Color[] verColors, List<float> id)
        {
            HashSet<int> verInId = new();
            int verL = verColors.Length;

            for (int vI = 0; vI < verL; vI++)
            {
                if (Gd_isIdInColor(id, verColors[vI]) == true) verInId.Add(vI);
            }

            return verInId;
        }

        /// <summary>
        /// Returns the index of all vertices that has the given id and exists inside the potentialVertices hashset
        /// </summary>
        public static HashSet<int> Gd_getSomeVerticesInId(Color[] verColors, List<float> id, HashSet<int> potentialVertices)
        {
            HashSet<int> verInId = new();

            foreach (int vI in potentialVertices)
            {
                if (Gd_isIdInColor(id, verColors[vI]) == true) verInId.Add(vI);
            }

            return verInId;
        }

        /// <summary>
        /// Returns a int that reprisent the id
        /// </summary>
        public static int Gd_getIntIdFromId(List<float> id)
        {
            if (id == null || id.Count == 0)
            {
                // Return a default hash code for an empty list
                return 0;
            }

            int hashCode = 0;
            foreach (float value in id)
            {
                // Convert each float to an integer by multiplying by 2^32
                int intValue = (int)(value * int.MaxValue);
                // XOR the integer representation of each float to the hash code
                hashCode ^= intValue;
            }

            return hashCode;
        }

        /// <summary>
        /// Transforms normals, vertices and mesh bounds of the given mesh by the given matrix
        /// </summary>
        public static void ConvertMeshWithMatrix(ref Mesh mesh, Matrix4x4 lTwMatrix)
        {
            //mesh.SetVertices(ConvertPositionsWithMatrix(mesh.vertices, lTwMatrix));
            mesh.SetVertices(ConvertPositionsWithMatrix(mesh.vertices, lTwMatrix));
            mesh.SetNormals(ConvertDirectionsWithMatrix(mesh.normals, lTwMatrix));
            mesh.RecalculateBounds();
            //return mesh;
        }

        /// <summary>
        /// Returns a float for each mesh that is each mesh size compared to each other. All floats added = 1.0f
        /// </summary>
        /// <param name="useMeshBounds">If true, Mesh.bounds is used to get scales (Much faster)</param>
        public static List<float> GetPerMeshScale(Mesh[] meshes, bool useMeshBounds = true)
        {
            List<float> meshVolumes = new();
            float totalVolume = 0.0f;
            for (int i = 0; i < meshes.Length; i += 1)
            {
                if (useMeshBounds == false) meshVolumes.Add(meshes[i].Volume());
                else meshVolumes.Add(GetBoundingBoxVolume(meshes[i].bounds));
                totalVolume += meshVolumes[i];
            }

            float perMCost = 1.0f / totalVolume;
            for (int i = 0; i < meshes.Length; i += 1)
            {
                meshVolumes[i] = perMCost * meshVolumes[i];
            }

            return meshVolumes;
        }

        /// <summary>
        /// Returns the volume of the bounds (How much water it can contain)
        /// </summary>
        public static float GetBoundingBoxVolume(Bounds bounds)
        {
            // Calculate the volume using the size of the bounds
            float volume = bounds.size.x * bounds.size.y * bounds.size.z;

            return volume;
        }

        /// <summary>
        /// Returns the most similar ver+tris in sourceMW for every ver+tris in newMW. newMW and sourceMW must in worldspace
        /// </summary>
        public static void GetMostSimilarTris(Mesh newMW, Mesh sourceMW, out int[] nVersBestSVer, out int[] nTrisBestSTri, float worldScale = 1.0f)
        {
            //get the most similar og triangel for every triangel on the comMesh
            int[] sTris = sourceMW.triangles;
            Vector3[] sVers = sourceMW.vertices;

            int[] nTris = newMW.triangles;
            Vector3[] nVers = newMW.vertices;

            int ntL = nTris.Length / 3;
            NativeArray<int> closeOTris = new(ntL, Allocator.Temp);
            int nvL = nVers.Length;
            int[] closeOVer = Enumerable.Repeat(-1, nvL).ToArray();

            //Debug_drawMesh(newMW, false, 10.0f);
            //Debug_drawMesh(sourceMW, false, 10.0f);

            Parallel.For(0, ntL, i =>
            {
                int tI = i * 3;

                closeOTris[i] = FractureHelperFunc.GetClosestTriOnMesh(
                    sVers,
                    sTris,
                    new Vector3[3] { nVers[nTris[tI]], nVers[nTris[tI + 1]], nVers[nTris[tI + 2]] },
                    worldScale);

                Vector3[] oTrisPoss = new Vector3[3] { sVers[sTris[closeOTris[i]]], sVers[sTris[closeOTris[i] + 1]], sVers[sTris[closeOTris[i] + 2]] };

                ////debug stuff
                //Vector3[] tPoss = new Vector3[3] { nVers[nTris[tI]], nVers[nTris[tI + 1]], nVers[nTris[tI + 2]] };
                //foreach (Vector3 pos in tPoss)
                //{
                //    Debug.DrawLine(pos, ClosestPointOnTriangle(oTrisPoss[0], oTrisPoss[1], oTrisPoss[2], pos), Color.yellow, 10.0f);
                //}

                lock (closeOVer)
                {
                    if (closeOVer[nTris[tI]] < 0)
                    {
                        closeOVer[nTris[tI]] = sTris[closeOTris[i] + FractureHelperFunc.GetClosestPointInArray(
                          oTrisPoss,
                          nVers[nTris[tI]])];

                        //if (oI == 0 && Vector3.Distance(oVerss[oI][closeOVer[cTris[tI]]], cVers[cTris[tI]]) > 0.001f) Debug.DrawLine(oVerss[oI][closeOVer[cTris[tI]]], cVers[cTris[tI]], Color.magenta, 30.0f);
                    }

                    if (closeOVer[nTris[tI + 1]] < 0)
                    {
                        closeOVer[nTris[tI + 1]] = sTris[closeOTris[i] + FractureHelperFunc.GetClosestPointInArray(
                          oTrisPoss,
                          nVers[nTris[tI + 1]])];

                        //if (oI == 0 && Vector3.Distance(oVerss[oI][closeOVer[cTris[tI + 1]]], cVers[cTris[tI + 1]]) > 0.001f) Debug.DrawLine(oVerss[oI][closeOVer[cTris[tI + 1]]], cVers[cTris[tI + 1]], Color.magenta, 30.0f);
                    }

                    if (closeOVer[nTris[tI + 2]] < 0)
                    {
                        closeOVer[nTris[tI + 2]] = sTris[closeOTris[i] + FractureHelperFunc.GetClosestPointInArray(
                          oTrisPoss,
                          nVers[nTris[tI + 2]])];

                        //if (oI == 0 && Vector3.Distance(oVerss[oI][closeOVer[cTris[tI + 2]]], cVers[cTris[tI + 2]]) > 0.001f) Debug.DrawLine(oVerss[oI][closeOVer[cTris[tI + 2]]], cVers[cTris[tI + 2]], Color.magenta, 30.0f);
                    }
                }
            });

            nVersBestSVer = closeOVer;
            nTrisBestSTri = closeOTris.ToArray();
            closeOTris.Dispose();
        }

        /// <summary>
        /// Returns a mesh that is as similar to sourceMesh as possible while being convex, sourceMeshW must be in worldspace
        /// </summary>
        public static Mesh MakeMeshConvex(Mesh sourceMeshW, bool verticsOnly = false, float worldScale = 1.0f)
        {   
            var calc = new QuickHull_convex();
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var normals = new List<Vector3>();

            bool didMake = calc.GenerateHull(sourceMeshW.vertices.ToList(), !verticsOnly, ref verts, ref tris, ref normals);

            if (didMake == false || verts.Count >= sourceMeshW.vertexCount)
            {
                //When unable to make convex or convex has more vertics than source, just use the source mesh
                if (verticsOnly == true)
                {
                    verts = sourceMeshW.vertices.ToList();
                    MergeSimilarVectors(ref verts, worldScale * 0.0001f);
                }
                else
                {
                    return UnityEngine.Object.Instantiate(sourceMeshW);
                    //tris = sourceMeshW.triangles.ToList();
                    //normals = sourceMeshW.normals.ToList();
                }
            }

            Mesh convexMeshW = new();
            convexMeshW.SetVertices(verts);
            if (verticsOnly == true) return convexMeshW;

            convexMeshW.SetTriangles(tris, 0);
            convexMeshW.SetNormals(normals);

            GetMostSimilarTris(convexMeshW, sourceMeshW, out int[] cVersBestSVer, out int[] cTrisBestSTri, worldScale);
            //if (GetMostSimilarTris_reversed(convexMeshW, sourceMeshW, out int[] cVersBestSVer, out int[] cTrisBestSTri) == false) return convexMeshW;

            //Vector3[] vers = sourceMeshW.vertices;
            //for (int i = 0; i < cVersBestSVer.Length; i++)
            //{
            //    Debug.DrawLine(verts[i], vers[cVersBestSVer[i]], Color.red, 10.0f);
            //}

            SetMeshFromOther(ref convexMeshW, sourceMeshW, cVersBestSVer, cTrisBestSTri, GetTrisSubMeshI(sourceMeshW), true);

            return convexMeshW;
        }

        /// <summary>
        /// Gets what submesh every triangel in the given mesh has, returned array has the same lenght as mesh.tris
        /// </summary>
        public static int[] GetTrisSubMeshI(Mesh mesh)
        {
            int[] sTrisSubMeshI = new int[mesh.triangles.Length];
            //int count = 0;

            for (int subI = 0; subI < mesh.subMeshCount; subI++)
            {
                SubMeshDescriptor subMesh = mesh.GetSubMesh(subI);
                //int indexEnd = (subMesh.indexStart / 3) + (subMesh.indexCount / 3);
                int indexEnd = (subMesh.indexStart) + (subMesh.indexCount);

                //for (int tI = subMesh.indexStart / 3; tI < indexEnd; tI++)
                for (int tI = subMesh.indexStart; tI < indexEnd; tI++)
                {
                    //count++;
                    //Debug.Log(tI);
                    sTrisSubMeshI[tI] = subI;
                }
            }

            //Debug.Log(count + " " + sTrisSubMeshI.Length);
            return sTrisSubMeshI;
        }

        /// <summary>
        /// Returns a list containing all tris that exists in the given subMeshI, list[X], list[X + 1] ,list[X + 2] = versForSubTrisX
        /// </summary>
        public static List<int> GetAllTrisInSubMesh(Mesh mesh, int subMeshI)
        {
            if (mesh.subMeshCount <= subMeshI) return new();
            return mesh.GetTriangles(subMeshI).ToList();
        }

        public static HashSet<int> GetAllVersInSubMesh(Mesh mesh, int subMeshI)
        {
            if (mesh.subMeshCount <= subMeshI) return new();

            HashSet<int> subVers = new();
            foreach (int vI in mesh.GetTriangles(subMeshI))
            {
                subVers.Add(vI);
            }

            return subVers;
        }

        public static void SetMeshInsideMats(ref Mesh mesh, ref List<Material> mMats, HashSet<int> insideVers, Dictionary<Material, Material> matInsideMat)
        {
            if (mesh.subMeshCount != mMats.Count)
            {
                Debug.LogError("Unable to get inside materials because the given mMats array is not valid for the given mesh");
                return;
            }

            List<List<int>> subMeshTris = new();
            int matCount = mMats.Count;

            for (int subI = 0; subI < matCount; subI++)
            {
                //get all tris in subI
                subMeshTris.Add(GetAllTrisInSubMesh(mesh, subI));

                //continue if this subI does not have a inside material
                if (matInsideMat.ContainsKey(mMats[subI]) == false) continue;

                //get all tris that is inside and add them to a new submesh
                int insideSubI = -1;

                for (int stI = subMeshTris[subI].Count - 3; stI >= 0; stI -= 3)
                {
                    if (insideVers.Contains(subMeshTris[subI][stI]) == true
                        || insideVers.Contains(subMeshTris[subI][stI + 1]) == true
                        || insideVers.Contains(subMeshTris[subI][stI + 2]) == true)
                    {
                        if (insideSubI < 0)
                        {
                            insideSubI = subMeshTris.Count;
                            subMeshTris.Add(new());
                            mMats.Add(matInsideMat[mMats[subI]]);
                        }

                        subMeshTris[insideSubI].Add(subMeshTris[subI][stI]);
                        subMeshTris[insideSubI].Add(subMeshTris[subI][stI + 1]);
                        subMeshTris[insideSubI].Add(subMeshTris[subI][stI + 2]);
                        subMeshTris[subI].RemoveRange(stI, 3);
                    }
                }
            }

            //remove unused submeshes
            for (int subI = subMeshTris.Count - 1; subI >= 0; subI--)
            {
                if (subMeshTris[subI].Count > 0) continue;

                subMeshTris.RemoveAt(subI);
                mMats.RemoveAt(subI);
            }

            //set submeshes
            mesh.subMeshCount = subMeshTris.Count;
            for (int subI = subMeshTris.Count - 1; subI >= 0; subI--)
            {
                mesh.SetTriangles(subMeshTris[subI], subI);
            }
        }

        /// <summary>
        /// Sets newMesh uvs+submeshes from the best sourceMesh uvs+submeshes, the returned list is what source subMesh every new subMesh reprisent
        /// </summary>
        public static List<int> SetMeshFromOther(ref Mesh newMesh, Mesh sourceMesh, int[] nVersBestSVer, int[] nTrisBestSTri, int[] sTrisSubMeshI, bool setUvs = true)
        {
            //set uvs
            if (setUvs == true)
            {
                Vector2[] sUvs = sourceMesh.uv;
                Vector2[] nUvs = new Vector2[newMesh.vertexCount];

                for (int nvI = 0; nvI < nUvs.Length; nvI++)
                {
                    nUvs[nvI] = sUvs[nVersBestSVer[nvI]];
                }

                newMesh.uv = nUvs;
            }

            //set boneWeights
            if (sourceMesh.bindposes.Length > 0 && sourceMesh.boneWeights.Length > 0)
            {
                newMesh.bindposes = sourceMesh.bindposes;
                BoneWeight[] nBoneWe = new BoneWeight[newMesh.vertexCount];
                BoneWeight[] sBoneWe = sourceMesh.boneWeights;

                for (int nvI = 0; nvI < nBoneWe.Length; nvI++)
                {
                    nBoneWe[nvI] = sBoneWe[nVersBestSVer[nvI]];
                }

                newMesh.boneWeights = nBoneWe;
            }

            //set submeshes
            //Creates two lists with the same lenght as source subMesh count. We cant use arrays since we will remove unused items later
            List<int> nSubSSubI = new();
            List<List<int>> nSubTris = new();

            for (int sI = 0; sI < sourceMesh.subMeshCount; sI++)
            {
                nSubTris.Add(new());
                nSubSSubI.Add(sI);
            }

            //get what source submesh each triangel in newMesh should have
            int[] nTris = newMesh.triangles;
            int triCount = nTris.Length / 3;

            for (int i = 0; i < triCount; i++)
            {
                int ntI = i * 3;
                int ssI = sTrisSubMeshI[nTrisBestSTri[i]];
               
                nSubTris[ssI].Add(nTris[ntI]);
                nSubTris[ssI].Add(nTris[ntI + 1]);
                nSubTris[ssI].Add(nTris[ntI + 2]);
            }
            
            //remove unused submeshes
            for (int nsI = nSubTris.Count - 1; nsI >= 0; nsI--)
            {
                if (nSubTris[nsI].Count > 0) continue;
                    
                nSubTris.RemoveAt(nsI);
                nSubSSubI.RemoveAt(nsI);
            }

            //set newMesh submeshes
            newMesh.subMeshCount = nSubTris.Count;

            for (int i = 0; i < nSubTris.Count; i++)
            {
                newMesh.SetTriangles(nSubTris[i], i);
            }

            return nSubSSubI;
        }

        /// <summary>
        /// Combines the meshes and updates fracParts rendVertexIndexes list
        /// </summary>
        /// <param name="fracMeshes">Must have same lenght as fracParts</param>
        public static Mesh CombineMeshes(Mesh[] fracMeshes, ref FractureThis.FracPart[] fracParts)
        {
            Debug.LogError("Not yet implemented??");
            return null;

            //int partCount = fracParts.Length;
            //Mesh comMesh = new();
            //comMesh.indexFormat = IndexFormat.UInt32;
            //List<CombineInstance> comMeshes = new();
            //List<bool> comHadSub = new();
            //int subMeshCount = 0;
            //
            //for (int i = 0; i < partCount; i++)
            //{
            //    comMeshes.Add(new() { mesh = fracMeshes[i], subMeshIndex = 0 });
            //    subMeshCount++;
            //    if (fracMeshes[i].subMeshCount > 1)
            //    {
            //        comMeshes.Add(new() { mesh = fracMeshes[i], subMeshIndex = 1 });
            //        comHadSub.Add(true);
            //    }
            //    else comHadSub.Add(false);
            //}
            //
            //comMesh.CombineMeshes(comMeshes.ToArray(), false, false, false);//if we lucky, combinedMesh submesh[0] == comMesh[0], if thats the case we can just use unity combine
            //comMesh.Optimize();//Should be safe to call since submeshes order does not change?
            //Debug_doesMeshContainUnusedVers(comMesh);
            //int partI = 0;
            //List<int> newSubTrisA = new();
            //List<int> newSubTrisB = new();
            //
            //for (int comI = 0; comI < comMeshes.Count; comI++)
            //{
            //    foreach (int vI in comMesh.GetTriangles(comI))
            //    {
            //        newSubTrisA.Add(vI);
            //        if (fracParts[partI].rendLinkVerIndexes.Contains(vI) == false) fracParts[partI].rendLinkVerIndexes.Add(vI);
            //    }
            //
            //    if (comHadSub[partI] == false)
            //    {
            //        partI++;
            //        continue;
            //    }
            //
            //    comI++;
            //    foreach (int vI in comMesh.GetTriangles(comI))
            //    {
            //        newSubTrisB.Add(vI);
            //        if (fracParts[partI].rendLinkVerIndexes.Contains(vI) == false) fracParts[partI].rendLinkVerIndexes.Add(vI);
            //    }
            //
            //    partI++;
            //}
            //
            //comMesh.subMeshCount = newSubTrisB.Count > 0 ? 2 : 1;
            //comMesh.SetTriangles(newSubTrisA, 0);
            //if (newSubTrisB.Count > 0) comMesh.SetTriangles(newSubTrisB, 1);
            //
            //Debug_doesMeshContainUnusedVers(comMesh);
            //return comMesh;
        }

        /// <summary>
        /// Returns the angular velocity
        /// </summary>
        public static Vector3 GetAngularVelocity(Quaternion prevRot, Quaternion currentRot, float deltaTime)
        {
            var q = currentRot * Quaternion.Inverse(prevRot);
            // no rotation?
            // You may want to increase this closer to 1 if you want to handle very small rotations.
            // Beware, if it is too close to one your answer will be Nan
            if (Mathf.Abs(q.w) > 1023.5f / 1024.0f)
                return new Vector3(0, 0, 0);
            float gain;
            // handle negatives, we could just flip it but this is faster
            if (q.w < 0.0f)
            {
                var angle = Mathf.Acos(-q.w);
                gain = -2.0f * angle / (Mathf.Sin(angle) * deltaTime);
            }
            else
            {
                var angle = Mathf.Acos(q.w);
                gain = 2.0f * angle / (Mathf.Sin(angle) * deltaTime);
            }

            return new Vector3(q.x * gain, q.y * gain, q.z * gain);
        }

        [BurstCompile]
        public static Vector3 GetObjectVelocityAtPoint(Matrix4x4 objWToLPrev, Matrix4x4 objLToWNow, Vector3 point, float deltatime)
        {
            //for what ever reason transforming (point - velOffset) seems to give slightly better result at the cost of 2 extra transformations??
            Vector3 velOffset = point - (objLToWNow.MultiplyPoint3x4(objWToLPrev.MultiplyPoint3x4(point)) - point);
            return (objLToWNow.MultiplyPoint3x4(objWToLPrev.MultiplyPoint3x4(velOffset)) - velOffset) / deltatime;

            //The one below is faster and in theory it should be more accurate but it aint???
            //return (objLToWNow.MultiplyPoint3x4(objWToLPrev.MultiplyPoint3x4(point)) - point) / deltatime;
        }

        /// <summary>
        /// Returns a unique hash, the order of the values does not matter (0,0,1)=(0,1,0). THE VALUES IN THE GIVEN NativeArray WILL BE MODIFIED!
        /// </summary>
        /// <param name="inputInts"></param>
        /// <returns></returns>
        [BurstCompile]
        public static int GetHashFromInts(ref NativeArray<int> inputInts)
        {
            inputInts.Sort();

            unchecked
            {
                int hash = 17;
                foreach (int inputInt in inputInts)
                {
                    hash = hash * 31 + inputInt.GetHashCode();
                }

                return hash;
            }
        }

        public static void ClampMagnitude(ref Vector3 vector, float maxLength)
        {
            float num = vector.sqrMagnitude;
            if (num > maxLength * maxLength)
            {
                float num2 = (float)Math.Sqrt(num);
                vector.x = (vector.x / num2) * maxLength;
                vector.y = (vector.y / num2) * maxLength;
                vector.z = (vector.z / num2) * maxLength;
            }
        }

        public static Vector3 RotatePointAroundPivot(Vector3 point, Vector3 pivot, Quaternion rotation)
        {
            // Translate the point so that the pivot becomes the origin
            Vector3 offset = point - pivot;

            // Apply the rotation to the offset
            Vector3 rotatedOffset = rotation * offset;

            // Translate the rotated point back to its original position
            Vector3 rotatedPoint = rotatedOffset + pivot;

            return rotatedPoint;
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

        public static Vector3 ClosestPointOnPlaneInfinit(Vector3 planePos, Vector3 planeNor, Vector3 queryPoint)
        {
            return queryPoint + (-Vector3.Dot(planeNor, queryPoint - planePos) / Vector3.Dot(planeNor, planeNor)) * planeNor;
        }

        public static Vector3 ClosestPointOnTriangle(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 queryPoint)
        {
            //first get closest point on plane
            Vector3 pNor = Vector3.Cross(p0 - p1, p0 - p2);
            Vector3 closeP = queryPoint + (-Vector3.Dot(pNor, queryPoint - p0) / Vector3.Dot(pNor, pNor)) * pNor;

            //if the closest point on plane is inside the triangel, return closest planePoint
            if (PointInTriangle(closeP) == true) return closeP;

            //return closest point on the closest edge
            //Vector3[] closeL = new Vector3[3] { ClosestPointOnLine(p0, p1, queryPoint), ClosestPointOnLine(p1, p2, queryPoint), ClosestPointOnLine(p0, p2, queryPoint) };
            Vector3 pLine0 = ClosestPointOnLine(p0, p1, queryPoint);
            Vector3 pLine1 = ClosestPointOnLine(p1, p2, queryPoint);
            Vector3 pLine2 = ClosestPointOnLine(p0, p2, queryPoint);

            float cLine0 = (pLine0 - queryPoint).sqrMagnitude;
            float cLine1 = (pLine1 - queryPoint).sqrMagnitude;
            float cLine2 = (pLine2 - queryPoint).sqrMagnitude;

            if (cLine0 < cLine1)
            {
                //0 or 2 is closest
                if (cLine2 < cLine0) return pLine2;
                return pLine0;
            }

            if (cLine1 < cLine2)
            {
                //1 is closest. We checked 0 before
                return pLine1;
            }

            return pLine2;


            //return closeL[GetClosestPointInArray(closeL, queryPoint, 0.0f)];

            bool PointInTriangle(Vector3 p)
            {
                // Lets define some local variables, we can change these
                // without affecting the references passed in
                Vector3 a = p0 - p;
                Vector3 b = p1 - p;
                Vector3 c = p2 - p;

                // The point should be moved too, so they are both
                // relative, but because we don't use p in the
                // equation anymore, we don't need it!
                // p -= p;

                // Compute the normal vectors for triangles:
                // u = normal of PBC
                // v = normal of PCA
                // w = normal of PAB

                Vector3 u = Vector3.Cross(b, c);
                Vector3 v = Vector3.Cross(c, a);
                Vector3 w = Vector3.Cross(a, b);

                // Test to see if the normals are facing 
                // the same direction, return false if not
                if (Vector3.Dot(u, v) < 0f)
                {
                    return false;
                }
                if (Vector3.Dot(u, w) < 0.0f)
                {
                    return false;
                }

                // All normals facing the same way, return true
                return true;
            }

            //// Calculate triangle normal
            //Vector3 triNorm = Vector3.Cross(pointA - pointB, pointA - pointC);
            //
            //// Calculate the projection of the query point onto the triangle plane
            //Plane triPlane = new(triNorm, pointA);
            //triPlane.Set3Points(pointA, pointB, pointC);
            //Vector3 projectedPoint = triPlane.ClosestPointOnPlane(queryPoint);
            //return projectedPoint;
            //
            //// Check if the projected point is inside the triangle
            //if (IsPointInTriangle(projectedPoint, pointA, pointB, pointC))
            //{
            //    return projectedPoint;
            //}
            //else
            //{
            //    // If not, find the closest point on each triangle edge
            //    Vector3 closestOnAB = ClosestPointOnSegment(pointA, pointB, queryPoint);
            //    Vector3 closestOnBC = ClosestPointOnSegment(pointB, pointC, queryPoint);
            //    Vector3 closestOnCA = ClosestPointOnSegment(pointC, pointA, queryPoint);
            //
            //    // Find the closest point among these three
            //    float distAB = Vector3.Distance(queryPoint, closestOnAB);
            //    float distBC = Vector3.Distance(queryPoint, closestOnBC);
            //    float distCA = Vector3.Distance(queryPoint, closestOnCA);
            //
            //    if (distAB < distBC && distAB < distCA)
            //        return closestOnAB;
            //    else if (distBC < distCA)
            //        return closestOnBC;
            //    else
            //        return closestOnCA;
            //}
            //
            //
            //bool IsPointInTriangle(Vector3 point, Vector3 A, Vector3 B, Vector3 C)
            //{
            //
            //    // Check if the point is inside the triangle using barycentric coordinates
            //    float alpha = ((B - C).y * (point.x - C.x) + (C - B).x * (point.y - C.y)) /
            //                  ((B - C).y * (A.x - C.x) + (C - B).x * (A.y - C.y));
            //
            //    float beta = ((C - A).y * (point.x - C.x) + (A - C).x * (point.y - C.y)) /
            //                 ((B - C).y * (A.x - C.x) + (C - B).x * (A.y - C.y));
            //
            //    float gamma = 1.0f - alpha - beta;
            //
            //    return alpha > 0 && beta > 0 && gamma > 0;
            //}
            //
            //Vector3 ClosestPointOnSegment(Vector3 start, Vector3 end, Vector3 queryPoint)
            //{
            //    // Find the closest point on a line segment
            //    Vector3 direction = end - start;
            //    float t = Mathf.Clamp01(Vector3.Dot(queryPoint - start, direction) / direction.sqrMagnitude);
            //    return start + t * direction;
            //}
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
        public static void SetColliderFromFromPoints(Collider col, Vector3[] possLocal, ref float newMaxExtent)
        {
            Transform colTrans = col.transform;
            Vector3 extents;

            if (col is MeshCollider mCol)
            {
                mCol.sharedMesh.SetVertices(possLocal, 0, possLocal.Length,
                      UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices
                | UnityEngine.Rendering.MeshUpdateFlags.DontResetBoneBounds
                | UnityEngine.Rendering.MeshUpdateFlags.DontNotifyMeshUsers
                | UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);

                extents = col.bounds.extents;
            }
            else if (col is BoxCollider bCol)
            {
                bCol.center = FractureHelperFunc.GetGeometricCenterOfPositions(possLocal);
                possLocal = FractureHelperFunc.ConvertPositionsWithMatrix(possLocal, colTrans.localToWorldMatrix);

                extents = Vector3.one * 0.001f;
                float cDis;
                Vector3 tPos = bCol.bounds.center;
                Vector3 tFor = colTrans.forward;
                Vector3 tSide = colTrans.right;
                Vector3 tUp = colTrans.up;

                foreach (Vector3 wPos in possLocal)
                {
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLineInfinit(wPos, tPos, tFor), tPos);
                    if (cDis > extents.z) extents.z = cDis;
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLineInfinit(wPos, tPos, tSide), tPos);
                    if (cDis > extents.x) extents.x = cDis;
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLineInfinit(wPos, tPos, tUp), tPos);
                    if (cDis > extents.y) extents.y = cDis;
                }

                bCol.size = extents * 2.0f;
                extents = colTrans.TransformVector(extents);
            }
            else if (col is SphereCollider sCol)
            {
                sCol.center = FractureHelperFunc.GetGeometricCenterOfPositions(possLocal);
                possLocal = FractureHelperFunc.ConvertPositionsWithMatrix(possLocal, colTrans.localToWorldMatrix);

                extents = Vector3.one * 0.001f;
                float cDis;
                Vector3 tPos = sCol.bounds.center;
                Vector3 tFor = colTrans.forward;
                Vector3 tSide = colTrans.right;
                Vector3 tUp = colTrans.up;

                foreach (Vector3 wPos in possLocal)
                {
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLineInfinit(wPos, tPos, tFor), tPos);
                    if (cDis > extents.z) extents.z = cDis;
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLineInfinit(wPos, tPos, tSide), tPos);
                    if (cDis > extents.x) extents.x = cDis;
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLineInfinit(wPos, tPos, tUp), tPos);
                    if (cDis > extents.y) extents.y = cDis;
                }

                extents.Scale(colTrans.localToWorldMatrix.lossyScale);
                sCol.radius = Mathf.Max(extents.x, extents.y, extents.z);
                extents = colTrans.TransformVector(extents);
            }
            else if (col is CapsuleCollider cCol)
            {
                cCol.center = FractureHelperFunc.GetGeometricCenterOfPositions(possLocal);
                possLocal = FractureHelperFunc.ConvertPositionsWithMatrix(possLocal, colTrans.localToWorldMatrix);

                extents = Vector3.one * 0.001f;
                float cDis;
                Vector3 tPos = cCol.bounds.center;
                Vector3 tFor = colTrans.forward;
                Vector3 tSide = colTrans.right;
                Vector3 tUp = colTrans.up;

                foreach (Vector3 wPos in possLocal)
                {
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLineInfinit(wPos, tPos, tFor), tPos);
                    if (cDis > extents.z) extents.z = cDis;
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLineInfinit(wPos, tPos, tSide), tPos);
                    if (cDis > extents.x) extents.x = cDis;
                    cDis = Vector3.Distance(FractureHelperFunc.ClosestPointOnLineInfinit(wPos, tPos, tUp), tPos);
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

                extents = colTrans.TransformVector(extents);
            }
            else
            {
                Debug.LogError(col.GetType() + " colliders are currently not supported, please only use Mesh, Box, Sphere and Capsule colliders!");
                return;
            }

            //Update max extents
            if (extents.x > newMaxExtent) newMaxExtent = extents.x;
            if (extents.y > newMaxExtent) newMaxExtent = extents.y;
            if (extents.z > newMaxExtent) newMaxExtent = extents.z;

            //renable collider to fix bug??
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
        public static Vector3 SubtractMagnitude(Vector3 vector, float amount)
        {
            if (vector.magnitude <= amount) return Vector3.zero;
            return vector.normalized * (vector.magnitude - amount);
        }

        /// <summary>
        /// Returns the relative velocity, if A move forward 4 and B moves forward 10 result is 6
        /// </summary>
        public static Vector3 GetRelativeVelocity(Vector3 velA, Vector3 velB)
        {
            if (velA.sqrMagnitude > velB.sqrMagnitude)
            {
                return velA - velB;
            }
            else
            {
                return velB - velA;
            }
        }

        /// <summary>
        /// Returns the square magnitude of the quaternion, like Vector3.SqrMagnitude
        /// </summary>
        public static float QuaternionSqrMagnitude(Quaternion quaternion)
        {
            return quaternion.x * quaternion.x + quaternion.y * quaternion.y + quaternion.z * quaternion.z + quaternion.w * quaternion.w;
        }

        /// <summary>
        /// Returns current lerped towards target by t and moved current towards target by at least min distance (Cant move past target)
        /// </summary>
        [BurstCompile]
        public static Vector3 Vec3LerpMin(Vector3 current, Vector3 target, float t, float min, out bool reachedTarget)
        {
            float dis = (target - current).magnitude;
            if (dis <= min)
            {
                reachedTarget = true;
                return target;
            }

            dis *= t;
            if (dis < min) dis = min;

            reachedTarget = false;
            return Vector3.MoveTowards(current, target, dis);
        }

        /// <summary>
        /// Returns current lerped towards target by t and rotated current towards target by at least min degrees (Cant rotate past target)
        /// </summary>
        [BurstCompile]
        public static Quaternion QuatLerpMin(Quaternion current, Quaternion target, float t, float min, out bool reachedTarget)
        {
            float ang = Quaternion.Angle(current, target);
            if (ang <= min)
            {
                reachedTarget = true;
                return target;
            }

            ang *= t;
            if (ang < min) ang = min;

            reachedTarget = false;
            return Quaternion.RotateTowards(current, target, ang);
        }

        /// <summary>
        /// Returns true if transToLookFor is a parent of transToSearchFrom (Includes indirect parents like transform.parent.parent)
        /// </summary>
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
        public static void Debug_doesMeshContainUnusedVers(Mesh mesh)
        {
            HashSet<int> usedV = new();

            foreach (int vI in mesh.triangles)
            {
                usedV.Add(vI);
            }

            if (usedV.Count != mesh.vertexCount) Debug.LogError("Unused faces");
        }

        public static void Debug_createMeshRend(Mesh meshW, Material mat = null)
        {
            GameObject newO = new();
            MeshFilter meshF = newO.AddComponent<MeshFilter>();
            MeshRenderer meshR = newO.AddComponent<MeshRenderer>();
            
            meshF.mesh = meshW;
            Material[] mats = new Material[meshW.subMeshCount];
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i] = mat;
            }

            meshR.sharedMaterials = mats;
        }

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

        public static void Debug_drawBox(Vector3 position, float size, Color color, float duration = 0.1f, bool doOcclusion = true)
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
            Debug.DrawLine(corners[0], corners[1], color, duration, doOcclusion);
            Debug.DrawLine(corners[1], corners[2], color, duration, doOcclusion);
            Debug.DrawLine(corners[2], corners[3], color, duration, doOcclusion);
            Debug.DrawLine(corners[3], corners[0], color, duration, doOcclusion);

            Debug.DrawLine(corners[4], corners[5], color, duration, doOcclusion);
            Debug.DrawLine(corners[5], corners[6], color, duration, doOcclusion);
            Debug.DrawLine(corners[6], corners[7], color, duration, doOcclusion);
            Debug.DrawLine(corners[7], corners[4], color, duration, doOcclusion);

            Debug.DrawLine(corners[0], corners[4], color, duration, doOcclusion);
            Debug.DrawLine(corners[1], corners[5], color, duration, doOcclusion);
            Debug.DrawLine(corners[2], corners[6], color, duration, doOcclusion);
            Debug.DrawLine(corners[3], corners[7], color, duration, doOcclusion);
        }
#endif
    }
}