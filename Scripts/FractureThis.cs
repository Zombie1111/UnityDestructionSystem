using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using UnityEditor;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;
using System;
using Time = UnityEngine.Time;
using Component = UnityEngine.Component;
using System.Data;
using System.Text.RegularExpressions;

namespace Zombie1111_uDestruction
{
    public class FractureThis : MonoBehaviour
    {
#if UNITY_EDITOR
        //########################Custom Editor######################################
        [CustomEditor(typeof(FractureThis))]
        public class YourScriptEditor : Editor
        {
            public override void OnInspectorGUI()
            {
                serializedObject.Update();

                FractureThis yourScript = (FractureThis)target;

                EditorGUILayout.Space();


                if (GUILayout.Button("Generate Fracture"))
                {
                    yourScript.Gen_fractureObject();
                }

                if (GUILayout.Button("Remove Fracture"))
                {
                    yourScript.Gen_loadAndMaybeSaveOgData(false);
                }

                EditorGUILayout.Space();

                DrawPropertiesExcluding(serializedObject, "generateNavMesh", "stopGenerating");

                serializedObject.ApplyModifiedProperties();
            }
        }
#endif

        //fracture settings
        [Header("Fracture")]
        [SerializeField] private float worldScale = 1.0f;
        [SerializeField] private int fractureCount = 15;
        [SerializeField] private bool dynamicFractureCount = true;
        [SerializeField] private int seed = -1;
        public GenerationQuality generationQuality = GenerationQuality.medium;

        [Space(10)]
        [Header("Physics")]
        [SerializeField] private float massDensity = 0.1f;
        [SerializeField] private PhysicMaterial phyMat_defualt = null;
        [SerializeField] private PhysicMaterial phyMat_broken = null;
        [SerializeField] private ColliderType colliderType = ColliderType.mesh;
        [SerializeField] private bool disableCollisionWithNeighbours = true;
        [SerializeField] private OptPhysicsMain phyMainOptions = new();
        [SerializeField] private OptPhysicsParts phyPartsOptions = new();

        [Space(10)]
        [Header("Destruction")]
        public float destructionThreshold = 1.0f;
        [SerializeField] private float destructionResistance = 4.0f;
        [SerializeField] private float minDelay = 0.05f;
        [SerializeField] private int minAllowedMainPhySize = 2;
        [SerializeField] private bool multithreadedDestruction = true;
        [SerializeField] private AnimationCurve destructionWidthCurve = AnimationCurve.EaseInOut(0.0f, 0.0f, 1.0f, 1.0f);
        [SerializeField] private float distanceFalloffStrenght = 20.0f;
        [SerializeField] private float distanceFalloffPower = 1.0f;
        public SelfCollisionMode selfCollisionCanDamage = SelfCollisionMode.mainOnly;
        [SerializeField] private DestructionRepairSupport repairSupport = DestructionRepairSupport.fullHigh;
        [SerializeField] private float repairSpeed = 1.0f;

        [Space(10)]
        [Header("Mesh")]
        [SerializeField] private float vertexDisplacementStenght = 1.0f;
        [SerializeField] private bool displacementIgnoresWidth = true;
        [SerializeField] private bool limitDisplacement = true;
        [SerializeField] private NormalRecalcMode recalculateOnDisplacement = NormalRecalcMode.normalsOnly;
        [SerializeField] private bool doVertexColors = false;
        [SerializeField] private bool setVertexColorOnBreak = true;

        [Space(10)]
        [Header("Material")]
        [SerializeField] private Material matInside_defualt = null;
        [SerializeField] private Material matOutside_defualt = null;

        [Space(50)]
        [Header("Debug (Dont touch)")]
        [SerializeField] private OrginalObjData ogData = null;
        [SerializeField] private FractureSaveAsset saveAsset = null;
        public Collider[] saved_allPartsCol = new Collider[0];
        public int saved_fracId = -1;
        [SerializeField] private bool isRealSkinnedM = false;
        /// <summary>
        /// Add a parent index here to update the parents info within a few frames
        /// </summary>
        private HashSet<int> parentIndexesToUpdate = new();

        /// <summary>
        /// All fractured parts have one of these as its parent
        /// </summary>
        [SerializeField] private List<FracParents> allFracParents = new();

        /// <summary>
        /// All the fractured parts.
        /// </summary>
        [System.NonSerialized] public FracParts[] allParts = new FracParts[0];

        /// <summary>
        /// If MainPhysicsType == overlappingIsKinematic, bool for all parts that is true if the part was is inside a non fractured mesh when generated
        /// </summary>
        [System.NonSerialized] public bool[] kinematicPartStatus = new bool[0];

        /// <summary>
        /// The renderer used to render the fractured mesh (always skinned)
        /// </summary>
        [SerializeField] private SkinnedMeshRenderer fracRend = null;

        private enum DestructionRepairSupport
        {
            fullHigh,
            fullLow,
            partsOnly,
            dontSupportRepair
        }

        public enum SelfCollisionMode
        {
            always,
            mainOnly,
            never
        }

        /// <summary>
        /// High = 2, medium = 1, low = 0
        /// </summary>
        public enum GenerationQuality
        {
            high = 2,
            medium = 1,
            low = 0
        }

        private enum ColliderType
        {
            mesh,
            boxLarge,
            boxSmall,
            sphereLarge,
            sphereSmall
        }

        [System.Serializable]
        private class OptPhysicsMain
        {
            public OptMainPhysicsType mainPhysicsType = OptMainPhysicsType.overlappingIsKinematic;
            public bool useGravity = true;
            public float massMultiplier = 0.5f;
            public float drag = 0.0f;
            public float angularDrag = 0.05f;
            public CollisionDetectionMode collisionDetection = CollisionDetectionMode.Discrete;
            public RigidbodyInterpolation interpolate = RigidbodyInterpolation.Interpolate;
            public RigidbodyConstraints constraints;
        }

        [System.Serializable]
        private class OptPhysicsParts
        {
            public OptPartPhysicsType partPhysicsType = OptPartPhysicsType.rigidbody_medium;
            public bool useGravity = true;
            public bool canRotate = true;
            public float drag = 0.0f;
            public float angularDrag = 0.05f;
            public RigidbodyInterpolation interpolate = RigidbodyInterpolation.Interpolate;
        }

        private enum NormalRecalcMode
        {
            normalsAndTagents,
            normalsOnly,
            none
        }

        private enum OptPartPhysicsType
        { 
            rigidbody_high,
            rigidbody_medium,
            particle_high,
            particle_medium,
            particle_low,
            verySimple
        }

        private enum OptMainPhysicsType
        {
            overlappingIsKinematic,
            orginalIsKinematic,
            alwaysDynamic,
            alwaysKinematic
        }

        [System.Serializable]
        private class FracParents
        {
            public Transform parentTrans;

            /// <summary>
            /// The parents rigidbody. mass = (childPartCount * massDensity * phyMainOptions.massMultiplier), isKinematic is updated based on phyMainOptions.MainPhysicsType
            /// </summary>
            public Rigidbody parentRb;
            public FractureParent fParent;
            public List<int> partIndexes;
        }

        [System.Serializable]
        public struct FracParts
        {
            /// <summary>
            /// The part collider
            /// </summary>
            public Collider col;
            /// <summary>
            /// The vertex indexes on the main renderer that is for this part. mainMesh.vertics[rendVertexIndexes[0]] = thisMesh.vertics[0]
            /// </summary>
            public List<int> rendVertexIndexes;
            public List<int> neighbourParts;

            /// <summary>
            /// 0.0 = no cracks, 1.0 = completely broken
            /// </summary>
            public float partBrokenness;
            public int parentIndex;
        }

        [System.Serializable]
        private class OrginalCompData
        {
            public Component comp;
            public bool wasEnabled;
        }

        [System.Serializable]
        private class OrginalObjData
        {
            public List<OrginalCompData> ogCompData = new();
            public Material[] ogMats = new Material[0];
            public Transform[] ogBones = new Transform[0];
            public Transform ogRootBone = null;
            public Mesh ogMesh = null;
            public bool ogEnable = true;
            public bool hadRend = false;
            public bool rendWasSkinned = false;
        }

        /// <summary>
        /// Call to fracture the object the mesh is attatched to
        /// </summary>
        /// <param name="objectToFracture"></param>
        public void Gen_fractureObject()
        {
            //fracture the object
            GameObject objectToFracture = gameObject;

            //restore orginal data
            Gen_loadAndMaybeSaveOgData(false);

            //Get the meshes to fracture
            float worldScaleDis = worldScale * 0.0001f;
            OrginalObjData ogDefualtSkin = new();

            List<MeshData> meshesToFracture = Gen_getMeshesToFracture(objectToFracture, ref ogDefualtSkin, worldScaleDis);
            if (meshesToFracture == null) return;

            //Fracture the meshes into pieces
            List<Mesh> fracturedMeshes = Gen_fractureMeshes(meshesToFracture, fractureCount, dynamicFractureCount, worldScaleDis, seed, false);
            if (fracturedMeshes == null) return;

            //Save current orginal data (Save as late as possible)
            Gen_loadAndMaybeSaveOgData(true);

            //setup part basics, like defualt frac parent, create parts transform+colliders, convert mesh to localspace
            List<Mesh> fracturedMeshesLocal = Gen_setupPartBasics(new(fracturedMeshes), phyMat_defualt);
            if (fracturedMeshesLocal == null)
            {
                Gen_loadAndMaybeSaveOgData(false);
                return;
            }

            //setup fracture renderer, setup renderer
            Gen_setupRenderer(ref allParts, fracturedMeshes, transform, matInside_defualt, matOutside_defualt);

            //setup real skinned mesh
            if (isRealSkinnedM == true)
            {
                //Mesh bMesh = new();
                //fracRend.BakeMesh(bMesh, true);
                //Gen_setupSkinnedMesh(bMesh, ogSkinBones, fracRend.transform.localToWorldMatrix, worldScaleDis);
                //Gen_setupSkinnedMesh(Instantiate(fracRend.sharedMesh), ogSkinBones, fracRend.transform.localToWorldMatrix, worldScaleDis);
                Gen_setupSkinnedMesh(ogDefualtSkin.ogMesh, ogDefualtSkin.ogBones, ogDefualtSkin.ogRootBone.localToWorldMatrix, worldScaleDis);
            }

            //save to save asset
            SaveOrLoadAsset(true);

            //log result when done
            if (Mathf.Approximately(transform.lossyScale.x, transform.lossyScale.y) == false || Mathf.Approximately(transform.lossyScale.z, transform.lossyScale.y) == false) Debug.Log("(Warning) " + transform.name + " lossy scale XYZ should all be the same. If not stretching may accure when rotating parts");
            if (transform.TryGetComponent<Rigidbody>(out _) == true) Debug.Log("(Warning) " + transform.name + " has a rigidbody and it may cause issues. Its recommended to remove it and use the fracture physics options instead");
            Debug.Log("Fractured " + objectToFracture.transform.name + " into " + fracturedMeshesLocal.Count + " parts, total vertex count = " + fracturedMeshes.Sum(mesh => mesh.vertexCount));
        }

