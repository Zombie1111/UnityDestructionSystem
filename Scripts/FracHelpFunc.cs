using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;
using System.Reflection;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using UnityEditor;
#endif

namespace zombDestruction
{
#if !FRAC_NO_BURST
    [BurstCompile]
#endif
    public static class FracHelpFuncBurst
    {
        /// <summary>
        /// Returns a unique hash, the order of the values does not matter (0,0,1)=(0,1,0). THE VALUES IN THE GIVEN NativeArray WILL BE SORTED!
        /// </summary>
#if !FRAC_NO_BURST
        [BurstCompile]
#endif
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

        /// <summary>
        /// Resizes a native array. If an empty native array is passed, it will create a new one.
        /// </summary>
        /// <typeparam name="T">The type of the array</typeparam>
        /// <param name="array">Target array to resize</param>
        /// <param name="capacity">New size of native array to resize</param>
#if !FRAC_NO_BURST
        [BurstCompile]
#endif
        public static void ResizeArray<T>(this ref NativeArray<T> array, int capacity) where T : struct
        {
            var newArray = new NativeArray<T>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            NativeArray<T>.Copy(array, newArray, array.Length);
            array.Dispose();

            array = newArray;
        }

        /// <summary>
        /// Rezises the array and adds all elements from otherArray to the new array. otherArray is disposed
        /// </summary>
#if !FRAC_NO_BURST
        [BurstCompile]
#endif
        public static void CombineArray<T>(this ref NativeArray<T> array, ref NativeArray<T> otherArray) where T : struct
        {
            int ogLenght = array.Length;
            array.ResizeArray(ogLenght + otherArray.Length);
            otherArray.CopyTo(array.GetSubArray(ogLenght, otherArray.Length));
            otherArray.Dispose();
        }

        /// <summary>
        /// Returns the closest triangel index on the mesh
        /// </summary>
#if !FRAC_NO_BURST
        [BurstCompile]
#endif
        public static int GetClosestTriOnMesh(ref NativeArray<Vector3> meshWorldVers, ref NativeArray<int> meshTris, ref Vector3 posA, ref Vector3 posB, ref Vector3 posC, float worldScale = 1.0f)
        {
            int bestI = 0;
            float bestD = float.MaxValue;
            float disT;
            float disP;
            float worldScaleDis = worldScale * 0.00001f;
            worldScaleDis *= worldScaleDis;
            int trisL = meshTris.Length;

            Vector3 tPosA;
            Vector3 tPosB;
            Vector3 tPosC;
            Vector3 crossPos;
            Vector3 result = new();

            for (int i = 0; i < trisL; i += 3)
            {
                //if distance to tris is < 2x the distance to plane, We can use plane distance
                disT = 0.0f;
                disP = 0.0f;
                tPosA = meshWorldVers[meshTris[i]];
                tPosB = meshWorldVers[meshTris[i + 1]];
                tPosC = meshWorldVers[meshTris[i + 2]];
                crossPos = Vector3.Cross(tPosA - tPosB, tPosA - tPosC);

                ClosestPointOnTriangle(ref tPosA, ref tPosB, ref tPosC, ref posA, ref result);
                disT += (posA - result).sqrMagnitude;
                ClosestPointOnPlaneInfinit(ref tPosA, ref crossPos, ref posA, ref result);
                disP += (posA - result).sqrMagnitude;

                ClosestPointOnTriangle(ref tPosA, ref tPosB, ref tPosC, ref posB, ref result);
                disT += (posB - result).sqrMagnitude;
                ClosestPointOnPlaneInfinit(ref tPosA, ref crossPos, ref posB, ref result);
                disP += (posB - result).sqrMagnitude;

                ClosestPointOnTriangle(ref tPosA, ref tPosB, ref tPosC, ref posC, ref result);
                disT += (posC - result).sqrMagnitude;
                ClosestPointOnPlaneInfinit(ref tPosA, ref crossPos, ref posC, ref result);
                disP += (posC - result).sqrMagnitude;

                //if (disT < disP * 3.0f) disT = disP;
                if (disP < worldScaleDis) disT /= 9.0f;//The odds of this being true for "incorrect faces" and false for "correct faces" is besically 0%,
                                                       //so it should never cause a problem. Since disT is squared 9.0f = 3.0f

                //if (disT < bestD)
                if (disT < bestD)
                {
                    //bestD = disT;
                    bestD = disT;
                    bestI = i;
                    if (bestD < 0.000001f) break;

                    //if (currentD < preExitTolerance) break;
                }
            }

            return bestI;
        }

#if !FRAC_NO_BURST
        [BurstCompile]
#endif
        public static void ClosestPointOnTriangle(ref Vector3 p0, ref Vector3 p1, ref Vector3 p2, ref Vector3 queryPoint, ref Vector3 result)
        {
            //first get closest point on plane
            Vector3 pNor = Vector3.Cross(p0 - p1, p0 - p2);
            Vector3 closeP = queryPoint + (-Vector3.Dot(pNor, queryPoint - p0) / Vector3.Dot(pNor, pNor)) * pNor;

            //if the closest point on plane is inside the triangel, return closest planePoint
            if (PointInTriangle(ref closeP, ref p0, ref p1, ref p2) == true)
            {
                result = closeP;
                return;
            }

            //return closest point on the closest edge
            Vector3 pLine0 = new();
            ClosestPointOnLine(ref p0, ref p1, ref queryPoint, ref pLine0);

            Vector3 pLine1 = new();
            ClosestPointOnLine(ref p1, ref p2, ref queryPoint, ref pLine1);

            Vector3 pLine2 = new();
            ClosestPointOnLine(ref p0, ref p2, ref queryPoint, ref pLine2);

            float cLine0 = (pLine0 - queryPoint).sqrMagnitude;
            float cLine1 = (pLine1 - queryPoint).sqrMagnitude;
            float cLine2 = (pLine2 - queryPoint).sqrMagnitude;

            if (cLine0 < cLine1)
            {
                //0 or 2 is closest
                if (cLine2 < cLine0)
                {
                    result = pLine2;
                    return;
                }

                result = pLine0;
                return;
            }

            if (cLine1 < cLine2)
            {
                //1 is closest. We checked 0 before
                result = pLine1;
                return;
            }

            result = pLine2;
            return;

#if !FRAC_NO_BURST
            [BurstCompile]
#endif
            static bool PointInTriangle(ref Vector3 p, ref Vector3 p00, ref Vector3 p11, ref Vector3 p22)
            {
                // Lets define some local variables, we can change these
                // without affecting the references passed in
                Vector3 a = p00 - p;
                Vector3 b = p11 - p;
                Vector3 c = p22 - p;

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
        }

        /// <summary>
        /// Returns the closest position to point on a line between start and end
        /// </summary>
#if !FRAC_NO_BURST
        [BurstCompile]
#endif
        public static void ClosestPointOnLine(ref Vector3 lineStart, ref Vector3 lineEnd, ref Vector3 point, ref Vector3 result)
        {
            Vector3 lineDirection = lineEnd - lineStart;
            float lineLength = lineDirection.magnitude;
            lineDirection.Normalize();

            // Project the point onto the line
            float dotProduct = Vector3.Dot(point - lineStart, lineDirection);
            dotProduct = Mathf.Clamp(dotProduct, 0f, lineLength); // Ensure point is within the segment
            result = lineStart + dotProduct * lineDirection;
            return;
        }

        /// <summary>
        /// Returns the closest position on a plane at the given position with the given normal
        /// </summary>
#if !FRAC_NO_BURST
        [BurstCompile]
#endif
        public static void ClosestPointOnPlaneInfinit(ref Vector3 planePos, ref Vector3 planeNor, ref Vector3 queryPoint, ref Vector3 result)
        {
            result = queryPoint + (-Vector3.Dot(planeNor, queryPoint - planePos) / Vector3.Dot(planeNor, planeNor)) * planeNor;
            return;
        }

        /// <summary>
        /// Returns 0 if possA is closest to pos, returns 2 if possC is closest to poss....
        /// </summary>
#if !FRAC_NO_BURST
        [BurstCompile]
#endif
        public static int GetClosestPointOfPoss(ref Vector3 possA, ref Vector3 possB, ref Vector3 possC, ref Vector3 pos)
        {
            int bestI = 0;
            float bestD = float.MaxValue;
            float currentD;

            //A
            currentD = (pos - possA).sqrMagnitude;

            if (currentD < bestD)
            {
                bestD = currentD;
                bestI = 0;
            }

            //B
            currentD = (pos - possB).sqrMagnitude;

            if (currentD < bestD)
            {
                bestD = currentD;
                bestI = 1;
            }

            //C
            if ((pos - possC).sqrMagnitude < bestD)
            {
                bestI = 2;
            }

            return bestI;
        }

#if !FRAC_NO_BURST
        [BurstCompile]
#endif
        public static void DecomposeMatrix(ref Matrix4x4 matrix, ref Vector3 position, ref Quaternion rotation, ref Vector3 scale)
        {
            position = matrix.GetColumn(3);
            rotation = Quaternion.LookRotation(matrix.GetColumn(2), matrix.GetColumn(1));
            scale = new Vector3(matrix.GetColumn(0).magnitude, matrix.GetColumn(1).magnitude, matrix.GetColumn(2).magnitude);
        }

#if !FRAC_NO_BURST
        [BurstCompile]
#endif
        public static void InterpolateMatrix(ref Matrix4x4 A, ref Matrix4x4 B, float t)
        {
            // Decompose matrices
            Vector3 posA = Vector3.zero, posB = Vector3.zero, scaleA = Vector3.one, scaleB = Vector3.one;
            Quaternion rotA = Quaternion.identity, rotB = Quaternion.identity;

            DecomposeMatrix(ref A, ref posA, ref rotA, ref scaleA);
            DecomposeMatrix(ref B, ref posB, ref rotB, ref scaleB);

            // Interpolate components
            Vector3 pos = Vector3.Lerp(posA, posB, t);
            Quaternion rot = Quaternion.Slerp(rotA, rotB, t);
            Vector3 scale = Vector3.Lerp(scaleA, scaleB, t);

            // Recompose matrix
            A = Matrix4x4.TRS(pos, rot, scale);
        }

#if !FRAC_NO_BURST
        [BurstCompile]
#endif
        public static void AddNoiseToVectorArray(ref NativeArray<Vector3> array, float currentTime, float maxOffset, float scale, int seed)
        {
            Vector3 temp = new();
            float xPos = currentTime;
            int lenght = array.Length - 1;

            for (int i = 1; i < lenght; i++)
            {
                xPos += (array[i] - array[i + 1]).magnitude * scale;

                temp.x = (Mathf.PerlinNoise(xPos, seed) - 0.5f) * maxOffset;
                temp.y = (Mathf.PerlinNoise(xPos, seed + 100) - 0.5f) * maxOffset;
                temp.z = (Mathf.PerlinNoise(xPos, seed + 200) - 0.5f) * maxOffset;
                array[i] = array[i] + temp;
            }
        }

        /// <summary>
        /// Returns positions along a bezier curve and makes sure the average distance between is position is equal to spacing
        /// (The returned nativeArray will have a temp allocator)
        /// </summary>
#if !FRAC_NO_BURST
        [BurstCompile]
#endif
        public static void GetPointsAlongCurve(ref Vector3 start, ref Vector3 startOffset,
            ref Vector3 endOffset, ref Vector3 end, ref float spacing, ref NativeArray<Vector3> result)
        {
            //Get step size to get a similar distance between each point
            float lineDis = (end - start).magnitude + ((startOffset - start).magnitude * 0.25f)
                + ((end - endOffset).magnitude * 0.25f);

            //Create the points
            result = new((int)Math.Floor(lineDis / spacing) + 1, Allocator.Temp);
            int maxCount = result.Length;

            int i = 0;
            for (float linePos = 0.0f; linePos <= lineDis; linePos += spacing)
            {
                if (i >= maxCount) break;//Happens very rarely

                float t = linePos / lineDis;

                float u = 1 - t;
                float tt = t * t;
                float uu = u * u;
                float uuu = uu * u;
                float ttt = tt * t;

                Vector3 p = uuu * start; // (1-t)^3 * p0
                p += 3 * uu * t * startOffset; // 3 * (1-t)^2 * t * p1
                p += 3 * u * tt * endOffset; // 3 * (1-t) * t^2 * p2
                p += ttt * end; // t^3 * p3

                result[i] = p;
                i++;
            }
        }

        /// <summary>
        /// Returns the position on the bezier curve at T
        /// </summary>
#if !FRAC_NO_BURST
        [BurstCompile]
#endif
        public static void GetPointOnCurve(ref Vector3 start, ref Vector3 startOffset,
            ref Vector3 endOffset, ref Vector3 end, ref float t, ref Vector3 result)
        {
            float u = 1 - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;

            result = uuu * start; // (1-t)^3 * p0
            result += 3 * uu * t * startOffset; // 3 * (1-t)^2 * t * p1
            result += 3 * u * tt * endOffset; // 3 * (1-t) * t^2 * p2
            result += ttt * end; // t^3 * p3
        }
    }

