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
using System.Collections.Concurrent;
using UnityEngine.Experimental.AI;
using System.Threading;
using UnityEngine.UIElements;
using UnityEditor.Rendering;
using UnityEditor.Search;
using Unity.Mathematics;
using System.Runtime.CompilerServices;
using UnityEditor.SceneManagement;
using JetBrains.Annotations;
using UnityEditor.XR;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Jobs;
using Unity.Collections;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;
namespace Zombie1111_uDestruction
{
    public class FractureThis : MonoBehaviour
    {

        #region EditorAndOptions

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
                    yourScript.Gen_removeFracture();
                }

                EditorGUILayout.Space();

                DrawPropertiesExcluding(serializedObject, "generateNavMesh", "stopGenerating");

                serializedObject.ApplyModifiedProperties();
            }

            private void OnSceneGUI()
            {
                //draw resistance multiplier resize handels
                FractureThis script = (FractureThis)target;

                foreach (FractureThis.FracVolume fVolume in script.resistanceMultipliers)
                {
                    if (fVolume.boundsColor.a < 0.8f) continue;
                    //Bounds bounds = fVolume.volume; // Get the Bounds from your script
                    Bounds bounds = FractureHelperFunc.ConvertBoundsWithMatrix(new() { center = fVolume.volume.center, extents = fVolume.volume.extents }, script.transform.localToWorldMatrix);

                    // Calculate handle positions
                    Vector3 center = bounds.center;
                    Vector3 size = bounds.size;

                    Vector3 min = center - size * 0.5f;
                    Vector3 max = center + size * 0.5f;

                    EditorGUI.BeginChangeCheck();

                    // Record the object for undo
                    Undo.RecordObject(script, "Resize Bounds");

                    Vector3 newMin = Handles.PositionHandle(min, Quaternion.LookRotation(Vector3.up, Vector3.left));
                    Vector3 newMax = Handles.PositionHandle(max, Quaternion.LookRotation(Vector3.up, Vector3.left));

                    if (EditorGUI.EndChangeCheck())
                    {
                        // Calculate the new size based on handle movement
                        size = newMax - newMin;

                        // Update the bounds with the new size and center
                        bounds.size = size;
                        bounds.center = (newMin + newMax) * 0.5f;

                        // Apply the new bounds to your script
                        fVolume.volume = FractureHelperFunc.ConvertBoundsWithMatrix(bounds, script.transform.worldToLocalMatrix);

                        // Register the object for complete undo
                        Undo.RegisterCompleteObjectUndo(script, "Resize Voxel Bounds");
                    }
                }

            }
        }

        public void CopyFracturePropertiesFrom(FractureThis from)
        {
            halfUpdateRate = from.halfUpdateRate;
            worldScale = from.worldScale;
            fractureCount = from.fractureCount;
            dynamicFractureCount = from.dynamicFractureCount;
            seed = from.seed;
            maxFractureAttempts = from.maxFractureAttempts;
            generationQuality = from.generationQuality;
            massDensity = from.massDensity;
            colliderType = from.colliderType;
            phyMainOptions = from.phyMainOptions;
            phyPartsOptions = from.phyPartsOptions;
            destructionThreshold = from.destructionThreshold;
            destructionResistance = from.destructionResistance;
            minDelay = from.minDelay;
            minAllowedMainPhySize = from.minAllowedMainPhySize;
            multithreadedDestruction = from.multithreadedDestruction;
            //destructionWidthCurve = from.destructionWidthCurve;
            falloffStrenght = from.falloffStrenght;
            falloffPower = from.falloffPower;
            //selfCollisionMultiplier = from.selfCollisionMultiplier;
            repairSupport = from.repairSupport;
            repairSpeed = from.repairSpeed;
            vertexDisplacementStenght = from.vertexDisplacementStenght;
            displacementIgnoresWidth = from.displacementIgnoresWidth;
            recalculateOnDisplacement = from.recalculateOnDisplacement;
            doVertexColors = from.doVertexColors;
            setVertexColorOnBreak = from.setVertexColorOnBreak;
            matInside_defualt = from.matInside_defualt;
            matOutside_defualt = from.matOutside_defualt;
            physicsMat = from.physicsMat;

            resistanceMultipliers = from.resistanceMultipliers;
        }
#endif

        //fracture settings
        [Header("Fracture")]
        public FractureSaveAsset saveAsset = null;
        [Tooltip("Time until the fracture can be destroyed after awake, can also be set to <0.0f to disable destruction")] public float immortalTime = 1.0f;
        [SerializeField] private bool halfUpdateRate = true;
        [SerializeField] private float worldScale = 1.0f;
        [SerializeField] private int fractureCount = 15;
        [SerializeField] private bool dynamicFractureCount = true;
        [SerializeField] private int seed = -1;
        [SerializeField] private byte maxFractureAttempts = 20;
        public GenerationQuality generationQuality = GenerationQuality.medium;

        [Space(10)]
        [Header("Physics")]
        [SerializeField] private float massDensity = 0.1f;
        [SerializeField] private ColliderType colliderType = ColliderType.mesh;
        [SerializeField] private SelfCollideWithRule selfCollisionRule = SelfCollideWithRule.ignoreNeighbours;
        [SerializeField] private OptPhysicsMain phyMainOptions = new();
        [SerializeField] private OptPhysicsParts phyPartsOptions = new();

        [Space(10)]
        [Header("Destruction")]
        public float destructionThreshold = 1.0f;
        [SerializeField] private float destructionResistance = 4.0f;
        [SerializeField] private float destructionConsumption = 4.0f;
        [SerializeField] private float minDelay = 0.05f;
        [SerializeField] private int minAllowedMainPhySize = 2;
        [SerializeField] private bool multithreadedDestruction = true;
        [SerializeField] private float falloffStrenght = 20.0f;
        [SerializeField] private float falloffPower = 1.0f;
        [SerializeField] private float partResistanceFactor = 1.0f;
        //[SerializeField] private float widthFalloffStrenght = 20.0f;
        //[SerializeField] private float widthFalloffPower = 1.0f;
        [SerializeField] private DestructionRepairSupport repairSupport = DestructionRepairSupport.fullHigh;
        [SerializeField] private float repairSpeed = 1.0f;

        [Space(10)]
        [Header("Mesh")]
        [SerializeField] private float vertexDisplacementStenght = 1.0f;
        [SerializeField] private bool displacementIgnoresWidth = true;
        //[SerializeField] private bool limitDisplacement = true;
        [SerializeField] private NormalRecalcMode recalculateOnDisplacement = NormalRecalcMode.normalsOnly;
        [SerializeField] private bool doVertexColors = false;
        [SerializeField] private bool setVertexColorOnBreak = true;

        [Space(10)]
        [Header("Material")]
        [SerializeField] private Material matInside_defualt = null;
        [SerializeField] private Material matOutside_defualt = null;
        [SerializeField] private PhysicMaterial physicsMat = null;
        public List<FracVolume> resistanceMultipliers = new();

        [System.Serializable]
        public class FracVolume
        {
            public Bounds volume = new();
            public float fadeStrenght = 1.0f;
            public float multiplier = 1.0f;

#if UNITY_EDITOR
            [Tooltip("The color of volume bounds shown in the editor, useful to know what multiplier is for what area")] public Color boundsColor = Color.black;
#endif
        }


#if UNITY_EDITOR
        [Space(10)]
        [Header("Debug")]
        [SerializeField][Tooltip("Only works at runtime or directly after fracturing")] private DebugMode debugMode = DebugMode.none;

        private enum DebugMode
        {
            none,
            showStructure,
            showBones,
            showDestruction
        }

        private bool eOnly_globalHandlerHasBeenChecked = false;
        private int eOnly_loadedId = -1;

        private void OnDrawGizmos()
        {
            if (Application.isPlaying == true) return;

            //load saveAsset if needed
            if (saveAsset != null && saveAsset.fracSavedData.id != eOnly_loadedId && saved_fracId >= 0 && fracRend != null)
            {
                SaveOrLoadAsset(false);
                eOnly_loadedId = saveAsset.fracSavedData.id; //we set directly from save file because if unable to load it should only try once
            }

            //Verify that globalHandler is valid
            if (globalHandler == null)
            {
                if (VerifyGlobalHandler(!eOnly_globalHandlerHasBeenChecked) == false) eOnly_globalHandlerHasBeenChecked = true;
                else
                {
                    eOnly_globalHandlerHasBeenChecked = false;
                    EditorUtility.SetDirty(this);
                }
            }

            //delete saveAsset if temp and globalHandler is null
            if (globalHandler == null && saveAsset != null && AssetDatabase.GetAssetPath(saveAsset).Contains("TempFracSaveAssets") == true)
            {
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(saveAsset));
            }

            //verify that fracRend reference the correct object
            if (fracRend != null && fracRend.transform != transform)
            {
                Debug.LogError(transform.name + " fracture does not reference the correct object and will be removed (Has the script been copied?)");
                Gen_removeFracture();
                return;
            }
        }

        private void OnDrawGizmosSelected()
        {
            //draw resistance multiplier bounds
            for (int i = 0; i < resistanceMultipliers.Count; i += 1)
            {
                if (resistanceMultipliers[i].boundsColor == new Color(0.0f, 0.0f, 0.0f, 0.0f))
                {
                    resistanceMultipliers[i].multiplier = 1.0f;
                    resistanceMultipliers[i].fadeStrenght = 1.0f;
                    resistanceMultipliers[i].boundsColor = Color.white;
                    resistanceMultipliers[i].volume.center = transform.position + (Vector3.one * 0.5f);
                    resistanceMultipliers[i].volume.extents = Vector3.one;
                    resistanceMultipliers[i].volume = FractureHelperFunc.ConvertBoundsWithMatrix(resistanceMultipliers[i].volume, transform.worldToLocalMatrix);
                }

                if (resistanceMultipliers[i].boundsColor.a < 0.01f) continue;

                Bounds bound = FractureHelperFunc.ConvertBoundsWithMatrix(new() { center = resistanceMultipliers[i].volume.center, extents = resistanceMultipliers[i].volume.extents }, transform.localToWorldMatrix);
                Gizmos.color = resistanceMultipliers[i].boundsColor;
                Gizmos.DrawWireCube(bound.center, bound.size);
                Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, Gizmos.color.a / 3.0f);
                Gizmos.DrawWireCube(bound.center, bound.size + (Vector3.one / resistanceMultipliers[i].fadeStrenght));
            }

            if (debugMode == DebugMode.none || allParts == null || allParts.Length <= 0) return;

            //visualize fracture structure
            if (debugMode == DebugMode.showStructure)
            {
                int pIndex;

                for (int i = 0; i < allParts.Length; i += 1)
                {
                    pIndex = allParts[i].parentIndex;

                    foreach (int ii in allParts[i].neighbourParts)
                    {
                        if (allParts[ii].parentIndex < 0 || allParts[ii].parentIndex != pIndex) continue;
                        if (isRealSkinnedM == true && allParts[ii].parentIndex == 0) Debug.DrawLine(allSkinPartCols[i].bounds.center, allSkinPartCols[ii].bounds.center, Color.red, 0.0f, false);
                        else Debug.DrawLine(allParts[i].col.bounds.center, allParts[ii].col.bounds.center, Color.red, 0.0f, false);
                    }
                }
            }

            //visualize fracture bones
            if (debugMode == DebugMode.showBones)
            {
                Transform[] tBones = fracRend.bones;
                for (int i = 0; i < tBones.Length - 1; i += 1)
                {
                    Debug.DrawLine(tBones[i].position, tBones[i + 1].position, Color.blue, 0.0f, false);
                }

                //SkinnedMeshRenderer sRend = fracRend;
                //if (sRend == null) sRend = transform.GetComponentInChildren<SkinnedMeshRenderer>();
                //if (sRend == null) return;
                //
                //Mesh bMesh = new();
                //sRend.BakeMesh(bMesh, true);
                //Vector3[] vers = FractureHelperFunc.ConvertPositionsWithMatrix(bMesh.vertices, sRend.transform.localToWorldMatrix);
                //BoneWeight[] wes = sRend.sharedMesh.boneWeights;
                //
                //for (int i = 0; i < vers.Length; i += 3)
                //{
                //    if (wes[i].weight0 > 0.01f) Debug.DrawLine(vers[i], sRend.bones[wes[i].boneIndex0].position, Color.blue, 0.0f, false);
                //    if (wes[i].weight1 > 0.01f) Debug.DrawLine(vers[i], sRend.bones[wes[i].boneIndex1].position, Color.blue, 0.0f, false);
                //    if (wes[i].weight2 > 0.01f) Debug.DrawLine(vers[i], sRend.bones[wes[i].boneIndex2].position, Color.blue, 0.0f, false);
                //    if (wes[i].weight3 > 0.01f) Debug.DrawLine(vers[i], sRend.bones[wes[i].boneIndex3].position, Color.blue, 0.0f, false);
                //}
            }
        }