        public int triIndex = 0;
        public Transform debugTrans = null;

       private void OnDrawGizmos()
       {
            SkinnedMeshRenderer sRend = fracRend;
            if (sRend == null) sRend = transform.GetComponentInChildren<SkinnedMeshRenderer>();

           if (debugTrans == null || sRend == null) return;
       
           Mesh bMesh = new();
            sRend.BakeMesh(bMesh, true);
           //Vector3[] vers = FractureHelperFunc.ConvertPositionsWithMatrix(bMesh.vertices, fracRend.transform.localToWorldMatrix);
           Vector3[] vers = FractureHelperFunc.ConvertPositionsWithMatrix(bMesh.vertices, sRend.transform.localToWorldMatrix);
           BoneWeight[] wes = sRend.sharedMesh.boneWeights;

            for (int i = 0; i < vers.Length; i += 1)
            {
               Debug.DrawLine(vers[i], sRend.bones[wes[i].boneIndex1].position, Color.blue, 0.1f);
               //Debug.DrawLine(vers[i], fracRend.bones[wes[i].boneIndex2].position, Color.blue, 0.1f);
            }
           //int[] tris = bMesh.triangles;
           //Vector3 point = FractureHelperFunc.ClosestPointOnTriangle(vers[tris[triIndex]], vers[tris[triIndex + 1]], vers[tris[triIndex + 2]], debugTrans.position);
           //Debug.DrawLine(point, debugTrans.position, Color.blue, 0.1f);
       }

        /// <summary>
        /// Only called if real skinned mesh to copy its animated bones to fractured mesh
        /// </summary>
        private void Gen_setupSkinnedMesh(Mesh skinMesh, Transform[] skinBones, Matrix4x4 skinLtW, float worldScaleDis)
        {
            //################No vertex uses trail bone 3 after fracture. And way less uses trail bone 5
            //add bind poses and bone transforms from real skinned rend
            int boneIShift = fracRend.bones.Length;
            List<Transform> newBones = fracRend.bones.ToList();
            List<Matrix4x4> newMatrixs = fracRend.sharedMesh.bindposes.ToList();

            //newMatrixs = newMatrixs.Select(mat => fracRend.transform.localToWorldMatrix * mat).ToList();

            // Assuming skinBones is a List<Transform> containing the new bones
            for (int i = 0; i < skinBones.Length; i++)
            {
                // Add new bone and its bind pose matrix
                newBones.Add(skinBones[i]);
                newMatrixs.Add(skinMesh.bindposes[i]);
            }

            //fracRend.bones = newBones.ToArray();
            //fracRend.sharedMesh.bindposes = newMatrixs.ToArray();
            //fracRend.sharedMesh.boneWeights = fracBoneWe;

            //set boneWeights
            skinMesh = FractureHelperFunc.ConvertMeshWithMatrix(skinMesh, skinLtW);
            BoneWeight[] skinBoneWe = skinMesh.boneWeights.ToArray();
            Vector3[] skinWVer = skinMesh.vertices;
            int[] skinTris = skinMesh.triangles;
            BoneWeight[] fracBoneWe = fracRend.sharedMesh.boneWeights.ToArray();
            BoneWeight[] newBoneWe = new BoneWeight[fracBoneWe.Length];
            Vector3[] fracWVer = FractureHelperFunc.ConvertPositionsWithMatrix(fracRend.sharedMesh.vertices, fracRend.transform.localToWorldMatrix);
            List<int> unusedVers = Enumerable.Range(0, fracWVer.Length).ToList();

            //FractureHelperFunc.Debug_drawMesh(skinMesh, false, 10.0f);
            //FractureHelperFunc.Debug_drawMesh(FractureHelperFunc.ConvertMeshWithMatrix(Instantiate(fracRend.sharedMesh), fracRend.transform.localToWorldMatrix), false, 10.0f);

            //loop all vertics on fractured mesh to link their bone weights
            float bestD;
            int bestI;
            float nowD;

            for (int i = 0; i < fracWVer.Length; i += 1)
            {
                //get nearest skin vertex
                bestD = float.MaxValue;
                bestI = 0;

                for (int ii = 0; ii < skinWVer.Length; ii += 1)
                {
                    nowD = (fracWVer[i] - skinWVer[ii]).sqrMagnitude;

                    if (nowD < bestD)
                    {
                        bestD = nowD;
                        bestI = ii;
                    }
                }

                newBoneWe[i].boneIndex0 = fracBoneWe[i].boneIndex0;
                //newBoneWe[i].weight0 = fracBoneWe[i].weight0;
                newBoneWe[i].weight0 = 0.0f;


                newBoneWe[i].boneIndex1 = skinBoneWe[bestI].boneIndex0 + boneIShift;
                newBoneWe[i].weight1 = skinBoneWe[bestI].weight0;
                newBoneWe[i].boneIndex2 = skinBoneWe[bestI].boneIndex1 + boneIShift;
                newBoneWe[i].weight2 = skinBoneWe[bestI].weight1;
                newBoneWe[i].boneIndex3 = skinBoneWe[bestI].boneIndex2 + boneIShift;
                newBoneWe[i].weight3 = skinBoneWe[bestI].weight2;

                //newBoneWe[i].weight1 = 0.0f;
                //newBoneWe[i].weight2 = 0.0f;
                //newBoneWe[i].weight3 = 0.0f;
            }

            //int verI;
            //
            //while (unusedVers.Count > 0)
            //{
            //    verI = unusedVers[0];
            //    unusedVers.RemoveAt(0);
            //
            //    BoneWeight newBWe = GetBestBoneWeightForFracVertex(verI);
            //
            //    foreach (int vIi in verticsLinkedThreaded[verI].intList)
            //    {
            //        fracBoneWe[vIi] = newBWe;
            //        unusedVers.Remove(vIi);
            //    }
            //}

            //assign updated data to mesh and renderer
            fracRend.bones = newBones.ToArray();
            fracRend.sharedMesh.bindposes = newMatrixs.ToArray();
            fracRend.sharedMesh.boneWeights = newBoneWe;

            float bestTriD;
            int bestTriI;
            float currentTriD;

            BoneWeight GetBestBoneWeightForFracVertex(int vI)
            {
                //get nearest skin triangel
                bestTriD = float.MaxValue;
                bestTriI = 0;

                for (int i = 0; i < skinTris.Length; i += 3)
                {
                    currentTriD = (FractureHelperFunc.ClosestPointOnTriangle(skinWVer[skinTris[i]], skinWVer[skinTris[i + 1]], skinWVer[skinTris[i + 2]], fracWVer[vI]) - fracWVer[vI]).sqrMagnitude;

                    if (currentTriD < bestTriD)
                    {
                        bestTriD = currentTriD;
                        bestTriI = i;
                    }
                }

                //Debug.DrawLine(fracWVer[vI], skinWVer[skinTris[bestTriI]], Color.red, 10.0f);

                //get best bone weight to use
                List<BoneWeight1> newBoneWe = GetBestBoneWeightsFromSkinTri(bestTriI, vI);
                BoneWeight boneWe = new();

                //boneWe.weight0 = fracBoneWe[vI].weight0;
                boneWe.weight0 = 0.0f;
                boneWe.boneIndex0 = fracBoneWe[vI].boneIndex0;
                boneWe.weight1 = newBoneWe[0].weight;
                boneWe.boneIndex1 = newBoneWe[0].boneIndex + boneIShift;
                //print(boneWe.boneIndex1 + " " + newBoneWe[0].boneIndex + " " + boneIShift + " " + newMatrixs.Count + " " + newBones.Count);

                if (newBoneWe.Count > 1)
                {
                    boneWe.weight2 = newBoneWe[1].weight;
                    boneWe.boneIndex2 = newBoneWe[1].boneIndex + boneIShift;

                    if (newBoneWe.Count > 2)
                    {
                        boneWe.weight3 = newBoneWe[2].weight;
                        boneWe.boneIndex3 = newBoneWe[2].boneIndex + boneIShift;
                    }
                    else
                    {
                        boneWe.weight3 = 0.0f;
                        boneWe.boneIndex3 = boneWe.boneIndex2;
                    }
                }
                else
                {
                    boneWe.weight2 = 0.0f;
                    boneWe.boneIndex2 = boneWe.boneIndex1;
                    boneWe.weight3 = 0.0f;
                    boneWe.boneIndex3 = boneWe.boneIndex1;
                }

                return boneWe;
            }

            float bestVerD;
            int bestVerI;
            float currentVerD;

            List<BoneWeight1> GetBestBoneWeightsFromSkinTri(int triIndex, int fromVerIndex)
            {
                List<BoneWeight1> tempBoneWe = new();

                //get nearest vertex in triangel
                bestVerD = float.MaxValue;
                bestVerI = 0;

                for (int i = triIndex; i < triIndex + 3; i++)
                {
                    currentVerD = (skinWVer[skinTris[i]] - fracWVer[fromVerIndex]).sqrMagnitude;
                
                    if (currentVerD < bestVerD)
                    {
                        bestVerD = currentVerD;
                        bestVerI = skinTris[i];
                    }
                }

                //for (int i = 0; i < skinWVer.Length; i += 1)
                //{
                //    currentVerD = (skinWVer[i] - fracWVer[fromVerIndex]).sqrMagnitude;
                //
                //    if (currentVerD < bestVerD)
                //    {
                //        bestVerD = currentVerD;
                //        bestVerI = i;
                //    }
                //}

                //get and return bone weights
                if (skinBoneWe[bestVerI].weight0 > 0.0f) tempBoneWe.Add(new() { boneIndex = skinBoneWe[bestVerI].boneIndex0, weight = skinBoneWe[bestVerI].weight0 });
                if (skinBoneWe[bestVerI].weight1 > 0.0f) tempBoneWe.Add(new() { boneIndex = skinBoneWe[bestVerI].boneIndex1, weight = skinBoneWe[bestVerI].weight1 });
                if (skinBoneWe[bestVerI].weight2 > 0.0f) tempBoneWe.Add(new() { boneIndex = skinBoneWe[bestVerI].boneIndex2, weight = skinBoneWe[bestVerI].weight2 });
                if (skinBoneWe[bestVerI].weight3 > 0.0f && tempBoneWe.Count < 3) tempBoneWe.Add(new() { boneIndex = skinBoneWe[bestVerI].boneIndex3, weight = skinBoneWe[bestVerI].weight3 });

                return tempBoneWe;
            }
        }

