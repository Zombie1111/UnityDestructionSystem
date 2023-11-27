using Unity.Burst;
using UnityEngine;
using Random = System.Random;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using UnityEditor;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;
using System;
using Time = UnityEngine.Time;
using Component = UnityEngine.Component;
using System.ComponentModel;
using Unity.Jobs;
using Unity.Collections;
using Unity.VisualScripting;
using System.Data;
using System.Text.RegularExpressions;
using UnityEngine.Jobs;
using System.Threading;
using System.ComponentModel.Design;

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
                    yourScript.Gen_fractureObject(yourScript.gameObject);
                }

                if (GUILayout.Button("Remove Fracture"))
                {
                    yourScript.Gen_loadAndMaybeSaveOgData(yourScript.gameObject, false);
                }

                EditorGUILayout.Space();

                DrawPropertiesExcluding(serializedObject, "generateNavMesh", "stopGenerating");

                serializedObject.ApplyModifiedProperties();
            }
        }
#endif

        //fracture settings
        [Header("General")]
        [SerializeField] private float worldScale = 1.0f;
        [SerializeField] private int fractureCount = 15;
        [SerializeField] private bool dynamicFractureCount = true;
        [SerializeField] private int seed = -1;

        [Space(10)]
        [Header("Physics")]
        [SerializeField] private float massDensity = 0.1f;
        [SerializeField] private PhysicMaterial phyMat_defualt = null;
        [SerializeField] private ColliderType colliderType = ColliderType.mesh;
        [SerializeField] private OptPhysicsMain phyMainOptions = new();

        [Space(10)]
        [Header("Destruction")]
        public float destructionThreshold = 1.0f;
        [SerializeField] private float minDelay = 0.05f;

        [Space(10)]
        [Header("Material")]
        [SerializeField] private Material matInside_defualt = null;
        [SerializeField] private Material matOutside_defualt = null;

        [Space(50)]
        [Header("Debug (Dont touch)")]
        [SerializeField] private OrginalObjData ogData = null;

        /// <summary>
        /// All fractured parts have one of these as its parent
        /// </summary>
        [SerializeField] private List<FracParents> allFracParents = new();

        /// <summary>
        /// All the fractured parts.
        /// </summary>
        [SerializeField] private FracParts[] allParts = new FracParts[0];

        /// <summary>
        /// If MainPhysicsType == overlappingIsKinematic, bool for all parts that is true if the part was is inside a non fractured mesh when generated
        /// </summary>
        [SerializeField] private bool[] kinematicPartStatus = new bool[0];

        /// <summary>
        /// The renderer used to render the fractured mesh (always skinned)
        /// </summary>
        [SerializeField] private SkinnedMeshRenderer fracRend = null;

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
            public OptMainPhysicsType MainPhysicsType = OptMainPhysicsType.overlappingIsKinematic;
            public bool useGravity = true;
            public float massMultiplier = 0.5f;
            public float drag = 0.0f;
            public float angularDrag = 0.05f;
            public CollisionDetectionMode collisionDetection = CollisionDetectionMode.Discrete;
            public RigidbodyInterpolation interpolate = RigidbodyInterpolation.Interpolate;
            public RigidbodyConstraints constraints;
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

        //create data to save
        public void Gen_fractureObject(GameObject objectToFracture)
        {
            //fracture the object
            //restore orginal data
            Gen_loadAndMaybeSaveOgData(objectToFracture, false);

            //Get the meshes to fracture
            float worldScaleDis = worldScale * 0.0001f;
            List<MeshData> meshesToFracture = Gen_getMeshesToFracture(objectToFracture, worldScaleDis);
            if (meshesToFracture == null) return;

            //Fracture the meshes into pieces
            List<Mesh> fracturedMeshes = Gen_fractureMeshes(meshesToFracture, fractureCount, dynamicFractureCount, worldScaleDis, seed, false);
            if (fracturedMeshes == null) return;

            //Save current orginal data (Save as late as possible)
            Gen_loadAndMaybeSaveOgData(objectToFracture, true);

            //setup part basics, like defualt frac parent, create parts transform+colliders, convert mesh to localspace
            List<Mesh> fracturedMeshesLocal = Gen_setupPartBasics(new(fracturedMeshes), phyMat_defualt);
            if (fracturedMeshesLocal == null)
            {
                Gen_loadAndMaybeSaveOgData(objectToFracture, false);
                return;
            }

            //setup fracture renderer
            Gen_setupRenderer(ref allParts, fracturedMeshes, transform, matInside_defualt, matOutside_defualt);

            //log result when done
            if (Mathf.Approximately(transform.lossyScale.x, transform.lossyScale.y) == false || Mathf.Approximately(transform.lossyScale.z, transform.lossyScale.y) == false) Debug.Log("(Warning) " + transform.name + " lossy scale XYZ should all be the same. If not stretching may accure when rotating parts");
            if (transform.TryGetComponent<Rigidbody>(out _) == true) Debug.Log("(Warning) " + transform.name + " has a rigidbody and it may cause issues. Its recommended to remove it and use the fracture physics options instead");
            Debug.Log("Fractured the object into " + fracturedMeshesLocal.Count + " parts, total vertex count = " + fracturedMeshes.Sum(mesh => mesh.vertexCount));
            //debug, visualize
            //foreach (MeshData mD in meshesToFracture)
            //{
            //    FractureHelperFunc.Debug_drawMesh(mD.mesh, false, 5.0f);
            //}

            //foreach (Mesh mesh in fracturedMeshes)
            //{
            //    FractureHelperFunc.Debug_drawMesh(mesh, false, 0.1f);
            //}
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
            //get and setup combined mesh bones
            Mesh comMesh = FractureHelperFunc.ConvertMeshWithMatrix(FractureHelperFunc.CombineMeshes(partMeshes, ref fParts), rendHolder.worldToLocalMatrix);

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
            //comMesh.bindposes = fParts.Select(part => part.col.transform.worldToLocalMatrix * rendHolder.localToWorldMatrix).ToArray();
            comMesh.bindposes = fParts.Select(part => part.col.transform.worldToLocalMatrix * rendHolder.localToWorldMatrix).ToArray();

            //set renderer
            SkinnedMeshRenderer sRend = rendHolder.GetOrAddComponent<SkinnedMeshRenderer>();
            sRend.enabled = true;
            sRend.rootBone = rendHolder;
            sRend.bones = fParts.Select(part => part.col.transform).ToArray();
            sRend.sharedMaterials = new Material[2] { matInside, matOutside };
            sRend.sharedMesh = comMesh;
            ////combine all meshes and assign to mesh filter
            //MeshFilter mF = rendHolder.GetOrAddComponent<MeshFilter>();
            //mF.sharedMesh = FractureHelperFunc.CombineMeshes(partMeshes, ref fParts);
            //
            ////set renderer
            //MeshRenderer mR = rendHolder.GetOrAddComponent<MeshRenderer>();
            //mR.sharedMaterials = new Material[2] { matInside, matOutside };
        }

        private List<Mesh> Gen_setupPartBasics(List<Mesh> meshes, PhysicMaterial phyMatToUse)
        {
            //save the world space meshes
            Mesh[] worldMeshes = meshes.ToArray();

            //create defualt parent
            Transform pTrans = new GameObject("fracParentBase_" + transform.name).transform;
            pTrans.SetParent(transform);
            pTrans.SetPositionAndRotation(transform.position, transform.rotation);
            pTrans.localScale = Vector3.one;
            allFracParents = new() { new() { parentTrans = pTrans, partIndexes = Enumerable.Range(0, meshes.Count).ToList() } };

            //add rigidbody to parent
            allFracParents[0].parentRb = pTrans.GetOrAddComponent<Rigidbody>();
            allFracParents[0].parentRb.collisionDetectionMode = phyMainOptions.collisionDetection;
            allFracParents[0].parentRb.interpolation = phyMainOptions.interpolate;
            allFracParents[0].parentRb.useGravity = phyMainOptions.useGravity;
            allFracParents[0].parentRb.drag = phyMainOptions.drag;
            allFracParents[0].parentRb.angularDrag = phyMainOptions.angularDrag;
            allFracParents[0].parentRb.constraints = phyMainOptions.constraints;

            //add parent script to parent
            allFracParents[0].fParent = pTrans.GetOrAddComponent<FractureParent>();
            allFracParents[0].fParent.fractureDaddy = this;

            //create part transforms
            allParts = new FracParts[meshes.Count];

            for (int i = 0; i < meshes.Count; i += 1)
            {
                Transform newT = new GameObject("Part(" + i + ")_" + transform.name).transform;
                newT.SetParent(pTrans);
                //newT.SetPositionAndRotation(meshes[i].bounds.center, pTrans.rotation);
                newT.SetPositionAndRotation(FractureHelperFunc.GetMedianPosition(meshes[i].vertices), pTrans.rotation);
                newT.localScale = Vector3.one;

                meshes[i] = FractureHelperFunc.ConvertMeshWithMatrix(Instantiate(meshes[i]), newT.worldToLocalMatrix); //Instantiate new mesh to keep worldSpaceMeshes

                //the part data is created here
                FracParts newP = new() { col = Gen_createPartCollider(newT, meshes[i], phyMatToUse), rendVertexIndexes = new(), partBrokenness = 0.0f, neighbourParts = new() };
                allParts[i] =newP;
            }

            //setup part neighbours and isKinematic
            Vector3[] wVerts;

            float worldDis = worldScale * 0.01f;
            if (phyMainOptions.MainPhysicsType == OptMainPhysicsType.overlappingIsKinematic) kinematicPartStatus = new bool[allParts.Length];
            else kinematicPartStatus = new bool[0];

            for (int i = 0; i < allParts.Length; i += 1)
            {
                wVerts = worldMeshes[i].vertices;

                for (int ii = 0; ii < wVerts.Length; ii += 1)
                {
                    Gen_getKinematicAndNeighboursFromTrans(Physics.OverlapSphere(wVerts[ii], worldDis).Select(col => col.transform).ToArray(), i);
                }

                Gen_getKinematicAndNeighboursFromTrans(FractureHelperFunc.LinecastsBetweenPositions(wVerts).Select(col => col.transform).ToArray(), i);
            }

            //update parent info
            Run_updateParentInfo(0);

            //return meshes since it has been converted to parent localspace
            return meshes;

            void Gen_getKinematicAndNeighboursFromTrans(Transform[] transs, int ogPi)
            {
                FractureThis pFracThis;
                int nearI;

                for (int i = 0; i < transs.Length; i += 1)
                {
                    //get part index from hit trans
                    pFracThis = transs[i].GetComponentInParent<FractureThis>();

                    nearI = Run_tryGetPartIndexFromTrans(transs[i], false);
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
                        //Hit is a new neighbour part, add to neighbour part list
                        print(nearI + " " + ogPi);
                        allParts[ogPi].neighbourParts.Add(nearI);
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
        public void Gen_loadAndMaybeSaveOgData(GameObject objToUse, bool doSave = false)
        {
            //load/restore og object
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

                ogData = null;
            }

            //destroy all frac parents
            for (int i = 0; i < allFracParents.Count; i += 1)
            {
                if (allFracParents[i].parentTrans == null) continue;
                DestroyImmediate(allFracParents[i].parentTrans.gameObject);
            }

            allFracParents.Clear();
            allParts = new FracParts[0];

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
        private List<MeshData> Gen_getMeshesToFracture(GameObject obj, float worldScaleDis = 0.0001f)
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
                    break;
                }
                else if (rend.TryGetComponent(out MeshFilter meshF) == true)
                {
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

        //############################RUNTIME########################################
        private void Run_updateParentInfo(int pIndex)
        {
            //update parent total mass
            allFracParents[pIndex].parentRb.mass = allFracParents[pIndex].partIndexes.Count * massDensity * phyMainOptions.massMultiplier;

            //update isKinematic
            if (phyMainOptions.MainPhysicsType == OptMainPhysicsType.alwaysDynamic)
            {
                allFracParents[pIndex].parentRb.isKinematic = false;
            }
            else if (phyMainOptions.MainPhysicsType == OptMainPhysicsType.alwaysKinematic)
            {
                allFracParents[pIndex].parentRb.isKinematic = true;
            }
            else if (phyMainOptions.MainPhysicsType == OptMainPhysicsType.orginalIsKinematic)
            {
                allFracParents[pIndex].parentRb.isKinematic = pIndex == 0;
            }
            else
            {
                bool pIsKin = false;

                foreach (int pI in allFracParents[pIndex].partIndexes)
                {
                    if (kinematicPartStatus[pI] == true)
                    {
                        pIsKin = true;
                        break;
                    }
                }

                allFracParents[pIndex].parentRb.isKinematic = pIsKin;
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
            if (impact_totalForce > 0.0f)
            {
                if (currentDelayTime >= minDelay && ThreadCalcDes == null)
                {
                    currentDelayTime = 0.0f;
                    StartCoroutine(CalculateDestruction());
                }

                currentDelayTime += Time.deltaTime;
            }
        }

        private struct DestructionData
        {

        }

        private List<Vector3> impact_positions = new();
        private float impact_totalForce = 0.0f;
        private Vector3 impact_Direction = Vector3.zero;

        /// <summary>
        /// Calculates destruction to apply the parent as soon as possible
        /// </summary>
        /// <param name="parentIndex"></param>
        /// <param name="impactPositions"></param>
        /// <param name="impactTotalForce"></param>
        /// <param name="impactDirection"></param>
        /// <returns></returns>
        //public void RequestDestruction(Vector3[] impPositions, float impTotalForce, Vector3 impDirection)
        public void RequestDestruction(Vector3 impPosition, float impTotalForce, Vector3 impDirection)
        {
            if (impact_totalForce <= 0.0f) currentDelayTime = 0.0f;

            impact_positions.Add(impPosition);
            impact_totalForce += impTotalForce;
            if (impact_Direction == Vector3.zero) impact_Direction = impDirection;
            else impact_Direction = Vector3.Lerp(impact_Direction, impDirection, impTotalForce / impact_totalForce).normalized;
        }

        Task<DestructionData> ThreadCalcDes = null;

        private IEnumerator CalculateDestruction()
        {
            print("destruction");

            foreach (Vector3 pos in impact_positions)
            {
                Debug.DrawLine(pos, pos + impact_Direction, Color.red, 0.5f, false);
            }

            Vector3[] partPoss = allParts.Select(part => part.col.transform.position).ToArray();
            ThreadCalcDes = Task.Run(() => CalculateDestructionThread(allParts.ToArray(), partPoss, impact_positions.ToArray(), impact_totalForce, impact_Direction));
            while (ThreadCalcDes.IsCompleted == false && ThreadCalcDes.IsFaulted == false) yield return null;
            if (ThreadCalcDes.IsFaulted == true)
            {
                //when error accure, dont reset so it will try to destroy again
                ThreadCalcDes = null;
                yield break;
            }

            //apply the destruction
            DestructionData desData = ThreadCalcDes.Result;

            //reset when done
            ThreadCalcDes = null;
            impact_positions.Clear();
            impact_totalForce = 0.0f;
            impact_Direction = Vector3.zero;
        }

        private DestructionData CalculateDestructionThread(FracParts[] fParts, Vector3[] fPartsPos, Vector3[] impPoss, float impTotalF, Vector3 impDir)
        {
            return new();
        }
    }
}