    public static class FracHelpFunc
    {
        private static System.Random random = new System.Random();

        /// <summary>
        /// Returns the closest position on a plane at the given position with the given normal
        /// </summary>
        public static Vector3 ClosestPointOnPlane(ref Vector3 planePos, ref Vector3 planeNor, Vector3 queryPoint)
        {
            return queryPoint + (-Vector3.Dot(planeNor, queryPoint - planePos) / Vector3.Dot(planeNor, planeNor)) * planeNor;
        }

        /// <summary>
        /// Returns a random index, example array[GetRandomIndex(array.Lenght)]
        /// </summary>
        public static int GetRandomIndex(int containerLenght)
        {
            return random.Next(0, containerLenght);
        }

        /// <summary>
        /// 0.8 = 80% chance to return true
        /// </summary>
        public static bool RandomChance(float chance)
        {
            return random.NextDouble() < chance;
        }

        /// <summary>
        /// Returns a unique hash, the order of the values does not matter (0,0,1)=(0,1,0). THE VALUES IN THE GIVEN Array WILL BE MODIFIED!
        /// </summary>
        public static int GetHashFromInts(ref int[] inputInts)
        {
            Array.Sort(inputInts);

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

        /// <summary>
        /// Returns how much the given point has moved in the last second between objWToLPrev and objLToWNow
        /// </summary>
        public static Vector3 GetObjectVelocityAtPoint(ref Matrix4x4 objWToLPrev, ref Matrix4x4 objLToWNow, ref Vector3 point, float deltatime)
        {
            //for what ever reason transforming (point - velOffset) seems to give slightly better result at the cost of 2 extra transformations??
            Vector3 velOffset = point - (objLToWNow.MultiplyPoint3x4(objWToLPrev.MultiplyPoint3x4(point)) - point);
            return (objLToWNow.MultiplyPoint3x4(objWToLPrev.MultiplyPoint3x4(velOffset)) - velOffset) / deltatime;

            ////The one below is faster and in theory it should be more accurate but it aint???
            //return (objLToWNow.MultiplyPoint3x4(objWToLPrev.MultiplyPoint3x4(point)) - point) / deltatime;
        }

        /// <summary>
        /// Returns current lerped towards target by t and moved current towards target by at least min distance (Cant move past target)
        /// </summary>
        public static float FloatLerpMin(float current, float target, float t, float min, out bool reachedTarget)
        {
            float dis = Math.Abs(target - current);
            if (dis <= min)
            {
                reachedTarget = true;
                return target;
            }

            dis *= t;
            if (dis < min) dis = min;

            reachedTarget = false;
            return Mathf.MoveTowards(current, target, dis);
        }

        /// <summary>
        /// Returns current lerped towards target by t and moved current towards target by at least min distance (Cant move past target)
        /// </summary>
        public static Vector3 Vec3LerpMin(Vector3 current, Vector3 target, float t, float min)
        {
            float3 vel = target - current;
            float dis = math.sqrt(Vector3.Dot(vel, vel));
            if (dis <= min)
            {
                //reachedTarget = true;
                return target;
            }

            dis *= t; //Expecting t to always be within 0.0-1.0 range
            if (dis < min) dis = min;

            //reachedTarget = false;
            return current + (Vector3.Normalize(vel) * dis);

            //float dis = (target - current).magnitude;
            //if (dis <= min)
            //{
            //    reachedTarget = true;
            //    return target;
            //}
            //
            //dis *= t;
            //if (dis < min) dis = min;
            //
            //reachedTarget = false;
            //return Vector3.MoveTowards(current, target, dis);
        }

        /// <summary>
        /// Returns current lerped towards target by t and rotated current towards target by at least min degrees (Cant rotate past target)
        /// </summary>
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

        public static Vector3 Min(this Vector3 vectorA, Vector3 vectorB)
        {
            return Vector3.Min(vectorA, vectorB);
        }

        public static Vector3 Max(this Vector3 vectorA, Vector3 vectorB)
        {
            return Vector3.Max(vectorA, vectorB);
        }

        public static Bounds ConvertBoundsWithMatrix(Bounds bounds, Matrix4x4 matrix)
        {
            Vector3 center = matrix.MultiplyPoint(bounds.center);
            Vector3 size = matrix.MultiplyVector(bounds.size);

            return new Bounds(center, size);
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

        public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                collection.Add(item);
            }
        }