        /// <summary>
        /// Creates a meshfilter+render on rendHolder and assigns it with proper values. Also updates fParts rendVertexIndexes
        /// </summary>
        /// <param name="fParts"></param>
        /// <param name="partMeshes"></param>
        /// <param name="rendHolder"></param>
        /// <param name="matInside"></param>
        /// <param name="matOutside"></param>
        private void Gen_setupRenderer(ref FracParts[] fParts, List<Mesh> partMeshes, Transform rendHolder, Material matInside, Material matOutside)
        {
            //combine meshes and assign verticsLinkedThreaded
            Mesh comMesh = FractureHelperFunc.CombineMeshes(partMeshes, ref fParts);

            Vector3[] vertics = comMesh.vertices;
            verticsLinkedThreaded = new IntList[vertics.Length];
            float worldDis = worldScale * 0.01f;

            Parallel.For(0, verticsLinkedThreaded.Length, i =>
            {
                List<int> intList = FractureHelperFunc.GetAllVertexIndexesAtPos(vertics, vertics[i], worldDis, -1);

                lock (verticsLinkedThreaded)
                {
                    verticsLinkedThreaded[i] = new() { intList = intList };
                }
            });

            comMesh = FractureHelperFunc.ConvertMeshWithMatrix(comMesh, rendHolder.worldToLocalMatrix);

            //setup combined mesh bones
            BoneWeight[] boneW = new BoneWeight[comMesh.vertexCount];
            for (int i = 0; i < fParts.Length; i += 1)
            {
                foreach (int vI in fParts[i].rendVertexIndexes)
                {
                    boneW[vI].weight0 = 1.0f;
                    boneW[vI].boneIndex0 = i;
                }
            }

            comMesh.boneWeights = boneW;
            comMesh.bindposes = fParts.Select(part => part.col.transform.worldToLocalMatrix * rendHolder.localToWorldMatrix).ToArray();

            //setup vertex colors
            if (doVertexColors == true) comMesh.colors = Enumerable.Repeat(new Color(1.0f, 1.0f, 1.0f, 0.0f), comMesh.vertexCount).ToArray();

            //set renderer
            comMesh.OptimizeIndexBuffers(); //should be safe to call since vertics order does not change
            SkinnedMeshRenderer sRend = rendHolder.GetOrAddComponent<SkinnedMeshRenderer>();
            sRend.enabled = true;
            sRend.rootBone = rendHolder;
            sRend.bones = fParts.Select(part => part.col.transform).ToArray();
            sRend.sharedMaterials = new Material[2] { matInside, matOutside };
            sRend.sharedMesh = comMesh;

            //setup verticsPartThreaded
            verticsPartThreaded = new int[vertics.Length];
            for (int i = 0; i < allParts.Length; i += 1)
            {
                foreach (int vI in allParts[i].rendVertexIndexes)
                {
                    verticsPartThreaded[vI] = i;
                }
            }
        }

        private List<Mesh> Gen_setupPartBasics(List<Mesh> meshes, PhysicMaterial phyMatToUse)
        {
            //save the world space meshes
            Mesh[] worldMeshes = meshes.ToArray();

            //create defualt parent
            int parentIndex = Run_createNewParent();
            allFracParents[parentIndex].partIndexes = Enumerable.Range(0, meshes.Count).ToList();
            Transform parentTrans = allFracParents[parentIndex].parentTrans;

            //create part transforms
            allParts = new FracParts[meshes.Count];

            for (int i = 0; i < meshes.Count; i += 1)
            {
                Transform newT = new GameObject("Part(" + i + ")_" + transform.name).transform;
                newT.SetParent(parentTrans);
                newT.SetPositionAndRotation(FractureHelperFunc.GetMedianPosition(meshes[i].vertices), parentTrans.rotation);
                newT.localScale = Vector3.one;

                meshes[i] = FractureHelperFunc.ConvertMeshWithMatrix(Instantiate(meshes[i]), newT.worldToLocalMatrix); //Instantiate new mesh to keep worldSpaceMeshes

                //the part data is created here
                FracParts newP = new() { col = Gen_createPartCollider(newT, meshes[i], phyMatToUse), rendVertexIndexes = new(), partBrokenness = 0.0f, neighbourParts = new(), parentIndex = 0 };
                allParts[i] = newP;
            }

            //setup part neighbours and isKinematic
            List<Vector3> wVerts = new();

            float worldDis = worldScale * 0.01f;
            if (phyMainOptions.mainPhysicsType == OptMainPhysicsType.overlappingIsKinematic) kinematicPartStatus = new bool[allParts.Length];
            else kinematicPartStatus = new bool[0];

            for (int i = 0; i < allParts.Length; i += 1)
            {
                if (generationQuality != GenerationQuality.high && (kinematicPartStatus.Length > 0 || generationQuality == GenerationQuality.low)) Gen_getKinematicAndNeighboursFromTrans(Physics.OverlapBox(worldMeshes[i].bounds.center, worldMeshes[i].bounds.extents * 1.05f), i, generationQuality != GenerationQuality.low);
                if (generationQuality == GenerationQuality.low) continue;

                //wVerts = worldMeshes[i].vertices;
                worldMeshes[i].GetVertices(wVerts);

                for (int ii = 0; ii < wVerts.Count; ii += 1)
                {
                    Gen_getKinematicAndNeighboursFromTrans(Physics.OverlapSphere(wVerts[ii], worldDis), i, false);
                }

                if (generationQuality == GenerationQuality.high) Gen_getKinematicAndNeighboursFromTrans(FractureHelperFunc.LinecastsBetweenPositions(wVerts).ToArray(), i);
            }

            //setup part og resistance
            partsOgResistanceThreaded = new float[allParts.Length];
            for (int i = 0; i < partsOgResistanceThreaded.Length; i += 1)
            {
                partsOgResistanceThreaded[i] = destructionResistance;
            }

            //update parent info
            Run_updateParentInfo(0);

            //return meshes since it has been converted to parent localspace
            return meshes;

            void Gen_getKinematicAndNeighboursFromTrans(Collider[] transs, int ogPi, bool kinematicOnly = false)
            {
                FractureThis pFracThis;
                int nearI;

                for (int i = 0; i < transs.Length; i += 1)
                {
                    //get part index from hit trans
                    pFracThis = transs[i].GetComponentInParent<FractureThis>();

                    nearI = Run_tryGetPartIndexFromTrans(transs[i].transform, false);
                    if (nearI == ogPi) continue;

                    if (nearI < 0)
                    {
                        //hit is not a neighbour part
                        if (kinematicPartStatus.Length > 0 && pFracThis == null)
                        {
                            kinematicPartStatus[ogPi] = true;
                        }
                    }
                    else if (allParts[ogPi].neighbourParts.Contains(nearI) == false && pFracThis == this)
                    {
                        //hit is a new neighbour part, add to neighbour part list
                        if (kinematicOnly == false) allParts[ogPi].neighbourParts.Add(nearI);
                    }
                }
            }

            Collider Gen_createPartCollider(Transform partTrans, Mesh partMesh, PhysicMaterial phyMat)
            {
                if (colliderType == ColliderType.mesh)
                {
                    //mesh
                    MeshCollider newCol = partTrans.GetOrAddComponent<MeshCollider>();
                    newCol.sharedMesh = partMesh;
                    newCol.convex = true;
                    newCol.sharedMaterial = phyMat;
                    return newCol;
                }
                else if (colliderType == ColliderType.boxLarge)
                {
                    //box large
                    BoxCollider newCol = partTrans.GetOrAddComponent<BoxCollider>();
                    newCol.size = partMesh.bounds.size;
                    newCol.sharedMaterial = phyMat;
                    return newCol;
                }
                else if (colliderType == ColliderType.boxSmall)
                {
                    //box small
                    BoxCollider newCol = partTrans.GetOrAddComponent<BoxCollider>();
                    newCol.size = Vector3.one * ((Mathf.Abs(partMesh.bounds.extents.x) + Mathf.Abs(partMesh.bounds.extents.y) + Mathf.Abs(partMesh.bounds.extents.z)) / 3.0f);
                    newCol.sharedMaterial = phyMat;
                    return newCol;
                }
                else if (colliderType == ColliderType.sphereLarge)
                {
                    //sphere large
                    SphereCollider newCol = partTrans.GetOrAddComponent<SphereCollider>();
                    newCol.radius = (Mathf.Abs(partMesh.bounds.extents.x) + Mathf.Abs(partMesh.bounds.extents.y) + Mathf.Abs(partMesh.bounds.extents.z)) / 3.0f;
                    newCol.sharedMaterial = phyMat;
                    return newCol;
                }
                else
                {
                    //sphere small
                    SphereCollider newCol = partTrans.GetOrAddComponent<SphereCollider>();
                    newCol.radius = partMesh.bounds.extents.magnitude / 3.0f;
                    newCol.sharedMaterial = phyMat;
                    return newCol;
                }
            }
        }