#endif

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

        private enum SelfCollideWithRule
        {
            ignoreNone,
            ignoreNeighbours,
            ignoreAll
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
            public byte objectLayer = 0;
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

        #endregion EditorAndOptions






        #region SaveLoadFractureSystem

        [Space(100)]
        [Header("#DONT TOUCH#")]
        [SerializeField] private OrginalObjData ogData = null;
        public Collider[] saved_allPartsCol = new Collider[0];
        public int saved_fracId = -1;

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

        private void SaveOrLoadAsset(bool doSave)
        {
            if (saveAsset == null)
            {
                Debug.LogError("No saveAsset has been assigned to " + transform.name + " fracture");
                return;
            }

            if (doSave == true)
            {
                //save
                saved_fracId = saveAsset.Save(this);
                return;
            }

            if (saveAsset.Load(this) == false)
            {
                //when cant load, log error
                if (saveAsset.fracSavedData.id < 0 || saved_fracId < 0)
                {
                    Debug.LogError("No fracture has been generated for " + transform.name);
                    return;
                }

                Debug.LogError("Unable to load data from " + saveAsset.name + " to " + transform.name + " fracture because it has been saved by another fracture!" +
                    " (Use a different saveAsset or use a prefab to have multiable identical fractures)");
                return;
            }

#if UNITY_EDITOR
            //update fracRend bounds in editor
            if (Application.isPlaying == false && allFracParents != null) UpdateFracRendBounds();
#endif
        }

        /// <summary>
        /// Restores all components on the previous saved objToUse
        /// </summary>
        /// <param name="doSave">If true, objToUse og data will be saved</param>
        private void Gen_loadAndMaybeSaveOgData(bool doSave = false)
        {
            //make sure we can load and save
#if UNITY_EDITOR
            if (GetFracturePrefabType() == 1)
            {
                Debug.LogError(transform.name + " cannot be removed because its a prefab instance, open the prefab asset and remove inside it");
                return;
            }
#endif

            //load/restore og object
            saved_fracId = -1;
            GameObject objToUse = gameObject;
            EditorUtility.SetDirty(objToUse);

            if (ogData != null)
            {
                //load og components
                foreach (OrginalCompData ogD in ogData.ogCompData)
                {
                    if (ogD.comp == null
                        || ogD.comp.gameObject == null
                        || FractureHelperFunc.GetIfTransformIsAnyParent(transform, ogD.comp.transform) == false) continue;

                    Type targetType = ogD.comp.GetType();
                    if (IsValidType(targetType) == false) continue;

                    var enabledProperty = targetType.GetProperty("enabled");

                    if (enabledProperty == null || enabledProperty.PropertyType != typeof(bool)) continue;

                    enabledProperty.SetValue(ogD.comp, ogD.wasEnabled, null);
                }

                //load og renderer
                if (ogData.hadRend == true && fracRend != null && fracRend.transform == transform)
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
                else if (fracRend != null && fracRend.transform == transform)
                {
                    DestroyImmediate(fracRend);
                }

                ogData = null;
            }

            //destroy all frac parents
            for (int i = 0; i < allFracParents.Count; i += 1)
            {
                if (allFracParents[i].parentTrans == null || allFracParents[i].parentTrans.parent != transform) continue;
                if (i == 0 && isRealSkinnedM == true)
                {

                    DestroyImmediate(allFracParents[i].parentRb);
                    DestroyImmediate(allFracParents[i].fParent);

                    for (int ii = 0; ii < allFracParents[i].partIndexes.Count; ii += 1)
                    {
                        if (saved_allPartsCol.Length <= 0)
                        {
                            Debug.LogError(transform.name + " parts list was for some weird reason cleared before parts was removed (You must delete them manually)");
                            break;
                        }

                        if (saved_allPartsCol[allFracParents[i].partIndexes[ii]] != null) DestroyImmediate(saved_allPartsCol[allFracParents[i].partIndexes[ii]].gameObject);
                    }

                    continue;
                }

                DestroyImmediate(allFracParents[i].parentTrans.gameObject);
            }

            //remove real skin bone cols
            for (int i = 0; i < allSkinPartCols.Length; i += 1)
            {
                if (allSkinPartCols[i] == null || FractureHelperFunc.GetIfTransformIsAnyParent(transform, allSkinPartCols[i].transform) == false) continue;
                DestroyImmediate(allSkinPartCols[i]);
            }

            allSkinPartCols = new Collider[0];
            allFracParents.Clear();
            verticsLinkedThreaded = new IntList[0];
            allParts = new FracParts[0];
            allPartsResistance = new float[0];
            boneWe_broken = new BoneWeight[0];

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

        #endregion SaveLoadFractureSystem






        #region GenerateFractureSystem

        private int[] tempFracVerOgMeshId = null; //only set during the mesh fracturing process
        private List<int> tempPartOgMeshId = null; //only set during the mesh fracturing process
        private OrginalObjData tempOgRealSkin = null; //only set during the mesh fracturing process

        [System.Serializable]
        public class FracParents
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

            /// <summary>
            /// The part trans
            /// </summary>
            public Transform trans;
        }

        /// <summary>
        /// Verifies that a valid globalHandler exists and assigns it, returns false if no valid globalHandler exists
        /// </summary>
        /// <returns></returns>
        private bool VerifyGlobalHandler(bool canLogError = true)
        {
            FractureGlobalHandler[] handlers = GameObject.FindObjectsOfType<FractureGlobalHandler>(true);
            if (handlers == null || handlers.Length < 1 || handlers[0].isActiveAndEnabled == false)
            {
                if (canLogError == true) Debug.LogError("There is no active FractureGlobalHandler script in this scene, make sure a active Gameobject has the script attatch to it");
                return false;
            }
            else if (handlers.Length > 1)
            {
                if (canLogError == true) Debug.LogError("There are more than one FractureGlobalHandler script in this scene, please remove all but one and refracture all objects");
                return false;
            }

            if (handlers[0].gameObject.scene != gameObject.scene) return false;

            globalHandler = handlers[0];
            return true;
        }

#if UNITY_EDITOR
        private bool eOnly_isPrefabAsset = false;
#endif

        /// <summary>
        /// Call to remove the fracture, returns true if successfully removed the fracture
        /// </summary>
        public bool Gen_removeFracture(bool isPrefabAsset = false)
        {
#if UNITY_EDITOR
            //set if should always count as prefab asset
            eOnly_isPrefabAsset = isPrefabAsset;

            //if prefab instance, remove on the prefab asset
            if (GetFracturePrefabType() == 1)
            {
                bool okRem = true;
                using (var editingScope = new PrefabUtility.EditPrefabContentsScope(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject)))
                {
                    foreach (FractureThis fracT in editingScope.prefabContentsRoot.GetComponentsInChildren<FractureThis>())
                    {
                        if (okRem == true) okRem = fracT.Gen_removeFracture(true);
                        else fracT.Gen_removeFracture(true);
                    }
                }

                return okRem;
            }
#endif

            //remove the fracture
            Gen_loadAndMaybeSaveOgData(false);
            return true;
        }

        /// <summary>
        /// Call to fracture the object the mesh is attatched to, returns true if successfully fractured the object
        /// </summary>
        public bool Gen_fractureObject(bool isPrefabAsset = false)
        {
#if UNITY_EDITOR
            //set if should always count as prefab asset
            eOnly_isPrefabAsset = isPrefabAsset;

            //if prefab instance, fracture on the prefab asset
            if (GetFracturePrefabType() == 1)
            {
                bool okFrac = true;
                bool didFindFrac = false;
                string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
                PrefabUtility.ApplyObjectOverride(this, prefabPath, InteractionMode.AutomatedAction);

                using (var editingScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
                {
                    //This script properties must always be synced
                    foreach (FractureThis fracT in editingScope.prefabContentsRoot.GetComponentsInChildren<FractureThis>())
                    {
                        if (okFrac == true) okFrac = fracT.Gen_fractureObject(true);
                        else fracT.Gen_fractureObject(true);

                        didFindFrac = true;
                    }
                }

                if (didFindFrac == false)
                {
                    Debug.LogError(transform.name + " prefab does not contain the fracture script (Have you applied overrides?)");
                    return false;
                }

                return okFrac;
            }
#endif

            //return if required stuff is missing
            VerifyGlobalHandler();

            if (saveAsset == null && (globalHandler == null || globalHandler.TryCreateTempSaveAsset(this) == false))
            {
                Debug.LogError("You must assign a saveAsset to " + transform.name);
                return false;
            }

            if (gameObject.GetComponentsInParent<FractureThis>().Length > 1 || gameObject.GetComponentsInChildren<FractureThis>().Length > 1)
            {
                Debug.LogError("Cannot fracture " + transform.name + " because there is another fracture script in any of its parents or children");
                return false;
            }

            //fracture the object
            GameObject objectToFracture = gameObject;

            //restore orginal data
            Gen_loadAndMaybeSaveOgData(false);

            //Get the meshes to fracture
            float worldScaleDis = worldScale * 0.0001f;
            tempOgRealSkin = new();

            List<MeshData> meshesToFracture = Gen_getMeshesToFracture(objectToFracture, worldScaleDis);
            if (meshesToFracture == null) return false;

            //Fracture the meshes into pieces
            tempPartOgMeshId = new();
            List<Mesh> fracturedMeshes = Gen_fractureMeshes(meshesToFracture, fractureCount, dynamicFractureCount, worldScaleDis, seed, true);
            if (fracturedMeshes == null) return false;

            //Save current orginal data (Save as late as possible)
            Gen_loadAndMaybeSaveOgData(true);

            //setup part basics, like defualt frac parent, create parts transform+colliders, convert mesh to localspace
            List<Mesh> fracturedMeshesLocal = Gen_setupPartBasics(new(fracturedMeshes), physicsMat);
            if (fracturedMeshesLocal == null)
            {
                Gen_loadAndMaybeSaveOgData(false);
                return false;
            }

            //setup fracture renderer, setup renderer
            Gen_setupRenderer(ref allParts, fracturedMeshes, transform, matInside_defualt, matOutside_defualt);

            //setup real skinned mesh
            if (isRealSkinnedM == true)
            {
                Gen_setupSkinnedMesh(tempOgRealSkin.ogMesh, tempOgRealSkin.ogBones, tempOgRealSkin.ogRootBone.localToWorldMatrix, fracturedMeshes);
            }

            tempPartOgMeshId = null;
            tempFracVerOgMeshId = null;
            tempOgRealSkin = null;

            //apply resistance multipliers to parts
            Gen_setupResistanceMultiply();

            //save to save asset
            SaveOrLoadAsset(true);

            //log result when done, log when done
            if (fracRend.bones.Length > 500) Debug.LogWarning(transform.name + " has " + fracRend.bones.Length + " bones (skinnedMeshRenderers seems to have a limitation of ~500 bones before it breaks)");
            if (Mathf.Approximately(transform.lossyScale.x, transform.lossyScale.y) == false || Mathf.Approximately(transform.lossyScale.z, transform.lossyScale.y) == false) Debug.LogWarning(transform.name + " lossy scale XYZ should all be the same. If not stretching may accure when rotating parts");
            if (transform.TryGetComponent<Rigidbody>(out _) == true) Debug.LogWarning(transform.name + " has a rigidbody and it may cause issues. Its recommended to remove it and use the fracture physics options instead");
            Debug.Log("Fractured " + objectToFracture.transform.name + " into " + fracturedMeshesLocal.Count + " parts, total vertex count = " + fracturedMeshes.Sum(mesh => mesh.vertexCount));

            return true;
        }

        /// <summary>
        /// Modifies allparts resistance multiply value
        /// </summary>
        private void Gen_setupResistanceMultiply()
        {
            //get all parts resistance multiply value from all multiply volumes
            allPartsResistance = new float[allParts.Length];

            Vector3 pPos;
            List<float> multipliers = new();
            float totalMultiply;
            float thisMultiply;
            float totalExtents = 0.0f;
            int extentCount = 0;

            for (int i = 0; i < allParts.Length; i += 1)
            {
                //get the avg bounding box extent for all parts combined (We can do this inside the same loop)
                totalExtents += Math.Abs(allParts[i].col.bounds.extents.x);
                totalExtents += Math.Abs(allParts[i].col.bounds.extents.y);
                totalExtents += Math.Abs(allParts[i].col.bounds.extents.z);
                extentCount += 3;

                //get this part resistance multiply value
                pPos = isRealSkinnedM == true ? allSkinPartCols[i].bounds.center : allParts[i].trans.position;
                totalMultiply = 0.0f;
                multipliers.Clear();

                for (int ii = 0; ii < resistanceMultipliers.Count; ii += 1)
                {
                    thisMultiply = GetVolumeMultiplier(ii, pPos);
                    if (Mathf.Approximately(thisMultiply, 1.0f) == true) continue;

                    multipliers.Add(thisMultiply);
                    totalMultiply += multipliers[^1];
                }

                if (totalMultiply <= 0.0f) totalMultiply = 1.0f;
                if (multipliers.Count > 0) totalMultiply /= multipliers.Count;

                allPartsResistance[i] = destructionResistance * totalMultiply;
            }

            partAvgBoundsExtent = (totalExtents / extentCount) * 1.1f;

            float GetVolumeMultiplier(int volumeIndex, Vector3 pos)
            {
                Bounds bound = FractureHelperFunc.ConvertBoundsWithMatrix(new() { center = resistanceMultipliers[volumeIndex].volume.center, extents = resistanceMultipliers[volumeIndex].volume.extents }, transform.localToWorldMatrix);
                return Mathf.Lerp(resistanceMultipliers[volumeIndex].multiplier, 1.0f
                    , Mathf.Clamp01(Vector3.Distance(bound.ClosestPoint(pos), pos) * resistanceMultipliers[volumeIndex].fadeStrenght));
            }
        }

        public int triIndex = 0;
        public Transform debugTrans = null;

        private class boneWeData
        {
            public float weight = 0.0f;
            public int boneIndex = 0;
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
            //combine meshes
            Mesh comMesh = FractureHelperFunc.CombineMeshes(partMeshes, ref fParts);

            Vector3[] vertics = comMesh.vertices;
            verticsLinkedThreaded = new IntList[vertics.Length];
            float worldDis = worldScale * 0.01f;//0.01??

            //get all vertics ogMesh id
            tempFracVerOgMeshId = new int[vertics.Length];
            for (int i = 0; i < fParts.Length; i += 1)
            {
                foreach (int vI in fParts[i].rendVertexIndexes)
                {
                    tempFracVerOgMeshId[vI] = tempPartOgMeshId[i];
                }
            }

            //get all vertics at the ~same position using the same ogMesh
            Parallel.For(0, verticsLinkedThreaded.Length, i =>
            {
                List<int> intList = FractureHelperFunc.GetAllVertexIndexesAtPos_id(vertics, tempFracVerOgMeshId, vertics[i], tempFracVerOgMeshId[i], worldDis, -1);

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
            int parentIndex = Run_createNewParent(Vector3.zero);
            allFracParents[parentIndex].partIndexes = Enumerable.Range(0, meshes.Count).ToList();
            Transform parentTrans = allFracParents[parentIndex].parentTrans;

            //create part transforms
            allParts = new FracParts[meshes.Count];

            for (int i = 0; i < meshes.Count; i += 1)
            {
                Transform newT = new GameObject("Part(" + i + ")_" + transform.name).transform;
#if UNITY_EDITOR
                if (Application.isPlaying == false) StageUtility.PlaceGameObjectInCurrentStage(newT.gameObject);
#endif
                newT.SetParent(parentTrans);
                newT.SetPositionAndRotation(FractureHelperFunc.GetMedianPosition(meshes[i].vertices), parentTrans.rotation);
                newT.localScale = Vector3.one;
                newT.gameObject.layer = gameObject.layer;

                meshes[i] = FractureHelperFunc.ConvertMeshWithMatrix(Instantiate(meshes[i]), newT.worldToLocalMatrix); //Instantiate new mesh to keep worldSpaceMeshes

                //the part data is created here
                FracParts newP = new() { trans = newT, col = Gen_createPartCollider(newT, meshes[i], phyMatToUse), rendVertexIndexes = new(), partBrokenness = 0.0f, neighbourParts = new(), parentIndex = 0 };
                allParts[i] = newP;
            }

            //setup part neighbours and isKinematic
            //get physics scene
            PhysicsScene phyScene = gameObject.scene.GetPhysicsScene();

            //perform the checks to get neighbours and isKinematic
            List<Vector3> wVerts = new();

            float worldDis = worldScale * 0.01f;
            if (phyMainOptions.mainPhysicsType == OptMainPhysicsType.overlappingIsKinematic) kinematicPartStatus = new bool[allParts.Length];
            else kinematicPartStatus = new bool[0];

            Collider[] lapCols = new Collider[200]; //This is never allowed to be too small;
            int colCount;

            for (int i = 0; i < allParts.Length; i += 1)
            {
                if (generationQuality != GenerationQuality.high && (kinematicPartStatus.Length > 0 || generationQuality == GenerationQuality.low))
                {
                    colCount = phyScene.OverlapBox(worldMeshes[i].bounds.center,
                        worldMeshes[i].bounds.extents * 1.05f,
                        lapCols,
                        Quaternion.identity,
                        Physics.AllLayers,
                        QueryTriggerInteraction.Ignore);

                    Gen_getKinematicAndNeighboursFromTrans(lapCols, colCount, i, generationQuality != GenerationQuality.low);
                }

                if (generationQuality == GenerationQuality.low) continue;

                worldMeshes[i].GetVertices(wVerts);

                for (int ii = 0; ii < wVerts.Count; ii += 1)
                {
                    colCount = phyScene.OverlapSphere(wVerts[ii], worldDis, lapCols, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                    Gen_getKinematicAndNeighboursFromTrans(lapCols, colCount, i, false);
                }

                if (generationQuality == GenerationQuality.high)
                {
                    lapCols = FractureHelperFunc.LinecastsBetweenPositions(wVerts, phyScene).ToArray();
                    Gen_getKinematicAndNeighboursFromTrans(lapCols, lapCols.Length, i, false);
                }
            }

            //update parent info
            Run_updateParentInfo(0);

            //verify that all parts are connected in its defualt state
            List<int> conParts = new() { 0 };
            for (int i = 0; i < conParts.Count; i += 1)
            {
                foreach (int nI in allParts[conParts[i]].neighbourParts)
                {
                    if (conParts.Contains(nI) == true) continue;

                    conParts.Add(nI);
                }
            }

            if (conParts.Count != allParts.Length) Debug.LogError("Not all parts in " + transform.name + " are connected, make sure there are no floating triangels. (This may cause issues at runtime)");

            //return meshes since it has been converted to parent localspace
            return meshes;

            void Gen_getKinematicAndNeighboursFromTrans(Collider[] overlapCols, int colCount, int ogPi, bool kinematicOnly = false)
            {
                FractureThis pFracThis;
                int nearI;

                for (int i = 0; i < colCount; i += 1)
                {
                    //get part index from hit trans
                    pFracThis = overlapCols[i].GetComponentInParent<FractureThis>();

                    nearI = Run_tryGetPartIndexFromString(overlapCols[i].transform.name);
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
                //This is the only place we add new colliders to the parts in
                //(We do also add colliders in the copyColliders function but since it copies all collider properties it does not really matter)
                partMesh = FractureHelperFunc.MergeVerticesInMesh(Instantiate(partMesh));
                partMesh.uv = new Vector2[0];
                partMesh.normals = new Vector3[0];
                partMesh.triangles = new int[0];
                Collider newCol;

                if (colliderType == ColliderType.mesh)
                {
                    //mesh
                    newCol = partTrans.GetOrAddComponent<MeshCollider>();
                    //newCol.convex = true;
                    //newCol.sharedMesh = partMesh;
                    //newCol.sharedMaterial = phyMat;
                    //return newCol;
                }
                else if (colliderType == ColliderType.boxLarge)
                {
                    //box large
                    newCol = partTrans.GetOrAddComponent<BoxCollider>();
                    //newCol.size = partMesh.bounds.size;
                    //newCol.sharedMaterial = phyMat;
                    //return newCol;
                }
                else if (colliderType == ColliderType.boxSmall)
                {
                    //box small
                    newCol = partTrans.GetOrAddComponent<BoxCollider>();
                    //newCol.size = Vector3.one * ((Mathf.Abs(partMesh.bounds.extents.x) + Mathf.Abs(partMesh.bounds.extents.y) + Mathf.Abs(partMesh.bounds.extents.z)) / 3.0f);
                    //newCol.sharedMaterial = phyMat;
                    //return newCol;
                }
                else if (colliderType == ColliderType.sphereLarge)
                {
                    //sphere large
                    newCol = partTrans.GetOrAddComponent<SphereCollider>();
                    //newCol.radius = (Mathf.Abs(partMesh.bounds.extents.x) + Mathf.Abs(partMesh.bounds.extents.y) + Mathf.Abs(partMesh.bounds.extents.z)) / 3.0f;
                    //newCol.sharedMaterial = phyMat;
                    //return newCol;
                }
                else
                {
                    //sphere small
                    newCol = partTrans.GetOrAddComponent<SphereCollider>();
                    //newCol.radius = partMesh.bounds.extents.magnitude / 3.0f;
                    //newCol.sharedMaterial = phyMat;
                    //return newCol;
                }

                FractureHelperFunc.SetColliderFromFromPoints(newCol, partTrans, FractureHelperFunc.ConvertPositionsWithMatrix(partMesh.vertices, partTrans.localToWorldMatrix), true, false);
                newCol.sharedMaterial = phyMat;
                newCol.hasModifiableContacts = true; //This must always be true for all fracture colliders
                return newCol;
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

        private bool mustConfirmHighCount = true;

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
            int nextOgMeshId = 0;
            if (seed < 0) seed = UnityEngine.Random.Range(0, int.MaxValue);

            //get per mesh scale, so each mesh can get ~equally sized
            List<Mesh> meshes = meshesToFracture.Select(meshData => meshData.mesh).ToList();
            List<float> meshScales = FractureHelperFunc.GetPerMeshScale(meshes, useMeshBounds);
            if (dynamicChunkCount == true) totalChunkCount = Mathf.CeilToInt(totalChunkCount * FractureHelperFunc.GetBoundingBoxVolume(FractureHelperFunc.GetCompositeMeshBounds(meshes.ToArray())));
            meshes.Clear();

            if (mustConfirmHighCount == true && totalChunkCount > 500)
            {
                mustConfirmHighCount = false;
                Debug.LogError("You are trying to fracture a mesh into ~" + totalChunkCount + " parts, thats a lot (Fracture again to fracture anyway)");
                return null;
            }
            else if (totalChunkCount < 500) mustConfirmHighCount = true;

            //fractrue the meshes into chunks that are ~equally sized
            for (int i = 0; i < meshesToFracture.Count; i += 1)
            {
                Gen_fractureMesh(meshesToFracture[i].mesh, ref meshes, Mathf.RoundToInt(totalChunkCount * meshScales[i]));
                nextOgMeshId += 1;
            }

            //return the result
            return meshes;

            void Gen_fractureMesh(Mesh meshToFrac, ref List<Mesh> newMeshes, int chunkCount)
            {
                //fractures the given mesh into pieces and adds the new pieces to the newMeshes list
                if (chunkCount <= 1)
                {
                    tempPartOgMeshId.Add(nextOgMeshId);
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

                byte loopMaxAttempts = maxFractureAttempts;
                bool meshIsValid = false;
                List<Mesh> newMeshesTemp = new();

                while (loopMaxAttempts > 0)
                {
                    loopMaxAttempts--;
                    meshIsValid = true;
                    newMeshesTemp.Clear();

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
                        newMeshesTemp.Add(ExtractChunkMesh(fractureTool, i));
                        if (FractureHelperFunc.IsMeshValid(newMeshesTemp[^1], worldScaleDis) == false)
                        {
                            meshIsValid = false;
                            break;
                        }
                    }

                    if (meshIsValid == false) continue;

                    newMeshes.AddRange(newMeshesTemp);
                    for (int i = 0; i < newMeshesTemp.Count; i += 1)
                    {
                        tempPartOgMeshId.Add(nextOgMeshId);
                    }

                    break;
                }

                if (meshIsValid == false)
                {
                    Debug.LogError("Unable to fracture chunk " + nextOgMeshId + " of " + transform.name + " (Some parts of the mesh may be missing)");
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

                    //get defualt parent from rootbone
                    Transform rootBoneTrans = skinnedR.rootBone;
                    while (rootBoneTrans != null && rootBoneTrans.parent != transform)
                    {
                        rootBoneTrans = rootBoneTrans.parent;
                    }

                    if (rootBoneTrans == null || rootBoneTrans.parent != transform)
                    {
                        Debug.LogError(skinnedR.transform.name + " (skinnedRend) rootBone must be a child of " + transform.name);
                        return null;
                    }

                    if (Vector3.Distance(skinnedR.transform.position, transform.position) > worldScale * 0.01f)
                    {
                        Debug.LogWarning(skinnedR.transform.name + " world position does not match " + transform.name + " world position, this may cause the fractured mesh to be misplaced");
                    }

                    //save orginal real skinnedmesh data to use it on setup later
                    tempOgRealSkin.ogBones = skinnedR.bones;
                    tempOgRealSkin.ogRootBone = skinnedR.transform;
                    tempOgRealSkin.ogMesh = Instantiate(skinnedR.sharedMesh);
                    tempOgRealSkin.ogCompData = new() { new() { comp = skinnedR.rootBone.parent } };
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

        #endregion GenerateFractureSystem





        //##############################RUNTIME########################################

        #region RealSkinnedMeshes

        /// <summary>
        /// Contains the collider on the skinned bone for all parts (Only if real skinned)
        /// </summary>
        public Collider[] allSkinPartCols = new Collider[0];

        private BoneWeight[] boneWe_defualt;
        [System.NonSerialized] public BoneWeight[] boneWe_broken = new BoneWeight[0];
        private BoneWeight[] boneWe_current;
        private bool boneWeightNeedUpdate = false;
        public bool isRealSkinnedM = false;

        /// <summary>
        /// Only called if real skinned mesh to copy its animated bones to fractured mesh
        /// </summary>
        private void Gen_setupSkinnedMesh(Mesh skinMesh, Transform[] skinBones, Matrix4x4 skinLtW, List<Mesh> partMeshesWorld)
        {
            float worldDis = worldScale * 0.0001f;

            //add bind poses and bone transforms from real skinned rend
            int boneIShift = fracRend.bones.Length;
            List<Transform> newBones = fracRend.bones.ToList();
            List<Matrix4x4> newMatrixs = fracRend.sharedMesh.bindposes.ToList();

            for (int i = 0; i < skinBones.Length; i++)
            {
                // Add new bone and its bind pose matrix
                newBones.Add(skinBones[i]);
                newMatrixs.Add(skinMesh.bindposes[i]);
            }

            //set boneWeights
            skinMesh = FractureHelperFunc.ConvertMeshWithMatrix(skinMesh, skinLtW);
            FractureHelperFunc.Debug_drawMesh(skinMesh, false, 40.0f);

            BoneWeight[] skinBoneWe = skinMesh.boneWeights.ToArray();
            Vector3[] skinWVer = skinMesh.vertices;
            int[] skinTris = skinMesh.triangles;
            int[] fracTris = fracRend.sharedMesh.triangles;
            boneWe_broken = fracRend.sharedMesh.boneWeights.ToArray();
            BoneWeight[] newBoneWe = new BoneWeight[boneWe_broken.Length];
            Vector3[] fracWVer = FractureHelperFunc.ConvertPositionsWithMatrix(fracRend.sharedMesh.vertices, fracRend.localToWorldMatrix);
            Vector3[] fracWNor = FractureHelperFunc.ConvertDirectionsWithMatrix(fracRend.sharedMesh.normals, fracRend.localToWorldMatrix);
            List<int> unusedVers = Enumerable.Range(0, fracWVer.Length).ToList();

            //link vertics bone weights with best weight on skinned mesh
            int verI;

            while (unusedVers.Count > 0)
            {
                verI = unusedVers[0];
                unusedVers.RemoveAt(0);

                BoneWeight newWe = GetBestWeightForVertex(verI);
                newBoneWe[verI] = newWe;

                foreach (int vIi in verticsLinkedThreaded[verI].intList)
                {
                    unusedVers.Remove(vIi);
                    newBoneWe[vIi] = newWe;
                }
            }

            //assign updated data to mesh and renderer
            fracRend.bones = newBones.ToArray();
            fracRend.sharedMesh.bindposes = newMatrixs.ToArray();
            fracRend.sharedMesh.boneWeights = newBoneWe;

            //add colliders to real skinned bones
            List<boneWeData> colBoneWe = new();
            float weight = 0.0f;
            int boneIndex = 0;
            int weListI;

            allSkinPartCols = new Collider[allParts.Length];

            for (int i = 0; i < allParts.Length; i += 1)
            {
                foreach (int vI in allParts[i].rendVertexIndexes)
                {
                    for (int ii = 0; ii < 4; ii++)
                    {
                        switch (ii)
                        {
                            case 0:
                                weight = newBoneWe[vI].weight0;
                                boneIndex = newBoneWe[vI].boneIndex0;
                                break;

                            case 1:
                                weight = newBoneWe[vI].weight1;
                                boneIndex = newBoneWe[vI].boneIndex1;
                                break;

                            case 2:
                                weight = newBoneWe[vI].weight2;
                                boneIndex = newBoneWe[vI].boneIndex2;
                                break;

                            case 3:
                                weight = newBoneWe[vI].weight3;
                                boneIndex = newBoneWe[vI].boneIndex3;
                                break;
                        }

                        if (weight <= 0.01f) continue;

                        weListI = colBoneWe.FindIndex(we => we.boneIndex == boneIndex);
                        if (weListI < 0)
                        {
                            colBoneWe.Add(new() { boneIndex = boneIndex, weight = weight });
                            continue;
                        }

                        colBoneWe[weListI].weight += weight;
                    }
                }

                colBoneWe = colBoneWe.OrderByDescending(item => item.weight).ToList();
                allSkinPartCols[i] = FractureHelperFunc.CopyColliderToTransform(allParts[i].col, newBones[colBoneWe[0].boneIndex]);
                allParts[i].col.enabled = false;

                colBoneWe.Clear();
            }

            //modify colliders to match defualt skinned pose
            Mesh sMesh = new();
            fracRend.BakeMesh(sMesh, true);
            sMesh = FractureHelperFunc.ConvertMeshWithMatrix(sMesh, fracRend.localToWorldMatrix);

            Vector3[] fracWVerOg = fracWVer.ToArray();
            Vector3[] fracWNorOg = fracWNor.ToArray();
            fracWVer = sMesh.vertices;
            fracWNor = sMesh.normals;
            List<Vector3> partWver = new();
            fracRend.sharedMesh.boneWeights = boneWe_broken;
            List<Vector3> usedNors = new();
            bool alreadyTested;

            for (int i = 0; i < allParts.Length; i += 1)
            {
                partWver.Clear();
                usedNors.Clear();

                foreach (int vI in allParts[i].rendVertexIndexes)
                {
                    partWver.Add(fracWVer[vI]);
                }

                FractureHelperFunc.MergeSimilarVectors(ref partWver, worldDis);
                FractureHelperFunc.SetColliderFromFromPoints(allSkinPartCols[i], allSkinPartCols[i].transform, partWver.ToArray(), false, false);
                //allParts[i].trans.SetPositionAndRotation(allSkinPartCols[i].transform.position, allSkinPartCols[i].transform.rotation);
                //newMatrixs[i] = allParts[i].trans.worldToLocalMatrix * fracRend.localToWorldMatrix;
                //FractureHelperFunc.SetColliderFromFromPoints(allParts[i].col, allParts[i].trans, partWver.ToArray(), false, false);

                //move allPart cols to match defualt skinned pose
                //for (int rvI = 0; rvI < allParts[i].rendVertexIndexes.Count; rvI += 1)
                foreach (int rvI in allParts[i].rendVertexIndexes)
                {
                    alreadyTested = false;

                    for (int ii = 0; ii < usedNors.Count; ii += 1)
                    {
                        if (Vector3.Dot(usedNors[ii], fracWNorOg[rvI]) >= 0.95f)
                        {
                            alreadyTested = true;
                            break;
                        }
                    }

                    if (alreadyTested == true) continue;

                    usedNors.Add(fracWNorOg[rvI]);

                    Vector3 rotationAxis = Vector3.Cross(fracWNorOg[rvI], fracWNor[rvI]);
                    float rotationAngle = Vector3.SignedAngle(fracWNorOg[rvI], fracWNor[rvI], rotationAxis);
                    allParts[i].trans.RotateAround(fracWVerOg[rvI], rotationAxis, rotationAngle);
                    allParts[i].trans.position += fracWVer[rvI] - fracWVerOg[rvI];

                    fracRend.BakeMesh(sMesh, true);
                    sMesh = FractureHelperFunc.ConvertMeshWithMatrix(sMesh, fracRend.localToWorldMatrix);
                    fracWVerOg = sMesh.vertices;
                    fracWNorOg = sMesh.normals;
                }

                // Calculate the new bind pose matrix
                //Matrix4x4 desiredLocalTransform = Matrix4x4.TRS(allSkinPartCols[i].transform.position, Quaternion.identity, Vector3.one);
                //newMatrixs[i] = desiredLocalTransform * newMatrixs[i];
                //
                //// Update only the translation part of the transform
                //newMatrixs[i].SetColumn(3, new Vector4(allSkinPartCols[i].transform.position.x, allSkinPartCols[i].transform.position.y, allSkinPartCols[i].transform.position.z, 1.0f));
                //allParts[i].trans.SetPositionAndRotation(allSkinPartCols[i].transform.position, allSkinPartCols[i].transform.rotation);
                //newMatrixs[i] = allParts[i].trans.worldToLocalMatrix * fracRend.localToWorldMatrix;

                //int rvI = 0;
                //Vector3 rotationAxis = Vector3.Cross(fracWNorOg[allParts[i].rendVertexIndexes[rvI]], fracWNor[allParts[i].rendVertexIndexes[rvI]]);
                //float rotationAngle = Vector3.SignedAngle(fracWNorOg[allParts[i].rendVertexIndexes[rvI]], fracWNor[allParts[i].rendVertexIndexes[rvI]], rotationAxis);
                //allParts[i].trans.RotateAround(fracWVerOg[allParts[i].rendVertexIndexes[rvI]], rotationAxis, rotationAngle);




            }

            fracRend.sharedMesh.boneWeights = newBoneWe;
            fracRend.sharedMesh.bindposes = newMatrixs.ToArray();

            BoneWeight GetBestWeightForVertex(int fracVi)
            {
                //get the closest skinned triangel and its total bones and their total weight
                BoneWeight newWe;

                int fracTriTemp = FractureHelperFunc.FindTriangleIndexWithVertex(fracTris, fracVi);
                int closeSkinTriI = FractureHelperFunc.GetClosestTriOnMesh(skinWVer, skinTris,
                    new Vector3[3] { fracWVer[fracTris[fracTriTemp]], fracWVer[fracTris[fracTriTemp + 1]],
                        fracWVer[fracTris[fracTriTemp + 2]] }, worldDis);

                int closeSkinTriAdd = FractureHelperFunc.GetClosestPointInArray(new Vector3[3] { skinWVer[skinTris[closeSkinTriI]], skinWVer[skinTris[closeSkinTriI + 1]], skinWVer[skinTris[closeSkinTriI + 2]] }, fracWVer[fracVi], worldDis);
                newWe = skinBoneWe[skinTris[closeSkinTriI + closeSkinTriAdd]];
                newWe.boneIndex0 += boneIShift;
                newWe.boneIndex1 += boneIShift;
                newWe.boneIndex2 += boneIShift;
                newWe.boneIndex3 += boneIShift;

                return newWe;
            }
        }

        private void Run_setBoneWeights(int partIndex, bool toDefualt)
        {
            if (toDefualt == false)
            {
                foreach (int vI in allParts[partIndex].rendVertexIndexes)
                {
                    boneWe_current[vI] = boneWe_broken[vI];
                }
            }
            else
            {
                foreach (int vI in allParts[partIndex].rendVertexIndexes)
                {
                    boneWe_current[vI] = boneWe_defualt[vI];
                }
            }

            boneWeightNeedUpdate = true;
        }

        #endregion RealSkinnedMeshes







        #region MainUpdateFunctions

        private bool updateThisFrame = false;

        /// <summary>
        /// Makes the fracture undestructable for X seconds (Must be called with StartCoroutine)
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        public IEnumerator DisableDestructionForXSeconds(float x)
        {
            immortalTime = -1.0f;
            yield return new WaitForSeconds(x);
            immortalTime = x;
        }

        private void OnDestroy()
        {
            //make sure all jobs are completed
            if (j_active == true)
            {
                j_handle.Complete();
                j_active = false;
            }

            //dispose all nativeArray
            j_boneTrans.Dispose();
            j_fracBonesLToW.Dispose();
            j_fracBonesPos.Dispose();
            j_hasMoved.Dispose();
            allFracBonesLToW.Dispose();
        }

        private void Awake()
        {
            //verify fracture
            if (fracRend == null) return;
            if (globalHandler == null && VerifyGlobalHandler() == false)
            {
                Debug.LogError(transform.name + " globalHandler is null, destruction will not work (Make sure a active FractureGlobalHandler script exists in all scenes)");
                return;
            }

            //make immortal
            if (immortalTime > 0.0f)
            {
                StartCoroutine(DisableDestructionForXSeconds(immortalTime));
            }

            //load from save aset
            SaveOrLoadAsset(false);

            //setup system to get skinned vertics positions in realtime
            SetupRealVerticsWorld();

            //setup collider instanceid references
            globalHandler.AddReferencesFromFracture(this);

            //assign variabels for destruction
            impactRadius = globalHandler.impactZoneRadius;
            impactRadiusSquared = impactRadius * impactRadius;
            ogObjectLayer = gameObject.layer;
            des_partsBrokeness = new float[allParts.Length];
            AllRendBones = fracRend.bones;
            des_partsOffset = new Vector3[allParts.Length];
            des_partsToBreak = new();
            des_newParents = new();

            //assign variabels for mesh deformation+colors
            if (vertexDisplacementStenght > 0.0f || doVertexColors == true)
            {
                mDef_verUse = new();
                mDef_verNow = new Vector3[fracRend.sharedMesh.vertexCount];
                des_verForceApply = new float[fracRend.sharedMesh.vertexCount];
                des_verForceApplied = new float[fracRend.sharedMesh.vertexCount];
                fracRend.sharedMesh.GetVertices(mDef_verUse);
                mDef_verDefAmount = new float[mDef_verUse.Count];
                if (isRealSkinnedM == false) verticsBonesThreaded = fracRend.sharedMesh.boneWeights.Select(bone => bone.boneIndex0).ToArray();
                else verticsBonesThreaded = boneWe_broken.Select(bone => bone.boneIndex0).ToArray();

                //set vertex color
                mDef_verColUse = new();
                fracRend.sharedMesh.GetColors(mDef_verColUse);
                if (mDef_verColUse.Count <= 0) mDef_verColUse = new Color[mDef_verNow.Length].ToList();
            }

            //assign variabels for repair system
            if (repairSupport != DestructionRepairSupport.dontSupportRepair)
            {
                rep_partsNowPosition = allParts.Select(part => part.trans.position).ToArray();
                rep_partsNowRotation = allParts.Select(part => part.trans.rotation).ToArray();
                rep_LocalPartOgToWorld = new Matrix4x4[allParts.Length];
                rep_partsParentRot = new Quaternion[allParts.Length];

                if (isRealSkinnedM == true)
                {
                    rep_ogLocalPartPoss = new Vector3[allParts.Length];
                    rep_ogLocalPartRots = new Quaternion[allParts.Length];

                    for (int i = 0; i < allParts.Length; i += 1)
                    {
                        rep_ogLocalPartPoss[i] = allSkinPartCols[i].transform.InverseTransformPoint(allParts[i].trans.position);
                        rep_ogLocalPartRots[i] = Quaternion.Inverse(allSkinPartCols[i].transform.rotation) * allParts[i].trans.rotation;
                    }
                }
                else
                {
                    rep_ogLocalPartPoss = allParts.Select(part => part.trans.localPosition).ToArray();
                    rep_ogLocalPartRots = allParts.Select(part => part.trans.localRotation).ToArray();
                }

                if (repairSupport != DestructionRepairSupport.partsOnly) rep_verticsOrginal = mDef_verUse.ToArray();
            }

            //disable collision with neighbours, can this be done in edit mode once after generating the fracture?
            if (selfCollisionRule == SelfCollideWithRule.ignoreNeighbours)
            {
                for (int i = 0; i < allParts.Length; i++)
                {
                    foreach (int nI in allParts[i].neighbourParts)
                    {
                        Physics.IgnoreCollision(allParts[nI].col, allParts[i].col, true);
                        if (isRealSkinnedM == true) Physics.IgnoreCollision(allSkinPartCols[nI], allParts[i].col, true);
                    }
                }
            }

            //disable collision between all colliders on this fracture
            if (selfCollisionRule == SelfCollideWithRule.ignoreAll)
            {
                for (int i = 0; i < allParts.Length; i++)
                {
                    for (int ii = i + 1; ii < allParts.Length; ii++)
                    {
                        Physics.IgnoreCollision(allParts[ii].col, allParts[i].col, true);
                        if (isRealSkinnedM == true) Physics.IgnoreCollision(allSkinPartCols[ii], allParts[i].col, true);
                    }
                }
            }

            //half update rate
            if (halfUpdateRate == true) repairSpeed *= 2.0f;
            updateThisFrame = saved_fracId % 2 == 0;

            //assign variabels for real skinned meshes
            boneWe_current = fracRend.sharedMesh.boneWeights;

            if (isRealSkinnedM == true)
            {
                boneWe_defualt = fracRend.sharedMesh.boneWeights;
            }

            BoneHandleJob_setup();
        }

        private void FixedUpdate()
        {
            if (allParts.Length == 0) return;

            //FixedUpdate is called before collsionModifyEvent
            //Start update fracRend bones data job
            BoneHandleJob_run();

            //apply destruction result
            if (calcDesThread_isActive == true) DestructionSolverComplete();

            //sync part brokenness
            if (damBrokenessNeedsSync == true)
            {
                for (int i = 0; i < des_partsBrokeness.Length; i++)
                {
                    des_partsBrokeness[i] = allParts[i].partBrokenness;
                }

                damBrokenessNeedsSync = false;
            }

            //update parent info
            while (parentIndexesToUpdate.Count > 0)
            {
                int parentToUpdate = parentIndexesToUpdate.FirstOrDefault();
                if (parentToUpdate >= 0 && parentToUpdate < allFracParents.Count) Run_updateParentInfo(parentToUpdate);
                parentIndexesToUpdate.Remove(parentToUpdate);
            }

            //Make sure late fixedUpdate runs
            StartCoroutine(LateFixedUpdate());
        }

        private IEnumerator LateFixedUpdate()
        {
            yield return new WaitForFixedUpdate();

            //This runs after collsionModifyEvent
            //Finish update fracRend bones data job
            BoneHandleJob_complete();

            //compute destruction and deformation
            if (calcDesThread_isActive == false)
            {
                //if (damPartsUsed.Count > 0 && calcDefThread == null)
                //{
                //    StartCoroutine(ComputeDeformation());
                //}

                if (damToCompute.Count > 0)
                {
                    DestructionSolverRun();
                }
            }
        }

        private bool damBrokenessNeedsSync = false;

        private void Update()
        {
            if (allParts.Length == 0) return;

            //update bone weights
            if (boneWeightNeedUpdate == true)
            {
                boneWeightNeedUpdate = false;
                fracRend.sharedMesh.boneWeights = boneWe_current;
            }

            //do repair
            if (rep_partsToRepair.Count > 0)
            {
                Run_repairUpdate();
            }

            //debug keys
            if (Input.GetKey(KeyCode.R))
            {
                Run_requestRepairPart(Run_tryGetFirstDamagedPart());
            }
        }

        /// <summary>
        /// Call to update the bounds of the fracture renderer manually
        /// </summary>
        private void UpdateFracRendBounds()
        {
            var min = Vector3.one * -694200;
            var max = Vector3.one * 694200;
            Vector3 tempPos;

            for (int i = 0; i < allParts.Length; i++)
            {
                tempPos = allParts[i].trans.position;
                min = Vector3.Min(min, tempPos);
                max = Vector3.Max(max, tempPos);
            }

            fracRend.bounds = new() { min = min + (-2.0f * partAvgBoundsExtent * Vector3.one), max = max + (2.0f * partAvgBoundsExtent * Vector3.one) };
        }

        private TransformAccessArray j_boneTrans;

        /// <summary>
        /// The matrix each fracRend bone had the previous frame (localToWorld)(Threaded, written to on mainthread in RunBoneHandleJob())
        /// </summary>
        private NativeArray<Matrix4x4> allFracBonesLToW;

        private NativeArray<Matrix4x4> j_fracBonesLToW;
        private NativeArray<Vector3> j_fracBonesPos;
        private JobHandle j_handle;
        private HandleBoneTransJob j_job;
        private NativeQueue<bool> j_hasMoved;
        private bool j_active = false;

        private void BoneHandleJob_setup()
        {
            //Assign variabels used in boneHandle job
            j_boneTrans = new(fracRend.bones);
            allFracBonesLToW = new NativeArray<Matrix4x4>(j_boneTrans.length, Allocator.Persistent);
            j_fracBonesLToW = new NativeArray<Matrix4x4>(j_boneTrans.length, Allocator.Persistent);
            j_fracBonesPos = new NativeArray<Vector3>(j_boneTrans.length, Allocator.Persistent);
            j_hasMoved = new NativeQueue<bool>(Allocator.Persistent);
            j_active = false;
        }

        private void BoneHandleJob_run()
        {
            //Run the job
            j_job = new HandleBoneTransJob()
            {
                fracBonesLToW = j_fracBonesLToW,
                fracBonesPos = j_fracBonesPos,
                hasMoved = j_hasMoved.AsParallelWriter()
            };

            j_handle = j_job.Schedule(j_boneTrans);
            j_active = true;
        }

        private void BoneHandleJob_complete()
        {
            if (j_active == false) return;

            //get job result
            j_handle.Complete();
            j_active = false;

            if (j_hasMoved.Count > 0)
            {
                j_hasMoved.Clear();
                allFracBonesLToW.CopyFrom(j_fracBonesLToW);

                Vector3 min = Vector3.one * 694200;
                Vector3 max = Vector3.one * -694200;
                foreach (Vector3 vec in j_fracBonesPos)
                {
                    if (min.x > vec.x) min.x = vec.x;
                    else if (max.x < vec.x) max.x = vec.x;
                    if (min.y > vec.y) min.y = vec.y;
                    else if (max.y < vec.y) max.y = vec.y;
                    if (min.z > vec.z) min.z = vec.z;
                    else if (max.z < vec.z) max.z = vec.z;
                }

                fracRend.bounds = new()
                {
                    min = min + (-2.0f * partAvgBoundsExtent * Vector3.one),
                    max = max + (2.0f * partAvgBoundsExtent * Vector3.one)
                };
            }
        }

        public struct HandleBoneTransJob : IJobParallelForTransform
        {
            public NativeArray<Matrix4x4> fracBonesLToW;
            public NativeArray<Vector3> fracBonesPos;
            public NativeQueue<bool>.ParallelWriter hasMoved;

            public void Execute(int index, TransformAccess transform)
            {
                //If fracRend bone trans has moved, add to hasMoved queue
                if ((transform.position - fracBonesPos[index]).sqrMagnitude > 0.1f)//Should it be more sensitive?
                {
                    //get fracRend bone lToW matrix and its world pos
                    fracBonesLToW[index] = transform.localToWorldMatrix;
                    fracBonesPos[index] = transform.position;
                    hasMoved.Enqueue(true);
                }
            }
        }

        #endregion MainUpdateFunctions







        #region ParentSystem

        /// <summary>
        /// Add a parent index here to update the parents info within a few frames
        /// </summary>
        private HashSet<int> parentIndexesToUpdate = new();

        /// <summary>
        /// All fractured parts have one of these as its parent
        /// </summary>
        public List<FracParents> allFracParents = new();

        private void Run_updateParentInfo(int parentIndex)
        {
            //remove parent if no kids
            if (allFracParents[parentIndex].partIndexes.Count <= 0)
            {
                DestroyImmediate(allFracParents[parentIndex].parentRb);
                //allFracParents[parentIndex].parentRb.isKinematic = true;
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

            //update rigidbody stuff
            Run_initRigidbodyForParent(parentIndex);

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
                allFracParents[parentIndex].parentRb.interpolation = parentIndex == 0 ? RigidbodyInterpolation.None : phyMainOptions.interpolate;
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
                allFracParents[parentIndex].parentRb.interpolation = pIsKin == true ? RigidbodyInterpolation.None : phyMainOptions.interpolate;
            }
        }

        /// <summary>
        /// Creates a empty frac parent object and returns its index
        /// </summary>
        /// <returns></returns>
        private int Run_createNewParent(Vector3 parentVelocity, int sourceParent = -1)
        {
            //If empty parent exists, use it as new parent (excluding base parent)
            int newParentIndex;
            Transform pTrans;

            for (int i = 1; i < allFracParents.Count; i += 1)
            {
                if (allFracParents[i].partIndexes.Count > 0) continue;

                pTrans = allFracParents[i].parentTrans;
                parentIndexesToUpdate.Add(i);
                newParentIndex = i;
                SetNewParentProperties();
                return i;
            }

            //create the parent transform
            if (allFracParents.Count == 0 && isRealSkinnedM == true)
            {
                pTrans = (Transform)tempOgRealSkin.ogCompData[0].comp;
                //pTrans.name = "fracParent" + allFracParents.Count + "_" + transform.name;
            }
            else
            {
                pTrans = new GameObject("fracParent" + allFracParents.Count + "_" + transform.name).transform;
#if UNITY_EDITOR
                if (Application.isPlaying == false) StageUtility.PlaceGameObjectInCurrentStage(pTrans.gameObject);
#endif
                pTrans.SetParent(transform);
                pTrans.SetPositionAndRotation(transform.position, transform.rotation);
                pTrans.localScale = Vector3.one;
                pTrans.gameObject.layer = gameObject.layer;
            }

            newParentIndex = allFracParents.Count;
            allFracParents.Add(new() { parentTrans = pTrans, partIndexes = new() });

            //add rigidbody to parent
            SetNewParentProperties();

            //add parent script to parent
            allFracParents[newParentIndex].fParent = pTrans.GetOrAddComponent<FractureParent>();
            allFracParents[newParentIndex].fParent.fractureDaddy = this;
            allFracParents[newParentIndex].fParent.thisParentIndex = newParentIndex;
            parentIndexesToUpdate.Add(newParentIndex);

            return newParentIndex;

            void SetNewParentProperties()
            {
                Run_initRigidbodyForParent(newParentIndex);

                //add velocity to rigidbody
                if (sourceParent >= 0)
                {
                    allFracParents[newParentIndex].parentRb.velocity = allFracParents[sourceParent].parentRb.velocity;
                    allFracParents[newParentIndex].parentRb.angularVelocity = allFracParents[sourceParent].parentRb.angularVelocity;
                }

                if (sourceParent == 0 && allFracParents[sourceParent].parentRb.isKinematic == true)
                {
                    allFracParents[newParentIndex].parentRb.isKinematic = false;
                    allFracParents[newParentIndex].parentRb.velocity += parentVelocity / 2.0f;
                }
            }
        }

        private void Run_initRigidbodyForParent(int parentIndex)
        {
            if (allFracParents[parentIndex].parentRb != null) return;

            //create+set rigidbody
            allFracParents[parentIndex].parentRb = allFracParents[parentIndex].parentTrans.GetOrAddComponent<Rigidbody>();
            allFracParents[parentIndex].parentRb.collisionDetectionMode = phyMainOptions.collisionDetection;
            allFracParents[parentIndex].parentRb.interpolation = phyMainOptions.interpolate;
            allFracParents[parentIndex].parentRb.useGravity = phyMainOptions.useGravity;
            allFracParents[parentIndex].parentRb.drag = phyMainOptions.drag;
            allFracParents[parentIndex].parentRb.angularDrag = phyMainOptions.angularDrag;
            allFracParents[parentIndex].parentRb.constraints = phyMainOptions.constraints;
#if UNITY_EDITOR
            if (Application.isPlaying == true) globalHandler.OnAddRigidbody(allFracParents[parentIndex].parentRb, allFracParents[parentIndex].parentRb.mass);
#else
            globalHandler.OnAddRigidbody(allFracParents[parentIndex].parentRb, allFracParents[parentIndex].parentRb.mass);
#endif
        }

        /// <summary>
        /// Sets partsToInclude parent to newParentIndex
        /// </summary>
        /// <param name="partsToInclude"></param>
        /// <param name="newParentIndex">If < 0, a new parent will be created</param>
        private void Run_setPartsParent(HashSet<int> partsToInclude, Vector3 newParentVelocity, int newParentIndex = -1)
        {
            //create new parent if needed
            if (newParentIndex < 0)
            {
                newParentIndex = Run_createNewParent(newParentVelocity, allParts[partsToInclude.FirstOrDefault()].parentIndex);
            }

            //set the parts parent to the new parent
            foreach (int partI in partsToInclude)
            {
                Run_setPartParent(partI, newParentIndex, true);
            }
        }

        /// <summary>
        /// Sets the given part parent
        /// </summary>
        /// <param name="partIndex"></param>
        /// <param name="newParentIndex"></param>
        private void Run_setPartParent(int partIndex, int newParentIndex, bool setRealSkinned = true)
        {
            if (calcDesThread != null) Debug.LogError("Run_setPartParent() was called while computing destruction, race may accure!");

            //remove from repair list
            rep_partsToRepair.RemoveAll(rep => rep.partIndex == partIndex);
            if (rep_partsToRepair.FindIndex(rep => rep.partIndex == partIndex) >= 0) return;

            //return early if already has that parent
            if (newParentIndex == allParts[partIndex].parentIndex)
            {
                if (allParts[partIndex].parentIndex >= 0 && allParts[partIndex].col.attachedRigidbody != null && allParts[partIndex].col.attachedRigidbody.transform == allParts[partIndex].trans)
                {
                    globalHandler.OnDestroyRigidbody(allParts[partIndex].col.attachedRigidbody);
                    Destroy(allParts[partIndex].col.attachedRigidbody);
                }

                return;
            }

            //since parent changed a part may be damaged
            mayAnyPartBeDamaged = true;

            //update previous parent
            int prevParentI = allParts[partIndex].parentIndex;
            if (prevParentI >= 0)
            {
                allFracParents[prevParentI].partIndexes.Remove(partIndex);
                parentIndexesToUpdate.Add(prevParentI);
            }

            if (newParentIndex < 0)
            {
                //remove parent
                allParts[partIndex].trans.SetParent(transform, true);
                allParts[partIndex].parentIndex = -1;
                allParts[partIndex].col.hasModifiableContacts = false; //to prevent empty pieces from going through ground
                allParts[partIndex].trans.gameObject.layer = phyPartsOptions.objectLayer;

                if (isRealSkinnedM == true)
                {
                    SetRealSkinCol(false);
                }

                //allParts[partIndex].col.enabled = false;
                //allParts[partIndex].col.enabled = true;
                return;
            }

            //set new parent
            if (allParts[partIndex].col.attachedRigidbody != null && allParts[partIndex].col.attachedRigidbody.transform == allParts[partIndex].trans)
            {
                globalHandler.OnDestroyRigidbody(allParts[partIndex].col.attachedRigidbody);
                Destroy(allParts[partIndex].col.attachedRigidbody);
            }

            if (isRealSkinnedM == true)
            {
                SetRealSkinCol(newParentIndex == 0);
            }

            allParts[partIndex].trans.SetParent(allFracParents[newParentIndex].parentTrans, true);
            allFracParents[newParentIndex].partIndexes.Add(partIndex);
            allParts[partIndex].parentIndex = newParentIndex;
            parentIndexesToUpdate.Add(allParts[partIndex].parentIndex);
            allParts[partIndex].col.hasModifiableContacts = true; //to make this collider register destruction
            allParts[partIndex].trans.gameObject.layer = ogObjectLayer;

            //allParts[partIndex].col.enabled = false;
            //allParts[partIndex].col.enabled = true;

            void SetRealSkinCol(bool toDefualt)
            {
                if (setRealSkinned == false) return;

                if (toDefualt == true)
                {
                    allParts[partIndex].col.enabled = false;
                    allSkinPartCols[partIndex].enabled = true;

                    Run_setBoneWeights(partIndex, true);
                }
                else
                {
                    //allParts[partIndex].trans.SetPositionAndRotation(allSkinPartCols[partIndex].transform.position, allSkinPartCols[partIndex].transform.rotation);
                    if (prevParentI == 0) allParts[partIndex].trans.SetPositionAndRotation(allSkinPartCols[partIndex].transform.TransformPoint(rep_ogLocalPartPoss[partIndex]), allSkinPartCols[partIndex].transform.rotation * rep_ogLocalPartRots[partIndex]);
                    allParts[partIndex].col.enabled = true;
                    allSkinPartCols[partIndex].enabled = false;

                    Run_setBoneWeights(partIndex, false);
                }
            }
        }

        #endregion ParentSystem









        #region InternalFractureData

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
        public SkinnedMeshRenderer fracRend = null;

        /// <summary>
        /// The position of all vertics in world space (Updated when deformation is calculated)
        /// </summary>
        private Vector3[] mDef_verNow;

        /// <summary>
        /// Contains all vertics of the skinned mesh in localspace (skinnedmesh vertics is assigned with this array when applying deformation)
        /// </summary>
        private List<Vector3> mDef_verUse;

        /// <summary>
        /// Contains the color of each vertex. (Skinnedmesh vertex colors are assigned with this array when applying deformation)
        /// </summary>
        private List<Color> mDef_verColUse = new();

        /// <summary>
        /// How much damage each vertics has taken (deformation+colors). 0 = no damage, 1 = completely damaged
        /// </summary>
        private float[] mDef_verDefAmount;

        /// <summary>
        /// Contains all vertics and all other vertex indexes that share the ~same position as X (Including self)
        /// </summary>
        [System.NonSerialized] public IntList[] verticsLinkedThreaded = new IntList[0];

        /// <summary>
        /// Contains the resistance used for all parts, set when fracture object. (ImpactForce / AllPartsResistanceThreaded[i])
        /// </summary>
        [System.NonSerialized] public float[] allPartsResistance = new float[0];

        /// <summary>
        /// Contains all vertics and the index of the part they are a part of
        /// </summary>
        [System.NonSerialized] public int[] verticsPartThreaded = new int[0];

        /// <summary>
        /// The index of the bone each vertex uses
        /// </summary>
        private int[] verticsBonesThreaded = new int[0];

        /// <summary>
        /// Contains the transform for each bone in fracRend
        /// </summary>
        private Transform[] AllRendBones;

        [SerializeField] private FractureGlobalHandler globalHandler;

        /// <summary>
        /// The parts combined avg extent (Multiplied by 1.1). 
        /// </summary>
        public float partAvgBoundsExtent = 0.0f;

        [System.Serializable]
        public class IntList
        {
            public List<int> intList;
        }

        #endregion InternalFractureData










        #region DestructionSystem

        private class DamageToCompute
        {
            public List<FractureGlobalHandler.ImpPoint> impPoints;
            public Rigidbody rbCauseImp;
            public Vector3 impVelocity;
            public float impForce;
            public bool willBreakParts;
        }

        private ConcurrentDictionary<Rigidbody, DamageToCompute> damToCompute = new();
        //private List<DamageToCompute> damToCompute = new();
        private List<DamageToCompute> damComputing = new();

        /// <summary>
        /// Register a impact to compute destruction as soon as possible. Returns true if the impact is strong enough to break at least one part
        /// </summary>
        /// <param name="impactPoints">Should contain the world positions where the collision occurred and the indexes of the parts that it collided with</param>
        /// <param name="impactForce">How strong the impact is, higher value = more destruction</param>
        /// <param name="impactVelocity">The velocity of the object that caused the impact</param>
        /// <param name="rbCauseImpact">The rigidbody that caused the impact</param>
        /// <returns></returns>
        public bool RegisterCollision(List<FractureGlobalHandler.ImpPoint> impactPoints, float impactForce, Vector3 impactVelocity, Rigidbody rbCauseImpact)
        {
            if (rbCauseImpact == null)
            {
                Debug.LogError("How the fuck is the rigidbody null");
                return false;
            }

            //get if any part will break
            bool willBreak = false;
            foreach (var impPoint in impactPoints)
            {
                //reading partBrokenness here is not thread safe
                if (allParts[impPoint.partIndex].partBrokenness >= 1.0f) continue;
                if (allParts[impPoint.partIndex].partBrokenness + (impactForce / allPartsResistance[impPoint.partIndex]) >= 1.0f)
                {
                    willBreak = true;
                    break;
                }
            }

            //Register the collision
            DamageToCompute newDam;
            if (damToCompute.TryGetValue(rbCauseImpact, out newDam) == true)
            {
                newDam.impPoints.AddRange(impactPoints);
                if (newDam.impForce < impactForce)
                {
                    newDam.impForce = impactForce;
                    newDam.impVelocity = impactVelocity;
                }

                if (newDam.willBreakParts == false) newDam.willBreakParts = willBreak;
                damToCompute.TryUpdate(rbCauseImpact, newDam, null);
            }
            else
            {
                newDam = new()
                {
                    impForce = impactForce,
                    impPoints = impactPoints,
                    impVelocity = impactVelocity,
                    rbCauseImp = rbCauseImpact
                };

                damToCompute.TryAdd(rbCauseImpact, newDam);
            }

            return willBreak;
        }

        private bool calcDesThread_isActive = false;
        private Task calcDesThread = null;

        private class DesToBreakData
        {
            public Rigidbody rbCausedImpact;
            public int partIndex;
            public float velMulti;
            public Vector3 velocity;
        }

        private class DesPartsUsed
        {
            public int partIndex;
            public Vector3 offsetDir;
            public float offsetAmount;
            public bool doBreak;
        }

        private class DesNewParentData
        {
            public HashSet<int> partsIncluded;
            public Vector3 parentVelocity;
        }

        private class PartInCalc
        {
            public int partI;
            public VecAndFloat powerLeft;
        }

        private class VecAndFloat
        {
            public float value;
            public Vector3 vec;
        }

        private List<DesNewParentData> damNewParentParts = new();
        private List<DesToBreakData> damPartsToBreak = new();
        private List<DesPartsUsed> damPartsUsed = new();
        private int damPartsUsedZeroI = 0;
        private int ogObjectLayer = 0;

        /// <summary>
        /// Contains the indexes of the parts that has been modified in anyway this destruction time
        /// </summary>
        private HashSet<int> damModifiedPartIndexes = new();

        private void DestructionSolverComplete()
        {
            //Make sure compute thread has finished
            if (multithreadedDestruction == true)
            {
                if (calcDesThread == null) return;
                if (calcDesThread.IsCompleted == false) calcDesThread.Wait();

                if (calcDesThread.IsFaulted == true)
                {
                    //when error accure
                    Debug.LogException(calcDesThread.Exception);
                    Debug.LogWarning("Above error was thrown by destructionSolver for " + transform.name);
                    calcDesThread = null;
                    return;
                }

                calcDesThread = null;
            }

            calcDesThread_isActive = false;

            //apply destruction result 
            //break parts
            for (int i = 0; i < des_partsToBreak.Count; i++)
            {
                if (des_partsToBreak[i].directHit == true && damComputing[des_partsToBreak[i].impIndex].rbCauseImp != null)
                {
                    damComputing[des_partsToBreak[i].impIndex].rbCauseImp.velocity = FractureHelperFunc.SubtractMagnitude(
                        damComputing[des_partsToBreak[i].impIndex].rbCauseImp.velocity,
                        des_partsToBreak[i].forceConsumed * damComputing[des_partsToBreak[i].impIndex].impVelocity.magnitude);
                }

                Run_breakPart(
                    des_partsToBreak[i].partIndex,
                    damComputing[des_partsToBreak[i].impIndex].impVelocity,
                    des_partsToBreak[i].forceConsumed,
                    des_partsToBreak[i].forceApplied);
            }

           //apply deformation
           fracRend.sharedMesh.SetVertices(mDef_verUse, 0, mDef_verUse.Count,
               UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices
               | UnityEngine.Rendering.MeshUpdateFlags.DontResetBoneBounds
               | UnityEngine.Rendering.MeshUpdateFlags.DontNotifyMeshUsers
               | UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
           
           fracRend.sharedMesh.SetColors(mDef_verColUse, 0, mDef_verColUse.Count,
               UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices
               | UnityEngine.Rendering.MeshUpdateFlags.DontResetBoneBounds
               | UnityEngine.Rendering.MeshUpdateFlags.DontNotifyMeshUsers
               | UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);

            if (recalculateOnDisplacement == NormalRecalcMode.normalsOnly) fracRend.sharedMesh.RecalculateNormals();
            else if (recalculateOnDisplacement == NormalRecalcMode.normalsAndTagents)
            {
                fracRend.sharedMesh.RecalculateNormals();
                fracRend.sharedMesh.RecalculateTangents();
            }

            //create new parents
            for (int i = 0; i < des_newParents.Count; i++)
            {
                Run_setPartsParent(
                    des_newParents[i].partsToInclude,
                    des_newParents[i].velToGive / Math.Max(1.0f, des_newParents[i].partsToInclude.Count * massDensity * phyMainOptions.massMultiplier));
            }
        }

        private void DestructionSolverRun()
        {
            //get what to compute
            damComputing.Clear();
            lock (damToCompute)
            {
                foreach (var kvp in damToCompute)
                {
                    damComputing.Add(kvp.Value);
                }

                damToCompute.Clear();
            }

            damPartsUsedZeroI = damPartsUsed.Count;

            //run compute thread
            des_parentPartIndexes = allFracParents.Select(fParent => fParent.partIndexes).ToArray();
            
            if (multithreadedDestruction == true) calcDesThread = Task.Run(() => DestructionSolverThread());
            else DestructionSolverThread();
            calcDesThread_isActive = true;
        }

        private class DesToBreak
        {
            public bool directHit;
            public int partIndex;
            public int impIndex;
            public float forceApplied;
            public float forceConsumed;
        }

        private class DesNewParent
        {
            public HashSet<int> partsToInclude;
            public Vector3 velToGive;
        }

        private List<DesToBreak> des_partsToBreak;
        private List<DesNewParent> des_newParents;
        private float[] des_partsBrokeness = new float[0];
        private Vector3[] des_partsOffset;
        private List<int>[] des_parentPartIndexes;
        private float[] des_verForceApply;
        private float[] des_verForceApplied;
        private float impactRadius;
        private float impactRadiusSquared;

        private void DestructionSolverThread()
        {
            Debug_toggleTimer();

            //do destruction, solve destruction
            //clear old result
            des_partsToBreak.Clear();
            des_newParents.Clear();
            HashSet<int> partsToIgnore = new();
            RealtimeVer_syncBones();

            //loop through all impSources and compute each seperatly
            for (int i = 0; i < damComputing.Count; i++)
            {
                CalcImpSource(i);
            }

            float cDis;
            float closeDis;
            void CalcImpSource(int impIndex)
            {
                //Get the parts to start searching from
                List<int> oList = new();
                List<FractureGlobalHandler.ImpPoint> impPoints = damComputing[impIndex].impPoints;
                float impForce = damComputing[impIndex].impForce;
                int partI;
                Vector3 impDir = damComputing[impIndex].impVelocity.normalized;
                float highestForceApplied;

                foreach (FractureGlobalHandler.ImpPoint iPoint in impPoints)
                {
                    partI = iPoint.partIndex;

                    if (damComputing[impIndex].willBreakParts == true)
                    {
                        if (des_partsBrokeness[partI] < 1.0f && partsToIgnore.Add(partI) == true)
                        {
                            highestForceApplied = impForce / allPartsResistance[partI];
                            des_partsBrokeness[partI] += highestForceApplied;
                            if (des_partsBrokeness[partI] < 1.0f) des_partsBrokeness[partI] = 1.0f;

                            AddNewToBreak(partI, highestForceApplied, impIndex, true);
                        }
                    }
                    else if (des_partsBrokeness[partI] < 1.0f && partsToIgnore.Add(partI) == true)
                    {
                        oList.Add(partI);
                    }
                    
                    foreach (int borI in allParts[partI].neighbourParts)//verify thread safety
                    {
                        if (des_partsBrokeness[borI] >= 1.0f || partsToIgnore.Add(borI) == false) continue;
                        oList.Add(borI);
                    }
                }

                //Spread the destruction through the mesh
                Color tempCol;

                for (int oI = 0; oI < oList.Count; oI++)
                {
                    //get the world position of the vertices used by this part
                    partI = oList[oI];
                    highestForceApplied = -1.0f;
                    RealtimeVer_prepareChange(partI);

                    //loop through all part vertices and get the force applied to them
                    foreach (int vI in allParts[partI].rendVertexIndexes)//verify thread safety
                    {
                        des_verForceApply[vI] = math.max(0.0f, (impForce - (MathF.Pow(GetClosestImpPoint(mDef_verNow[vI]), falloffPower) * falloffStrenght)) / allPartsResistance[partI]);
                        
                        if (des_verForceApply[vI] <= 0.0f) continue;
                        if (highestForceApplied < des_verForceApply[vI]) highestForceApplied = des_verForceApply[vI];
                    }

                    if (highestForceApplied <= 0.0f) continue;

                    //try add neighbours to oList to continue spreading
                    foreach (int borI in allParts[partI].neighbourParts)//verify thread safety
                    {
                        if (allParts[borI].parentIndex != allParts[partI].parentIndex || partsToIgnore.Add(borI) == false || des_partsBrokeness[borI] >= 1.0f) continue;//verify thread safety
                        oList.Add(borI);
                    }

                    //apply the highest vertex force to the part itself
                    highestForceApplied /= partResistanceFactor;
                    des_partsBrokeness[partI] += highestForceApplied;
                    if (des_partsBrokeness[partI] >= 1.0f)
                    {
                        AddNewToBreak(partI, highestForceApplied, impIndex, false);
                        continue;
                    }

                    //loop through all part vertices and deform them
                    //float partVerForce = 0.0f;

                    foreach (int vI in allParts[partI].rendVertexIndexes)//verify thread safety
                    {
                        des_verForceApply[vI] = Math.Min(des_verForceApply[vI], 1.0f - des_verForceApplied[vI]);
                        if (des_verForceApply[vI] <= 0.00001f)
                        {
                            //partVerForce += des_verForceApplied[vI];
                            continue;
                        }
                        
                        des_verForceApplied[vI] += des_verForceApply[vI];
                        //partVerForce += des_verForceApplied[vI];

                        mDef_verNow[vI] += des_verForceApply[vI] * vertexDisplacementStenght * impDir;
                        tempCol = mDef_verColUse[vI];
                        tempCol.a = des_verForceApplied[vI];
                        mDef_verColUse[vI] = tempCol;
                    }

                    //Offset the part and save part vertics offsets
                    //partVerForce /= allParts[partI].rendVertexIndexes.Count;
                    RealtimeVer_prepareApply(partI);
                }

                Debug_toggleTimer();

#if UNITY_EDITOR
                if (debugMode == DebugMode.showDestruction)
                {
                    foreach (FractureGlobalHandler.ImpPoint iPoint in impPoints)
                    {

                        FractureHelperFunc.Debug_drawDisc(iPoint.impPos, impDir, impactRadius, 6);

                    }

                    Debug.DrawLine(impPoints[0].impPos, impPoints[0].impPos + impDir, Color.white, 0.5f, false);
                }
#endif

                float GetClosestImpPoint(Vector3 closestToThis)
                {
                    closeDis = float.MaxValue;

                    foreach (FractureGlobalHandler.ImpPoint iPoint in impPoints)
                    {
                        cDis = (iPoint.impPos - closestToThis).magnitude;
                        //cDis = (FractureHelperFunc.ClosestPointOnDisc(closestToThis, iPoint.impPos, impDir, impactRadius, impactRadiusSquared) - closestToThis).magnitude;
                        if (cDis >= closeDis) continue;

                        closeDis = cDis;
                        //closePos = iPoint.impPos;
                    }

                    return closeDis;
                }
            }

            void AddNewToBreak(int partIndex, float forceApplied, int impIndex, bool wasDirectHit)
            {
                float forceCons = (1.0f - (des_partsBrokeness[partIndex] - forceApplied)) * (allPartsResistance[partIndex] / damComputing[impIndex].impForce);
                des_partsToBreak.Add(new() {
                    forceConsumed = forceCons,
                    forceApplied = 1.0f - forceCons,
                    partIndex = partIndex,
                    impIndex = impIndex,
                    directHit = wasDirectHit
                });
            }
        }

        /// <summary>
        /// Makes the part a broken piece
        /// </summary>
        /// <param name="partIndex"></param>
        /// <param name="breakVelocity"></param>
        private void Run_breakPart(int partIndex, Vector3 breakVelocity, float forceConsumed, float forceLeft)
        {
            if (allParts[partIndex].parentIndex < 0) return;

            //update parent
            Vector3 partWorldPosition = GetPartWorldPosition(partIndex);
            allFracParents[allParts[partIndex].parentIndex].parentRb.AddForceAtPosition(
                forceConsumed / Math.Max(1.0f, allFracParents[allParts[partIndex].parentIndex].parentRb.mass) * breakVelocity,
                partWorldPosition,
                ForceMode.VelocityChange);

            Run_setPartParent(partIndex, -1, true);

            //setup physics for the part
            if (phyMainOptions.mainPhysicsType != OptMainPhysicsType.overlappingIsKinematic || kinematicPartStatus[partIndex] == false)
            {
                if (phyPartsOptions.partPhysicsType == OptPartPhysicsType.rigidbody_medium || phyPartsOptions.partPhysicsType == OptPartPhysicsType.rigidbody_high)
                {
                    //if rigidbody, would it be faster if the rb always exists and just toggle kinematic?
                    Rigidbody newRb = allParts[partIndex].col.GetOrAddComponent<Rigidbody>();
                    newRb.mass = massDensity;
                    newRb.drag = phyPartsOptions.drag;
                    newRb.angularDrag = phyPartsOptions.angularDrag;
                    newRb.interpolation = phyPartsOptions.interpolate;
                    newRb.collisionDetectionMode = phyPartsOptions.partPhysicsType == OptPartPhysicsType.rigidbody_medium ? CollisionDetectionMode.Discrete : CollisionDetectionMode.ContinuousDynamic;
                    newRb.isKinematic = false;

                    //Would be nice to add somesort of verification to prevent the part to be moved inside a "solid" wall
                    if (Physics.Raycast(partWorldPosition, breakVelocity, out RaycastHit nHit, partAvgBoundsExtent + (breakVelocity.magnitude * Time.fixedDeltaTime)) == false || globalHandler.TryGetFracPartFromColInstanceId(nHit.colliderInstanceID) != null)
                    newRb.transform.position += breakVelocity * Time.fixedDeltaTime; //verification above seems to work but is it fast enough

                    //newRb.MovePosition(newRb.transform.position + (breakVelocity * Time.fixedDeltaTime));
                    //print(velocityMultiplier);
                    newRb.velocity = forceLeft * breakVelocity;
                    globalHandler.OnAddRigidbody(newRb, newRb.mass);
                    //allParts[partIndex].col.enabled = false;
                    //newRb.AddForce(breakVelocity, ForceMode.VelocityChange);
                }
            }
            else
            {
                allParts[partIndex].col.enabled = false;
            }
        }

        /// <summary>
        /// Returns the current position of the given part index in worldspace
        /// </summary>
        /// <param name="partIndex"></param>
        /// <returns></returns>
        public Vector3 GetPartWorldPosition(int partIndex)
        {
            if (isRealSkinnedM == true) return allParts[partIndex].parentIndex != 0 ? allParts[partIndex].col.bounds.center : allSkinPartCols[partIndex].bounds.center;
            else return allParts[partIndex].trans.position;
        }

        #endregion DestructionSystem



        public bool debugSnap;



        #region RepairSystem

        /// <summary>
        /// The orginal local pos for each part (If real skinned, colBone space else defualtParent space, threaded)
        /// </summary>
        private Vector3[] rep_ogLocalPartPoss = new Vector3[0];

        /// <summary>
        /// The orginal local rotation for each part (Only set if real skinned, colBone space, threaded)
        /// </summary>
        private Quaternion[] rep_ogLocalPartRots = new Quaternion[0];

        /// <summary>
        /// The matrix to convert local og pos/rot foreach part to world space (Threaded)
        /// </summary>
        private Matrix4x4[] rep_LocalPartOgToWorld = new Matrix4x4[0];
        private Quaternion[] rep_partsParentRot = new Quaternion[0];

        private Vector3[] rep_verticsOrginal = new Vector3[0];
        private List<RepPartData> rep_partsToRepair = new();

        private Task<List<RepResult>> repUpdateThread = null;

        /// <summary>
        /// The position of the part durring repair (Threaded)
        /// </summary>
        private Vector3[] rep_partsNowPosition = new Vector3[0];

        /// <summary>
        /// The rotation of the part durring repair (Threaded)
        /// </summary>
        private Quaternion[] rep_partsNowRotation = new Quaternion[0];

        private class RepPartData
        {
            public int partIndex;
            public bool firstLoop;
        }

        public void Run_requestRepairPart(int partToRepair)
        {
            //return if part is already being repaired
            if (partToRepair < 0 || repairSupport == DestructionRepairSupport.dontSupportRepair || rep_partsToRepair.Any(part => part.partIndex == partToRepair) == true) return;

            //reset part brokeness
            allParts[partToRepair].partBrokenness = 0.0f;
            damBrokenessNeedsSync = true;

            //set part parent to defualt and add it to repair list
            Run_setPartParent(partToRepair, 0, false);
            rep_partsToRepair.Add(new()
            {
                partIndex = partToRepair,
                firstLoop = true
            }); ;

            //disable part collider (We dont want it to collide with stuff while moving)
            allParts[partToRepair].col.enabled = false;
        }

        /// <summary>
        /// Calls everyframe there is something to repair
        /// </summary>
        private void Run_repairUpdate()
        {
            if (repUpdateThread != null) //running this directly after thread start fix bug
            {
                if (repUpdateThread.IsCompleted == false) repUpdateThread.Wait();

                if (repUpdateThread.IsCompletedSuccessfully == true)
                {
                    //apply modified properties to fracture
                    ApplyNewValuesToParts(repUpdateThread.Result);
                }

                repUpdateThread = null;
            }

            //if still more to repair, run repair thread again
            if (rep_partsToRepair.Count <= 0) return;

            for (int i = 0; i < rep_partsToRepair.Count; i += 1)
            {
                if (isRealSkinnedM == false)
                {
                    rep_LocalPartOgToWorld[rep_partsToRepair[i].partIndex] = allFracParents[0].parentTrans.localToWorldMatrix;
                    rep_partsParentRot[rep_partsToRepair[i].partIndex] = allFracParents[0].parentTrans.rotation;
                }
                else
                {
                    rep_LocalPartOgToWorld[rep_partsToRepair[i].partIndex] = allSkinPartCols[rep_partsToRepair[i].partIndex].transform.localToWorldMatrix;
                    rep_partsParentRot[rep_partsToRepair[i].partIndex] = allSkinPartCols[rep_partsToRepair[i].partIndex].transform.rotation;
                }

                if (rep_partsToRepair[i].firstLoop == false) continue;

                rep_partsNowPosition[rep_partsToRepair[i].partIndex] = allParts[rep_partsToRepair[i].partIndex].trans.position;
                rep_partsNowRotation[rep_partsToRepair[i].partIndex] = allParts[rep_partsToRepair[i].partIndex].trans.rotation;
                rep_partsToRepair[i].firstLoop = false;
            }

            float dTime = Time.deltaTime;

            if (multithreadedDestruction == true)
            {
                float scale = fracRend.transform.lossyScale.magnitude;
                RepPartData[] repD = rep_partsToRepair.ToArray();
                repUpdateThread = Task.Run(() => Run_repairUpdateThread(repD, dTime, scale));
            }
            else
            {
                ApplyNewValuesToParts(Run_repairUpdateThread(rep_partsToRepair.ToArray(), dTime, fracRend.transform.lossyScale.magnitude));
            }

            //for (int i = rep_partsToRepair.Count - 1; i >= 0; i -= 1)
            //{
            //    if (Run_repairUpdatePart(rep_partsToRepair[i].partIndex, i) == true) didFullRepairAny = true;
            //}

            void ApplyNewValuesToParts(List<RepResult> repResult)
            {
                //bool[] repResult = repUpdateThread.Result;
                bool didFullRepairAny = false;
                int partIndex;
                int repI;

                //set parts position and rotation
                for (int i = rep_partsToRepair.Count - 1; i >= 0; i -= 1)
                {
                    partIndex = rep_partsToRepair[i].partIndex;
                    repI = repResult.FindIndex(rep => rep.partIndex == partIndex);
                    if (repI < 0) continue;

                    allParts[partIndex].trans.SetPositionAndRotation(rep_partsNowPosition[partIndex], rep_partsNowRotation[partIndex]);

                    if (repResult[repI].isRepaired == true)
                    {
                        //when part gets repaired
                        //enable cols and set bone weights
                        if (isRealSkinnedM == false) allParts[partIndex].col.enabled = true;
                        else
                        {
                            Run_setBoneWeights(partIndex, true);
                            allSkinPartCols[partIndex].enabled = true;
                        }

                        didFullRepairAny = true;
                        rep_partsToRepair.RemoveAt(i);
                    }
                }

                //set mesh vertics and colors
                if (repairSupport == DestructionRepairSupport.fullHigh || (didFullRepairAny == true && repairSupport == DestructionRepairSupport.fullLow))
                {
                    fracRend.sharedMesh.SetVertices(mDef_verUse);
                    fracRend.sharedMesh.SetColors(mDef_verColUse);

                    if (recalculateOnDisplacement != NormalRecalcMode.none)
                    {
                        fracRend.sharedMesh.RecalculateNormals();
                        if (recalculateOnDisplacement == NormalRecalcMode.normalsAndTagents) fracRend.sharedMesh.RecalculateTangents();
                    }
                }
            }
        }

        private struct RepResult
        {
            public int partIndex;
            public bool isRepaired;
        }

        private List<RepResult> Run_repairUpdateThread(RepPartData[] partsToRep, float deltaTime, float scaleMag)
        {
            List<RepResult> repairedParts = new();
            float speedScaled = repairSpeed * deltaTime;
            float speedScaledVer = (speedScaled / scaleMag) * 0.6f;
            float speedScaledMin = speedScaled / speedScaled;
            float speedScaledRot = speedScaled * 80.0f;
            float speedScaledCol = speedScaled * 0.5f;
            bool isRepaired;
            Vector3 ogWorldPos;
            Quaternion ogWorldRot;
            Vector3 tempVec;
            Color tempColor;

            for (int i = 0; i < partsToRep.Length; i += 1)
            {
                repairedParts.Add(new() { partIndex = partsToRep[i].partIndex, isRepaired = repairPart(partsToRep[i].partIndex) });
            }

            return repairedParts;

            bool repairPart(int partIndex)
            {
                isRepaired = true;

                //move part position and rotation towards orginal
                ogWorldPos = rep_LocalPartOgToWorld[partIndex].MultiplyPoint(rep_ogLocalPartPoss[partIndex]);
                if ((ogWorldPos - rep_partsNowPosition[partIndex]).magnitude >= speedScaled) isRepaired = false;
                rep_partsNowPosition[partIndex] = CustomLerp(rep_partsNowPosition[partIndex], ogWorldPos);

                ogWorldRot = rep_partsParentRot[partIndex] * rep_ogLocalPartRots[partIndex];
                if (Quaternion.Angle(rep_partsNowRotation[partIndex], ogWorldRot) >= speedScaledRot) isRepaired = false;
                rep_partsNowRotation[partIndex] = Quaternion.RotateTowards(rep_partsNowRotation[partIndex], ogWorldRot, speedScaledRot);

                //move vertics and colors towards orginal
                if (repairSupport == DestructionRepairSupport.fullHigh)
                {
                    foreach (int vI in allParts[partIndex].rendVertexIndexes)
                    {
                        tempVec = rep_verticsOrginal[vI] - mDef_verUse[vI];
                        if (tempVec.sqrMagnitude > 0.0001f) isRepaired = false;
                        mDef_verUse[vI] += Vector3.ClampMagnitude(tempVec, speedScaledVer);

                        tempColor = mDef_verColUse[vI];
                        if (tempColor.a > 0.0001f) isRepaired = false;
                        //tempColor.a = Mathf.MoveTowards(tempColor.a, 0.0f, rep_speedScaled * Time.deltaTime);
                        tempColor.a = Math.Max(tempColor.a - speedScaledCol, 0.0f);
                        mDef_verColUse[vI] = tempColor;
                    }
                }

                if (isRepaired == true)
                {
                    //when part gets restored
                    //set part pos to orginal
                    rep_partsNowPosition[partIndex] = ogWorldPos;
                    rep_partsNowRotation[partIndex] = ogWorldRot;

                    //reset part vertics force, pos and color
                    foreach (int vI in allParts[partIndex].rendVertexIndexes)
                    {
                        mDef_verDefAmount[vI] = 0.0f;
                        mDef_verUse[vI] = rep_verticsOrginal[vI];

                        tempColor = mDef_verColUse[vI];
                        tempColor.a = 0.0f;
                        mDef_verColUse[vI] = tempColor;
                    }
                }

                return isRepaired;

                Vector3 CustomLerp(Vector3 current, Vector3 target)
                {
                    if ((target - current).sqrMagnitude < speedScaledMin)
                    {
                        // If the squared distance is below the squared minimum speed, move at least the minimum speed
                        return Vector3.MoveTowards(current, target, speedScaled);
                    }

                    // Otherwise, use standard Lerp
                    return Vector3.Lerp(current, target, speedScaled);
                }
            }
        }

        #endregion RepairSystem







        #region HelperFunctions

        //debug stopwatch
        private System.Diagnostics.Stopwatch stopwatch = new();

        private void Debug_toggleTimer()
        {
            if (stopwatch.IsRunning == false)
            {
                stopwatch.Restart();
            }
            else
            {
                stopwatch.Stop();
                Debug.Log("time: " + stopwatch.Elapsed.TotalMilliseconds + "ms");
            }
        }

        /// <summary>
        /// Returns true if the given collider count as a self collider (Depends on selfCollisionCanDamage)
        /// </summary>
        /// <returns></returns>
        public float Run_getDamageMultiplier(Rigidbody colWith)
        {
            //if (colWith == null || colWith.isKinematic == true) return selfCollisionMultiplier; //itself caused the impact
            //if (colWith.transform.parent == transform) return selfCollisionMultiplier; //a part from itself caused the impact
            return 1.0f;
        }

        /// <summary>
        /// All parts transform names contains the index of its part. This function will get the index from the given string, -1 if the string does not contain a part index 
        /// </summary>
        public int Run_tryGetPartIndexFromString(String transName)
        {
            //The part index is stored in the transform name
            Match match = Regex.Match(transName, @"Part\((\d+)\)");

            if (match.Success == true && int.TryParse(match.Groups[1].Value, out int partId) == true) return partId;

            return -1;
        }

        /// <summary>
        /// Returns the index of the part the given collider is for, -1 if not a part
        /// </summary>
        /// 
        public int Run_tryGetPartIndexFromCol(Collider col)
        {
            if (isRealSkinnedM == false)
            {
                for (int i = 0; i < allParts.Length; i += 1)
                {
                    if (allParts[i].col == col) return i;
                }
            }
            else
            {
                for (int i = 0; i < allParts.Length; i += 1)
                {
                    if (allSkinPartCols[i] == col || allParts[i].col == col) return i;
                }

            }

            return -1;
        }

        /// <summary>
        /// Returns the index of a part that has a collider with the given instanceId, partCollider.GetInstanceID();
        /// </summary>
        /// <param name="instanceId"></param>
        /// <returns></returns>
        public int Run_tryGetPartIndexFromInstanceId(int instanceId)
        {
            return globalHandler.TryGetFracPartFromColInstanceId(instanceId).partIndex;
        }

        private bool mayAnyPartBeDamaged = true;

        /// <summary>
        /// Returns the index of the ~first part that has taken damage, -1 if no part has taken damage
        /// </summary>
        /// <returns></returns>
        public int Run_tryGetFirstDamagedPart()
        {
            if (mayAnyPartBeDamaged == false) return -1;
            if (allFracParents[0].partIndexes.Count <= 0) return 0;

            if (allFracParents[0].partIndexes.Count != allParts.Length)
            {
                foreach (int partI in allFracParents[0].partIndexes)
                {
                    if (allParts[partI].partBrokenness > 0.0f && rep_partsToRepair.Any(part => part.partIndex == partI) == false) return partI;

                    foreach (int nI in allParts[partI].neighbourParts)
                    {
                        if (allParts[nI].parentIndex != 0 && rep_partsToRepair.Any(part => part.partIndex == nI) == false)
                        {
                            return nI;
                        }
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

            mayAnyPartBeDamaged = false;
            return -1;
        }

        /// <summary>
        /// Returns 0 if no prefab, 1 if prefab instance, 2 if prefab asset (Will always return 0 at runtime)
        /// </summary>
        /// <returns></returns>
        public byte GetFracturePrefabType()
        {
#if UNITY_EDITOR
            if (Application.isPlaying == false)
            {
                if (gameObject.scene.path.Length == 0 || eOnly_isPrefabAsset == true) return 2;
                if (PrefabUtility.IsPartOfPrefabThatCanBeAppliedTo(gameObject) == true) return 1;
            }
#endif

            return 0;
        }

        #endregion HelperFunctions




        #region MeshDeformation

        private Matrix4x4[] mBake_boneMatrices;
        private Matrix4x4[] mBake_meshBindposes;

        private void SetupRealVerticsWorld()
        {
            if (fracRend == null) return; //return if not fractured

            mBake_boneMatrices = new Matrix4x4[fracRend.bones.Length];
            mBake_meshBindposes = fracRend.sharedMesh.bindposes;
        }

        private BoneWeight mBake_weight;
        private Matrix4x4 mBake_bm0;
        private Matrix4x4 mBake_bm1;
        private Matrix4x4 mBake_bm2;
        private Matrix4x4 mBake_bm3;
        private Matrix4x4 mBake_vms = new();

        /// <summary>
        /// Should be called once before using prepareChange to sync the boneMatrices used by prepareChange with the actual bones
        /// </summary>
        private void RealtimeVer_syncBones()
        {
            for (int i = 0; i < mBake_boneMatrices.Length; i++) mBake_boneMatrices[i] = allFracBonesLToW[i] * mBake_meshBindposes[i];
        }

        /// <summary>
        /// After calling this, you can assign mDef_verNow[allParts[partIndex].rendVertexIndexes] with new world positions
        /// </summary>
        private void RealtimeVer_prepareChange(int partIndex)
        {
            foreach (int vI in allParts[partIndex].rendVertexIndexes)
            {
                mBake_weight = boneWe_current[vI];
                mBake_bm0 = mBake_boneMatrices[mBake_weight.boneIndex0];
                mBake_bm1 = mBake_boneMatrices[mBake_weight.boneIndex1];
                mBake_bm2 = mBake_boneMatrices[mBake_weight.boneIndex2];
                mBake_bm3 = mBake_boneMatrices[mBake_weight.boneIndex3];
                
                mBake_vms.m00 = mBake_bm0.m00 * mBake_weight.weight0 + mBake_bm1.m00 * mBake_weight.weight1 + mBake_bm2.m00 * mBake_weight.weight2 + mBake_bm3.m00 * mBake_weight.weight3;
                mBake_vms.m01 = mBake_bm0.m01 * mBake_weight.weight0 + mBake_bm1.m01 * mBake_weight.weight1 + mBake_bm2.m01 * mBake_weight.weight2 + mBake_bm3.m01 * mBake_weight.weight3;
                mBake_vms.m02 = mBake_bm0.m02 * mBake_weight.weight0 + mBake_bm1.m02 * mBake_weight.weight1 + mBake_bm2.m02 * mBake_weight.weight2 + mBake_bm3.m02 * mBake_weight.weight3;
                mBake_vms.m03 = mBake_bm0.m03 * mBake_weight.weight0 + mBake_bm1.m03 * mBake_weight.weight1 + mBake_bm2.m03 * mBake_weight.weight2 + mBake_bm3.m03 * mBake_weight.weight3;
                
                mBake_vms.m10 = mBake_bm0.m10 * mBake_weight.weight0 + mBake_bm1.m10 * mBake_weight.weight1 + mBake_bm2.m10 * mBake_weight.weight2 + mBake_bm3.m10 * mBake_weight.weight3;
                mBake_vms.m11 = mBake_bm0.m11 * mBake_weight.weight0 + mBake_bm1.m11 * mBake_weight.weight1 + mBake_bm2.m11 * mBake_weight.weight2 + mBake_bm3.m11 * mBake_weight.weight3;
                mBake_vms.m12 = mBake_bm0.m12 * mBake_weight.weight0 + mBake_bm1.m12 * mBake_weight.weight1 + mBake_bm2.m12 * mBake_weight.weight2 + mBake_bm3.m12 * mBake_weight.weight3;
                mBake_vms.m13 = mBake_bm0.m13 * mBake_weight.weight0 + mBake_bm1.m13 * mBake_weight.weight1 + mBake_bm2.m13 * mBake_weight.weight2 + mBake_bm3.m13 * mBake_weight.weight3;
                
                mBake_vms.m20 = mBake_bm0.m20 * mBake_weight.weight0 + mBake_bm1.m20 * mBake_weight.weight1 + mBake_bm2.m20 * mBake_weight.weight2 + mBake_bm3.m20 * mBake_weight.weight3;
                mBake_vms.m21 = mBake_bm0.m21 * mBake_weight.weight0 + mBake_bm1.m21 * mBake_weight.weight1 + mBake_bm2.m21 * mBake_weight.weight2 + mBake_bm3.m21 * mBake_weight.weight3;
                mBake_vms.m22 = mBake_bm0.m22 * mBake_weight.weight0 + mBake_bm1.m22 * mBake_weight.weight1 + mBake_bm2.m22 * mBake_weight.weight2 + mBake_bm3.m22 * mBake_weight.weight3;
                mBake_vms.m23 = mBake_bm0.m23 * mBake_weight.weight0 + mBake_bm1.m23 * mBake_weight.weight1 + mBake_bm2.m23 * mBake_weight.weight2 + mBake_bm3.m23 * mBake_weight.weight3;
                
                mDef_verNow[vI] = mBake_vms.MultiplyPoint3x4(mDef_verUse[vI]);
            }
        }

        /// <summary>
        /// You should call this after calling RealtimeVer_prepareChange with the same partIndex
        /// </summary>
        private void RealtimeVer_prepareApply(int partIndex)
        {
            foreach (int vI in allParts[partIndex].rendVertexIndexes)
            {
                mBake_weight = boneWe_current[vI];
                mBake_bm0 = mBake_boneMatrices[mBake_weight.boneIndex0].inverse;
                mBake_bm1 = mBake_boneMatrices[mBake_weight.boneIndex1].inverse;
                mBake_bm2 = mBake_boneMatrices[mBake_weight.boneIndex2].inverse;
                mBake_bm3 = mBake_boneMatrices[mBake_weight.boneIndex3].inverse;
                
                mBake_vms.m00 = mBake_bm0.m00 * mBake_weight.weight0 + mBake_bm1.m00 * mBake_weight.weight1 + mBake_bm2.m00 * mBake_weight.weight2 + mBake_bm3.m00 * mBake_weight.weight3;
                mBake_vms.m01 = mBake_bm0.m01 * mBake_weight.weight0 + mBake_bm1.m01 * mBake_weight.weight1 + mBake_bm2.m01 * mBake_weight.weight2 + mBake_bm3.m01 * mBake_weight.weight3;
                mBake_vms.m02 = mBake_bm0.m02 * mBake_weight.weight0 + mBake_bm1.m02 * mBake_weight.weight1 + mBake_bm2.m02 * mBake_weight.weight2 + mBake_bm3.m02 * mBake_weight.weight3;
                mBake_vms.m03 = mBake_bm0.m03 * mBake_weight.weight0 + mBake_bm1.m03 * mBake_weight.weight1 + mBake_bm2.m03 * mBake_weight.weight2 + mBake_bm3.m03 * mBake_weight.weight3;
                
                mBake_vms.m10 = mBake_bm0.m10 * mBake_weight.weight0 + mBake_bm1.m10 * mBake_weight.weight1 + mBake_bm2.m10 * mBake_weight.weight2 + mBake_bm3.m10 * mBake_weight.weight3;
                mBake_vms.m11 = mBake_bm0.m11 * mBake_weight.weight0 + mBake_bm1.m11 * mBake_weight.weight1 + mBake_bm2.m11 * mBake_weight.weight2 + mBake_bm3.m11 * mBake_weight.weight3;
                mBake_vms.m12 = mBake_bm0.m12 * mBake_weight.weight0 + mBake_bm1.m12 * mBake_weight.weight1 + mBake_bm2.m12 * mBake_weight.weight2 + mBake_bm3.m12 * mBake_weight.weight3;
                mBake_vms.m13 = mBake_bm0.m13 * mBake_weight.weight0 + mBake_bm1.m13 * mBake_weight.weight1 + mBake_bm2.m13 * mBake_weight.weight2 + mBake_bm3.m13 * mBake_weight.weight3;
                
                mBake_vms.m20 = mBake_bm0.m20 * mBake_weight.weight0 + mBake_bm1.m20 * mBake_weight.weight1 + mBake_bm2.m20 * mBake_weight.weight2 + mBake_bm3.m20 * mBake_weight.weight3;
                mBake_vms.m21 = mBake_bm0.m21 * mBake_weight.weight0 + mBake_bm1.m21 * mBake_weight.weight1 + mBake_bm2.m21 * mBake_weight.weight2 + mBake_bm3.m21 * mBake_weight.weight3;
                mBake_vms.m22 = mBake_bm0.m22 * mBake_weight.weight0 + mBake_bm1.m22 * mBake_weight.weight1 + mBake_bm2.m22 * mBake_weight.weight2 + mBake_bm3.m22 * mBake_weight.weight3;
                mBake_vms.m23 = mBake_bm0.m23 * mBake_weight.weight0 + mBake_bm1.m23 * mBake_weight.weight1 + mBake_bm2.m23 * mBake_weight.weight2 + mBake_bm3.m23 * mBake_weight.weight3;
                
                mDef_verUse[vI] = mBake_vms.MultiplyPoint3x4(mDef_verNow[vI]);
            }
        }

        private Task calcDefThread = null;
        private DesPartsUsed[] defToUse = null;

        private IEnumerator ComputeDeformation()
        {
            //get the parts to use in deformation calc
            defToUse = damPartsUsed.ToArray();
            damPartsUsed.Clear();
            yield return new WaitForEndOfFrame();
            ////run the compute deformation thread
            //calcDefThread = Task.Run(() => ComputeDeformationThread());
            //while (calcDefThread.IsCompleted == false && calcDefThread.IsFaulted == false) yield return null;
            //
            //if (calcDefThread.IsFaulted == true)
            //{
            //    //when error accure
            //    Debug.LogException(calcDefThread.Exception);
            //    Debug.LogWarning("A race may have accured while computing deformation for " + transform.name);
            //    calcDefThread = null;
            //    yield break;
            //}

            calcDefThread = null;

            //apply deformation
            fracRend.sharedMesh.SetVertices(mDef_verUse, 0, mDef_verUse.Count, UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices | UnityEngine.Rendering.MeshUpdateFlags.DontResetBoneBounds | UnityEngine.Rendering.MeshUpdateFlags.DontNotifyMeshUsers | UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
            fracRend.sharedMesh.SetColors(mDef_verColUse, 0, mDef_verColUse.Count, UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices | UnityEngine.Rendering.MeshUpdateFlags.DontResetBoneBounds | UnityEngine.Rendering.MeshUpdateFlags.DontNotifyMeshUsers | UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
        }

        #endregion MeshDeformation
    }
}