        public static void SetVelocityAtPosition(Vector3 targetVelocity, Vector3 positionOfForce, Rigidbody rb)
        {
            //rb.AddForceAtPosition(rb.mass * (targetVelocity - rb.velocity) / Time.fixedDeltaTime, positionOfForce, ForceMode.Force);
#if UNITY_2023_3_OR_NEWER
            rb.AddForceAtPosition(targetVelocity - rb.linearVelocity, positionOfForce, ForceMode.VelocityChange);
#else
            rb.AddForceAtPosition(targetVelocity - rb.velocity, positionOfForce, ForceMode.VelocityChange);
#endif
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

        /// <summary>
        /// Sets the mass of the given rigidbody to newMass clamped with FracGlobalSettings.rbMinMass and  FracGlobalSettings.rbMaxMass,
        /// Returns the mass the rigidbody actually got 
        /// </summary>
        public static float SetRbMass(ref Rigidbody rb, float newMass)
        {
            if (newMass > FracGlobalSettings.rbMaxMass)
            {
                rb.mass = FracGlobalSettings.rbMaxMass;
                return FracGlobalSettings.rbMaxMass;
            }
            else if (newMass < FracGlobalSettings.rbMinMass)
            {
                rb.mass = FracGlobalSettings.rbMinMass;
                return newMass;
            }

            rb.mass = newMass;
            return newMass;
        }

        /// <summary>
        /// Returns a array containing evenly distributed directions with the largest possible avg difference between each direction
        /// </summary>
        public static Vector3[] GetSphereDirections(int directionCount)
        {
            Vector3[] directions = new Vector3[directionCount];

            float goldenRatio = (1 + (float)Math.Sqrt(5)) / 2;
            float angleIncrement = Mathf.PI * 2 * goldenRatio;

            for (int i = 0; i < directionCount; i++)
            {
                float inclination = (float)Math.Acos(1 - 2 * ((float)i / directionCount));
                float azimuth = angleIncrement * i;

                directions[i] = new Vector3((float)Math.Sin(inclination) * (float)Math.Cos(azimuth),
                    (float)Math.Sin(inclination) * (float)Math.Sin(azimuth), (float)Math.Cos(inclination));
            }

            return directions;
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
            float volume = 0.0f;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            object lockObject = new();

            Parallel.For(0, triangles.Length / 3, i =>
            {
                int index = i * 3;
                var p1 = vertices[triangles[index + 0]];
                var p2 = vertices[triangles[index + 1]];
                var p3 = vertices[triangles[index + 2]];
                float signedVolume = SignedVolumeOfTriangle(p1, p2, p3);
                if (signedVolume < 0.0f) signedVolume *= -1.0f;

                lock (lockObject)
                {
                    volume += signedVolume;
                }
            });

            return Math.Abs(volume);
        }

        /// <summary>
        /// Returns 8 positions, a position for each corner of the bounds
        /// </summary>
        public static Vector3[] GetBoundsVertics(this Bounds bounds) => new[]
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
            return bounds.GetBoundsVertics()
                .Select(bv => from.transform.TransformPoint(bv, to.transform))
                .ToBounds();
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

        public static List<Vector3> ConvertPositionsWithMatrix(List<Vector3> localPoss, Matrix4x4 lTwMat)
        {
            for (int i = localPoss.Count - 1; i >= 0; i--)
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
            public NativeParallelHashMap<int, float> indexValueToSortValue;

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
                indexValueToSortValue = new NativeParallelHashMap<int, float>(initialCapacity, allocator);
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

        public static void SetVelocityUsingForce(Vector3 targetVelocity, Rigidbody rb)
        {
#if UNITY_2023_3_OR_NEWER
            rb.AddForce(rb.mass * (targetVelocity - rb.linearVelocity) / Time.fixedDeltaTime, ForceMode.Force);
#else
            rb.AddForce(rb.mass * (targetVelocity - rb.velocity) / Time.fixedDeltaTime, ForceMode.Force);
#endif
        }

        public static void SetAngularVelocityUsingTorque(Vector3 targetAngularVelocity, Rigidbody rb)
        {
            Vector3 torque = Vector3.Scale(rb.inertiaTensor, (targetAngularVelocity - rb.angularVelocity) / Time.fixedDeltaTime);
            rb.AddTorque(torque, ForceMode.Force);
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

        public static Dictionary<TKey, TValue> NativeHashMapToDictorary<TKey, TValue>(NativeParallelHashMap<TKey, TValue> nativeHashMap)
       where TKey : unmanaged, System.IEquatable<TKey>
       where TValue : unmanaged
        {
            // Create a new Dictionary<TKey, TValue> to hold the converted elements
            Dictionary<TKey, TValue> dictionary = new(nativeHashMap.Count());

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
                list.Capacity = desiredLength;
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
        /// Returns the closest position to point on the given line
        /// </summary>
        public static Vector3 ClosestPointOnLineInfinit(Vector3 point, Vector3 linePosition, Vector3 lineDirection)
        {
            return linePosition + (Vector3.Dot(point - linePosition, lineDirection) / lineDirection.sqrMagnitude) * lineDirection;
        }

        /// <summary>
        /// Returns the closest position to pos on the given mesh
        /// </summary>
        public static Vector3 ClosestPointOnMesh(Vector3[] meshWorldVers, int[] meshTris, Vector3 pos)
        {
            float bestD = float.MaxValue;
            Vector3 closePos = pos;
            float disT;
            Vector3 posT = new();

            for (int i = 0; i < meshTris.Length; i += 3)
            {
                FracHelpFuncBurst.ClosestPointOnTriangle(ref meshWorldVers[meshTris[i]], ref meshWorldVers[meshTris[i + 1]],
                    ref meshWorldVers[meshTris[i + 2]], ref pos, ref posT);
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

            //create mapping and find duplicates
            for (int i = 0; i < verts.Length; i++)
            {
                if (duplicateHashTable.ContainsKey(verts[i]) == false)
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
            for (int i = 0; i < vectors.Count; i++)
            {
                //for (int ii = i + 1; ii < vectors.Count; ii++)
                for (int ii = vectors.Count - 1; ii > i; ii--)
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
        /// Returns the direction in availableDirs that is the most similar to targetDir
        /// </summary>
        public static Vector3 GetMostSimilarDirection(Vector3 targetDir, Vector3[] availableDirs)
        {
            float bestDot = float.MinValue;
            Vector3 bestDir = availableDirs[0];

            foreach (var dir in availableDirs)
            {
                float thisDot = Vector3.Dot(targetDir, dir);
                if (thisDot < bestDot) continue;
                bestDot = thisDot;
                bestDir = dir;
            }

            return bestDir;
        }

        /// <summary>
        /// Returns each transform direction in worldspace (X, -X, Y, -Y, Z, -Z) (Dirs array must have a lenght of 6)
        /// </summary>
        public static void GetTransformDirections(Transform trans, ref Vector3[] dirs)
        {
            dirs[0] = trans.forward;
            dirs[1] = -dirs[0];
            dirs[2] = trans.up;
            dirs[3] = -dirs[2];
            dirs[4] = trans.right;
            dirs[5] = -dirs[4];
        }

        public static Vector3 GuessClosestPointOnRb(Rigidbody rb, Vector3 posTarget, Vector3 posHandle, Vector3 nor, LayerMask mask)
        {
            Vector3 rayOrg = rb.ClosestPointOnBounds(posHandle - (nor * 69.0f));
            if (Physics.Linecast(posTarget - (nor * 0.05f), rb.worldCenterOfMass, out RaycastHit nHit, mask, QueryTriggerInteraction.Ignore) == true
                && nHit.rigidbody == rb)
            {
                return nHit.point;
            }

            return rayOrg;
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

            if (mesh.bounds.extents.magnitude < EPSILON * 3) return false;

            if (checkIfValidHull == false) return true;
            NativeArray<Vector3> mVertics = new(mesh.vertices, Allocator.Persistent);
            bool isValid = HasValidHull(mVertics);
            mVertics.Dispose();
            return isValid;

#if !FRAC_NO_BURST
            [BurstCompile]
#endif
            bool HasValidHull(NativeArray<Vector3> points)
            {
                var count = points.Length;

                for (int i0 = 0; i0 < count - 3; i0++)
                {
                    for (int i1 = i0 + 1; i1 < count - 2; i1++)
                    {
                        var p0 = points[i0];
                        var p1 = points[i1];

                        //if (AreCoincident(p0, p1)) continue;
                        if ((p0 - p1).magnitude <= EPSILON) continue;

                        for (int i2 = i1 + 1; i2 < count - 1; i2++)
                        {
                            var p2 = points[i2];

                            //if (AreCollinear(p0, p1, p2)) continue;
                            if (Cross(p2 - p0, p2 - p1).magnitude <= EPSILON) continue;

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

#if !FRAC_NO_BURST
            [BurstCompile]
#endif
            bool AreCollinear(Vector3 a, Vector3 b, Vector3 c)
            {
                return Cross(c - a, c - b).magnitude <= EPSILON;
            }

#if !FRAC_NO_BURST
            [BurstCompile]
#endif
            Vector3 Cross(Vector3 a, Vector3 b)
            {
                return new Vector3(
                    a.y * b.z - a.z * b.y,
                    a.z * b.x - a.x * b.z,
                    a.x * b.y - a.y * b.x);
            }

#if !FRAC_NO_BURST
            [BurstCompile]
#endif
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
        }

        private class NewFracSource
        {
            public List<List<int>> subTris = new();
            public List<Material> subMat = new();
            public Dictionary<int, int> ogSubToSub = new();

            public List<Vector3> mVerts = new();
            public List<Vector3> mNors = new();
            public List<Vector2> mUvs = new();
            public List<BoneWeight> mBones = new();
            public List<Color> mCols = new();
        }

        /// <summary>
        /// Splits ogMeshD so all triangels in the new meshes has the same ogTrisId, all meshes in the returned list combined is equal to ogMeshD
        /// </summary>
        /// <param name="ogMeshD">Only ogMeshD.meshW and ogMeshD.mMats must be assigned</param>
        /// <param name="ogTrisId">Must have the same lenght as ogMeshW.triangles.lengt / 3</param>
        /// <param name="triIdToGroupId">All different values that exist in ogTrisId must exist as key in triIdToGroupId</param>
        public static List<DestructableObject.FracSource> SplitMeshByTrisIds(DestructableObject.FracSource ogMeshD, int[] ogTrisId, Dictionary<int, List<float>> triIdToGroupId)
        {
            //Get source mesh data
            Mesh ogMeshW = ogMeshD.meshW;
            int[] ogTris = ogMeshW.triangles;
            Vector3[] omVerts = ogMeshW.vertices;
            Vector3[] omNors = ogMeshW.normals;
            Vector2[] omUvs = ogMeshW.uv;
            BoneWeight[] omBones = ogMeshW.boneWeights;
            Color[] omCols = ogMeshW.colors;

            int ogTrisICount = ogMeshW.triangles.Length;
            int[] ogTrisSubI = GetTrisSubMeshI(ogMeshW);
            Dictionary<int, NewFracSource> idToNew = new(2);

            // Verify mesh properties and handle mismatches if necessary
            if (omCols.Length != omVerts.Length)
            {
                if (omCols.Length > 0) Debug.LogWarning(ogMeshW.name + " vertex colors has not been setup properly");
                omCols = new Color[omVerts.Length];//Some people may not need vertexColors, is memory saved by not having them be worth the cost of adding checks if they are assigned?
            }

            if (omBones.Length != omVerts.Length)
            {
                if (omBones.Length > 0) Debug.LogWarning(ogMeshW.name + " boneWeights has not been setup properly");
                omBones = new BoneWeight[omVerts.Length];
            }

            if (omUvs.Length != omVerts.Length)
            {
                Debug.LogWarning("The uvs for the mesh " + ogMeshW.name + " may not be valid");
                omUvs = new Vector2[omVerts.Length];
            }

            if (omNors.Length != omVerts.Length)
            {
                Debug.LogWarning("The normals for the mesh " + ogMeshW.name + " may not be valid");
                omNors = new Vector3[omVerts.Length];
            }

            //Split mesh, so all tris in each new mesh had the same ogTrisId
            for (int ogTrisI = 0; ogTrisI < ogTrisICount; ogTrisI += 3)
            {
                //Get if any new mesh already has this trisId
                if (idToNew.TryGetValue(ogTrisId[ogTrisI / 3], out NewFracSource fracSource) == false)
                {
                    fracSource = new();
                    idToNew.Add(ogTrisId[ogTrisI / 3], fracSource);
                }

                //Get what new subMeshIndex this triangel should use
                if (fracSource.ogSubToSub.TryGetValue(ogTrisSubI[ogTrisI], out int tSubI) == false)
                {
                    tSubI = fracSource.ogSubToSub.Count;
                    fracSource.subTris.Add(new());
                    fracSource.subMat.Add(ogMeshD.mMats[ogTrisSubI[ogTrisI]]);
                    fracSource.ogSubToSub.Add(ogTrisSubI[ogTrisI], tSubI);
                }

                //Add new tris
                int newVerI = fracSource.mVerts.Count;
                fracSource.subTris[tSubI].Add(newVerI);
                fracSource.subTris[tSubI].Add(newVerI + 1);
                fracSource.subTris[tSubI].Add(newVerI + 2);

                //Add new vertex data
                AddVerDataFromOgVerI(ogTris[ogTrisI]);
                AddVerDataFromOgVerI(ogTris[ogTrisI + 1]);
                AddVerDataFromOgVerI(ogTris[ogTrisI + 2]);

                void AddVerDataFromOgVerI(int ogVerI)
                {
                    fracSource.mVerts.Add(omVerts[ogVerI]);
                    fracSource.mNors.Add(omNors[ogVerI]);
                    fracSource.mUvs.Add(omUvs[ogVerI]);
                    fracSource.mBones.Add(omBones[ogVerI]);
                    fracSource.mCols.Add(omCols[ogVerI]);
                }
            }

            //Create new meshes
            List<DestructableObject.FracSource> newSources = new();

            foreach (var idNew in idToNew)
            {
                NewFracSource newS = idNew.Value;
                Mesh newM = new();
                newM.SetVertices(newS.mVerts);
                newM.SetNormals(newS.mNors);
                newM.SetUVs(0, newS.mUvs);
                newM.SetColors(newS.mCols);
                newM.boneWeights = newS.mBones.ToArray();//newM.SetBoneWeights uses nativeArray, faster to just assign boneWeights and use .ToArray()
                newM.subMeshCount = newS.subTris.Count;

                for (int subI = 0; subI < newS.subTris.Count; subI++)
                {
                    newM.SetTriangles(newS.subTris[subI], subI);
                }

                newSources.Add(new()
                {
                    meshW = newM,
                    mMats = newS.subMat,
                    mGroupId = triIdToGroupId[idNew.Key]
                });
            }

            return newSources;
        }

        /// <summary>
        /// Returns 2 meshes, [0] is the one containing vertexIndexesToSplit. May remove tris at split edges if some tris vertics are in vertexIndexesToSplit and some not
        /// </summary>
        public static List<DestructableObject.FracSource> SplitMeshInTwo(HashSet<int> vertexIndexesToSplit, DestructableObject.FracSource orginalMeshD, bool doBones)
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
            if (oCols.Length != oVerts.Length)
            {
                Debug.LogWarning(oMesh.name + " vertex colors has not been setup properly");
                oCols = new Color[oVerts.Length];
            }

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

            DestructableObject.FracSource newMA = CreateMesh(splitVerA, splitNorA, splitUvsA, splitColsA, splitBonA, splitTriA, splitLinkA);
            DestructableObject.FracSource newMB = CreateMesh(splitVerB, splitNorB, splitUvsB, splitColsB, splitBonB, splitTriB, splitLinkB);

            return new List<DestructableObject.FracSource> { newMA, newMB };

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

            DestructableObject.FracSource CreateMesh(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<Color> colors, List<BoneWeight> boneWeights, List<int> triangles, List<int> splitTrisOgTris)
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
            return new()
            {
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
        /// Returns all triangels connected to the given triangelIndex
        /// </summary>
        /// <param name="verDisTol">All vertics within this radius will count as the same vertex</param>
        public static HashSet<int> GetConnectedTriangels(Vector3[] vers, int[] tris, int triangelIndex, float verDisTol = 0.0001f)
        {
            //setup
            HashSet<int> usedVerts = new();
            int trisL = tris.Length / 3;
            List<int> trisToSearch = new();
            HashSet<int> usedFaces = new() { triangelIndex };
            GetAllTrisAtPos(vers[tris[triangelIndex]]);
            GetAllTrisAtPos(vers[tris[triangelIndex + 1]]);
            GetAllTrisAtPos(vers[tris[triangelIndex + 2]]);

            //get all connected, potential performance gain, add all vertex at pos to already searched
            for (int i = 0; i < trisToSearch.Count; i++)
            {
                int vI = tris[trisToSearch[i]];
                if (usedVerts.Contains(vI) == false) GetAllTrisAtPos(vers[vI]);

                vI = tris[trisToSearch[i] + 1];
                if (usedVerts.Contains(vI) == false) GetAllTrisAtPos(vers[vI]);

                vI = tris[trisToSearch[i] + 2];
                if (usedVerts.Contains(vI) == false) GetAllTrisAtPos(vers[vI]);
            }

            return usedFaces;

            void GetAllTrisAtPos(Vector3 pos)
            {
                Parallel.For(0, trisL, i =>
                {
                    int tI = i * 3;
                    bool didFind = false;

                    if ((vers[tris[tI]] - pos).sqrMagnitude < verDisTol)
                    {
                        lock (usedVerts)
                        {
                            usedVerts.Add(tris[tI]);
                        }

                        didFind = true;
                    }

                    if ((vers[tris[tI + 1]] - pos).sqrMagnitude < verDisTol)
                    {
                        lock (usedVerts)
                        {
                            usedVerts.Add(tris[tI + 1]);
                        }

                        didFind = true;
                    }

                    if ((vers[tris[tI + 2]] - pos).sqrMagnitude < verDisTol)
                    {
                        lock (usedVerts)
                        {
                            usedVerts.Add(tris[tI + 2]);
                        }

                        didFind = true;
                    }

                    if (didFind == true)
                    {
                        lock (trisToSearch)
                        {
                            if (usedFaces.Add(tI) == true) trisToSearch.Add(tI);
                        }
                    }
                });
            }
        }

        public static HashSet<int> GetAllTriangels(int trisL)
        {
            HashSet<int> trisI = new(trisL / 3);

            for (int tI = 0; tI < trisL; tI += 3)
            {
                trisI.Add(tI);
            }

            return trisI;
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
        public static bool Gd_isPartLinkedWithPart(FracPart partA, FracPart partB)
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
        /// Returns the index of all triangels that has the given id
        /// </summary>
        public static HashSet<int> Gd_getAllTriangelsInId(Color[] verColors, int[] tris, List<float> id)
        {
            HashSet<int> trisInId = new();
            int trisL = tris.Length;

            for (int tI = 0; tI < trisL; tI += 3)
            {
                if (Gd_isIdInColor(id, verColors[tris[tI]]) == true) trisInId.Add(tI);
            }

            return trisInId;
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
        /// Returns the index of all triangels that has the given id and exists inside the potentialTriangels hashset
        /// </summary>
        public static HashSet<int> Gd_getSomeTriangelsInId(Color[] verColors, int[] tris, List<float> id, HashSet<int> potentialTriangels)
        {
            HashSet<int> trisInId = new();

            foreach (int tI in potentialTriangels)
            {
                if (Gd_isIdInColor(id, verColors[tris[tI]]) == true) trisInId.Add(tI);
            }

            return trisInId;
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
        /// Returns a float for each mesh that is each mesh rough size compared to each other. All floats added = 1.0f
        /// </summary>
        /// <param name="getAccurateScales">If true, a more accurate but much slower algorythm will be used</param>
        public static float[] GetPerMeshScale(Mesh[] meshes, bool getAccurateScales = true)
        {
            int lengt = meshes.Length;
            float[] meshVolumes = new float[lengt];
            float totalVolume = 0.0f;

            for (int i = 0; i < lengt; i++)
            {
                //if (getAccurateScales == true) meshVolumes[i] = GetAccuratePointsVolume(meshes[i].vertices);
                if (getAccurateScales == true)
                {
                    //meshVolumes[i] = (GetBoundingBoxVolume(meshes[i].bounds) + meshes[i].Volume()) / 2.0f;
                    meshVolumes[i] = meshes[i].Volume();
                }
                else meshVolumes[i] = GetBoundingBoxVolume(meshes[i].bounds);
                totalVolume += meshVolumes[i];
            }

            float perMCost = 1.0f / totalVolume;
            for (int i = 0; i < lengt; i++)
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
            //int[] sTris = sourceMW.triangles;
            NativeArray<int> sTris = new(sourceMW.triangles, Allocator.Temp);
            //Vector3[] sVers = sourceMW.vertices;
            NativeArray<Vector3> sVers = new(sourceMW.vertices, Allocator.Temp);

            //int[] nTris = newMW.triangles;
            NativeArray<int> nTris = new(newMW.triangles, Allocator.Temp);
            //Vector3[] nVers = newMW.vertices;
            NativeArray<Vector3> nVers = new(newMW.vertices, Allocator.Temp);

            int ntL = nTris.Length / 3;
            NativeArray<int> closeOTris = new(ntL, Allocator.Temp);
            int nvL = nVers.Length;
            //int[] closeOVer = Enumerable.Repeat(-1, nvL).ToArray();
            NativeArray<int> closeOVer = new(Enumerable.Repeat(-1, nvL).ToArray(), Allocator.Temp);
            System.Object lockObject = new();

            //Debug_drawMesh(newMW, false, 10.0f);
            //Debug_drawMesh(sourceMW, false, 10.0f);

            Parallel.For(0, ntL, i =>
            {
                int tI = i * 3;
                Vector3 tPosA = nVers[nTris[tI]];
                Vector3 tPosB = nVers[nTris[tI + 1]];
                Vector3 tPosC = nVers[nTris[tI + 2]];

                int closeTrisI = FracHelpFuncBurst.GetClosestTriOnMesh(
                    ref sVers,
                    ref sTris,
                    ref tPosA, ref tPosB, ref tPosC,
                    worldScale);

                closeOTris[i] = closeTrisI;
                //Vector3[] oTrisPoss = new Vector3[3] { sVers[sTris[closeTrisI]], sVers[sTris[closeTrisI + 1]], sVers[sTris[closeTrisI + 2]] };
                Vector3 ttPosA = sVers[sTris[closeTrisI]];
                Vector3 ttPosB = sVers[sTris[closeTrisI + 1]];
                Vector3 ttPosC = sVers[sTris[closeTrisI + 2]];

                lock (lockObject)
                {
                    if (closeOVer[nTris[tI]] < 0)
                    {
                        closeOVer[nTris[tI]] = sTris[closeTrisI + FracHelpFuncBurst.GetClosestPointOfPoss(
                             ref ttPosA, ref ttPosB, ref ttPosC,
                             ref tPosA)];
                    }

                    if (closeOVer[nTris[tI + 1]] < 0)
                    {
                        closeOVer[nTris[tI + 1]] = sTris[closeTrisI + FracHelpFuncBurst.GetClosestPointOfPoss(
                            ref ttPosA, ref ttPosB, ref ttPosC,
                            ref tPosB)];
                    }

                    if (closeOVer[nTris[tI + 2]] < 0)
                    {
                        closeOVer[nTris[tI + 2]] = sTris[closeTrisI + FracHelpFuncBurst.GetClosestPointOfPoss(
                            ref ttPosA, ref ttPosB, ref ttPosC,
                            ref tPosC)];
                    }
                }
            });

            nVersBestSVer = closeOVer.ToArray();
            nTrisBestSTri = closeOTris.ToArray();
            closeOTris.Dispose();
        }

        /// <summary>
        /// Returns a mesh that is as similar to sourceMesh as possible while being convex, sourceMeshW must be in worldspace
        /// </summary>
        public static Mesh MakeMeshConvex(Mesh sourceMeshW, bool verticsOnly = false, float worldScale = 1.0f, bool useSourceIfBetter = true)
        {
            var calc = new QuickHull_convex();
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var normals = new List<Vector3>();

            bool didMake = calc.GenerateHull(sourceMeshW.vertices.ToList(), !verticsOnly, ref verts, ref tris, ref normals);

            if (didMake == false || (useSourceIfBetter == true && verts.Count >= sourceMeshW.vertexCount))
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

            //Potential improvement, just store source tris ids in normals (Since normals are kept after fracturing,
            //I can then use those ids to get source face(No, normals for new inside faces are not created from input??)
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
        public static List<int> SetMeshFromOther(ref Mesh newMesh, Mesh sourceMesh, int[] nVersBestSVer, int[] nTrisBestSTri, int[] sTrisSubMeshI, bool setUvs = true, Color[] sourceVColors = null)
        {
            //set uvs
            int newVCount = newMesh.vertexCount;

            if (setUvs == true)
            {
                Vector2[] sUvs = sourceMesh.uv;
                Vector2[] nUvs = new Vector2[newVCount];

                for (int nvI = 0; nvI < newVCount; nvI++)
                {
                    nUvs[nvI] = sUvs[nVersBestSVer[nvI]];
                }

                newMesh.uv = nUvs;
            }

            //set colors
            if (sourceVColors != null && sourceVColors.Length == newVCount)
            {
                Color[] nCols = new Color[newVCount];

                for (int nvI = 0; nvI < newVCount; nvI++)
                {
                    nCols[nvI] = sourceVColors[nVersBestSVer[nvI]];
                }

                newMesh.colors = nCols;
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
        /// Returns the angular velocity
        /// </summary>
        public static Vector3 GetAngularVelocity(Quaternion prevRot, Quaternion currentRot, float deltaTime)
        {
            var q = currentRot * Quaternion.Inverse(prevRot);
            // no rotation?
            // You may want to increase this closer to 1 if you want to handle very small rotations.
            // Beware, if it is too close to one your answer will be Nan
            if (Math.Abs(q.w) > 1023.5f / 1024.0f)
                return new Vector3(0, 0, 0);
            float gain;
            // handle negatives, we could just flip it but this is faster
            if (q.w < 0.0f)
            {
                var angle = (float)Math.Acos(-q.w);
                gain = -2.0f * angle / ((float)Math.Sin(angle) * deltaTime);
            }
            else
            {
                var angle = (float)Math.Acos(q.w);
                gain = 2.0f * angle / ((float)Math.Sin(angle) * deltaTime);
            }

            return new Vector3(q.x * gain, q.y * gain, q.z * gain);
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

        /// <summary>
        /// Performs a linecast for all positions between all positions
        /// </summary>
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

        public static int FindTriangleIndexWithVertex(int[] triangles, int vertexIndex)
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
                newMeshCol.sharedMesh = new() { vertices = FracHelpFunc.ConvertPositionsWithMatrix(FracHelpFunc.ConvertPositionsWithMatrix(ogMeshCol.sharedMesh.vertices, ogCol.transform.localToWorldMatrix), targetTrans.worldToLocalMatrix) };
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
                mCol.enabled = false;
                mCol.enabled = true;
                extents = col.bounds.extents;
            }
            else if (col is BoxCollider bCol)
            {
                bCol.center = FracHelpFunc.GetGeometricCenterOfPositions(possLocal);
                possLocal = FracHelpFunc.ConvertPositionsWithMatrix(possLocal, colTrans.localToWorldMatrix);

                extents = Vector3.one * 0.001f;
                float cDis;
                Vector3 tPos = bCol.bounds.center;
                Vector3 tFor = colTrans.forward;
                Vector3 tSide = colTrans.right;
                Vector3 tUp = colTrans.up;

                foreach (Vector3 wPos in possLocal)
                {
                    cDis = Vector3.Distance(FracHelpFunc.ClosestPointOnLineInfinit(wPos, tPos, tFor), tPos);
                    if (cDis > extents.z) extents.z = cDis;
                    cDis = Vector3.Distance(FracHelpFunc.ClosestPointOnLineInfinit(wPos, tPos, tSide), tPos);
                    if (cDis > extents.x) extents.x = cDis;
                    cDis = Vector3.Distance(FracHelpFunc.ClosestPointOnLineInfinit(wPos, tPos, tUp), tPos);
                    if (cDis > extents.y) extents.y = cDis;
                }

                //bCol.size = colTrans.InverseTransformVector(extents) * 2.0f;
                extents.Scale(colTrans.localToWorldMatrix.lossyScale);
                bCol.size = extents * 2.0f;
                extents = colTrans.TransformVector(extents);

            }
            else if (col is SphereCollider sCol)
            {
                sCol.center = FracHelpFunc.GetGeometricCenterOfPositions(possLocal);
                possLocal = FracHelpFunc.ConvertPositionsWithMatrix(possLocal, colTrans.localToWorldMatrix);

                extents = Vector3.one * 0.001f;
                float cDis;
                Vector3 tPos = sCol.bounds.center;
                Vector3 tFor = colTrans.forward;
                Vector3 tSide = colTrans.right;
                Vector3 tUp = colTrans.up;

                foreach (Vector3 wPos in possLocal)
                {
                    cDis = Vector3.Distance(FracHelpFunc.ClosestPointOnLineInfinit(wPos, tPos, tFor), tPos);
                    if (cDis > extents.z) extents.z = cDis;
                    cDis = Vector3.Distance(FracHelpFunc.ClosestPointOnLineInfinit(wPos, tPos, tSide), tPos);
                    if (cDis > extents.x) extents.x = cDis;
                    cDis = Vector3.Distance(FracHelpFunc.ClosestPointOnLineInfinit(wPos, tPos, tUp), tPos);
                    if (cDis > extents.y) extents.y = cDis;
                }

                extents.Scale(colTrans.localToWorldMatrix.lossyScale);
                sCol.radius = Mathf.Max(extents.x, extents.y, extents.z);
                extents = colTrans.TransformVector(extents);
            }
            else if (col is CapsuleCollider cCol)
            {
                cCol.center = FracHelpFunc.GetGeometricCenterOfPositions(possLocal);
                possLocal = FracHelpFunc.ConvertPositionsWithMatrix(possLocal, colTrans.localToWorldMatrix);

                extents = Vector3.one * 0.001f;
                float cDis;
                Vector3 tPos = cCol.bounds.center;
                Vector3 tFor = colTrans.forward;
                Vector3 tSide = colTrans.right;
                Vector3 tUp = colTrans.up;

                foreach (Vector3 wPos in possLocal)
                {
                    cDis = Vector3.Distance(FracHelpFunc.ClosestPointOnLineInfinit(wPos, tPos, tFor), tPos);
                    if (cDis > extents.z) extents.z = cDis;
                    cDis = Vector3.Distance(FracHelpFunc.ClosestPointOnLineInfinit(wPos, tPos, tSide), tPos);
                    if (cDis > extents.x) extents.x = cDis;
                    cDis = Vector3.Distance(FracHelpFunc.ClosestPointOnLineInfinit(wPos, tPos, tUp), tPos);
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
        public static Vector3 GetGeometricCenterOfPositions(Vector3[] positions)
        {
            Vector3 min = positions[0];
            Vector3 max = positions[0];

            //Find the minimum and maximum coordinates along each axis
            for (int i = 1; i < positions.Length; i++)
            {
                min = Vector3.Min(min, positions[i]);
                max = Vector3.Max(max, positions[i]);
            }

            return (min + max) * 0.5f;
        }

        /// <summary>
        /// Returns the geometric/(not average) center of given positions
        /// </summary>
        public static Vector3 GetGeometricCenterOfPositions(List<Vector3> positions)
        {
            Vector3 min = positions[0];
            Vector3 max = positions[0];

            //Find the minimum and maximum coordinates along each axis
            for (int i = 1; i < positions.Count; i++)
            {
                min = Vector3.Min(min, positions[i]);
                max = Vector3.Max(max, positions[i]);
            }

            return (min + max) * 0.5f;
        }

        /// <summary>
        /// Returns false if the given vector has a axis that is either NaN or Infinity
        /// </summary>
        public static bool IsVectorValid(Vector3 vector)
        {
            if (float.IsNaN(vector.x) || float.IsNaN(vector.y) || float.IsNaN(vector.z)) return false;
            if (float.IsInfinity(vector.x) || float.IsInfinity(vector.y) || float.IsInfinity(vector.z)) return false;
            return true;
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
        /// Returns true if transToLookFor is a parent of transToSearchFrom (Includes self and indirect parents like transform.parent.parent)
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
        /// Returns true if the given transform has a uniform lossyScale (If lossyScale XYZ are all the same)
        /// </summary>
        public static bool TransformHasUniformScale(Transform trans)
        {
            return (trans.lossyScale - (Vector3.one * trans.lossyScale.x)).magnitude < 0.001f;
        }

        /// <summary>
        /// Sets the world position+rotation+layer of each child in transform A to the same values as the children in transform B
        /// </summary>
        /// <param name="A"></param>
        /// <param name="B"></param>
        public static void MatchChildTransforms(Transform A, Transform B)
        {
            int childCount = Mathf.Min(A.childCount, B.childCount);

            for (int i = 0; i < childCount; i++)
            {
                Transform childA = A.GetChild(i);
                Transform childB = B.GetChild(i);

                childA.SetPositionAndRotation(childB.position, childB.rotation);
                childA.gameObject.layer = childB.gameObject.layer;//Is layer really worth the cost, it will only be changed if user has manually assigned a new layer at runtime
                MatchChildTransforms(childA, childB);
            }
        }

        /// <summary>
        /// Asks the user if we are allowed to save and returns true if we are (Always true at runtime)
        /// </summary>
        public static bool AskEditorIfCanSave(bool willRemoveFrac)
        {
#if UNITY_EDITOR
            if (Application.isPlaying == false && EditorSceneManager.GetActiveScene().isDirty == true
    && EditorUtility.DisplayDialog("", willRemoveFrac == true ? "All open scenes must be saved before removing fracture!"
    : "All open scenes must be saved before fracturing!", willRemoveFrac == true ? "Save and remove" : "Save and fracture", "Cancel") == false)
            {
                return false;
            }
#endif

            return true;
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

        public static int EncodeHierarchyPath(Transform child, Transform parent)
        {
            if (child == parent) return -1;
            int path = 0;
            int shift = 0;
            Transform current = child;

            while (current != parent)
            {
                Transform parentTransform = current.parent;
                if (parentTransform == null) return -1;

                int index = current.GetSiblingIndex();

                path |= (index + 1) << shift; //Store index + 1 to avoid issues with 0 index
                shift += 5; //Max 32 children

                current = parentTransform;
            }

            return path;
        }

        public static Transform DecodeHierarchyPath(Transform parent, int path)
        {
            if (path == -1) return parent;


            //int shift = 0;
            Stack<int> kidPath = new(2);

            while (path != 0)
            {
                int index = ((path >> 0) & 31) - 1; //Extract 5 bits and subtract 1 to get original index
                kidPath.Push(index);//Is adding + removing from stack fastest way to reverse order?
                path >>= 5;
            }

            Transform current = parent;

            while (kidPath.TryPop(out int kidIndex) == true)
            {
                current = current.GetChild(kidIndex);
            }

            return current;
        }

        /// <summary>
        /// Returns a dictorary that uses the given keys array as keys and values array is values (Must have same lenght)
        /// </summary>
        public static Dictionary<T1, T2> CreateDictionaryFromArrays<T1, T2>(T1[] keys, T2[] values)
        {
            if (keys.Length != values.Length)
            {
                throw new ArgumentException("Both arrays must have the same length.");
            }

            Dictionary<T1, T2> dictionary = new();

            for (int i = 0; i < keys.Length; i++)
            {
                dictionary[keys[i]] = values[i];
            }

            return dictionary;
        }

        /// <summary>
        /// Returns two arrays, one with the keys and one with the values from the dictorary
        /// </summary>
        public static void DictoraryToArrays<TKey, TValue>(Dictionary<TKey, TValue> dictionary, out TKey[] keys, out TValue[] values)
        {
            keys = new TKey[dictionary.Count];
            values = new TValue[dictionary.Count];

            int index = 0;
            foreach (var kvp in dictionary)
            {
                keys[index] = kvp.Key;
                values[index] = kvp.Value;
                index++;
            }
        }

        public static Joint CopyJoint(Joint source, GameObject destination, Rigidbody connectedBody, Vector3 anchorPosition)
        {
            if (source is HingeJoint ogHinge)
            {
                var newJoint = destination.AddComponent<HingeJoint>();
                SetGlobalProperties(source, newJoint);

                newJoint.useSpring = ogHinge.useSpring;
                newJoint.spring = ogHinge.spring;
                newJoint.limits = ogHinge.limits;
                newJoint.motor = ogHinge.motor;
                newJoint.useLimits = ogHinge.useLimits;
                newJoint.useMotor = ogHinge.useMotor;
                newJoint.useAcceleration = ogHinge.useAcceleration;
                newJoint.useSpring = ogHinge.useSpring;
                newJoint.extendedLimits = ogHinge.extendedLimits;
                return newJoint;
            }

            if (source is SpringJoint ogSpring)
            {
                var newJoint = destination.AddComponent<SpringJoint>();
                SetGlobalProperties(source, newJoint);

                newJoint.spring = ogSpring.spring;
                newJoint.damper = ogSpring.damper;
                newJoint.minDistance = ogSpring.minDistance;
                newJoint.maxDistance = ogSpring.maxDistance;
                newJoint.tolerance = ogSpring.tolerance;
                return newJoint;
            }

            if (source is CharacterJoint ogCharacter)
            {
                var newJoint = destination.AddComponent<CharacterJoint>();
                SetGlobalProperties(source, newJoint);

                newJoint.swingAxis = ogCharacter.swingAxis;
                newJoint.twistLimitSpring = ogCharacter.twistLimitSpring;
                newJoint.lowTwistLimit = ogCharacter.lowTwistLimit;
                newJoint.highTwistLimit = ogCharacter.highTwistLimit;
                newJoint.swingLimitSpring = ogCharacter.swingLimitSpring;
                newJoint.swing1Limit = ogCharacter.swing1Limit;
                newJoint.swing2Limit = ogCharacter.swing2Limit;
                newJoint.enableProjection = ogCharacter.enableProjection;
                newJoint.projectionDistance = ogCharacter.projectionDistance;
                newJoint.projectionAngle = ogCharacter.projectionAngle;
                return newJoint;
            }

            if (source is FixedJoint)
            {
                var newJoint = destination.AddComponent<FixedJoint>();
                SetGlobalProperties(source, newJoint);

                return newJoint;
            }

            if (source is ConfigurableJoint ogConfigurable)
            {
                var newJoint = destination.AddComponent<ConfigurableJoint>();
                SetGlobalProperties(source, newJoint);

                newJoint.secondaryAxis = ogConfigurable.secondaryAxis;
                newJoint.xMotion = ogConfigurable.xMotion;
                newJoint.yMotion = ogConfigurable.yMotion;
                newJoint.zMotion = ogConfigurable.zMotion;
                newJoint.angularXMotion = ogConfigurable.angularXMotion;
                newJoint.angularYMotion = ogConfigurable.angularYMotion;
                newJoint.angularZMotion = ogConfigurable.angularZMotion;
                newJoint.linearLimitSpring = ogConfigurable.linearLimitSpring;
                newJoint.linearLimit = ogConfigurable.linearLimit;
                newJoint.angularXLimitSpring = ogConfigurable.angularXLimitSpring;
                newJoint.lowAngularXLimit = ogConfigurable.lowAngularXLimit;
                newJoint.highAngularXLimit = ogConfigurable.highAngularXLimit;
                newJoint.angularYZLimitSpring = ogConfigurable.angularYZLimitSpring;
                newJoint.angularYLimit = ogConfigurable.angularYLimit;
                newJoint.angularZLimit = ogConfigurable.angularZLimit;
                newJoint.targetPosition = ogConfigurable.targetPosition;
                newJoint.targetVelocity = ogConfigurable.targetVelocity;
                newJoint.xDrive = ogConfigurable.xDrive;
                newJoint.yDrive = ogConfigurable.yDrive;
                newJoint.zDrive = ogConfigurable.zDrive;
                newJoint.targetRotation = ogConfigurable.targetRotation;
                newJoint.targetAngularVelocity = ogConfigurable.targetAngularVelocity;
                newJoint.rotationDriveMode = ogConfigurable.rotationDriveMode;
                newJoint.angularXDrive = ogConfigurable.angularXDrive;
                newJoint.angularYZDrive = ogConfigurable.angularYZDrive;
                newJoint.slerpDrive = ogConfigurable.slerpDrive;
                newJoint.projectionMode = ogConfigurable.projectionMode;
                newJoint.projectionDistance = ogConfigurable.projectionDistance;
                newJoint.projectionAngle = ogConfigurable.projectionAngle;
                newJoint.configuredInWorldSpace = ogConfigurable.configuredInWorldSpace;
                newJoint.swapBodies = ogConfigurable.swapBodies;
                return newJoint;
            }

            throw new Exception("Copying " + source.GetType() + "s has not been implemented!");

            void SetGlobalProperties(Joint ogJoint, Joint newJoint)
            {
                newJoint.anchor = anchorPosition;
                //newJoint.connectedAnchor = connectedAnchorPosition;
                newJoint.connectedBody = connectedBody;
                newJoint.connectedAnchor = ogJoint.connectedAnchor;
                newJoint.autoConfigureConnectedAnchor = ogJoint.autoConfigureConnectedAnchor;
                newJoint.breakForce = ogJoint.breakForce;
                newJoint.breakTorque = ogJoint.breakTorque;
                newJoint.connectedMassScale = ogJoint.connectedMassScale;
                newJoint.enableCollision = ogJoint.enableCollision;
                newJoint.enablePreprocessing = ogJoint.enablePreprocessing;
                newJoint.massScale = ogJoint.massScale;
                newJoint.axis = ogJoint.axis;
            }
        }

        public static Rigidbody CopyRigidbody(Rigidbody source, GameObject destination)
        {
            Rigidbody newRb = destination.GetOrAddComponent<Rigidbody>();

            newRb.isKinematic = source.isKinematic;
            newRb.inertiaTensor = source.inertiaTensor;
            newRb.centerOfMass = source.centerOfMass;
            newRb.includeLayers = source.includeLayers;
            newRb.useGravity = source.useGravity;
            newRb.interpolation = source.interpolation;
            newRb.mass = source.mass;
            newRb.maxAngularVelocity = source.maxAngularVelocity;
            newRb.maxLinearVelocity = source.maxLinearVelocity;
            newRb.maxDepenetrationVelocity = source.maxDepenetrationVelocity;
#if UNITY_2023_3_OR_NEWER
            newRb.angularDamping = source.angularDamping;
            newRb.linearDamping = source.linearDamping;
#else
            newRb.angularDrag = source.angularDrag;
            newRb.drag = source.drag;
#endif
            newRb.freezeRotation = source.freezeRotation;
            newRb.constraints = source.constraints;
            if (newRb.isKinematic == false)
            {
#if UNITY_2023_3_OR_NEWER
                newRb.linearVelocity = source.linearVelocity;
#else
                newRb.velocity = source.velocity;
#endif
                newRb.angularVelocity = source.angularVelocity;
            }
            newRb.collisionDetectionMode = source.collisionDetectionMode;
            newRb.automaticCenterOfMass = source.automaticCenterOfMass;
            newRb.automaticInertiaTensor = source.automaticInertiaTensor;
            newRb.excludeLayers = source.excludeLayers;
            newRb.includeLayers = source.includeLayers;

            return newRb;
        }

        /// <summary>
        /// Adds the given component to the destination object and tries to copy as many values from source component as possible
        /// </summary>
        public static T CopyComponent<T>(T source, GameObject destination) where T : Component
        {
            //Create new component
            Type type = source.GetType();

            T copy = (T)destination.AddComponent(type);
            if (copy == null) return null;

            //Copy all values we can from source to new 
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                field.SetValue(copy, field.GetValue(source));
            }

            foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (prop.CanWrite && prop.GetIndexParameters().Length == 0)
                {
                    prop.SetValue(copy, prop.GetValue(source, null), null);
                }
            }

            return copy;
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
            GameObject newO = new("Debug_meshRend");
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
                Vector3 start = worldCenter + discRadius * (float)Math.Cos(angle) * from + discRadius * (float)Math.Sin(angle) * to;
                angle = (i + 1) * 2 * Mathf.PI / segments;
                Vector3 end = worldCenter + discRadius * (float)Math.Cos(angle) * from + discRadius * (float)Math.Sin(angle) * to;
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