        /// <summary>
        /// Contains data about meshes to fracture
        /// </summary>
        public class MeshData
        {
            /// <summary>
            /// The mesh
            /// </summary>
            public Mesh mesh;

            /// <summary>
            /// The render used to render the mesh
            /// </summary>
            public Renderer rend;

            /// <summary>
            /// The mesh localToWorld matrix
            /// </summary>
            public Matrix4x4 lTwMatrix;
        }

        /// <summary>
        /// Restores all components on the previous saved objToUse
        /// </summary>
        /// <param name="objToUse"></param>
        /// <param name="doSave">If true, objToUse og data will be saved</param>
        public void Gen_loadAndMaybeSaveOgData(bool doSave = false)
        {
            //load/restore og object
            GameObject objToUse = gameObject;

            if (ogData != null)
            {
                //load og components
                foreach (OrginalCompData ogD in ogData.ogCompData)
                {
                    if (ogD.comp == null || ogD.comp.gameObject == null) continue;

                    Type targetType = ogD.comp.GetType();
                    if (IsValidType(targetType) == false) continue;

                    var enabledProperty = targetType.GetProperty("enabled");

                    if (enabledProperty == null || enabledProperty.PropertyType != typeof(bool)) continue;

                    enabledProperty.SetValue(ogD.comp, ogD.wasEnabled, null);
                }

                //load og renderer
                if (ogData.hadRend == true && fracRend != null)
                {
                    if (ogData.rendWasSkinned == false)
                    {
                        MeshRenderer mRend = transform.GetOrAddComponent<MeshRenderer>();
                        CopyRendProperties(fracRend, mRend, ogData.ogMats, ogData.ogEnable);
                        DestroyImmediate(fracRend);
                    }
                    else
                    {
                        SkinnedMeshRenderer sRend = transform.GetOrAddComponent<SkinnedMeshRenderer>();
                        sRend.sharedMesh = ogData.ogMesh;
                        sRend.bones = ogData.ogBones;
                        sRend.rootBone = ogData.ogRootBone;
                        CopyRendProperties(fracRend, sRend, ogData.ogMats, ogData.ogEnable);
                        if (transform != fracRend.transform) DestroyImmediate(fracRend);
                    }
                }
                else if (fracRend != null)
                {
                    DestroyImmediate(fracRend);
                }

                ogData = null;
            }

            //destroy all frac parents
            for (int i = 0; i < allFracParents.Count; i += 1)
            {
                if (allFracParents[i].parentTrans == null) continue;
                DestroyImmediate(allFracParents[i].parentTrans.gameObject);
            }

            allFracParents.Clear();
            verticsLinkedThreaded = new IntList[0];
            allParts = new FracParts[0];
            partsOgResistanceThreaded = new float[0];

            //clear save asset
            SaveOrLoadAsset(false, true);

            if (doSave == false) return;

            //save og object
            //save og renderer
            ogData = new();

            Renderer rend = transform.GetComponent<Renderer>();
            if (rend != null)
            {
                ogData.hadRend = true;
                ogData.ogEnable = rend.enabled;
                ogData.ogMats = rend.sharedMaterials;

                SkinnedMeshRenderer sRend = transform.GetComponent<SkinnedMeshRenderer>();
                if (sRend != null)
                {
                    ogData.ogMesh = sRend.sharedMesh;
                    ogData.ogBones = sRend.bones;
                    ogData.ogRootBone = sRend.rootBone;
                    ogData.rendWasSkinned = true;
                }
                else ogData.rendWasSkinned = false;
            }
            else ogData.hadRend = false;

            fracRend = transform.GetOrAddComponent<SkinnedMeshRenderer>();
            if (rend != null) CopyRendProperties(rend, fracRend, new Material[2] { matInside_defualt, matOutside_defualt }, true);

            //save og components
            bool newBoolValue;

            foreach (Component comp in objToUse.GetComponentsInChildren<Component>())
            {
                OrginalCompData newOgD = new();
                Type targetType = comp.GetType();
                if (IsValidType(targetType) == false) continue;

                newBoolValue = false;

                var enabledProperty = targetType.GetProperty("enabled");

                if (enabledProperty == null || enabledProperty.PropertyType != typeof(bool)) continue;

                newOgD.wasEnabled = (bool)enabledProperty.GetValue(comp);
                newOgD.comp = comp;
                enabledProperty.SetValue(comp, newBoolValue, null);

                ogData.ogCompData.Add(newOgD);
            }

            bool IsValidType(Type typeToCheck)
            {
                return typeof(Renderer).IsAssignableFrom(typeToCheck) == true || typeof(Collider).IsAssignableFrom(typeToCheck) == true;
            }

            void CopyRendProperties(Renderer source, Renderer target, Material[] targetMats, bool targetEnable)
            {
                target.enabled = targetEnable;
                target.sharedMaterials = targetMats;
                target.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
                target.shadowCastingMode = source.shadowCastingMode;
                target.receiveShadows = source.receiveShadows;
                target.lightProbeUsage = source.lightProbeUsage;
                target.lightProbeProxyVolumeOverride = source.lightProbeProxyVolumeOverride;
                target.motionVectorGenerationMode = source.motionVectorGenerationMode;
                target.probeAnchor = source.probeAnchor;
                target.realtimeLightmapIndex = source.realtimeLightmapIndex;
                target.realtimeLightmapScaleOffset = source.realtimeLightmapScaleOffset;
                //target.rayTracingMode = source.rayTracingMode;
                target.staticShadowCaster = source.staticShadowCaster;
                target.reflectionProbeUsage = source.reflectionProbeUsage;
                target.sortingLayerID = source.sortingLayerID;
                target.sortingLayerName = source.sortingLayerName;
                target.sortingLayerID = source.sortingLayerID;
                target.lightmapScaleOffset = source.lightmapScaleOffset;
                target.lightmapIndex = source.lightmapIndex;
                target.ResetBounds();
                target.ResetLocalBounds();
            }
        }

        /// <summary>
        /// Returns all mesh chunks that was generated from the meshesToFracture list
        /// </summary>
        /// <param name="meshesToFracture"></param>
        /// <param name="totalChunkCount"></param>
        /// <param name="seed"></param>
        /// <returns></returns>
        private List<Mesh> Gen_fractureMeshes(List<MeshData> meshesToFracture, int totalChunkCount, bool dynamicChunkCount, float worldScaleDis = 0.0001f, int seed = -1, bool useMeshBounds = false)
        {
            //get random seed
            if (seed < 0) seed = UnityEngine.Random.Range(0, int.MaxValue);

            //get per mesh scale, so each mesh can get ~equally sized
            List<Mesh> meshes = meshesToFracture.Select(meshData => meshData.mesh).ToList();
            List<float> meshScales = FractureHelperFunc.GetPerMeshScale(meshes, useMeshBounds);
            if (dynamicChunkCount == true) totalChunkCount = Mathf.CeilToInt(totalChunkCount * worldScale * FractureHelperFunc.GetBoundingBoxVolume(FractureHelperFunc.GetCompositeMeshBounds(meshes.ToArray())));
            meshes.Clear();

            //fractrue the meshes into chunks that are ~equally sized
            for (int i = 0; i < meshesToFracture.Count; i += 1)
            {
                Gen_fractureMesh(meshesToFracture[i].mesh, ref meshes, Mathf.RoundToInt(totalChunkCount * meshScales[i]));
            }

            //return the result
            return meshes;

            void Gen_fractureMesh(Mesh meshToFrac, ref List<Mesh> newMeshes, int chunkCount)
            {
                //fractures the given mesh into pieces and adds the new pieces to the newMeshes list
                if (chunkCount <= 1)
                {
                    newMeshes.Add(meshToFrac);
                    return;
                }

                //setup nvBlast
                NvBlastExtUnity.setSeed(seed);

                var nvMesh = new NvMesh(
                    meshToFrac.vertices,
                    meshToFrac.normals,
                    meshToFrac.uv,
                    meshToFrac.vertexCount,
                    meshToFrac.GetIndices(0),
                    (int)meshToFrac.GetIndexCount(0)
                );

                byte maxAttempts = 20;
                bool meshIsValid;

                while (maxAttempts > 0)
                {
                    maxAttempts--;
                    meshIsValid = true;

                    //execute nvBlast
                    var fractureTool = new NvFractureTool();
                    fractureTool.setRemoveIslands(false);
                    fractureTool.setSourceMesh(nvMesh);
                    var sites = new NvVoronoiSitesGenerator(nvMesh);
                    sites.uniformlyGenerateSitesInMesh(chunkCount);
                    fractureTool.voronoiFracturing(0, sites);
                    fractureTool.finalizeFracturing();

                    //extract mesh chunks from nvBlast
                    int meshCount = fractureTool.getChunkCount();
                    for (var i = 1; i < meshCount; i++)
                    {
                        newMeshes.Add(ExtractChunkMesh(fractureTool, i));
                        if (FractureHelperFunc.IsMeshValid(newMeshes[^1], worldScaleDis) == false)
                        {
                            print("New frac attempt");
                            meshIsValid = false;
                            break;
                        }
                    }

                    if (meshIsValid == false) continue;

                    break;
                }
            }

            Mesh ExtractChunkMesh(NvFractureTool fractureTool, int index)
            {
                //gets the fractured mesh chunk at the given index
                var outside = fractureTool.getChunkMesh(index, false);
                var inside = fractureTool.getChunkMesh(index, true);
                var chunkMesh = outside.toUnityMesh();
                chunkMesh.subMeshCount = 2;
                chunkMesh.SetIndices(inside.getIndexes(), MeshTopology.Triangles, 1);
                return chunkMesh;
            }
        }

        /// <summary>
        /// Returns the meshes to be used in fracturing. All meshes are in world space
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        private List<MeshData> Gen_getMeshesToFracture(GameObject obj, ref OrginalObjData ogDefualtSkin, float worldScaleDis = 0.0001f)
        {
            //Get all the meshes to fracture
            bool hasSkinned = false;

            List<MeshData> mDatas = new();
            foreach (Renderer rend in obj.GetComponentsInChildren<Renderer>())
            {
                MeshData newMData = new();

                if (rend.GetType() == typeof(SkinnedMeshRenderer))
                {
                    if (mDatas.Count > 0)
                    {
                        //if skinned there can only be 1 mesh source
                        Debug.LogError("There can only be 1 mesh if there is a skinnedMeshRenderer");
                        return null;
                    }

                    SkinnedMeshRenderer skinnedR = (SkinnedMeshRenderer)rend;
                    newMData.mesh = Instantiate(skinnedR.sharedMesh);
                    hasSkinned = true;
                    Vector3 rScale = skinnedR.transform.lossyScale;

                    foreach (Transform bone in skinnedR.bones)
                    {
                        Vector3 scaleDifference = new(Mathf.Abs(bone.lossyScale.x - rScale.x), Mathf.Abs(bone.lossyScale.y - rScale.y), Mathf.Abs(bone.lossyScale.z - rScale.z));

                        if (scaleDifference.x > 0.001f || scaleDifference.y > 0.001f || scaleDifference.z > 0.001f)
                        {
                            Debug.LogError("All bones of the skinnedMeshRenderer must have a scale of 1,1,1 (" + bone.name + " or any of its bone parents is invalid)");
                            return null;
                        }
                    }

                    ogDefualtSkin.ogBones = skinnedR.bones;
                    ogDefualtSkin.ogRootBone = skinnedR.transform;
                    ogDefualtSkin.ogMesh = Instantiate(skinnedR.sharedMesh);
                    //skinnedR.BakeMesh(ogDefualtSkin.ogMesh, true);
                    //ogDefualtSkin.ogMesh.bindposes = skinnedR.sharedMesh.bindposes.ToArray();
                    //ogDefualtSkin.ogMesh.boneWeights = skinnedR.sharedMesh.boneWeights.ToArray();
                }
                else if (rend.TryGetComponent(out MeshFilter meshF) == true)
                {
                    if (hasSkinned == true)
                    {
                        Debug.LogError("There can only be 1 mesh if there is a skinnedMeshRenderer");
                        return null;
                    }

                    newMData.mesh = Instantiate(meshF.sharedMesh);
                }
                else continue; //ignore if no MeshRenderer with meshfilter or skinnedMeshRenderer

                if (FractureHelperFunc.IsMeshValid(newMData.mesh, worldScaleDis) == false) continue; //continue if mesh is invalid

                newMData.rend = rend;
                newMData.lTwMatrix = rend.transform.localToWorldMatrix;
                mDatas.Add(newMData);
            }

            if (mDatas.Count == 0)
            {
                Debug.LogError("There are no valid mesh in " + transform.name + " or any of its children");
                return null;
            }

            //convert all meshes to world space
            for (int i = 0; i < mDatas.Count; i += 1)
            {
                mDatas[i].mesh = FractureHelperFunc.ConvertMeshWithMatrix(mDatas[i].mesh, mDatas[i].lTwMatrix);
            }

            //split meshes into chunks
            List<Mesh> splittedMeshes;
            for (int i = mDatas.Count - 1; i >= 0; i -= 1)
            {
                splittedMeshes = Gen_splitMeshIntoChunks(mDatas[i].mesh, hasSkinned, worldScaleDis);

                for (int ii = 0; ii < splittedMeshes.Count; ii += 1)
                {
                    mDatas.Add(new() { mesh = splittedMeshes[ii], lTwMatrix = mDatas[i].lTwMatrix, rend = mDatas[i].rend });
                }

                mDatas.RemoveAt(i);
            }

            //return result
            isRealSkinnedM = hasSkinned;
            return mDatas;
        }

        /// <summary>
        /// Splits the given mesh into chunks
        /// </summary>
        /// <param name="meshToSplit"></param>
        /// <returns></returns>
        private static List<Mesh> Gen_splitMeshIntoChunks(Mesh meshToSplit, bool doBones, float worldScaleDis = 0.0001f)
        {
            int maxLoops = 200;
            List<Mesh> splittedMeshes = new List<Mesh>();
            List<Mesh> tempM;
            while (maxLoops > 0)
            {
                maxLoops -= 1;
                if (meshToSplit.vertexCount < 4) break;
                tempM = FractureHelperFunc.SplitMeshInTwo(FractureHelperFunc.GetConnectedVertexIndexes(meshToSplit, 0, worldScaleDis), meshToSplit, doBones);
                splittedMeshes.Add(tempM[0]);
                meshToSplit = tempM[1];
            }

            return splittedMeshes;
        }

        //##############################RUNTIME########################################
        /// <summary>
        /// Returns true if the given collider count as a self collider (Depends on selfCollisionCanDamage)
        /// </summary>
        /// <returns></returns>
        public bool Run_isTransSelfTransform(Transform trans)
        {
            if (selfCollisionCanDamage == SelfCollisionMode.always) return false;

            if (trans.parent == transform) return true;
            if (selfCollisionCanDamage == SelfCollisionMode.never && trans.parent != null && trans.parent.parent == transform) return true;

            return false;
        }

        private void Run_updateParentInfo(int parentIndex)
        {
            //remove parent if no kids
            if (allFracParents[parentIndex].partIndexes.Count <= 0)
            {
                allFracParents[parentIndex].parentRb.isKinematic = true;
                return;

                //if (parentIndex == 0)
                //{
                //    //never remove base parent
                //    allFracParents[parentIndex].parentRb.isKinematic = true;
                //    return;
                //}
                //
                //allFracParents.RemoveAt(parentIndex);
                //Destroy(allFracParents[parentIndex].parentTrans.gameObject);
                //
                //return;
            }

            //update parent total mass
            allFracParents[parentIndex].parentRb.mass = allFracParents[parentIndex].partIndexes.Count * massDensity * phyMainOptions.massMultiplier;

            //update isKinematic
            if (phyMainOptions.mainPhysicsType == OptMainPhysicsType.alwaysDynamic)
            {
                allFracParents[parentIndex].parentRb.isKinematic = false;
            }
            else if (phyMainOptions.mainPhysicsType == OptMainPhysicsType.alwaysKinematic)
            {
                allFracParents[parentIndex].parentRb.isKinematic = true;
            }
            else if (phyMainOptions.mainPhysicsType == OptMainPhysicsType.orginalIsKinematic)
            {
                allFracParents[parentIndex].parentRb.isKinematic = parentIndex == 0;
            }
            else
            {
                bool pIsKin = false;

                foreach (int pI in allFracParents[parentIndex].partIndexes)
                {
                    if (kinematicPartStatus[pI] == true)
                    {
                        pIsKin = true;
                        break;
                    }
                }

                allFracParents[parentIndex].parentRb.isKinematic = pIsKin;
            }
        }

        /// <summary>
        /// Returns the given trans part index, -1 if not a part
        /// </summary>
        /// <param name="trans"></param>
        /// <param name="verifyScript">If true, also checks if the fracture script exists in any parent</param>
        /// <returns></returns>
        public int Run_tryGetPartIndexFromTrans(Transform trans, bool verifyScript = false)
        {
            if (verifyScript == true)
            {
                //verify script
                if (trans.GetComponentInParent<FractureThis>() != this) return -1;
            }

            //The part index is stored in the transform name
            Match match = Regex.Match(trans.name, @"Part\((\d+)\)");

            if (match.Success == true && int.TryParse(match.Groups[1].Value, out int partId) == true) return partId;

            return -1;
        }

        float currentDelayTime = 0.0f;

        private void Update()
        {
            if (allParts.Length == 0) return;

            //update renderer bounds
            fracRend.bounds = FractureHelperFunc.ToBounds(allParts.Select(part => part.col.transform.position));

            //calculate destruction
            if (impact_data.Count > 0)
            {
                if (currentDelayTime >= minDelay && ThreadCalcDes == null)
                {
                    currentDelayTime = 0.0f;
                    StartCoroutine(CalculateDestruction());
                }

                currentDelayTime += Time.deltaTime;
            }

            //do repair
            if (rep_partsToRepair.Count > 0)
            {
                bool didFullRepairAny = false;
                
                for (int i = rep_partsToRepair.Count - 1; i >= 0; i -= 1)
                {
                    if (Run_repairUpdate(rep_partsToRepair[i].partIndex, i) == true) didFullRepairAny = true;
                }

                if (repairSupport == DestructionRepairSupport.fullHigh || (didFullRepairAny == true && repairSupport == DestructionRepairSupport.fullLow))
                {
                    fracRend.sharedMesh.SetVertices(verticsOrginalThreaded);
                    fracRend.sharedMesh.SetColors(verticsColorThreaded);

                    if (recalculateOnDisplacement != NormalRecalcMode.none)
                    {
                        fracRend.sharedMesh.RecalculateNormals();
                        if (recalculateOnDisplacement == NormalRecalcMode.normalsAndTagents) fracRend.sharedMesh.RecalculateTangents();
                    }
                }
            }

            //update parent info
            if (parentIndexesToUpdate.Count > 0)
            {
                int parentToUpdate = parentIndexesToUpdate.FirstOrDefault();
                if (parentToUpdate >= 0 && parentToUpdate < allFracParents.Count) Run_updateParentInfo(parentToUpdate);
                parentIndexesToUpdate.Remove(parentToUpdate);
            }

            //debug keys
            if (Input.GetKey(KeyCode.R)) Run_requestRepairPart(Run_tryGetFirstDamagedPart());
        }

        public class ImpactData
        {
            public List<Vector3> poss = new();
            public int parentIndex = 0;
            public float totalForce = 0.0f;
            /// <summary>
            /// The impact direction, always normalized
            /// </summary>
            public Vector3 forceDir = Vector3.zero;
        }

        private List<ImpactData> impact_data = new();

        /// <summary>
        /// Calculates destruction to apply to the parent as soon as possible
        /// </summary>
        /// <param name="parentIndex"></param>
        /// <param name="impactPositions"></param>
        /// <param name="impactTotalForce"></param>
        /// <param name="impactDirection"></param>
        /// <returns></returns>
        //public void RequestDestruction(Vector3[] impPositions, float impTotalForce, Vector3 impDirection)
        public void RequestDestruction(Vector3 impPosition, Vector3 impDirection, float impForce, int impParentIndex)
        {
            if (impParentIndex < 0) return;
            if (impact_data.Count <= 0) currentDelayTime = 0.0f;

            int i = impact_data.FindIndex(impPos => impPos.parentIndex == impParentIndex);
            if (i < 0)
            {
                i = impact_data.Count;
                impact_data.Add(new() { parentIndex = impParentIndex });
            }

            impact_data[i].poss.Add(impPosition);

            impact_data[i].totalForce += impForce;
            if (impact_data[i].forceDir == Vector3.zero) impact_data[i].forceDir = impDirection;
            else impact_data[i].forceDir = Vector3.Lerp(impact_data[i].forceDir, impDirection, impForce / impact_data[i].totalForce).normalized;
        }

        private void Awake()
        {
            //load from save aset
            SaveOrLoadAsset(false);

            if (fracRend == null) return;

            //assign variabels for mesh deformation+colors
            if (vertexDisplacementStenght > 0.0f || doVertexColors == true)
            {
                bakedMesh = new();
                verticsOrginalThreaded = new();
                verticsCurrentThreaded = new();
                fracRend.sharedMesh.GetVertices(verticsOrginalThreaded);
                verticsForceThreaded = new float[verticsOrginalThreaded.Count];
                verticsBonesThreaded = fracRend.sharedMesh.boneWeights.Select(bone => bone.boneIndex0).ToArray();
                verticsUsedThreaded = new bool[verticsOrginalThreaded.Count];

                //set vertex color
                verticsColorThreaded = new();
                fracRend.sharedMesh.GetColors(verticsColorThreaded);
            }

            //assign variabels for repair system
            if (repairSupport != DestructionRepairSupport.dontSupportRepair)
            {
                rep_orginalLocalPartPoss = allParts.Select(part => part.col.transform.localPosition).ToArray();
                if (repairSupport != DestructionRepairSupport.partsOnly) rep_verticsOrginal = verticsOrginalThreaded.ToArray();
            }

            //disable collision with neighbours
            if (disableCollisionWithNeighbours == true)
            {
                for (int i = 0; i < allParts.Length; i += 1)
                {
                    foreach (int nI in allParts[i].neighbourParts)
                    {
                        Physics.IgnoreCollision(allParts[nI].col, allParts[i].col, true);
                    }
                }
            }
        }

        private Mesh bakedMesh;
        private bool[] verticsUsedThreaded = new bool[0];
        private List<Vector3> verticsCurrentThreaded;
        /// <summary>
        /// Contains all vertics of the skinned mesh (Is created on awake)
        /// </summary>
        private List<Vector3> verticsOrginalThreaded;
        private float[] verticsForceThreaded;

        /// <summary>
        /// Contains all vertics and all other vertex indexes that share the ~same position as X (Including self)
        /// </summary>
        [System.NonSerialized] public IntList[] verticsLinkedThreaded = new IntList[0];
        [System.NonSerialized] public float[] partsOgResistanceThreaded = new float[0];
        /// <summary>
        /// Contains all vertics and the index of the part they are a part of
        /// </summary>
        [System.NonSerialized] public int[] verticsPartThreaded = new int[0];
        private List<Color> verticsColorThreaded = new();
        /// <summary>
        /// The index of the bone each vertex uses
        /// </summary>
        private int[] verticsBonesThreaded = new int[0];
        private List<Matrix4x4> boneMatrixsCurrentThreaded = new();
        private Task<DestructionData> ThreadCalcDes = null;

        [System.Serializable]
        public struct IntList
        {
            public List<int> intList;
        }

        private IEnumerator CalculateDestruction()
        {
            //get all data that we can only access on the main thread
            if (vertexDisplacementStenght > 0.0f || doVertexColors == true)
            {
                fracRend.BakeMesh(bakedMesh);
                bakedMesh.GetVertices(verticsCurrentThreaded);
                boneMatrixsCurrentThreaded = allParts.Select(part => part.col.transform.localToWorldMatrix).ToList();
            }

            Vector3[] partPoss = allParts.Select(part => part.col.transform.position).ToArray();
            Matrix4x4 lTWMatrix = fracRend.transform.localToWorldMatrix;

            //run destruction compute thread
            DestructionData desData;

            if (multithreadedDestruction == true)
            {
                ThreadCalcDes = Task.Run(() => CalculateDestructionThread(allParts.ToArray(), partPoss, impact_data.ToList(), allFracParents.Select(fParent => fParent.partIndexes.ToList()).ToArray(), lTWMatrix));
                while (ThreadCalcDes.IsCompleted == false && ThreadCalcDes.IsFaulted == false) yield return null;

                if (ThreadCalcDes.IsFaulted == true)
                {
                    //when error accure, dont reset so it will try to destroy again
                    ThreadCalcDes = null;
                    yield break;
                }

                desData = ThreadCalcDes.Result;
            }
            else
            {
                desData = CalculateDestructionThread(allParts, partPoss, impact_data, allFracParents.Select(fParent => fParent.partIndexes.ToList()).ToArray(), fracRend.transform.localToWorldMatrix);
                yield return null;
            }

            //apply vertex deformation
            if (vertexDisplacementStenght > 0.0f)
            {
                fracRend.sharedMesh.SetVertices(verticsOrginalThreaded);

                if (recalculateOnDisplacement != NormalRecalcMode.none)
                {
                    fracRend.sharedMesh.RecalculateNormals();
                    if (recalculateOnDisplacement == NormalRecalcMode.normalsAndTagents) fracRend.sharedMesh.RecalculateTangents();
                }
            }
            if (doVertexColors == true) fracRend.sharedMesh.SetColors(verticsColorThreaded);

            //apply updated brokeness
            for (int i = 0; i < desData.partsBrokenness.Length; i += 1)
            {
                allParts[i].partBrokenness = desData.partsBrokenness[i];
            }

            //break parts
            for (int i = 0; i < desData.partsToBreak.Count; i += 1)
            {
                Run_breakPart(desData.partsToBreak[i], desData.partsToBreakVelocity[i]);
            }

            //create new parents
            for (int i = 0; i < desData.newParentParts.Count; i += 1)
            {
                Run_setPartsParent(desData.newParentParts[i], -1);
            }

            //reset when done
            ThreadCalcDes = null;
            impact_data.Clear();
        }

        /// <summary>
        /// Creates a empty frac parent object and returns its index
        /// </summary>
        /// <returns></returns>
        private int Run_createNewParent()
        {
            //If empty parent exists, use it as new parent (excluding base parent)
            for (int i = 1; i < allFracParents.Count; i += 1)
            {
                parentIndexesToUpdate.Add(i);
                if (allFracParents[i].partIndexes.Count <= 0) return i;
            }

            //create the parent transform
            Transform pTrans = new GameObject("fracParent" + allFracParents.Count + "_" + transform.name).transform;
            pTrans.SetParent(transform);
            pTrans.SetPositionAndRotation(transform.position, transform.rotation);
            pTrans.localScale = Vector3.one;
            int newParentIndex = allFracParents.Count;
            allFracParents.Add( new() { parentTrans = pTrans, partIndexes = new() } );

            //add rigidbody to parent
            allFracParents[newParentIndex].parentRb = pTrans.GetOrAddComponent<Rigidbody>();
            allFracParents[newParentIndex].parentRb.collisionDetectionMode = phyMainOptions.collisionDetection;
            allFracParents[newParentIndex].parentRb.interpolation = phyMainOptions.interpolate;
            allFracParents[newParentIndex].parentRb.useGravity = phyMainOptions.useGravity;
            allFracParents[newParentIndex].parentRb.drag = phyMainOptions.drag;
            allFracParents[newParentIndex].parentRb.angularDrag = phyMainOptions.angularDrag;
            allFracParents[newParentIndex].parentRb.constraints = phyMainOptions.constraints;

            //add parent script to parent
            allFracParents[newParentIndex].fParent = pTrans.GetOrAddComponent<FractureParent>();
            allFracParents[newParentIndex].fParent.fractureDaddy = this;
            allFracParents[newParentIndex].fParent.thisParentIndex = newParentIndex;

            parentIndexesToUpdate.Add(newParentIndex);
            return newParentIndex;
        }

        /// <summary>
        /// Sets partsToInclude parent to newParentIndex
        /// </summary>
        /// <param name="partsToInclude"></param>
        /// <param name="newParentIndex">If < 0, a new parent will be created</param>
        private void Run_setPartsParent(HashSet<int> partsToInclude, int newParentIndex = -1)
        {
            //create new parent if needed
            if (newParentIndex < 0)
            {
                newParentIndex = Run_createNewParent();
            }

            //set the parts parent to the new parent
            foreach (int partI in partsToInclude)
            {
                Run_setPartParent(partI, newParentIndex);
            }
        }

        /// <summary>
        /// Sets the given part parent
        /// </summary>
        /// <param name="partIndex"></param>
        /// <param name="parentIndex"></param>
        private void Run_setPartParent(int partIndex, int parentIndex)
        {
            //if (parentIndex == allParts[partIndex].parentIndex || rep_partsToRepair.Contains(partIndex) == true)
            if (parentIndex == allParts[partIndex].parentIndex || rep_partsToRepair.Any(part => part.partIndex == partIndex) == true)
            {
                if (allParts[partIndex].parentIndex >= 0 && allParts[partIndex].col.attachedRigidbody != null && allParts[partIndex].col.attachedRigidbody.transform == allParts[partIndex].col.transform) Destroy(allParts[partIndex].col.attachedRigidbody);
                return;
            }

            //update previous parent
            if (allParts[partIndex].parentIndex >= 0)
            {
                allFracParents[allParts[partIndex].parentIndex].partIndexes.Remove(partIndex);
                parentIndexesToUpdate.Add(allParts[partIndex].parentIndex);
            }

            if (parentIndex < 0)
            {
                //remove parent
                allParts[partIndex].col.transform.SetParent(transform, true);
                allParts[partIndex].parentIndex = -1;
                return;
            }

            //set new parent
            if (allParts[partIndex].col.attachedRigidbody != null && allParts[partIndex].col.attachedRigidbody.transform == allParts[partIndex].col.transform) Destroy(allParts[partIndex].col.attachedRigidbody);
            allParts[partIndex].col.transform.SetParent(allFracParents[parentIndex].parentTrans, true);
            allFracParents[parentIndex].partIndexes.Add(partIndex);
            allParts[partIndex].parentIndex = parentIndex;
            parentIndexesToUpdate.Add(allParts[partIndex].parentIndex);
        }

        /// <summary>
        /// Makes the part a broken piece
        /// </summary>
        /// <param name="partIndex"></param>
        /// <param name="breakVelocity"></param>
        private void Run_breakPart(int partIndex, Vector3 breakVelocity)
        {
            if (allParts[partIndex].parentIndex < 0) return;

            //update parent
            Run_setPartParent(partIndex, -1);

            //set collider material
            allParts[partIndex].col.sharedMaterial = phyMat_broken;
            
            //setup physics for the part
            if (phyMainOptions.mainPhysicsType != OptMainPhysicsType.overlappingIsKinematic || kinematicPartStatus[partIndex] == false)
            {
                if (phyPartsOptions.partPhysicsType == OptPartPhysicsType.rigidbody_medium || phyPartsOptions.partPhysicsType == OptPartPhysicsType.rigidbody_high)
                {
                    //if rigidbody
                    Rigidbody newRb = allParts[partIndex].col.GetOrAddComponent<Rigidbody>();
                    newRb.drag = phyPartsOptions.drag;
                    newRb.angularDrag = phyPartsOptions.angularDrag;
                    newRb.interpolation = phyPartsOptions.interpolate;
                    newRb.collisionDetectionMode = phyPartsOptions.partPhysicsType == OptPartPhysicsType.rigidbody_medium ? CollisionDetectionMode.Discrete : CollisionDetectionMode.ContinuousDynamic;
                    newRb.AddForce(breakVelocity, ForceMode.VelocityChange);
                }
            }
            else
            {
                allParts[partIndex].col.enabled = false;
            }
        }

        private struct DestructionData
        {
            public List<int> partsToBreak;
            public List<Vector3> partsToBreakVelocity;
            public List<HashSet<int>> newParentParts;
            public float[] partsBrokenness;
        }

        private struct OpenSetData
        {
            public int partIndex;
            public int prevOIndex;

            /// <summary>
            /// The force applied to this part
            /// </summary>
            public float force;
        }

        private DestructionData CalculateDestructionThread(FracParts[] fParts, Vector3[] fPartsPos, List<ImpactData> impData, List<int>[] parentPartIndexes, Matrix4x4 lToWMatrix)
        {
            //create variabels used for calculations, calc destruction
            DestructionData dData = new() { partsToBreak = new(), partsToBreakVelocity = new(), newParentParts = new() };
            ImpactData iD;
            HashSet<int> setClosed = new();
            List<OpenSetData> setOpen = new();
            OpenSetData newOpen = new();
            float[] partsBrokenness = fParts.Select(part => part.partBrokenness).ToArray();
            List<int> partsLeftToSearch = new();
            List<int> conParts = new();
            float vertexBrokennessToAdd;
            Vector3 verImpactDir;
            Vector3 closestImpPos;
            Vector3 dirToImpPos;
            int bIndex;
            float bDis;
            float cDis;
            float parentForce;

            //clear arrays
            if (vertexDisplacementStenght > 0.0f || doVertexColors == true)
            {
                if (limitDisplacement == false) Array.Clear(verticsForceThreaded, 0, verticsForceThreaded.Length);
                Array.Clear(verticsUsedThreaded, 0, verticsUsedThreaded.Length);
            }

            //loop all parents and calculate their destruction
            for (int i = 0; i < impData.Count; i += 1)
            {
                iD = impData[i];
                CalcDesParent(iD.parentIndex);
            }

            dData.partsBrokenness = partsBrokenness;
            return dData;

            void CalcDesParent(int parentIndex)
            {
                //divide impact force with hit count (Larger surface area = less force at each part of surface)
                parentForce = iD.totalForce / iD.poss.Count;

                //get all parts that was hit
                setOpen.Clear();

                for (int i = 0; i < iD.poss.Count; i += 1)
                {
                    bDis = float.MaxValue;
                    bIndex = 0;

                    foreach (int partI in parentPartIndexes[parentIndex])
                    {
                        if (partsBrokenness[partI] >= 1.0f) continue;

                        cDis = (fPartsPos[partI] - iD.poss[i]).sqrMagnitude;

                        if (cDis < bDis && setOpen.Any(pI => pI.partIndex == partI) == false)
                        {
                            bDis = cDis;
                            bIndex = partI;
                        }
                    }

                    if (bDis > 69420.0f) continue;
                    setOpen.Add(new() { partIndex = bIndex, force = parentForce, prevOIndex = -1 });
                }

                setClosed = setOpen.Select(sO => sO.partIndex).ToHashSet();

                //calculate the force to apply to the parts and vertics deformation
                for (int i = 0; i < setOpen.Count; i += 1)
                {
                    CalcDesPart_direct(parentIndex, i);
                }

                //if parent has disconnected parts, request new parent for them
                partsLeftToSearch = new(parentPartIndexes[parentIndex]);
                bool hasMainParent;
                bool allIsNew = true;

                while (partsLeftToSearch.Count > 0)
                {
                    //get all connected parts 
                    hasMainParent = false;
                    conParts.Clear();
                    setClosed.Clear();
                    conParts.Add(partsLeftToSearch[0]);
                    setClosed.Add(partsLeftToSearch[0]);
                    partsLeftToSearch.RemoveAt(0);
                
                    if (partsBrokenness[conParts[0]] >= 1.0f) continue;
                
                    for (int i = 0; i < conParts.Count; i += 1)
                    {
                        foreach (int nI in fParts[conParts[i]].neighbourParts)
                        {
                            partsLeftToSearch.Remove(nI);
                            if (partsBrokenness[nI] >= 1.0f || setClosed.Add(nI) == false || parentPartIndexes[parentIndex].Contains(nI) == false) continue;
                            
                            if (phyMainOptions.mainPhysicsType == OptMainPhysicsType.overlappingIsKinematic && kinematicPartStatus[nI] == true) hasMainParent = true;
                            conParts.Add(nI);
                        }
                    }

                    //get if should be new parent
                    if (hasMainParent == false)
                    {
                        if (conParts.Count < minAllowedMainPhySize)
                        {
                            foreach (int partI in conParts)
                            {
                                dData.partsToBreak.Add(partI);
                                dData.partsToBreakVelocity.Add(Vector3.zero);
                            }

                            continue;
                        }

                        dData.newParentParts.Add(conParts.ToHashSet());
                    }
                    else allIsNew = false;
                }

                //if no base parent left, make biggest parent base
                if (allIsNew == true && dData.newParentParts.Count > 0)
                {
                    int bI = 0;
                    int bC = 0;

                    for (int i = 0; i < dData.newParentParts.Count; i += 1)
                    {
                        if (dData.newParentParts[i].Count > bC)
                        {
                            bI = i;
                            bC = dData.newParentParts[i].Count;
                        }
                    }

                    dData.newParentParts.RemoveAt(bI);
                }
            }

            void CalcDesPart_direct(int parentIndex, int openIndex)
            {
                bool spreadFromThis = false;

                //get closest point
                closestImpPos = iD.poss[0];
                bDis = float.MaxValue;

                for (int i = 0; i < iD.poss.Count; i += 1)
                {
                    cDis = (fPartsPos[setOpen[openIndex].partIndex] - iD.poss[i]).sqrMagnitude;

                    if (cDis < bDis)
                    {
                        bDis = cDis;
                        closestImpPos = iD.poss[i];
                    }
                }

                //get vertics force
                if (vertexDisplacementStenght > 0.0f || doVertexColors == true)
                {
                    foreach (int vI in fParts[setOpen[openIndex].partIndex].rendVertexIndexes)
                    {
                        if (verticsForceThreaded[vI] >= 1.0f || verticsUsedThreaded[vI] == true) continue;

                        if (displacementIgnoresWidth == true) vertexBrokennessToAdd = Math.Max(0.0f, parentForce - GetForceAtPosition_noDir(lToWMatrix.MultiplyPoint(verticsCurrentThreaded[vI]))) / partsOgResistanceThreaded[setOpen[openIndex].partIndex];
                        else vertexBrokennessToAdd = Math.Max(0.0f, parentForce - GetForceAtPosition(lToWMatrix.MultiplyPoint(verticsCurrentThreaded[vI]))) / partsOgResistanceThreaded[setOpen[openIndex].partIndex];

                        if (vertexBrokennessToAdd <= 0.0f) continue;

                        vertexBrokennessToAdd = Mathf.Clamp(vertexBrokennessToAdd, 0.0f, 1.0f - verticsForceThreaded[vI]);
                        spreadFromThis = true;

                        foreach (int vIi in verticsLinkedThreaded[vI].intList)
                        {
                            if (verticsUsedThreaded[vIi] == true) continue;
                            if (partsBrokenness[verticsPartThreaded[vIi]] >= 1.0f || fParts[verticsPartThreaded[vIi]].parentIndex != parentIndex) continue;

                            verImpactDir = boneMatrixsCurrentThreaded[verticsBonesThreaded[vIi]].inverse.MultiplyVector(iD.forceDir);
                            verImpactDir.Scale(boneMatrixsCurrentThreaded[verticsBonesThreaded[vIi]].inverse.lossyScale);

                            verImpactDir *= vertexDisplacementStenght * vertexBrokennessToAdd;

                            verticsForceThreaded[vIi] += vertexBrokennessToAdd;
                            verticsUsedThreaded[vIi] = true;
                            verticsColorThreaded[vIi] = new Color(verticsColorThreaded[vIi].r, verticsColorThreaded[vIi].g, verticsColorThreaded[vIi].b, Math.Min(verticsForceThreaded[vIi], 1.0f));
                            verticsOrginalThreaded[vIi] += verImpactDir;
                        }
                    }
                }
                else spreadFromThis = true;

                //get force to apply to part
                partsBrokenness[setOpen[openIndex].partIndex] += Math.Max(0.0f, parentForce - GetForceAtPosition(fPartsPos[setOpen[openIndex].partIndex])) / partsOgResistanceThreaded[setOpen[openIndex].partIndex];

                foreach (int pI in fParts[setOpen[openIndex].partIndex].neighbourParts)
                {
                    if (partsBrokenness[pI] <= 0.0f) partsBrokenness[pI] = 0.01f;
                }

                float GetForceAtPosition(Vector3 pos)
                {
                    dirToImpPos = pos - closestImpPos;
                    return Mathf.Pow(dirToImpPos.magnitude * distanceFalloffStrenght, distanceFalloffPower) / destructionWidthCurve.Evaluate((Vector3.Dot(iD.forceDir, dirToImpPos.normalized) + 1.0f) / 2.0f);
                }

                float GetForceAtPosition_noDir(Vector3 pos)
                {
                    dirToImpPos = pos - closestImpPos;
                    return Mathf.Pow(dirToImpPos.magnitude * distanceFalloffStrenght, distanceFalloffPower);
                }

                //get if this part should break
                if (partsBrokenness[setOpen[openIndex].partIndex] >= 1.0f)
                {
                    dData.partsToBreak.Add(setOpen[openIndex].partIndex);
                    dData.partsToBreakVelocity.Add((partsBrokenness[setOpen[openIndex].partIndex] - 1.0f) * partsOgResistanceThreaded[setOpen[openIndex].partIndex] * iD.forceDir);
                    partsBrokenness[setOpen[openIndex].partIndex] = Mathf.Clamp01(partsBrokenness[setOpen[openIndex].partIndex]);

                    //set vertex color on break
                    if (setVertexColorOnBreak == true)
                    {
                        foreach (int vI in fParts[setOpen[openIndex].partIndex].rendVertexIndexes)
                        {
                            foreach (int vIi in verticsLinkedThreaded[vI].intList)
                            {
                                verticsColorThreaded[vIi] = new Color(verticsColorThreaded[vIi].r, verticsColorThreaded[vIi].g, verticsColorThreaded[vIi].b, 1.0f);
                            }
                        }
                    }
                }

                //continue spreading force
                if (spreadFromThis == false) return;

                foreach (int nI in fParts[setOpen[openIndex].partIndex].neighbourParts)
                {
                    if (partsBrokenness[nI] >= 1.0f || setClosed.Add(nI) == false || parentPartIndexes[parentIndex].Contains(nI) == false) continue;
                    newOpen.partIndex = nI;
                    newOpen.prevOIndex = openIndex;

                    setOpen.Add(newOpen);
                }
            }
        }

        private Vector3[] rep_orginalLocalPartPoss = new Vector3[0];
        private Vector3[] rep_verticsOrginal = new Vector3[0];
        private List<RepPartData> rep_partsToRepair = new();
        private float rep_speedScaled = 1.0f;

        private struct RepPartData
        {
            public int partIndex;
            public float partOgDis;
        }

        public void Run_requestRepairPart(int partToRepair)
        {
            //if (partToRepair < 0 || allParts[partToRepair].partBrokenness == 0.0f || repairSupport == DestructionRepairSupport.dontSupportRepair || rep_partsToRepair.Contains(partToRepair) == true) return;
            //if (partToRepair < 0 || repairSupport == DestructionRepairSupport.dontSupportRepair || rep_partsToRepair.Contains(partToRepair) == true) return;
            if (partToRepair < 0 || repairSupport == DestructionRepairSupport.dontSupportRepair || rep_partsToRepair.Any(part => part.partIndex == partToRepair) == true) return;

            Run_setPartParent(partToRepair, 0);
            rep_speedScaled = repairSpeed * allFracParents[0].parentTrans.lossyScale.magnitude;
            rep_partsToRepair.Add(new() { partIndex = partToRepair, partOgDis = Math.Max(1.0f,  
                Vector3.Distance(allParts[partToRepair].col.transform.position, allFracParents[0].parentTrans.TransformPoint(rep_orginalLocalPartPoss[partToRepair])) - repairSpeed) });

            allParts[partToRepair].col.enabled = false;
            allParts[partToRepair].col.sharedMaterial = phyMat_defualt;
        }

        public int Run_tryGetFirstDamagedPart()
        {
            if (allFracParents[0].partIndexes.Count <= 0) return 0;

            if (allFracParents[0].partIndexes.Count != allParts.Length)
            {
                foreach (int partI in allFracParents[0].partIndexes)
                {
                    foreach (int nI in allParts[partI].neighbourParts)
                    {
                        if (allParts[nI].parentIndex != 0 && rep_partsToRepair.Any(part => part.partIndex == nI) == false) return nI;
                    }
                }
            }
            else
            {
                foreach (int partI in allFracParents[0].partIndexes)
                {
                    if (allParts[partI].partBrokenness > 0.0f && rep_partsToRepair.Any(part => part.partIndex == partI) == false) return partI;
                }
            }

            return -1;
        }

        /// <summary>
        /// Returns true if the part is now fully repaired
        /// </summary>
        /// <param name="partIndex"></param>
        /// <param name="repIndex"></param>
        /// <returns></returns>
        private bool Run_repairUpdate(int partIndex, int repIndex)
        {
            bool hasRestoredAll = true;

            //restore part bone transform
            Vector3 tempVec = rep_orginalLocalPartPoss[partIndex] - allParts[partIndex].col.transform.localPosition;
            if (tempVec.sqrMagnitude > 0.0001f) hasRestoredAll = false;
            allParts[partIndex].col.transform.localPosition += Vector3.ClampMagnitude(tempVec, rep_speedScaled * rep_partsToRepair[repIndex].partOgDis * Time.deltaTime);

            //allParts[partIndex].col.transform.localPosition = Vector3.MoveTowards(allParts[partIndex].col.transform.localPosition, rep_orginalLocalPartPoss[partIndex], rep_speedScaled * Time.deltaTime);
            if (Quaternion.Angle(allParts[partIndex].col.transform.localRotation, Quaternion.identity) > 0.0001f) hasRestoredAll = false;
            allParts[partIndex].col.transform.localRotation = Quaternion.RotateTowards(allParts[partIndex].col.transform.localRotation, Quaternion.identity, rep_speedScaled * 80.0f * Time.deltaTime);

            //allParts[partIndex].col.transform.localScale = Vector3.MoveTowards(allParts[partIndex].col.transform.localScale, Vector3.one, rep_speedScaled * 0.4f * Time.deltaTime);
            tempVec = Vector3.one - allParts[partIndex].col.transform.localScale;
            if (tempVec.sqrMagnitude > 0.0001f) hasRestoredAll = false;
            allParts[partIndex].col.transform.localScale += Vector3.ClampMagnitude(tempVec, rep_speedScaled * Time.deltaTime);

            //restore part vertics
            if (repairSupport == DestructionRepairSupport.fullHigh)
            {
                Color tempColor;
                float tempSpeed = rep_speedScaled * 0.6f * Time.deltaTime;
                float tempSpeed2 = rep_speedScaled * Time.deltaTime;

                foreach (int vI in allParts[partIndex].rendVertexIndexes)
                {
                   tempVec = rep_verticsOrginal[vI] - verticsOrginalThreaded[vI];
                   if (tempVec.sqrMagnitude > 0.0001f) hasRestoredAll = false;
                   verticsOrginalThreaded[vI] += Vector3.ClampMagnitude(tempVec, tempSpeed);
                   
                   tempColor = verticsColorThreaded[vI];
                   if (tempColor.a > 0.0001f) hasRestoredAll = false;
                   //tempColor.a = Mathf.MoveTowards(tempColor.a, 0.0f, rep_speedScaled * Time.deltaTime);
                   tempColor.a = Math.Max(tempColor.a - tempSpeed2, 0.0f);
                   verticsColorThreaded[vI] = tempColor;
                }
            }

            //when part gets restored
            if (hasRestoredAll == true)
            {
                foreach (int vI in allParts[partIndex].rendVertexIndexes)
                {
                    verticsForceThreaded[vI] = 0.0f;
                }

                allParts[partIndex].partBrokenness = 0.0f;
                rep_partsToRepair.RemoveAt(repIndex);
                allParts[partIndex].col.enabled = true;

                //set position and rotation
                allParts[partIndex].col.transform.localRotation = Quaternion.identity;
                allParts[partIndex].col.transform.localPosition = rep_orginalLocalPartPoss[partIndex];

                if (repairSupport == DestructionRepairSupport.fullLow)
                {
                    Color tempColor;

                    foreach (int vI in allParts[partIndex].rendVertexIndexes)
                    {
                        verticsOrginalThreaded[vI] = rep_verticsOrginal[vI];

                        tempColor = verticsColorThreaded[vI];
                        tempColor.a = 0.0f;
                        verticsColorThreaded[vI] = tempColor;
                    }
                }

                return true;
            }

            return false;
        }

        private void SaveOrLoadAsset(bool doSave, bool removeAsset = false)
        {
            if (saveAsset == null) saveAsset = Resources.Load<FractureSaveAsset>("fractureSaveAsset");

            if (removeAsset == true)
            {
                saveAsset.RemoveSavedData(this, saved_fracId);
                return;
            }

            if (doSave == true) saved_fracId = saveAsset.Save(this, saved_fracId);
            else saveAsset.Load(this, saved_fracId);
        }
    }
}