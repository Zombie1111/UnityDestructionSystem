using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using UnityEditor;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;
using System;
using Component = UnityEngine.Component;
using System.Data;
using UnityEditor.SceneManagement;
using Unity.VisualScripting;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Jobs;
using Unity.Burst;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using System.Collections.Concurrent;
using JetBrains.Annotations;
using Unity.Collections.LowLevel.Unsafe;
using System.Reflection;
using UnityEditor.Compilation;
using UnityEngine.SceneManagement;
using UnityEditor.Rendering;
using System.Security;

namespace Zombie1111_uDestruction
{
    [ExecuteInEditMode]
    public class FractureThis : MonoBehaviour
    {

        #region EditorAndOptions

#if UNITY_EDITOR
        //########################Custom Editor######################################
        [CustomEditor(typeof(FractureThis))]
        public class YourScriptEditor : Editor
        {
            private static readonly string[] noFracSpecial = new string[]
            {
                "m_Script", "debugMode", "ogData", "saved_allPartsCol", "saved_fracId", "shouldBeFractured", "partMaxExtent", "fracFilter",
                "fracRend", "fr_bones", "partBoneOffset", "isRealSkinnedM", "allParents", "partsDefualtData", "globalHandler", "fracPrefabType"
               
            };

            public override void OnInspectorGUI()
            {
                serializedObject.Update();

                FractureThis yourScript = (FractureThis)target;

                EditorGUILayout.Space();


                if (yourScript.fractureIsValid == true)
                {
                    if (yourScript.fracPrefabType == 0)
                    {
                        //regenerate does not work for prefabs since it does not realize that the fracture has been regenerated, too lazy to actually fix
                        if (GUILayout.Button("Regenerate Fracture"))
                        {
                            yourScript.Gen_fractureObject();
                        }
                    }

                    if (GUILayout.Button("Remove Fracture"))
                    {
                        yourScript.Gen_removeFracture();
                    }

                    EditorGUILayout.Space();
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("debugMode"), true);
                    if (Application.isPlaying == true)
                    {
                        EditorGUILayout.Space();
                        EditorGUILayout.HelpBox("Modifying destructionMaterials at runtime may cause issues and should only be used to temporarly test different values", MessageType.Warning);
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("destructionMaterials"), true);
                    }

                    EditorGUILayout.HelpBox("Properties cannot be edited while fractured", MessageType.Info);

                    GUI.enabled = false;
                    DrawPropertiesExcluding(serializedObject, new string[] { "m_Script", "debugMode", Application.isPlaying == true ? "destructionMaterials" : "" });
                    GUI.enabled = true;
                }
                else
                {
                    if (GUILayout.Button("Generate Fracture"))
                    {
                        yourScript.Gen_fractureObject();
                    }

                    DrawPropertiesExcluding(serializedObject, noFracSpecial);
                    GUI.enabled = false;
                    for (int i = 1; i < noFracSpecial.Length; i++)
                    {
                        EditorGUILayout.PropertyField(serializedObject.FindProperty(noFracSpecial[i]), true);
                    }
                    GUI.enabled = true;
                }

                //Apply changes
                serializedObject.ApplyModifiedProperties();
            }
        }

        public void CopyFracturePropertiesFrom(FractureThis from)
        {
            fractureCount = from.fractureCount;
            dynamicFractureCount = from.dynamicFractureCount;
            seed = from.seed;
            generationQuality = from.generationQuality;
            colliderType = from.colliderType;
            phyMainOptions = from.phyMainOptions;
            phyPartsOptions = from.phyPartsOptions;
            insideMat_fallback = from.insideMat_fallback;
            insideMat_nameAddition = from.insideMat_nameAddition;
        }
#endif

        //fracture settings
        [Header("Fracture")]
        public FractureSaveAsset saveAsset = null;
        [SerializeField] private FractureSavedState saveState = null;
        [SerializeField] private int fractureCount = 15;
        [SerializeField] private bool dynamicFractureCount = true;
        [Tooltip("If < 1.0f, controls how random the angle of the cuts are. If >= 1.0f, voronoi is used")]
        [SerializeField][Range(0.0f, 1.0f)] private float randomness = 1.0f;
        [SerializeField] private int seed = -1;
        public GenerationQuality generationQuality = GenerationQuality.normal;
        [SerializeField] private FractureRemesh remeshing = FractureRemesh.defualt;

        [Space(10)]
        [Header("Physics")]
        [SerializeField] private ColliderType colliderType = ColliderType.mesh;
        [SerializeField] private SelfCollisionRule selfCollisionRule = SelfCollisionRule.ignoreNeighbours;
        public OptPhysicsMain phyMainOptions = new();
        [SerializeField] private OptPhysicsParts phyPartsOptions = new();

        [Space(10)]
        [Header("Destruction")]

        [Space(10)]
        [Header("Mesh")]

        [Space(10)]
        [Header("Material")]
        [SerializeField] private Material insideMat_fallback = null;
        [SerializeField] private string insideMat_nameAddition = "_inside";
        [SerializeField] private DefualtDesMatOptions defualtDestructionMaterial = new();

        [Space(10)]
        [Header("Advanced")]
        [SerializeField] private bool splitDisconnectedFaces = false;
        [SerializeField] private bool useGroupIds = false;
#if UNITY_EDITOR
        [SerializeField] private int visualizedGroupId = -1;
#endif
        /// <summary>
        /// Contains all destruction materials, 0 is always defualt, > 0 is for groupIds that has overrides
        /// </summary>
        [SerializeField] private List<DestructionMaterial> destructionMaterials = new();

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
        [SerializeField][Tooltip("Does not work at runtime")] private DebugMode debugMode = DebugMode.none;

        private enum DebugMode
        {
            none,
            showStructure,
            showBones,
            showDestruction
        }

        [System.NonSerialized] public bool eOnly_ignoreNextDraw = true;

        private unsafe void OnDrawGizmosSelected()
        {
            //if (Application.isPlaying == true || eOnly_ignoreNextDraw == true) return;
            if (eOnly_ignoreNextDraw == true) return;

            //draw selected group id
            if (visualizedGroupId < 0 || useGroupIds == false || fracRend != null)
            {
                if (visualizedGroupId >= 0 && useGroupIds == true) Debug.LogError("Cannot visualize groupId while object is fractured");
                visualizedGroupId = -1;
            }
            else
            {
                List<FracSource> fracMeshes = Gen_getMeshesToFracture(gameObject, out _, true, FracGlobalSettings.worldScale * 0.0001f);
                visualizedGroupId = Mathf.Min(visualizedGroupId, md_verGroupIds.Length - 1);
                if (fracMeshes == null || visualizedGroupId < 0) goto skipDrawGroups;

                Color[] verCols;
                Vector3[] vers;
                List<float> gId = md_verGroupIds[visualizedGroupId];
                Vector3 drawBoxSize = 0.05f * FracGlobalSettings.worldScale * Vector3.one;
                Gizmos.color = Color.yellow;

                for (int i = 0; i < fracMeshes.Count; i++)
                {
                    verCols = fracMeshes[i].meshW.colors;
                    vers = fracMeshes[i].meshW.vertices;

                    for (int vI = vers.Length - 1; vI >= 0; vI--)
                    {
                        if (FractureHelperFunc.Gd_isIdInColor(gId, verCols[vI]) == true)
                        {
                            Gizmos.DrawCube(vers[vI], drawBoxSize);
                            continue;
                        }
                    }
                }
            }

        skipDrawGroups:

            //for temporarly testing destruction material properties
            if (Application.isPlaying == true && jCDW_jobIsActive == false)
            {
                int desPropI = 0;
                foreach (DestructionMaterial.DesProperties desProp in destructionMaterials.Select(desMat => desMat.desProps))
                {
                    jCDW_job.desProps[desPropI] = desProp;
                    desPropI++;
                }
            }

            //visualize fracture structure
            if (debugMode == DebugMode.showStructure && fractureIsValid == true && jCDW_job.structPosL.IsCreated == true)
            {
                if (Application.isPlaying == true) ComputeDestruction_end();

                Gizmos.color = Color.red;
                Vector3 sPos;
                bool prevWasKind = true;

                for (int structI = 0; structI < allParts.Count; structI++)
                {
                    int parentI = jCDW_job.partsParentI[structI];
                    if (parentI < 0) continue;

                    //sPos = fr_bones[structI + partBoneOffset].localToWorldMatrix.MultiplyPoint(jCDW_job.structPosL[structI]);
                    sPos = GetStructWorldPosition(structI);
                    if (jCDW_job.kinematicPartIndexes.Contains(structI) == true)
                    {
                        if (prevWasKind == false)
                        {
                            Gizmos.color = Color.red;
                            prevWasKind = true;
                        }
                    }
                    else
                    {
                        if (prevWasKind == true)
                        {
                            Gizmos.color = Color.blue;
                            prevWasKind = false;
                        }
                    }

                    //foreach (int nStructI in allParts[structI].neighbourStructs)
                    FracStruct fStruct = jCDW_job.fStructs[structI];

                    for (byte neI = 0; neI < fStruct.neighbourPartI_lenght; neI++)
                    {
                        short nStructI = fStruct.neighbourPartI[neI];
                        if (jCDW_job.partsParentI[nStructI] != parentI) continue;
                        //Gizmos.DrawLine(fr_bones[nStructI + partBoneOffset].localToWorldMatrix.MultiplyPoint(jCDW_job.structPosL[nStructI]), sPos);
                        Gizmos.DrawLine(GetStructWorldPosition(nStructI), sPos);
                    }
                }
            }

            //visualize fracture bones
            if (debugMode == DebugMode.showBones)
            {
                Gizmos.color = Color.blue;

                for (int i = 0; i < fr_bones.Count - 1; i += 1)
                {
                    Gizmos.DrawLine(fr_bones[i].position, fr_bones[i + 1].position);
                }
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

        public enum SelfCollisionRule
        {
            alwaysCollide,
            ignoreNeighbours,
            ignoreDoubleNeighbours
        }

        /// <summary>
        /// High = 2, normal = 1
        /// </summary>
        public enum GenerationQuality
        {
            high = 2,
            normal = 1,
        }

        public enum FractureRemesh
        {
            defualt,
            convex
        }

        private enum ColliderType
        {
            mesh,
            box,
            capsule,
            sphere
        }

        private enum SelfDamageRule
        {
            ignoreNone = 0,
            ignoreSource = 3,
            ignoreSourceAndNeighbours = 4,
            ignoreNeighbours = 2,
            ignoreAll = 1
        }

        [System.Serializable]
        public class OptPhysicsMain
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
            public float massMultiplier = 4.0f;
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

        public enum OptMainPhysicsType
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
        public List<Collider> saved_allPartsCol = new();
        public int saved_fracId = -1;
        [SerializeField] private bool shouldBeFractured = false;

        /// <summary>
        /// The biggest extent any part has in worldspace, does not include deformation (part.meshW.bounds.extents.XYZ)
        /// </summary>
        [SerializeField] private float partMaxExtent = 0.1f;

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

        private class SkinSourceData
        {
            public Matrix4x4[] bindPoses;
            public Transform[] bones;
            public Transform rendTrans;
            public Transform rootBone;
        }

        /// <summary>
        /// Saves or load data from/to save asset, return true if successful
        /// </summary>
        private bool SaveOrLoadAsset(bool doSave)
        {
            if (saveAsset == null)
            {
                Debug.LogError("No saveAsset has been assigned to " + transform.name + " fracture");
                return false;
            }

            if (doSave == true)
            {
                //save
                SyncFracRendData(); //Rend must be synced before saving since fr_[] variabels are "saved" inside the renderer
                saved_fracId = saveAsset.Save(this);

                //save scene (Since the user agreed to saving before fracturing we can just save without asking)
#if UNITY_EDITOR
                if (globalHandler != null) globalHandler.Eonly_HasFracBeenCloned(this, true);
                if (Application.isPlaying == false) EditorSceneManager.SaveOpenScenes();
#endif
                return true;
            }

            //load
            if (saveAsset.Load(this) == false)
            {
                //when cant load, log error
                if (saveAsset.fracSavedData.id < 0 || saved_fracId < 0)
                {
                    Debug.LogError("No fracture has been generated for " + transform.name);
                    return false;
                }

                Debug.LogError("Unable to load data from " + saveAsset.name + " to " + transform.name + " fracture because it has been saved by another fracture!" +
                    " (Use a different saveAsset or use a prefab to have multiable identical fractures)");
                return false;
            }

            MarkParentAsModified(0);//There is always 1 parent when loading
            SyncFracRendData();
            return true;
        }

        /// <summary>
        /// Restores all components on the previous saved objToUse, returns true if anything changed
        /// </summary>
        /// <param name="doSave">If true, objToUse og data will be saved (Make sure its okay to save)</param>
        public bool Gen_loadAndMaybeSaveOgData(bool doSave = false)
        {
            //make sure we can load and save
#if UNITY_EDITOR
            if (GetFracturePrefabType() == 1)
            {
                Debug.LogError(transform.name + " cannot be removed because its a prefab instance, open the prefab asset and remove inside it");
                return false;
            }
#endif
            GameObject objToUse = gameObject;
            bool didChangeAnything = false;
            if (doSave == true) goto SkipLoadingOg;

            //load/restore og object
            saved_fracId = -1;

            if (ogData != null)
            {
                //load og components
                foreach (OrginalCompData ogD in ogData.ogCompData)
                {
                    didChangeAnything = true;

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
                if (ogData.hadRend == true)
                {
                    didChangeAnything = true;

                    if (ogData.rendWasSkinned == false)
                    {
                        MeshRenderer mRend = transform.GetOrAddComponent<MeshRenderer>();

                        if (fracRend != null)
                        {
                            CopyRendProperties(fracRend, mRend, ogData.ogMats, ogData.ogEnable);
                            if (transform != fracRend.transform) DestroyImmediate(fracRend);
                        }

                        if ((fracFilter != null && transform != fracFilter.transform) || ogData.ogMesh == null) DestroyImmediate(fracFilter);
                        if (ogData.ogMesh != null) transform.GetOrAddComponent<MeshFilter>().sharedMesh = ogData.ogMesh;
                    }
                    else
                    {
                        SkinnedMeshRenderer sRend = transform.GetOrAddComponent<SkinnedMeshRenderer>();
                        sRend.sharedMesh = ogData.ogMesh;
                        sRend.bones = ogData.ogBones;
                        sRend.rootBone = ogData.ogRootBone;

                        if (fracRend != null)
                        {
                            CopyRendProperties(fracRend, sRend, ogData.ogMats, ogData.ogEnable);
                            DestroyImmediate(fracRend);
                        }

                        DestroyImmediate(fracFilter);
                    }
                }
                else if (fracRend != null || fracFilter != null)
                {
                    didChangeAnything = true;

                    DestroyImmediate(fracRend);
                    DestroyImmediate(fracFilter);
                }

                ogData = null;
            }

            //destroy all frac parents and parts
            foreach (Collider col in saved_allPartsCol)
            {
                if (col == null) continue;
                DestroyImmediate(col.gameObject);
            }

            for (int i = 0; i < allParents.Count; i++)
            {
                if (allParents[i].parentTrans == null
                    || allParents[i].parentTrans.name.Contains("Parent(" + i + ")_") == false) continue;

                DestroyImmediate(allParents[i].parentTrans.gameObject);
            }

            //set debug mode to none, since debugMode can freeze if huge fracture its safer to have it always defualt to none to prevent "softlock"
            debugMode = DebugMode.none;

            //clear saved variabels
            saved_allPartsCol = new();
            allParts = new();
            allParents = new();
            partsDefualtData = new();
            parentsThatNeedsUpdating = new();
            desWeights = new();
            syncFR_modifiedPartsI = new();
            ClearUsedGpuAndCpuMemory();
            jCDW_job = new()
            {
                structPosL = new NativeList<Vector3>(Allocator.Persistent),
                partsParentI = new NativeList<int>(Allocator.Persistent),
                parentPartCount = new NativeList<short>(Allocator.Persistent),
                kinematicPartIndexes = new NativeHashSet<int>(0, Allocator.Persistent),
                fStructs = new NativeList<FracStruct>(Allocator.Persistent)
            };

            //remove defualt desMaterial override
            if (destructionMaterials != null && destructionMaterials.Count > 0
                && (destructionMaterials[0].affectedGroupIndexes == null || destructionMaterials[0].affectedGroupIndexes.Count == 0)
                && destructionMaterials[0].desProps.stenght == defualtDestructionMaterial.stenght
                && destructionMaterials[0].objLayerBroken == defualtDestructionMaterial.objLayerBroken
                && destructionMaterials[0].desProps.stiffness == defualtDestructionMaterial.stiffness)
            {
                destructionMaterials.RemoveAt(0);
            }

            //return if dont save og
            if (didChangeAnything == true) EditorUtility.SetDirty(objToUse);
            if (doSave == false) return didChangeAnything;

            //save og object
            //save og renderer
            SkipLoadingOg:
            didChangeAnything = true;
            ogData = new();

            if (transform.TryGetComponent<Renderer>(out var rend) == true)
            {
                ogData.hadRend = true;
                ogData.ogEnable = rend.enabled;
                ogData.ogMats = rend.sharedMaterials;

                if (transform.TryGetComponent<SkinnedMeshRenderer>(out var sRend) == true)
                {
                    ogData.ogMesh = sRend.sharedMesh;
                    ogData.ogBones = sRend.bones;
                    ogData.ogRootBone = sRend.rootBone;
                    ogData.rendWasSkinned = true;
                }
                else
                {
                    if (transform.TryGetComponent<MeshFilter>(out var meshF) == true) ogData.ogMesh = meshF.sharedMesh;
                    else ogData.ogMesh = null;

                    ogData.rendWasSkinned = false;
                }
            }
            else ogData.hadRend = false;

            fracFilter = transform.GetOrAddComponent<MeshFilter>();
            fracRend = transform.GetOrAddComponent<MeshRenderer>();
            if (rend != null) CopyRendProperties(rend, fracRend, new Material[0], true);

            //save og components
            bool newBoolValue;

            foreach (Component comp in objToUse.GetComponentsInChildren<Component>())
            {
                if (comp == fracRend || comp == fracFilter) continue;

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

            return didChangeAnything;

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

        [System.Serializable]
        public class FracParent
        {
            public Transform parentTrans;

            /// <summary>
            /// The parents rigidbody. mass = (childPartCount * massDensity * phyMainOptions.massMultiplier), isKinematic is updated based on phyMainOptions.MainPhysicsType
            /// </summary>
            public Rigidbody parentRb;
            public List<int> partIndexes;

            /// <summary>
            /// The total mass of all parts that uses this parent
            /// </summary>
            public float parentMass;

            /// <summary>
            /// If > 0 the parent is kinematic, usually also the total number of kinematic parts in this parent
            /// </summary>
            public int parentKinematic;

            public float totalTransportCoEfficiency;
            public float totalStiffness;
        }

        [System.Serializable]
        public unsafe struct FracWeight
        {
            /// <summary>
            /// The actuall lenght of the structsI and weights array
            /// </summary>
            public int stWe_lenght;

            /// <summary>
            /// The index of every struct in the weight
            /// </summary>
            public fixed int structsI[FracGlobalSettings.maxDeformationBones];

            /// <summary>
            /// The weight of each structI (Total = 1.0f)
            /// </summary>
            public fixed float weights[FracGlobalSettings.maxDeformationBones];

            /// <summary>
            /// Creates a new FracWeight and assigns it with the given lists. (The given lists lenght should be the same and less than FracGlobalSettings.maxDeformationBones to avoid data loss)
            /// </summary>
            public static FracWeight New(List<int> dStructsI, List<float> dWeights)
            {
                FracWeight newWE = new()
                {
                    stWe_lenght = Mathf.Min(dStructsI.Count, dWeights.Count, FracGlobalSettings.maxDeformationBones)
                };

#if !FRAC_NO_WARNINGS
                if (dStructsI.Count > FracGlobalSettings.maxDeformationBones || dWeights.Count > FracGlobalSettings.maxDeformationBones)
                    Debug.LogWarning("FracWeight cannot have more than " + FracGlobalSettings.maxDeformationBones + " bones, data will be lost!");
                else if (dStructsI.Count != dWeights.Count) Debug.LogWarning("dStructsI and dWeights must have the same lenght, data will be lost!");
#endif

                for (int i = 0; i < newWE.stWe_lenght; i++)
                {
                    newWE.structsI[i] = dStructsI[i];
                    newWE.weights[i] = dWeights[i];
                }

                return newWE;
            }
        }

        [System.Serializable]
        public unsafe struct FracStruct
        {
            /// <summary>
            /// The destruction material index this part uses
            /// </summary>
            public int desMatI;
            public byte neighbourPartI_lenght;
            public fixed short neighbourPartI[FracGlobalSettings.maxPartNeighbourCount];

            /// <summary>
            /// The maximum transport capacity this part has ever consumed in % (Example, 0.9 means if it had gotten 10% more force it would have broken)
            /// </summary>
            public float maxTransportUsed;
        }

        [System.Serializable]
        public class FracPart
        {
            /// <summary>
            /// The part collider
            /// </summary>
            //public Collider col;

            /// <summary>
            /// The groupIdInt the part has, used to get what groupData the part uses
            /// </summary>
            public int groupIdInt;

            /// <summary>
            /// All floats that is > 0.0f makes the groupId (If A-B contains all floats in B-A they can be connected, if any float differ they have different groupData)
            /// </summary>
            public List<float> groupId;

            /// <summary>
            /// Each float is a link (If two parts contains the same float they can be connected)
            /// </summary>
            public HashSet<float> groupLinks;

            /// <summary>
            /// The part trans
            /// </summary>
            //public Transform trans;

            /// <summary>
            /// The vertex indexes in fracRend.sharedmesh this part uses
            /// </summary>
            public List<int> partMeshVerts = new();

            /// <summary>
            /// The index of all other structs this struct is connected with
            /// </summary>
            //public List<short> neighbourStructs;
        }

        [System.Serializable]
        private class FracPartDefualt
        {
            /// <summary>
            /// The parent the part had when it was added
            /// </summary>
            public Transform defParent;
        }

        /// <summary>
        /// Returns true if the fracture is valid and sets fractureIsValid
        /// </summary>
        private bool VerifyFracture()
        {
#if UNITY_EDITOR
            //if prefab, overrides on this script MUST be reverted
            if (GetFracturePrefabType() == 1)
            {
                string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
                PrefabUtility.RevertObjectOverride(this, InteractionMode.AutomatedAction);
            }
#endif

            fractureIsValid = false;

            if (fracRend == null || fracFilter == null) return false;
            if (globalHandler == null && VerifyGlobalHandler(true) == false) return false;
            if (SaveOrLoadAsset(false) == false)
            {
                RemoveCorrupt();
                return false;
            }

            if (fr_verticesL == null || fr_verticesL.Count == 0 || fr_bones == null || fr_bones.Count == 0)
            {
                Debug.LogError(transform.name + " destruction does not have any bones or vertics");
                RemoveCorrupt();
                return false;
            }

#if UNITY_EDITOR
            if (Application.isPlaying == false && globalHandler != null && globalHandler.Eonly_HasFracBeenCloned(this, false) == true)
            {
                Debug.LogError(transform.name + " was copied while the source was fractured, please use a prefab or avoid copying fractured objects");
                RemoveCorrupt();
                return false;
            }
#endif

            fractureIsValid = true;
            return true;

            void RemoveCorrupt()
            {
#if UNITY_EDITOR
                if (Application.isPlaying == false)
                {
                    Gen_removeFracture(false, false, false);
                    Debug.LogError("Removing " + transform.name + " fracture because it has become corrupt");
                }
#endif
            }
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
                if (canLogError == true) Debug.LogError("There is no active FractureGlobalHandler script in " + gameObject.scene.name + " (Scene), make sure a active Gameobject has the script attatch to it");
                return false;
            }
            else if (handlers.Length > 1)
            {
                if (canLogError == true) Debug.LogError("There are more than one FractureGlobalHandler script in " + gameObject.scene.name + " (Scene), please remove all but one and refracture all objects");
                return false;
            }

            if (handlers[0].gameObject.scene != gameObject.scene) return GetFracturePrefabType() == 2;

            globalHandler = handlers[0];
            return true;
        }

#if UNITY_EDITOR
        private bool eOnly_isPrefabAsset = false;
#endif

        /// <summary>
        /// Asks the user if we are allowed to save and returns true if we are
        /// </summary>
        public bool Gen_askIfCanSave(bool willRemoveFrac)
        {
#if UNITY_EDITOR
            if (Application.isPlaying == false && EditorSceneManager.GetActiveScene().isDirty == true
    && EditorUtility.DisplayDialog("", willRemoveFrac == true ? "All open scenes must be saved before removing fracture!"
    : "All open scenes must be saved before fracturing!", willRemoveFrac == true ? "Save and remove" : "Save and fracture", "Cancel") == false)
            {
                //Debug.LogError("The scene must be saved before fracturing");
                return false;
            }
#endif

            return !Application.isPlaying;
        }

        /// <summary>
        /// Call to remove the fracture, returns true if successfully removed the fracture
        /// </summary>
        public bool Gen_removeFracture(bool isPrefabAsset = false, bool askForSavePermission = true, bool allowSave = true)
        {
#if UNITY_EDITOR
            //make sure we can save the scene later, save scene
            if (askForSavePermission == true && Gen_askIfCanSave(true) == false) return false;

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
                        if (okRem == true) okRem = fracT.Gen_removeFracture(true, false);
                        else fracT.Gen_removeFracture(true, false);
                    }
                }

                return okRem;
            }
#endif

            //remove the fracture and save the scene
            bool didRemoveAny = Gen_loadAndMaybeSaveOgData(false);
#if UNITY_EDITOR
            shouldBeFractured = false;
            if (allowSave == true && Application.isPlaying == false && didRemoveAny == true) EditorSceneManager.SaveOpenScenes();
#endif

            return true;
        }

        /// <summary>
        /// Call to fracture the object the mesh is attatched to, returns true if successfully fractured the object
        /// </summary>
        public bool Gen_fractureObject(bool isPrefabAsset = false, bool askForSavePermission = true)
        {
            float fracProgress = 0;

            try
            {
                return Execute();
            }
            //catch (Exception ex)
            //{
            //    // Handle the error
            //    Debug.LogError("Exception: " + ex.Message);
            //    Debug.LogError("StackTrace: " + ex.StackTrace);
            //
            //    //Display an error message to the user
            //    EditorUtility.DisplayDialog("Error", "An unexpected error occured while fracturing, look in console for more info", "OK");
            //
            //    //remove the fracture
            //    return CancelFracturing();
            //
            //    //set fracture as invalid
            //    fractureIsValid = false;
            //}
            finally
            {
                //Always clear the progressbar
                EditorUtility.ClearProgressBar();
            }

            bool Execute()
            {
                //get prefab type
                fracPrefabType = GetFracturePrefabType();

                //make sure we can save the scene later, save scene
                if (askForSavePermission == true && Gen_askIfCanSave(false) == false) return false;

                //verify if we can continue with fracturing here or on prefab
                if (UpdateProgressBar("Verifying objects") == false) return CancelFracturing();
                if (Gen_checkIfContinueWithFrac(out bool didFracOther, isPrefabAsset) == false) return didFracOther;

                //get prefab type
                fracPrefabType = GetFracturePrefabType();

                //restore orginal data
                Gen_loadAndMaybeSaveOgData(false);
                fractureIsValid = true; //Fracture is always valid when fracturing

                //Get the meshes to fracture
                if (UpdateProgressBar("Getting meshes") == false) return CancelFracturing();
                float worldScaleDis = FracGlobalSettings.worldScale * 0.0001f;

                List<FracSource> meshesToFracW = Gen_getMeshesToFracture(gameObject, out SkinSourceData skinRendSource, false, worldScaleDis);
                if (meshesToFracW == null) return CancelFracturing();

                //Fracture the meshes into pieces
                if (UpdateProgressBar("Fracturing meshes") == false) return CancelFracturing();
                List<FracMesh> partMeshesW = Gen_fractureMeshes(meshesToFracW, fractureCount, dynamicFractureCount, worldScaleDis, seed, true);
                if (partMeshesW == null) return CancelFracturing();

                //save orginal data (save as late as possible)
                if (UpdateProgressBar("Saving orginal objects") == false) return CancelFracturing();
                Gen_loadAndMaybeSaveOgData(true);

                //setup fracture renderer
                if (UpdateProgressBar("Creating renderer") == false) return CancelFracturing();
                Gen_setupFracRend(meshesToFracW[0], skinRendSource);

                //create fracObjects and add them to the destruction
                if (UpdateProgressBar("Creating destruction structure") == false) return CancelFracturing();
                CreateAndAddFObjectsFromFMeshes(partMeshesW);

                //save to save asset
#if UNITY_EDITOR
                shouldBeFractured = true;
#endif
                if (UpdateProgressBar("Saving") == false) return CancelFracturing();
                SaveOrLoadAsset(true);

                //log result when done, log when done
                if (fr_bones.Count > 500) Debug.LogWarning(transform.name + " has " + fr_bones.Count + " bones (skinnedMeshRenderers seems to have a limitation of ~500 bones before it breaks)");
                if (Mathf.Approximately(transform.lossyScale.x, transform.lossyScale.y) == false || Mathf.Approximately(transform.lossyScale.z, transform.lossyScale.y) == false) Debug.LogWarning(transform.name + " lossy scale XYZ should all be the same. If not stretching may accure when rotating parts");
                if (transform.TryGetComponent<Rigidbody>(out _) == true) Debug.LogWarning(transform.name + " has a rigidbody and it may cause issues. Its recommended to remove it and use the fracture physics options instead");
                Debug.Log("Fractured " + transform.name + " into " + partMeshesW.Count + " parts, total vertex count = " + partMeshesW.Sum(meshWG => meshWG.meshW.vertexCount));

                return true;
            }

            bool CancelFracturing()
            {
                Gen_loadAndMaybeSaveOgData(false);
                return false;
            }


            bool UpdateProgressBar(string message)
            {
#if UNITY_EDITOR
                if (Application.isPlaying == false)
                {
                    fracProgress += 1.0f / 12;

                    if (EditorUtility.DisplayCancelableProgressBar("Fracturing " + transform.name, message, fracProgress) == true)
                    {
                        Debug.Log("Canceled fracturing " + transform.name);
                        return false;
                    }
                }

                return true;
#endif
            }
        }

        public class FracMesh
        {
            public Mesh meshW;

            /// <summary>
            /// The mesh used to create meshW
            /// </summary>
            public FracSource sourceM;

            /// <summary>
            /// Main group id only
            /// </summary>
            public List<float> groupId;
        }

        public class FracSource
        {
            /// <summary>
            /// The mesh in worldSpace, dont forget to recalculate bounds after you transform mesh from local to world!
            /// </summary>
            public Mesh meshW;

            /// <summary>
            /// The mMats index each triangel in meshW uses
            /// </summary>
            public int[] trisSubMeshI;

            public List<Material> mMats;

            /// <summary>
            /// The renderer that used meshW
            /// </summary>
            public Renderer sRend;

            public List<float> mGroupId;
        }

        public class FracObject
        {
            /// <summary>
            /// The objects mesh in worldSpace
            /// </summary>
            public Mesh meshW;

            /// <summary>
            /// The material each subMesh in meshW has
            /// </summary>
            public List<Material> mMaterials;

            /// <summary>
            /// The collider of the object
            /// </summary>
            public Collider col;
            public List<float> groupId;
            public HashSet<float> groupLinks;
        }

        /// <summary>
        /// Uses groupIdInt as key and returns a groupIdsData index (if key exists)
        /// </summary>
        private Dictionary<int, int> groupIntIdToGroupIndex = new();

        /// <summary>
        /// The group data to use for stuff that does not have overrides
        /// </summary>
        //private GroupIdData groupDataDefualt = new();

        /// <summary>
        /// Contains all group ids that exists in the meshes to fracture, only assigned durring fracturing
        /// </summary>
        private List<float>[] md_verGroupIds = new List<float>[0];

        [System.Serializable]
        public class DestructionMaterial
        {
            public List<int> affectedGroupIndexes = new();
            public PhysicMaterial phyMat;
            public bool isKinematic = false;
            public byte objLayerDefualt = 0;
            public byte objLayerBroken = 0;
            public DesProperties desProps = new()
            {
                mass = 0.1f,
                stenght = 40.0f,
                falloff = 0.2f,
                chockResistance = 0.4f,
                stiffness = 0.7f,
                bendHardness = 0.2f,
                damageAccumulation = 0.5f
            };

            [System.Serializable]
            public struct DesProperties
            {
                public float mass;
                public float stenght;
                public float falloff;
                public float chockResistance;
                public float stiffness;
                public float bendHardness;

                /// <summary>
                /// How much less X can transport depending on how much force it has recieved at most. (actualTransportCapacity = transportCapacity - (maxForceRecieved * transportMaxDamage)) 
                /// </summary>
                public float damageAccumulation;
            }
        }

        [System.Serializable]
        private class DefualtDesMatOptions
        {
            public PhysicMaterial phyMat;
            public float mass = 0.1f;
            public byte objLayerBroken = 0;
            public float stenght = 40.0f;
            public float falloff = 0.2f;
            public float chockResistance = 0.4f;
            public float stiffness = 0.7f;
            public float bendHardness = 0.8f;
            public float damageAccumulation = 0.5f;
        }

        /// <summary>
        /// assigns groupDataDefualt and groupIntIdToGroupIndex from <defualtSettings> and groupDataOverrides+md_verGroupIds
        /// </summary>
        private void SetupGroupData()
        {
            //set defualt group data
            DestructionMaterial defualtGData = new()
            {
                affectedGroupIndexes = null,
                phyMat = defualtDestructionMaterial.phyMat,
                objLayerBroken = defualtDestructionMaterial.objLayerBroken,
                isKinematic = false,
                objLayerDefualt = (byte)gameObject.layer,
                desProps = new()
                {
                    mass = defualtDestructionMaterial.mass,
                    stiffness = defualtDestructionMaterial.stiffness,
                    bendHardness = defualtDestructionMaterial.bendHardness,
                    stenght = defualtDestructionMaterial.stenght,
                    falloff = defualtDestructionMaterial.falloff,
                    chockResistance = defualtDestructionMaterial.chockResistance,
                    damageAccumulation = defualtDestructionMaterial.damageAccumulation
                }
                
            };

            destructionMaterials.Insert(0, defualtGData);

            //assign groupIntIdToGroupIndex with proper keys and values
            groupIntIdToGroupIndex.Clear();

            for (int overrideI = 1; overrideI < destructionMaterials.Count; overrideI++)
            {
                if (destructionMaterials[overrideI].affectedGroupIndexes == null) continue;

                foreach (int groupI in destructionMaterials[overrideI].affectedGroupIndexes)
                {
                    if (groupI < 0 || groupI >= md_verGroupIds.Length) continue;

                    groupIntIdToGroupIndex.Add(FractureHelperFunc.Gd_getIntIdFromId(md_verGroupIds[groupI]), overrideI);
                }
            }
        }

        //fracRend mesh will be set from all fr_[] variabels when synced, they should only be modified by destructionSystem
        public MeshFilter fracFilter;
        public MeshRenderer fracRend;
        /// <summary>
        /// The desWeights index each vertex in fracRend.sharedmesh uses
        /// </summary>
        [System.NonSerialized] public List<int> fr_fracWeightsI;
        [System.NonSerialized] public List<Vector3> fr_verticesL;
        [System.NonSerialized] public List<Vector3> fr_normalsL;
        [System.NonSerialized] public List<int> fr_verToPartI;
        [System.NonSerialized] public List<Vector2> fr_uvs;
        [System.NonSerialized] public List<BoneWeight> fr_boneWeights;
        [System.NonSerialized] public List<BoneWeight> fr_boneWeightsSkin;
        [System.NonSerialized] public List<BoneWeight> fr_boneWeightsCurrent;
        [System.NonSerialized] public List<Material> fr_materials;
        [System.NonSerialized] public List<List<int>> fr_subTris;
        public List<Transform> fr_bones;

        /// <summary>
        /// fr_bones[partI + partBoneOffset] is the parts bone transform
        /// </summary>
        [SerializeField] private int partBoneOffset;
        [System.NonSerialized] public List<Matrix4x4> fr_bindPoses;

        /// <summary>
        /// All different weights a vertex in fracRend.sharedmesh can have
        /// </summary>
        [System.NonSerialized] public List<FracWeight> desWeights = new();

        /// <summary>
        /// Sets fracRend defualt values so its ready to get parts added to it, returns true if valid fracRend
        /// </summary>
        private bool Gen_setupFracRend(FracSource mainFracSource = null, SkinSourceData skinRendSource = null)
        {
            //return if no fracRend
            if (fracRend == null)
            {
                Debug.LogError(transform.name + " fracRend has not been created, was Gen_loadAndMaybeSaveOgData(true) called first?");
                return false;
            }

            //set renderer
            isRealSkinnedM = skinRendSource != null;
            fracFilter.sharedMesh = new();
            fracRend.sharedMaterials = new Material[0];

            //set meshData lists
            fr_verticesL = new();
            fr_verToPartI = new();
            fr_normalsL = new();
            fr_uvs = new();
            fr_boneWeights = new();
            fr_boneWeightsSkin = new();
            fr_boneWeightsCurrent = new();
            fr_materials = new();
            fr_subTris = new();
            fr_bones = new();
            fr_bindPoses = new();
            fr_fracWeightsI = new();

            //add data from source
            if (skinRendSource != null)
            {
                fr_bones.AddRange(skinRendSource.bones);
                fr_bindPoses.AddRange(skinRendSource.bindPoses);
            }

            jGTD_fracBoneTrans = new(fr_bones.ToArray());
            partBoneOffset = fr_bones.Count;

            SyncFracRendData();

            //create defualt parent
            Transform defualtParentTrans = null;
            if (mainFracSource != null)
            {
                if (skinRendSource != null) defualtParentTrans = skinRendSource.rootBone;
                else
                {
                    defualtParentTrans = mainFracSource.sRend.transform;
                    int loopCount;

                    for (loopCount = 0; loopCount < 10; loopCount++)
                    {
                        if ((defualtParentTrans.lossyScale - (Vector3.one * defualtParentTrans.lossyScale.x)).magnitude < 0.00001f
                            || defualtParentTrans == transform) break;
                        defualtParentTrans = defualtParentTrans.parent;
                    }
                  
                    if (loopCount != 0) Debug.LogWarning(mainFracSource.sRend.transform.name + " cant be used as base parent because it does not have a uniform lossy scale, " + defualtParentTrans.name + " will be used as base parent instead!");
                }
            }

            CreateNewParent(defualtParentTrans == transform ? null : defualtParentTrans);

            return true;
        }

        private bool wantToSyncFracRendData = false;

        /// <summary>
        /// Contains the index of the parts that will be recalculated when SyncFracRendData() is called
        /// </summary>
        private HashSet<int> syncFR_modifiedPartsI = new();

        /// <summary>
        /// Calls SyncFracRendData() once the next frame and if modifiedPartI >= 0 the given part will be recalculated
        /// </summary>
        private void RequestSyncFracRendData(int modifiedPartI = -1)
        {
            wantToSyncFracRendData = true;
            if (modifiedPartI >= 0) syncFR_modifiedPartsI.Add(modifiedPartI);
        }

        private ComputeBuffer buf_desWeights;
        private ComputeBuffer buf_boneBindsLToW;
        private ComputeBuffer buf_meshData;
        private ComputeBuffer buf_fr_boneWeightsCurrent;
        private ComputeBuffer buf_structs_posL;
        private ComputeBuffer buf_structs_posLPrev;
        private ComputeBuffer buf_structs_parentI;
        private GraphicsBuffer buf_verNors;
        private bool gpuIsReady = false;

        private static class ShaderIDs
        {
            public static int verNors = Shader.PropertyToID("verNors");
        }

        private struct MeshData
        {
            public Vector3 vertexL;
            public Vector3 normalL;
            public int fracWeightI;
            public int verToPartI;
        };

        /// <summary>
        /// Call to assign fracRend with data from fr_[] and sync with gpu (RequestSyncFracRendData() should be used instead to prevent SyncFracRendData() from being called many times a frame)
        /// </summary>
        private unsafe void SyncFracRendData()
        {
            gpuIsReady = false;
            wantToSyncFracRendData = false;
            float worldScaleDis = FracGlobalSettings.worldScale * 0.0001f;

            //set basics arrays
            if (des_deformedParts[0] == null)
            {
                des_deformedParts[0] = new();
                des_deformedParts[1] = new();
            }

            //read vertics and normals from gpu to keep deformed data
            if (buf_meshData != null && gpuIsReady == true)
            {
                MeshData[] tempData = new MeshData[buf_meshData.count];
                buf_meshData.GetData(tempData);//we may wanna replace this with async readback?

                for (int i = 0; i < tempData.Length; i++)
                {
                    fr_verticesL[i] = tempData[i].vertexL;//vertics and normals cant be removed so tempData can never be longer than fr_verticesL
                    fr_normalsL[i] = tempData[i].normalL;
                }
            }

            //set the renderer
            fracRend.SetSharedMaterials(fr_materials);
            fracFilter.sharedMesh.SetVertices(fr_verticesL);
            fracFilter.sharedMesh.SetNormals(fr_normalsL);
            fracFilter.sharedMesh.SetUVs(0, fr_uvs);
            FractureHelperFunc.SetListLenght(ref skinnedVerticsW, fr_verticesL.Count);

            //set rend submeshes
            fracFilter.sharedMesh.subMeshCount = fr_subTris.Count;

            for (int subI = 0; subI < fr_subTris.Count; subI++)
            {
                fracFilter.sharedMesh.SetTriangles(fr_subTris[subI], subI);
            }

            //update modified parts
            if (syncFR_modifiedPartsI.Count > 0)
            {
                SetModifiedParts();
            }

            //sync data with gpu
            SyncWithGpu();
            syncFR_modifiedPartsI.Clear();

            void SyncWithGpu()
            {
                //load compute shaders
                if (computeDestructionSolver == null || cpKernelId_ComputeSkinDef < 0)
                {

                    computeDestructionSolver = Instantiate(Resources.Load<ComputeShader>("ComputeDestructionSolver"));
                    if (computeDestructionSolver == null)
                    {
                        Debug.LogError("Expected destructionSolver compute shader to exist at path 'UltimateFracture/Resources/ComputeDestructionSolver.compute', have you deleted it?");
                        return;
                    }

                    cpKernelId_ComputeSkinDef = computeDestructionSolver.FindKernel("ComputeSkinDef");
                }

                //sync vertics, normals, verToPartI and fracWeightsI with gpu
                if (fr_verticesL.Count > 0 && fr_normalsL.Count > 0)
                {
                    buf_meshData = new ComputeBuffer(fr_verticesL.Count,
                        (sizeof(float) * 6) + (sizeof(int) * 2));

                    List<MeshData> newMD = new();
                    for (int i = 0; i < fr_verticesL.Count; i++)
                    {
                        newMD.Add(new MeshData()
                        {
                            fracWeightI = fr_fracWeightsI[i],
                            vertexL = fr_verticesL[i],
                            normalL = fr_normalsL[i],
                            verToPartI = fr_verToPartI[i],
                        });
                    }

                    buf_meshData.SetData(newMD);
                    computeDestructionSolver.SetBuffer(cpKernelId_ComputeSkinDef, "fr_meshData", buf_meshData);
                }

                //part related buffers only needs updating if parts has been modified
                if (desWeights != null && desWeights.Count > 0 && fr_boneWeightsCurrent != null && fr_boneWeightsCurrent.Count > 0
                    && (syncFR_modifiedPartsI.Count > 0 || buf_desWeights == null || buf_desWeights.IsValid() == false || buf_desWeights.count != desWeights.Count))
                {
                    //sync desWeights
                    buf_desWeights = new ComputeBuffer(desWeights.Count,
                        (sizeof(int) * FracGlobalSettings.maxDeformationBones) + (sizeof(float) * FracGlobalSettings.maxDeformationBones) + sizeof(int));

                    buf_desWeights.SetData(desWeights.ToArray());
                    computeDestructionSolver.SetBuffer(cpKernelId_ComputeSkinDef, "desWeights", buf_desWeights);

                    //sync boneWeights
                    buf_fr_boneWeightsCurrent = new ComputeBuffer(fr_boneWeightsCurrent.Count,
                        (sizeof(float) * 4) + (sizeof(int) * 4));

                    buf_fr_boneWeightsCurrent.SetData(fr_boneWeightsCurrent);
                    computeDestructionSolver.SetBuffer(cpKernelId_ComputeSkinDef, "fr_boneWeightsCurrent", buf_fr_boneWeightsCurrent);

                    if (buf_boneBindsLToW == null || buf_boneBindsLToW.IsValid() == false || buf_boneBindsLToW.count == 0)
                    {
                        GetTransformData_start();
                        GetTransformData_end();
                    }

                    computeDestructionSolver.SetBuffer(cpKernelId_ComputeSkinDef, "allFracBonesLToW", buf_boneBindsLToW);

                    //sync fracRend mesh vertics and normals for gpu write
                    Mesh mesh = fracFilter.sharedMesh;
                    fracRendDividedVerCount = Mathf.CeilToInt(fr_verticesL.Count / 128.0f);
                    computeDestructionSolver.SetInt("fracRendVerCount", fr_verticesL.Count);
                    mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;

                    mesh.SetVertexBufferParams(
                        vertexCount: mesh.vertexCount,
                        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3)
                    );

                    buf_verNors = mesh.GetVertexBuffer(index: 0);
                    computeDestructionSolver.SetBuffer(cpKernelId_ComputeSkinDef, ShaderIDs.verNors, buf_verNors);
                    fracFilter.sharedMesh = mesh;

                    //sync structs pos and parents
                    buf_structs_parentI = new ComputeBuffer(jCDW_job.partsParentI.Length,
                        sizeof(int));
                    buf_structs_posL = new ComputeBuffer(jCDW_job.structPosL.Length,
                        sizeof(float) * 3);
                    buf_structs_posLPrev = new ComputeBuffer(jCDW_job.structPosL.Length,
                        sizeof(float) * 3);

                    buf_structs_parentI.SetData(jCDW_job.partsParentI.AsArray());
                    //buf_structs_parentI.SetData(jCDW_job.partsParentI.ToArray());
                    buf_structs_posL.SetData(jCDW_job.structPosL.AsArray());
                    buf_structs_posLPrev.SetData(jCDW_job.structPosL.AsArray());
                    computeDestructionSolver.SetBuffer(cpKernelId_ComputeSkinDef, "structs_parentI", buf_structs_parentI);
                    computeDestructionSolver.SetBuffer(cpKernelId_ComputeSkinDef, "structs_posL", buf_structs_posL);
                    computeDestructionSolver.SetBuffer(cpKernelId_ComputeSkinDef, "structs_posLPrev", buf_structs_posLPrev);
                    computeDestructionSolver.SetInt("partBoneOffset", partBoneOffset);
                }

                gpuIsReady = true;
            }

            void SetModifiedParts()
            {
                //Set the desStruct weight for every vertex used by the modified parts
                //get world position of every struct
                GetTransformData_start();
                GetTransformData_end();

                Vector3[] structsWPos = new Vector3[allParts.Count];
                for (int partI = 0; partI < allParts.Count; partI++)
                {
                    structsWPos[partI] = GetStructWorldPosition(partI);
                }

                //get the weight for every vertex in every part
                HashSet<int> usedVers = new();
                List<List<int>> actuallPartVers = new();
                List<int> actuallPartVers_partI = new();
                Dictionary<int, FracWeight> newVersIFracWE = new();
                HashSet<int> skinnedVerParts = new();

                foreach (int partI in syncFR_modifiedPartsI)
                {
                    if (skinnedVerParts.Add(partI) == true) SkinPartVertics(partI);

                    usedVers.Clear();
                    HashSet<int> deAdded = new();

                    foreach (int vI in allParts[partI].partMeshVerts)
                    {
                        if (deAdded.Add(vI) == false) Debug.LogError("twice");
                        if (usedVers.Contains(vI) == true) continue;

                        newVersIFracWE.Add(vI, CreateFracWeightFromPartAndV(partI, vI));
                        actuallPartVers.Add(new() { vI });
                        actuallPartVers_partI.Add(partI);

                        //All vers in this part that share the ~same pos must always have the same weight 
                        foreach (int vII in allParts[partI].partMeshVerts)
                        {
                            if (vII == vI) continue;

                            if ((skinnedVerticsW[vI] - skinnedVerticsW[vII]).sqrMagnitude < worldScaleDis)
                            {
                                if (usedVers.Add(vII) == false)
                                {
                                    //Debug.LogError("How the fuck, " + vII + " " + ((skinnedVerticsW[vII] - skinnedVerticsW[vI]).sqrMagnitude < worldScaleDis) + " " +
                                    //    +vI + " " + partI + " " + newVersIFracWE[vII].GetHashCode() + " " + newVersIFracWE[vI].GetHashCode());

                                    FractureHelperFunc.Debug_drawBox(skinnedVerticsW[vI], 0.1f, Color.magenta, 10.0f);
                                    FractureHelperFunc.Debug_drawBox(skinnedVerticsW[vII], 0.1f, Color.red, 10.0f);
                                    continue;
                                }

                                actuallPartVers[^1].Add(vII);
                                newVersIFracWE.Add(vII, newVersIFracWE[vI]);
                            }
                        }
                    }
                }

                //Combine weights for vertics that share the same pos
                usedVers.Clear();
                int debugTotal = 0;

                for (int i = 0; i < actuallPartVers.Count; i++)
                {
                    //Get the vertics that share the same pos
                    HashSet<int> sameVers = new();
                    foreach (int partVI in actuallPartVers[i])
                    {
                        if (usedVers.Add(partVI) == false) continue;
                        sameVers.Add(partVI);
                    }

                    if (sameVers.Count == 0) continue; //Continue if all vers are already used

                    int vI = actuallPartVers[i][0];
                    int requiredFracWeI = -1;

                    FracStruct fStruct = jCDW_job.fStructs[actuallPartVers_partI[i]];
                    //foreach (int nearPart in allParts[actuallPartVers_partI[i]].neighbourStructs)
                    for (byte neI = 0; neI < fStruct.neighbourPartI_lenght; neI++)
                    {
                        short nearPart = fStruct.neighbourPartI[neI];
                        if (skinnedVerParts.Add(nearPart) == true) SkinPartVertics(nearPart);

                        foreach (int nearVI in allParts[nearPart].partMeshVerts)
                        {
                            //Loops once for all vertics in all neighbour parts
                            if (usedVers.Contains(nearVI) == true) continue;
                            if ((skinnedVerticsW[vI] - skinnedVerticsW[nearVI]).sqrMagnitude >= worldScaleDis) continue;
                            if (syncFR_modifiedPartsI.Contains(nearPart) == false)
                            {
                                if (requiredFracWeI < 0) requiredFracWeI = fr_fracWeightsI[nearVI];
                                else if (requiredFracWeI != fr_fracWeightsI[nearVI]) continue;
                            }

                            sameVers.Add(nearVI);
                            usedVers.Add(nearVI);
                        }
                    }

                    //Merge the weight of all vertices that share the ~same pos by
                    //adding all weights togehter and then normlilizing them
                    //add all weights togehter
                    //FracWeight newFW = new() { structsI = new(), weights = new() };
                    List<int> newStructsI = new();
                    List<float> newWeights = new();

                    Dictionary<int, int> structIToNewFWI = new();
                    float totalWeight = 0.0f;

                    foreach (int sameVI in sameVers)
                    {
                        if (newVersIFracWE.TryGetValue(sameVI, out FracWeight fracWE) == false) fracWE = desWeights[fr_fracWeightsI[sameVI]];

                        for (int wI = 0; wI < fracWE.stWe_lenght; wI++)
                        {
                            int sI = fracWE.structsI[wI];
                            float weight = fracWE.weights[wI];
                            totalWeight += weight;

                            if (structIToNewFWI.TryAdd(sI, newStructsI.Count) == true)
                            {
                                newStructsI.Add(sI);
                                newWeights.Add(weight);
                            }
                            else
                            {
                                newWeights[structIToNewFWI[sI]] += weight;
                            }
                        }
                    }

                    //normilize the weights
                    for (int wI = 0; wI < newWeights.Count; wI++)
                    {
                        newWeights[wI] /= totalWeight;
                    }

                    //assign updated weights to desWeights
                    if (requiredFracWeI < 0)
                    {
                        requiredFracWeI = desWeights.Count;
                        //desWeights.Add(new() { structsI = newStructsI.ToArray(), weights = newWeights.ToArray() });
                        desWeights.Add(FracWeight.New(newStructsI, newWeights));
                    }

                    foreach (int sameVI in sameVers)
                    {
                        debugTotal++;
                        fr_fracWeightsI[sameVI] = requiredFracWeI;
                    }
                }

                FracWeight CreateFracWeightFromPartAndV(int partI, int vI)
                {
                    //reset weights
                    //FracWeight newFracWE = new() { structsI = new(), weights = new() };
                    List<int> newStructsI = new();
                    List<float> newWeights = new();
                    float totalDis = 0.0f;

                    //add weights from each nearby struct (Get weights from distance to ver from struct)
                    AddWeightFromStruct(partI);

                    FracStruct fStruct = jCDW_job.fStructs[partI];
                    //foreach (int nearPI in allParts[partI].neighbourStructs)
                    for (byte neI = 0; neI < fStruct.neighbourPartI_lenght; neI++)
                    {
                        if (fStruct.neighbourPartI[neI] < 0)
                        {
                            Debug.Log(partI);
                        }
                        AddWeightFromStruct(fStruct.neighbourPartI[neI]);
                    }

                    //Nomilize weights so total weight = 1.0f
                    float totalWeight = 0.0f;//currently only used for debug
                    for (int wI = 0; wI < newWeights.Count; wI++)
                    {
                        newWeights[wI] /= totalDis;
                        totalWeight += newWeights[wI];
                    }

                    if (Mathf.Approximately(totalWeight, 1.0f) == false) Debug.LogError(totalWeight);

                    //return new() { structsI = newStructsI.ToArray(), weights = newWeights.ToArray() };
                    return FracWeight.New(newStructsI, newWeights);

                    void AddWeightFromStruct(int structI)
                    {
                        if (skinnedVerParts.Add(structI) == true) SkinPartVertics(structI);

                        newStructsI.Add(structI);
                        newWeights.Add((structsWPos[structI] - skinnedVerticsW[vI]).magnitude);
                        totalDis += newWeights[^1];
                    }
                }
            }
        }

        /// <summary>
        /// Creates a fracObject from a fracMesh and returns the new fracObject
        /// </summary>
        public FracObject CreateFObjectFromFMesh(FracMesh fMesh, Dictionary<Material, Material> matToInsideMat = null)
        {
            //create the fObject
            FracObject fObj = new();
            Transform pTrans = new GameObject("Part(unusedPart)_" + transform.name).transform;
            pTrans.gameObject.layer = fMesh.sourceM.sRend.gameObject.layer;
            pTrans.gameObject.tag = fMesh.sourceM.sRend.gameObject.tag;
            pTrans.position = FractureHelperFunc.GetGeometricCenterOfPositions(fMesh.meshW.vertices);
            fObj.meshW = fMesh.meshW;
            fObj.col = Gen_createPartCollider(pTrans, fObj.meshW, GetDesMatFromIntId(FractureHelperFunc.Gd_getIntIdFromId(fMesh.groupId)).phyMat);

            //get inside vers before we modify fObj.mesh
            HashSet<int> insideVers = FractureHelperFunc.GetAllVersInSubMesh(fObj.meshW, 1);

            //get fObject materials
            FractureHelperFunc.GetMostSimilarTris(fObj.meshW, fMesh.sourceM.meshW, out int[] nVersBestSVer, out int[] nTrisBestSTri, FracGlobalSettings.worldScale);
            List<int> nSubSSubI = FractureHelperFunc.SetMeshFromOther(ref fObj.meshW, fMesh.sourceM.meshW, nVersBestSVer, nTrisBestSTri, fMesh.sourceM.trisSubMeshI, false);

            fObj.mMaterials = new();
            for (int nsI = 0; nsI < nSubSSubI.Count; nsI++)
            {
                fObj.mMaterials.Add(fMesh.sourceM.mMats[nSubSSubI[nsI]]);
            }

            if (matToInsideMat != null) FractureHelperFunc.SetMeshInsideMats(ref fObj.meshW, ref fObj.mMaterials, insideVers, matToInsideMat); ;

            //get fObject id and links
            Color[] sColors = fMesh.sourceM.meshW.colors;

            if (sColors.Length > 0)
            {
                HashSet<Color> usedSColors = new();

                foreach (int svI in nVersBestSVer)
                {
                    usedSColors.Add(sColors[svI]);
                }

                HashSet<float> gLinks = new();
                FractureHelperFunc.GetLinksFromColors(usedSColors, ref gLinks);
                fObj.groupLinks = gLinks;
            }
            else fObj.groupLinks = new();

            fObj.groupId = fMesh.groupId;

            //return the new fObject
            return fObj;
        }

        /// <summary>
        /// Creates a FracObject from a gameobject
        /// </summary>
        public FracObject CreateFObjectFromObject(GameObject obj, List<float>[] groupIds = null)
        {
            FracObject fObj = new();
            Debug.LogError("Not implemented");
            return fObj;
        }

        /// <summary>
        /// Returns the groupData for the given intId (Use FractureHelperFunc.Gd_getIntIdFromId() to get intId)
        /// </summary>
        private DestructionMaterial GetDesMatFromIntId(int intId)
        {
            if (groupIntIdToGroupIndex.TryGetValue(intId, out int groupI) == true) return destructionMaterials[groupI];
            //return groupDataDefualt;
            return destructionMaterials[0];
        }

        private int GetDesMatIndexFromIntId(int intId)
        {
            if (groupIntIdToGroupIndex.TryGetValue(intId, out int groupI) == true) return groupI;
            return 0;
        }

        /// <summary>
        /// Adds the given fracObject to the destruction system, returns true if successfull
        /// </summary>
        /// <param name="fObj">All variabels inside this class must be set to a valid value</param>
        /// <param name="newPartParentI">The parent the newly added object should have. If 0 or -1 it will use the same parent as a neighbour,
        /// if no neighbour exists, -1 will create new parent and == 0 will use defualt parent instead</param>
        public bool AddFObjectToDestruction(FracObject fObj, int newPartParentI)
        {
            //return if invalid
            if (fracRend == null)
            {
                Debug.LogError("Cant add FracObject to " + transform.name + " because it has not been fractured yet");
                return false;
            }

            //get global data
            DestructionMaterial partGroupD;
            //if (fObj.meshW.bounds.extents.x > partMaxExtent) partMaxExtent = fObj.meshW.bounds.extents.x;
            //if (fObj.meshW.bounds.extents.y > partMaxExtent) partMaxExtent = fObj.meshW.bounds.extents.y;
            //if (fObj.meshW.bounds.extents.z > partMaxExtent) partMaxExtent = fObj.meshW.bounds.extents.z;

            //create fracPart and assign variabels
            FracPart newPart = new()
            {
                groupId = fObj.groupId,
                groupLinks = fObj.groupLinks,
                //col = fObj.col,
                //trans = fObj.col.transform,
                partMeshVerts = new(),
                //neighbourStructs = new()
            };

            Transform newTrans = fObj.col.transform;
            short newPartI = (short)allParts.Count;
            newTrans.name = newTrans.name.Replace("unusedPart", newPartI.ToString());

            //make sure everything is valid
            if (newPartI != jCDW_job.structPosL.Length)
                Debug.LogError(transform.name + " destructionStructure or parts is invalid! (part = " + newPartI + " struct = " + jCDW_job.structPosL.Length + ")");

            //Make sure the gameobject is in the same scene as source
            if (gameObject.scene != fObj.col.gameObject.scene)
            {
#if UNITY_EDITOR
                if (Application.isPlaying == false) EditorSceneManager.MoveGameObjectToScene(fObj.col.gameObject, gameObject.scene);
                else
#endif    
                    SceneManager.MoveGameObjectToScene(fObj.col.gameObject, gameObject.scene);
            }

            allParts.Add(newPart);
            fr_bones.Add(newTrans);
            saved_allPartsCol.Add(fObj.col);

            //create new fracStruct, the fracStruct will be added to jCDW_job.fStructs later in SetPartNeighboursAndConnections();
            FracStruct newStruct = new()
            {
                desMatI = 0,
                neighbourPartI_lenght = 0,
                maxTransportUsed = 0.0f
            };

            //get part connection
            int partMainNeighbour = -1;
            SetPartNeighboursAndConnections();


            //add part mesh
            Matrix4x4 rendWtoL = fracRend.transform.worldToLocalMatrix;
            int newBoneI = fr_bones.Count - 1;
            int partVerCount = fObj.meshW.vertexCount;
            int newVerOffset = fr_verticesL.Count;
            int newVerEndI = newVerOffset + partVerCount;

            for (int nvI = newVerOffset; nvI < newVerEndI; nvI++)
            {
                newPart.partMeshVerts.Add(nvI);
            }

            fr_bindPoses.Add(fObj.col.transform.worldToLocalMatrix * fracRend.transform.localToWorldMatrix);
            jCDW_job.structPosL.Add((fr_bones[newPartI + partBoneOffset].localToWorldMatrix * fr_bindPoses[newPartI + partBoneOffset]).inverse.MultiplyPoint3x4(GetPartWorldPosition(newPartI)));
            fr_verticesL.AddRange(FractureHelperFunc.ConvertPositionsWithMatrix(fObj.meshW.vertices, rendWtoL));
            fr_normalsL.AddRange(FractureHelperFunc.ConvertDirectionsWithMatrix(fObj.meshW.normals, rendWtoL));
            fr_verToPartI.AddRange(Enumerable.Repeat((int)newPartI, fObj.meshW.vertexCount));
            fr_uvs.AddRange(fObj.meshW.uv);
            fr_fracWeightsI.AddRange(new int[partVerCount]); //fr_fracWeights will be assigned later in SyncFracRendData
                                                             //but we still want to add here to prevent potential out of bounds error
            if (jGTD_fracBoneTrans.isCreated == true) jGTD_fracBoneTrans.Add(fr_bones[^1]);

            //add mesh submeshes+mats+tris
            for (int sI = 0; sI < fObj.mMaterials.Count; sI++)
            {
                //get what fracSubmesh uses this material, or create new submesh layer if material does not exists in frac
                int newSubI = -1;

                for (int nsI = 0; nsI < fr_materials.Count; nsI++)
                {
                    if (fr_materials[nsI] != fObj.mMaterials[sI]) continue;

                    newSubI = nsI;
                    break;
                }

                if (newSubI < 0)
                {
                    newSubI = fr_materials.Count;
                    fr_materials.Add(fObj.mMaterials[sI]);
                    fr_subTris.Add(new());
                }

                //add the tris to the correct submesh
                foreach (int vI in fObj.meshW.GetTriangles(sI))
                {
                    fr_subTris[newSubI].Add(newVerOffset + vI);
                }
            }

            //add mesh boneweights
            if (isRealSkinnedM == true)
            {
                BoneWeight[] boneWes = fObj.meshW.boneWeights;

                if (boneWes.Length != partVerCount)
                {
                    BoneWeight backupBoneWe;

                    if (partMainNeighbour < 0) backupBoneWe = new() { boneIndex0 = 0, weight0 = 1.0f };
                    else backupBoneWe = fr_boneWeightsSkin[allParts[partMainNeighbour].partMeshVerts[0]];

                    for (int i = 0; i < partVerCount; i++)
                    {
                        fr_boneWeightsSkin.Add(backupBoneWe);
                    }
                }
                else
                {
                    foreach (BoneWeight boneWe in boneWes)
                    {
                        //as long as source is the same the bone indexes will be correct
                        fr_boneWeightsSkin.Add(boneWe);
                    }
                }
            }

            BoneWeight newBoneWe = new() { boneIndex0 = newBoneI, weight0 = 1.0f };

            for (int i = 0; i < partVerCount; i++)
            {
                fr_boneWeights.Add(newBoneWe);
                fr_boneWeightsCurrent.Add(isRealSkinnedM == true ? fr_boneWeightsSkin[fr_boneWeightsCurrent.Count] : newBoneWe);
            }

            RequestSyncFracRendData(newPartI);

            //add part to global handler
#if UNITY_EDITOR
            if (Application.isPlaying == true)
#endif           //Always run if not in editor
                globalHandler.OnAddFracPart(this, newPartI);
            return true;

            unsafe void SetPartNeighboursAndConnections()
            {
                //Get all nearby colliders
                bool partIsKin = GetNearbyFracColliders(fObj.col, generationQuality, out List<short> nearPartIndexes, false);

                //get the groupData the part should use
                if (newPart.groupId == null && nearPartIndexes.Count > 0) newPart.groupId = allParts[nearPartIndexes[0]].groupId;
                newPart.groupIdInt = FractureHelperFunc.Gd_getIntIdFromId(newPart.groupId);
                newStruct.desMatI = GetDesMatIndexFromIntId(newPart.groupIdInt);
                jCDW_job.fStructs.Add(newStruct);
                partGroupD = GetDesMatFromIntId(newPart.groupIdInt);

                //set part parent
                jCDW_job.partsParentI.Add(-6969);
                if (newPartParentI <= 0 && nearPartIndexes.Count > 0) newPartParentI = jCDW_job.partsParentI[nearPartIndexes[0]];
                if (newPartParentI < 0) newPartParentI = CreateNewParent(null);
                SetPartParent(newPartI, newPartParentI);

                //add part to kinematic list if needed
                if ((partIsKin == true && (FracGlobalSettings.recalculateKinematicPartsOnLoad == 0 || (FracGlobalSettings.recalculateKinematicPartsOnLoad == 1 && GetFracturePrefabType() == 0))) 
                    || partGroupD.isKinematic == true) SetPartKinematicStatus(newPartI, true);

                //get what nearPartIndexes is actually valid neighbours and create structure for the new part
                //jCDW_job.structPosL.Add(fr_bones[newPartI + partBoneOffset].worldToLocalMatrix.MultiplyPoint(GetPartWorldPosition(newPartI)));
                foreach (short nearPartI in nearPartIndexes)
                {
                    //ignore if this neighbour part is invalid
                    if (jCDW_job.partsParentI[newPartI] != jCDW_job.partsParentI[nearPartI]
                        || FractureHelperFunc.Gd_isPartLinkedWithPart(newPart, allParts[nearPartI]) == false) continue;

                    //add neighbours to newPart struct and add newPart to neighbours
                    FracStruct fStruct = jCDW_job.fStructs[nearPartI];
#if !FRAC_NO_WARNINGS
                    if (fStruct.neighbourPartI_lenght >= FracGlobalSettings.maxPartNeighbourCount)
                    {
                        Debug.LogWarning("part index " + nearPartI + " cannot have more than " + FracGlobalSettings.maxPartNeighbourCount + " neighbours, data will be lost!");
                        continue;
                    }
#endif

                    fStruct.neighbourPartI[fStruct.neighbourPartI_lenght] = newPartI;
                    fStruct.neighbourPartI_lenght++;
                    jCDW_job.fStructs[nearPartI] = fStruct;

                    fStruct = jCDW_job.fStructs[newPartI];
#if !FRAC_NO_WARNINGS
                    if (fStruct.neighbourPartI_lenght >= FracGlobalSettings.maxPartNeighbourCount)
                    {
                        Debug.LogWarning("part index " + newPartI + " cannot have more than " + FracGlobalSettings.maxPartNeighbourCount + " neighbours, data will be lost!");
                        continue;
                    }
#endif

                    fStruct.neighbourPartI[fStruct.neighbourPartI_lenght] = nearPartI;
                    fStruct.neighbourPartI_lenght++;
                    jCDW_job.fStructs[newPartI] = fStruct;

                    partMainNeighbour = nearPartI;
                }
            }
        }

        /// <summary>
        /// Sets the given part kinematicStatus and updates parents
        /// </summary>
        public void SetPartKinematicStatus(int partI, bool newKinematicStatus)
        {
            //return if already has the given kinematicStatus
            if (jCDW_job.kinematicPartIndexes.Contains(partI) == newKinematicStatus) return;

            //add/remove from kinematic parts list
            if (newKinematicStatus == true) jCDW_job.kinematicPartIndexes.Add(partI);
            else jCDW_job.kinematicPartIndexes.Remove(partI);

            //update the parent the part uses
            int parentI = jCDW_job.partsParentI[partI];

            if (parentI >= 0)
            {
                if (newKinematicStatus == false) allParents[parentI].parentKinematic--;
                else allParents[parentI].parentKinematic++;

                MarkParentAsModified(parentI);
            }
        }

        /// <summary>
        /// Creates FracObjects from all fMeshesW and adds them to the destruction system
        /// </summary>
        private void CreateAndAddFObjectsFromFMeshes(List<FracMesh> fMeshesW, short newPartsParentI = 0)
        {
            Dictionary<Material, Material> matToInsideMat = new();
            HashSet<Material> testedMats = new();

            for (int fracI = 0; fracI < fMeshesW.Count; fracI++)
            {
                //get inside materials
                foreach (Material mat in fMeshesW[fracI].sourceM.mMats)
                {
                    if (testedMats.Add(mat) == false) continue;
                    Material insideMat = TryGetInsideMatFromMat(mat);

                    if (insideMat == null) continue;
                    matToInsideMat.TryAdd(mat, insideMat);
                }

                //create fObject
                FracObject newFO = CreateFObjectFromFMesh(fMeshesW[fracI], matToInsideMat);
                AddFObjectToDestruction(newFO, newPartsParentI);
            }
        }

        /// <summary>
        /// Sets the given parts parent to newParentI, if -1 the part will become lose
        /// </summary>
        private void SetPartParent(short partI, int newParentI, Vector3 losePartVelocity = default)
        {
            int partParentI = jCDW_job.partsParentI[partI];
            if (partParentI == newParentI) return;

            //get part groupData
            int partBoneI = partI + partBoneOffset;
            DestructionMaterial partDesMat = GetDesMatFromIntId(allParts[partI].groupIdInt);

            //remove part from previous parent
            if (partParentI >= 0)
            {
                jCDW_job.parentPartCount[partParentI]--;
                allParents[partParentI].partIndexes.Remove(partI);
                allParents[partParentI].parentMass -= partDesMat.desProps.mass;
                allParents[partParentI].totalStiffness -= partDesMat.desProps.stiffness;
                allParents[partParentI].totalTransportCoEfficiency -= partDesMat.desProps.falloff;
                if (jCDW_job.kinematicPartIndexes.Contains(partI) == true) allParents[partParentI].parentKinematic--;

                MarkParentAsModified(partParentI);
            }
            else FromNoParentToParent();

            jCDW_job.partsParentI[partI] = newParentI;

            //if want to remove parent
            if (newParentI < 0)
            {
                FromParentToNoParent();
                return;
            }

            //if want to change parent
            fr_bones[partBoneI].SetParent(newParentI > 0 || partsDefualtData.Count == 0 ? allParents[newParentI].parentTrans : partsDefualtData[partI].defParent);
            jCDW_job.parentPartCount[newParentI]++;
            allParents[newParentI].partIndexes.Add(partI);
            allParents[newParentI].parentMass += partDesMat.desProps.mass;
            allParents[newParentI].totalStiffness += partDesMat.desProps.stiffness;
            allParents[newParentI].totalTransportCoEfficiency += partDesMat.desProps.falloff;
            if (jCDW_job.kinematicPartIndexes.Contains(partI) == true) allParents[newParentI].parentKinematic++;
            MarkParentAsModified(newParentI);

            void FromParentToNoParent()
            {
                //set transform
                fr_bones[partBoneI].SetParent(transform);
                fr_bones[partBoneI].gameObject.layer = partDesMat.objLayerBroken;
                saved_allPartsCol[partI].hasModifiableContacts = false;

                //set physics
                //add and set rigidbody
                Rigidbody newRb = fr_bones[partBoneI].gameObject.AddComponent<Rigidbody>();

                newRb.mass = partDesMat.desProps.mass * phyPartsOptions.massMultiplier;
                newRb.interpolation = phyPartsOptions.interpolate;//Do we really need these properties to be configurable?
                newRb.drag = phyPartsOptions.drag;
                newRb.angularDrag = phyPartsOptions.angularDrag;
                newRb.useGravity = phyPartsOptions.useGravity;
                newRb.velocity = losePartVelocity;
            }

            void FromNoParentToParent()
            {
                //note that this also runs once for newly created objects
                //set transform
                fr_bones[partBoneI].gameObject.layer = partDesMat.objLayerDefualt;
                saved_allPartsCol[partI].hasModifiableContacts = true;
                //remove rigidbody
                //Destroy(allParts[partI].col.attachedRigidbody);
            }
        }

        /// <summary>
        /// Creates a new parent and returns its index (transToUse will be used as parent if it aint null)
        /// </summary>
        public short CreateNewParent(Transform transToUse = null)
        {
            //if empty&&valid parent exists, reuse it since we are not allowed to destroy unused parents.
            short newParentI = -1;

            if (transToUse == null)
            {
                for (short parentI = 1; parentI < allParents.Count; parentI++)
                {
                    if (allParents[parentI].partIndexes.Count == 0 && parentsThatNeedsUpdating.Contains(parentI) == false
                        && allParents[parentI].parentTrans != null && allParents[parentI].parentTrans.parent == transform
                        && (allParents[parentI].parentRb != null || phyMainOptions.mainPhysicsType == OptMainPhysicsType.alwaysKinematic))
                    {
                        newParentI = parentI;
                        allParents[newParentI].parentTrans.gameObject.SetActive(true);
                    }
                }
            }

            //if empty&&valid parent did not exist, create new parent object
            if (newParentI < 0)
            {
                newParentI = (short)allParents.Count;
                Rigidbody parentRb = null;

                if (transToUse == null)
                {
                    transToUse = new GameObject("Parent(" + newParentI + ")_" + transform.name).transform;
                    transToUse.gameObject.layer = gameObject.layer;
                    transToUse.gameObject.tag = gameObject.tag;
                    transToUse.position = transform.position;//just to make orgin somewhat relevant
                    transToUse.SetParent(transform);
                    if ((newParentI != 0 || phyMainOptions.mainPhysicsType != OptMainPhysicsType.orginalIsKinematic)
                        && phyMainOptions.mainPhysicsType != OptMainPhysicsType.alwaysKinematic) parentRb = transToUse.gameObject.AddComponent<Rigidbody>();
                }
                else parentRb = transToUse.GetComponent<Rigidbody>();

                allParents.Add(new()
                {
                    parentTrans = transToUse,
                    parentRb = parentRb,
                    partIndexes = new(),
                    parentMass = 0.0f
                });

                jCDW_job.parentPartCount.Add(new());
            }

            //set new parent defualt properties
            MarkParentAsModified(newParentI);

            return newParentI;
        }

        /// <summary>
        /// Notices the system that the given parent has been modified and needs updating
        /// </summary>
        private void MarkParentAsModified(int parentI)
        {
            parentsThatNeedsUpdating.Add(parentI);
        }

        /// <summary>
        /// Updates the given parent (Better to call MarkParentAsModified() as it will call this function the next frame, to prevent this from running many times a frame)
        /// </summary>
        private void UpdateParentData(int parentI)
        {
            //disable parent if no children, we cant destroy it since that would mean I will have to update every single parent index
            if (allParents[parentI].partIndexes.Count == 0)
            {
                allParents[parentI].parentTrans.gameObject.SetActive(false);
                return;//why bother updating other stuff if its disabled
            }

            //if main parent should be kinematic make sure it always is
            if (((phyMainOptions.mainPhysicsType == OptMainPhysicsType.orginalIsKinematic && parentI == 0)
                || phyMainOptions.mainPhysicsType == OptMainPhysicsType.alwaysKinematic) && allParents[parentI].parentKinematic <= 0) allParents[parentI].parentKinematic = 1;

            //update parent rigidbody
            Rigidbody parentRb = allParents[parentI].parentRb;
            if (parentRb != null)
            {
                float newRbMass = allParents[parentI].parentMass * phyPartsOptions.massMultiplier
                    * (allParents[parentI].partIndexes.Count * phyMainOptions.massMultiplier >= 1.0f ? phyMainOptions.massMultiplier : 1.0f);
                parentRb.mass = newRbMass;
                parentRb.isKinematic = allParents[parentI].parentKinematic > 0;

                //globalHandler.OnAddOrUpdateRb(parentRb, newRbMass);
                globalHandler.OnAddOrUpdateRb(parentRb, allParents[parentI].parentMass);
            }
        }

        /// <summary>
        /// Returns the center world position of the given part (center of the part collider)
        /// </summary>
        private Vector3 GetPartWorldPosition(int partI)
        {
            return fr_bones[partI + partBoneOffset].position;
        }

        private Vector3 GetStructWorldPosition(int structI)
        {
            return jCDW_job.boneBindsLToW[structI + partBoneOffset].MultiplyPoint3x4(jCDW_job.structPosL[structI]);
            //return fr_bones[structI + partBoneOffset].localToWorldMatrix.MultiplyPoint(jCDW_job.structPosL[structI]);
        }

        /// <summary>
        /// Returns true if we should continue with the fracture process
        /// </summary>
        /// <returns></returns>
        private bool Gen_checkIfContinueWithFrac(out bool didFracOther, bool isPrefabAsset)
        {
#if UNITY_EDITOR
            //set if should always count as prefab asset
            eOnly_isPrefabAsset = isPrefabAsset;

            //if prefab instance, fracture on the prefab asset
            if (fracPrefabType == 1)
            {
                didFracOther = true;
                bool didFindFrac = false;
                string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
                PrefabUtility.ApplyObjectOverride(this, prefabPath, InteractionMode.AutomatedAction);

                using (var editingScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
                {
                    //This script properties must always be synced
                    foreach (FractureThis fracT in editingScope.prefabContentsRoot.GetComponentsInChildren<FractureThis>())
                    {
                        if (didFracOther == true) didFracOther = fracT.Gen_fractureObject(true, false);
                        else fracT.Gen_fractureObject(true, false);

                        didFindFrac = true;
                    }
                }

                if (didFindFrac == false) Debug.LogError(transform.name + " prefab asset does not contain the fracture script (Have you applied overrides?)");

                return false;
            }
#endif

            //return if required stuff is missing
            VerifyGlobalHandler();
            didFracOther = false;

            if (saveAsset == null && (globalHandler == null || globalHandler.TryCreateTempSaveAsset(this) == false))
            {
                Debug.LogError("Cannot fracture " + transform.name + " because a saveAsset has not been assigned to it");
                return false;
            }

            if (gameObject.GetComponentsInParent<FractureThis>().Length > 1 || gameObject.GetComponentsInChildren<FractureThis>().Length > 1)
            {
                Debug.LogError("Cannot fracture " + transform.name + " because there is another fracture script in any of its parents or children");
                return false;
            }

            return true;
        }

        private Collider Gen_createPartCollider(Transform partTrans, Mesh partColMeshW, PhysicMaterial phyMat)
        {
            //This is the only place we add new colliders to the parts in
            //(We do also add colliders in the copyColliders function but since it copies all collider properties it does not really matter)
            partColMeshW = FractureHelperFunc.MergeVerticesInMesh(Instantiate(partColMeshW));
            //partColMesh = FractureHelperFunc.MakeMeshConvex(partColMesh, true);
            Collider newCol;

            if (colliderType == ColliderType.mesh)
            {
                //mesh
                MeshCollider mCol = partTrans.GetOrAddComponent<MeshCollider>();
                mCol.convex = true;
                mCol.sharedMesh = new();
                newCol = mCol;
            }
            else if (colliderType == ColliderType.box)
            {
                //box
                BoxCollider bCol = partTrans.GetOrAddComponent<BoxCollider>();
                newCol = bCol;
            }
            else if (colliderType == ColliderType.capsule)
            {
                //capsule
                CapsuleCollider cCol = partTrans.GetOrAddComponent<CapsuleCollider>();
                newCol = cCol;
            }
            else
            {
                //sphere
                SphereCollider sCol = partTrans.GetOrAddComponent<SphereCollider>();
                newCol = sCol;
            }

            Vector3[] partWVers = partColMeshW.vertices;

            partTrans.position = FractureHelperFunc.GetGeometricCenterOfPositions(partWVers);

            FractureHelperFunc.SetColliderFromFromPoints(
                newCol,
                FractureHelperFunc.ConvertPositionsWithMatrix(partWVers, partTrans.worldToLocalMatrix), ref partMaxExtent);

            newCol.sharedMaterial = phyMat;
            newCol.hasModifiableContacts = true; //This must always be true for all fracture colliders
            return newCol;
        }


        private bool mustConfirmHighCount = true;

        /// <summary>
        /// Returns all mesh chunks that was generated from the meshesToFracture list
        /// </summary>
        private List<FracMesh> Gen_fractureMeshes(List<FracSource> meshesToFrac, int totalChunkCount, bool dynamicChunkCount, float worldScaleDis = 0.0001f, int seed = -1, bool useMeshBounds = false)
        {
            //prefracture
            if (saveState != null && saveState.preS_fracedMeshes != null) return new(saveState.preS_fracedMeshes.ToFracMesh());

            //get random seed
            int nextOgMeshId = 0;

            //get per mesh scale, so each mesh to frac get ~equally sized chunks
            Mesh[] meshes = meshesToFrac.Select(meshData => meshData.meshW).ToArray();
            List<float> meshScales = FractureHelperFunc.GetPerMeshScale(meshes, useMeshBounds);
            Bounds meshBounds = FractureHelperFunc.GetCompositeMeshBounds(meshes);

            if (dynamicChunkCount == true) totalChunkCount = Mathf.CeilToInt(totalChunkCount * FractureHelperFunc.GetBoundingBoxVolume(meshBounds));

#if UNITY_EDITOR
            if (mustConfirmHighCount == true && totalChunkCount > 500 && Application.isPlaying == false)
            {
                mustConfirmHighCount = false;
                Debug.LogError("You are trying to fracture a mesh into ~" + totalChunkCount + " parts, thats a lot (Fracture again to fracture anyway)");
                return null;
            }
            else if (totalChunkCount < 500) mustConfirmHighCount = true;
#endif

            //fracture the meshes into chunks
            List<FracMesh> fracedMeshes = new();

            for (int i = 0; i < meshesToFrac.Count; i++)
            {
                //meshesToFrac[i].meshW = FractureHelperFunc.MergeSubMeshes(meshesToFrac[i].meshW);

                Gen_fractureMesh(meshesToFrac[i], ref fracedMeshes, Mathf.RoundToInt(totalChunkCount * meshScales[i]));

                nextOgMeshId++;
            }

            //return the result
            if (saveState != null)
            {
                FractureSavedState.PreS_fracedMeshesData savedMeshData = new();
                savedMeshData.FromFracMesh(fracedMeshes);

                saveState.SavePrefracture(null, savedMeshData);
            }

            return fracedMeshes;

            void Gen_fractureMesh(FracSource meshToFrac, ref List<FracMesh> newMeshes, int chunkCount)
            {
                //fractures the given mesh into pieces and adds the new pieces to the newMeshes list
                if (chunkCount <= 1)
                {
                    newMeshes.Add(new() { groupId = meshToFrac.mGroupId, meshW = meshToFrac.meshW, sourceM = meshToFrac });
                    return;
                }

                //setup nvBlast
                if (seed >= 0) NvBlastExtUnity.setSeed(seed);
                Mesh meshToF = FractureHelperFunc.MergeSubMeshes(meshToFrac.meshW);

                var nvMesh = new NvMesh(
                    meshToF.vertices,
                    meshToF.normals,
                    meshToF.uv,
                    meshToF.vertexCount,
                    meshToF.GetIndices(0),
                    (int)meshToF.GetIndexCount(0)
                );

                byte loopMaxAttempts = FracGlobalSettings.maxFractureAttempts;
                bool meshIsValid = false;
                bool hadInvalidChunkPart = false;
                List<Mesh> newMeshesTemp = new();

                while (loopMaxAttempts > 0)
                {
                    if (seed < 0)
                    {
                        //seed = UnityEngine.Random.Range(0, int.MaxValue);
                        NvBlastExtUnity.setSeed(UnityEngine.Random.Range(0, int.MaxValue));
                    }

                    loopMaxAttempts--;
                    meshIsValid = true;
                    newMeshesTemp.Clear();

                    //execute nvBlast
                    var fractureTool = new NvFractureTool();
                    fractureTool.setRemoveIslands(false);
                    fractureTool.setSourceMesh(nvMesh);

                    if (randomness >= 1.0f)
                    {
                        var sites = new NvVoronoiSitesGenerator(nvMesh);
                        sites.uniformlyGenerateSitesInMesh(chunkCount);
                        fractureTool.voronoiFracturing(0, sites);
                    }
                    else
                    {
                        // Calculate the volume of the mesh bounds
                        float totalVolume = meshBounds.size.x * meshBounds.size.y * meshBounds.size.z;

                        // Calculate the approximate volume of each resulting chunk
                        float chunkVolume = totalVolume / (chunkCount / 3.0f);

                        // Calculate the number of slices needed in each axis
                        int slicesX = Mathf.FloorToInt(meshBounds.size.x / Mathf.Pow(chunkVolume, 1f / 3f));
                        int slicesY = Mathf.FloorToInt(meshBounds.size.y / Mathf.Pow(chunkVolume, 1f / 3f));
                        int slicesZ = Mathf.FloorToInt(meshBounds.size.z / Mathf.Pow(chunkVolume, 1f / 3f));

                        fractureTool.slicing(0, new() { slices = new Vector3Int(slicesX, slicesY, slicesZ), angle_variations = randomness * 1.1f, offset_variations = randomness * 0.6f }, false);
                    }

                    fractureTool.finalizeFracturing();

                    //extract mesh chunks from nvBlast
                    int meshCount = fractureTool.getChunkCount();
                    for (var i = 1; i < meshCount; i++)
                    {
                        //extract the mesh and verify it
                        newMeshesTemp.Add(ExtractChunkMesh(fractureTool, i));
                        //if (FractureHelperFunc.IsMeshValid(newMeshesTemp[^1], false, worldScaleDis) == false)
                        if (FractureHelperFunc.IsMeshValid(newMeshesTemp[^1], true, worldScaleDis) == false) //is true better?
                        {
                            if (loopMaxAttempts > 0)
                            {
                                meshIsValid = false;
                                break;
                            }
                            else
                            {
                                hadInvalidChunkPart = true;
                                newMeshesTemp.RemoveAt(newMeshesTemp.Count - 1);
                                continue;
                            }
                        }

                        //optimize mesh
                        if (remeshing == FractureRemesh.convex)
                        {
                            newMeshesTemp[^1] = FractureHelperFunc.MakeMeshConvex(newMeshesTemp[^1], false, FracGlobalSettings.worldScale);
                        }
                    }

                    if (meshIsValid == false) continue;

                    for (int i = 0; i < newMeshesTemp.Count; i += 1)
                    {
                        newMeshes.Add(new() { meshW = newMeshesTemp[i], groupId = meshToFrac.mGroupId, sourceM = meshToFrac });
                    }

                    break;
                }

                //warn if unable to frac a chunk
                if (meshIsValid == false || hadInvalidChunkPart == true)
                {
                    //Debug.LogError("Unable to properly fracture chunk " + nextOgMeshId + " of " + transform.name + " (Some parts of the mesh may be missing)");
                    Debug.LogWarning("Chunk " + nextOgMeshId + " of " + transform.name + " was difficult to fracture (Some parts of the mesh may be missing)");
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
        /// <param name="obj">The object to the get meshes from</param>
        private List<FracSource> Gen_getMeshesToFracture(GameObject obj, out SkinSourceData skinRendSource, bool getRawOnly = false, float worldScaleDis = 0.0001f)
        {
            //Get all the meshes to fracture
            bool hasSkinned = false;
            skinRendSource = null;

            List<FracSource> meshesToFrac = new();
            foreach (Renderer rend in obj.GetComponentsInChildren<Renderer>())
            {
                FracSource newToFrac = new();

                if (rend.GetType() == typeof(SkinnedMeshRenderer))
                {
                    if (meshesToFrac.Count > 0)
                    {
                        //if skinned there can only be 1 mesh source
                        Debug.LogError("There can only be 1 mesh if there is a skinnedMeshRenderer");
                        return null;
                    }

                    SkinnedMeshRenderer skinnedR = (SkinnedMeshRenderer)rend;
                    if (getRawOnly == false) newToFrac.meshW = Instantiate(skinnedR.sharedMesh);
                    else
                    {
                        newToFrac.meshW = new();
                        skinnedR.BakeMesh(newToFrac.meshW, true);
                    }

                    hasSkinned = true;
                    Vector3 rScale = skinnedR.transform.lossyScale;

                    foreach (Transform bone in skinnedR.bones)
                    {
                        if (bone == null)
                        {
                            Debug.LogError("A bone in " + skinnedR.transform.name + " skinnedMeshRenderer is null");
                            return null;
                        }

                        Vector3 scaleDifference = new(Mathf.Abs(bone.lossyScale.x - rScale.x), Mathf.Abs(bone.lossyScale.y - rScale.y), Mathf.Abs(bone.lossyScale.z - rScale.z));

                        if (scaleDifference.x > 0.001f || scaleDifference.y > 0.001f || scaleDifference.z > 0.001f)
                        {
                            Debug.LogError("All bones of the skinnedMeshRenderer must have a scale of 1,1,1 (" + bone.name + " or any of its bone parents is invalid)");
                            return null;
                        }
                    }

                    //get defualt parent from rootbone
                    if (FractureHelperFunc.GetIfTransformIsAnyParent(transform, skinnedR.rootBone) == false)
                    {
                        Debug.LogError(skinnedR.transform.name + " (skinnedRend) rootBone must be a child of " + transform.name);
                        return null;
                    }

                    if (Vector3.Distance(skinnedR.transform.position, transform.position) > FracGlobalSettings.worldScale * 0.01f)
                    {
                        Debug.LogWarning(skinnedR.transform.name + " world position does not match " + transform.name + " world position, this may cause the fractured mesh to be misplaced");
                    }

                    //save orginal real skinnedmesh data to use it on setup later
                    if (getRawOnly == false)
                    {
                        skinRendSource = new()
                        {
                            bindPoses = skinnedR.sharedMesh.bindposes,
                            bones = skinnedR.bones,
                            rendTrans = skinnedR.transform,
                            rootBone = skinnedR.rootBone
                        };
                    }
                }
                else if (rend.TryGetComponent(out MeshFilter meshF) == true)
                {
                    //When regular renderer with meshFilter
                    if (hasSkinned == true)
                    {
                        Debug.LogError("There can only be 1 mesh if there is a skinnedMeshRenderer");
                        return null;
                    }

                    if (rend.transform == transform)
                    {
                        Debug.LogError("The Renderer to fracture cannot be attatched to the same object as the FractureThis script (It must be a child of it)");
                        return null;
                    }

                    newToFrac.meshW = Instantiate(meshF.sharedMesh);
                }
                else continue; //ignore if no MeshRenderer with meshfilter or skinnedMeshRenderer

                if (FractureHelperFunc.IsMeshValid(newToFrac.meshW, true, worldScaleDis) == false) continue; //continue if mesh is invalid

                newToFrac.sRend = rend;
                meshesToFrac.Add(newToFrac);
            }

            if (meshesToFrac.Count == 0)
            {
                Debug.LogError("There are no valid mesh in " + transform.name + " or any of its children");
                return null;
            }

            //transform all meshes to worldSpace
            HashSet<List<float>> newGroupIds = new();
            for (int i = 0; i < meshesToFrac.Count; i++)
            {
                FractureHelperFunc.ConvertMeshWithMatrix(ref meshesToFrac[i].meshW, meshesToFrac[i].sRend.localToWorldMatrix);
                FractureHelperFunc.Gd_getIdsFromColors(meshesToFrac[i].meshW.colors, ref newGroupIds);
            }

            //setup group ids
            md_verGroupIds = newGroupIds.ToArray();
            Array.Sort(md_verGroupIds, new FractureHelperFunc.HashSetComparer());
            SetupGroupData();

            //return early if we only want raw
            if (getRawOnly == true)
            {
                return meshesToFrac;
            }

            //split meshes into chunks
            List<FracSource> splittedMeshes;
            for (int i = meshesToFrac.Count - 1; i >= 0; i--)
            {
                //get submesh materials from renderer
                meshesToFrac[i].mMats = meshesToFrac[i].sRend.sharedMaterials.ToList();
                int subMeshCountDiff = meshesToFrac[i].meshW.subMeshCount - meshesToFrac[i].mMats.Count;
                if (subMeshCountDiff > 0) meshesToFrac[i].mMats.AddRange(new Material[subMeshCountDiff]);

                //split the mesh
                splittedMeshes = Gen_splitMeshIntoChunks(meshesToFrac[i], hasSkinned, worldScaleDis);
                if (splittedMeshes == null) return null;

                //add split result
                for (int ii = 0; ii < splittedMeshes.Count; ii += 1)
                {
                    meshesToFrac.Add(new()
                    {
                        meshW = splittedMeshes[ii].meshW,
                        sRend = meshesToFrac[i].sRend,
                        mGroupId = splittedMeshes[ii].mGroupId,
                        mMats = splittedMeshes[ii].mMats,
                        trisSubMeshI = FractureHelperFunc.GetTrisSubMeshI(splittedMeshes[ii].meshW)
                    });
                }

                meshesToFrac.RemoveAt(i);
            }

            //return result
            GetIfValidPrefracture();
            return meshesToFrac;

            bool GetIfValidPrefracture()
            {
                //return true if has valid prefracture, other wise false
                if (saveState == null) return false;

                Bounds[] fRendBounds = new Bounds[meshesToFrac.Count];
                int totalVerCount = 0;

                for (int i = 0; i < meshesToFrac.Count; i++)
                {
                    fRendBounds[i] = meshesToFrac[i].sRend.bounds;
                    totalVerCount += meshesToFrac[i].meshW.vertexCount;
                }

                FractureSavedState.FloatList[] mdVersList = FractureSavedState.FloatList.FromFloatArray(md_verGroupIds);

                if (saveState.preS_toFracData == null
                    || saveState.preS_toFracData.fractureCount != fractureCount
                    || saveState.preS_toFracData.dynamicFractureCount != dynamicFractureCount
                    || saveState.preS_toFracData.generationQuality != generationQuality
                    || FractureSavedState.FloatList.IsTwoArraySame(saveState.preS_toFracData.md_verGroupIds, mdVersList) == false
                    || saveState.preS_toFracData.randomness != randomness
                    || saveState.preS_toFracData.remeshing != remeshing
                    || saveState.preS_toFracData.seed != seed
                    || saveState.preS_toFracData.worldScale != FracGlobalSettings.worldScale
                    || saveState.preS_toFracData.totalVerCount != totalVerCount
                    || FractureHelperFunc.AreBoundsArrayEqual(saveState.preS_toFracData.toFracRendBounds, fRendBounds) == false)
                {
                    //if has saveState but mayor stuff has changed, return false and clear savedPrefracture
                    saveState.ClearSavedPrefracture();

                    saveState.SavePrefracture(new()
                    {
                        dynamicFractureCount = dynamicFractureCount,
                        worldScale = FracGlobalSettings.worldScale,
                        seed = seed,
                        fractureCount = fractureCount,
                        generationQuality = generationQuality,
                        md_verGroupIds = mdVersList,
                        randomness = randomness,
                        remeshing = remeshing,
                        toFracRendBounds = fRendBounds,
                        totalVerCount = totalVerCount
                    });

                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Splits the given mesh into chunks
        /// </summary>
        /// <param name="meshToSplit"></param>
        /// <returns></returns>
        private List<FracSource> Gen_splitMeshIntoChunks(FracSource meshToSplit, bool doBones, float worldScaleDis = 0.0001f)
        {
            int maxLoops = 200;
            List<FracSource> splittedMeshes = new();
            List<FracSource> tempM;
            Color[] verCols;
            List<float> tempG;

            while (maxLoops > 0)
            {
                maxLoops--;
                if (meshToSplit.meshW.vertexCount < 4) break;

                verCols = meshToSplit.meshW.colors;
                bool useVerCols = verCols.Length == meshToSplit.meshW.vertexCount;

                if (splitDisconnectedFaces == false && (useGroupIds == false || useVerCols == false))
                {
                    splittedMeshes.Add(new() { meshW = meshToSplit.meshW, mGroupId = null, mMats = meshToSplit.sRend.sharedMaterials.ToList() });

                    return splittedMeshes;
                }

                tempG = useVerCols == true ? FractureHelperFunc.Gd_getIdFromColor(verCols[0]) : null;
                HashSet<int> vertsToSplit;

                if (splitDisconnectedFaces == true)
                {
                    vertsToSplit = FractureHelperFunc.GetConnectedVertics(meshToSplit.meshW, 0, worldScaleDis);
                    if (useVerCols == true) vertsToSplit = FractureHelperFunc.Gd_getSomeVerticesInId(verCols, tempG, vertsToSplit);
                }
                else
                {
                    vertsToSplit = FractureHelperFunc.Gd_getAllVerticesInId(verCols, tempG);
                }

                tempM = FractureHelperFunc.SplitMeshInTwo(vertsToSplit, meshToSplit, doBones);

                if (tempM == null) return null;
                if (tempM[0].meshW.vertexCount >= 4) splittedMeshes.Add(new() { meshW = tempM[0].meshW, mGroupId = tempG, mMats = tempM[0].mMats });
                meshToSplit = tempM[1];
            }

            if (meshToSplit.meshW.vertexCount >= 4) splittedMeshes.Add(meshToSplit);

            return splittedMeshes;
        }

#endregion GenerateFractureSystem




        #region MainUpdateFunctions

        /// <summary>
        /// Is true if the fractuture is animated
        /// </summary>
        public bool isRealSkinnedM;

        /// <summary>
        /// This is true if the destructionSystem is valid and running
        /// </summary>
        [System.NonSerialized] public bool fractureIsValid = false;

        private unsafe void Start()
        {
#if UNITY_EDITOR
            if (Application.isPlaying == false) return;
#endif

            //verify fracture
            if (VerifyFracture() == false) return;

            //setup collider instanceid references
            for (short partI = 0; partI < allParts.Count; partI++)
            {
                globalHandler.OnAddFracPart(this, partI);
            }

            //set parts colliders, ignore neighbours and modifiable contacts
            var tempFStructs = jCDW_job.fStructs;

            for (int partI = 0; partI < allParts.Count; partI++)
            {
                saved_allPartsCol[partI].hasModifiableContacts = true;

                if (selfCollisionRule == SelfCollisionRule.ignoreNeighbours)
                {
                    FracStruct fPart = tempFStructs[partI];

                    for (int nI = 0; nI < fPart.neighbourPartI_lenght; nI++)
                    {
                        Physics.IgnoreCollision(saved_allPartsCol[partI], saved_allPartsCol[fPart.neighbourPartI[nI]], true);
                    }
                }
                else if (selfCollisionRule == SelfCollisionRule.ignoreDoubleNeighbours)
                {
                    FracStruct fPart = tempFStructs[partI];

                    for (int nI = 0; nI < fPart.neighbourPartI_lenght; nI++)
                    {
                        int nPI = fPart.neighbourPartI[nI];
                        Physics.IgnoreCollision(saved_allPartsCol[partI], saved_allPartsCol[nPI], true);

                        FracStruct fPartB = tempFStructs[nPI];

                        for (int nIB = 0; nIB < fPartB.neighbourPartI_lenght; nIB++)
                        {
                            Physics.IgnoreCollision(saved_allPartsCol[partI], saved_allPartsCol[fPartB.neighbourPartI[nIB]], true);
                        }
                    }
                }
            }

            //loop all destruction materials to get lowest transportCapacity
            lowestTransportCapacity = float.MaxValue;
            for (int i = 0; i < destructionMaterials.Count; i++)
            {
                if (destructionMaterials[i].desProps.stenght >= lowestTransportCapacity) continue;
                lowestTransportCapacity = destructionMaterials[i].desProps.stenght;
            }

            //if (groupDataDefualt.transportCapacity < lowestTransportCapacity) lowestTransportCapacity = groupDataDefualt.transportCapacity;

            //setup compute destruction
            jCDW_job.desSources = new NativeArray<DestructionSource>(0, Allocator.Persistent);
            jCDW_job.desProps = destructionMaterials.Select(desMat => desMat.desProps).ToList().ToNativeArray(Allocator.Persistent);
            jCDW_job.partBoneOffset = partBoneOffset;
            jCDW_job.partsToBreak = new NativeHashMap<int, DesPartToBreak>(8, Allocator.Persistent);
            jCDW_job.newParentsData = new NativeHashMap<byte, DesNewParentData>(4, Allocator.Persistent);
            jCDW_job.partsNewParentI = new NativeArray<byte>(allParts.Count, Allocator.Persistent);
            jCDW_job.optMainPhyType = phyMainOptions.mainPhysicsType;
            destructionSources = new();
            destructionBodies = new();
            destructionPairs = new();

            //setup gpu readback
            gpuMeshVertexData = new GpuMeshVertex[allParts.Count];
            gpuMeshBonesLToW = new Matrix4x4[fr_bones.Count];
        }

        private void Update()
        {
#if UNITY_EDITOR
            if (eOnly_ignoreNextDraw == true)
            {
                eOnly_ignoreNextDraw = false;
                return;
            }

            if (Application.isPlaying == false)
            {
                EditorUpdate();
            }
#endif
            GlobalUpdate();

#if UNITY_EDITOR
            void EditorUpdate()
            {
                //delete saveAsset if temp and globalHandler is null
                if (globalHandler == null && saveAsset != null && AssetDatabase.GetAssetPath(saveAsset).Contains("TempFracSaveAssets") == true)
                {
                    AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(saveAsset));
                }

                //verify that fracRend reference the correct object
                if (fracRend != null && fracRend.transform != transform)
                {
                    Debug.LogError(transform.name + " fracture does not reference the correct objects and will be removed (Has the script been copied?)");
                    Gen_removeFracture(false, false, false);
                    return;
                }

                //verify if generate status does not match
                //if (shouldBeFractured != fractureIsValid || (allParts != null && allParts.Count > 0 && allParts[0].trans == null)
                if (shouldBeFractured != fractureIsValid
                    || (saveAsset != null && saved_fracId >= 0 && saveAsset.fracSavedData.id != saved_fracId))
                {
                    shouldBeFractured = VerifyFracture();
                }

                //update fracture
                GlobalUpdate();
                GlobalUpdate_fixed();

                if (fractureIsValid == false) return;

                //make sure compute shader aint null
                if (computeDestructionSolver == null)
                {
                    SyncFracRendData();
                }

                //get transform data
                GetTransformData_start();
                GetTransformData_end(); //send skin+def to gpu happens inside this
                                        //ComputeDestruction_start();
            }
#endif
        }

        private void FixedUpdate()
        {
            if (fractureIsValid == false) return;

            ComputeDestruction_end();
            GlobalUpdate_fixed();
            GetTransformData_start();

            //run late fixedUpdate later 
            StartCoroutine(LateFixedUpdate());
        }

        private IEnumerator LateFixedUpdate()
        {
            //wait for late fixed update
            yield return new WaitForFixedUpdate();

            GetTransformData_end(); //send skin+def to gpu happens inside this
            ComputeDestruction_start();
        }

        private void OnDestroy()
        {
            //Clear memory, should we really do it here? If the user just temporarly disabled it we would need to reallocolate everything again
            ClearUsedGpuAndCpuMemory();
        }


        /// <summary>
        /// Runs on drawGizmos in editor and on Update at runtime
        /// </summary>
        private void GlobalUpdate()
        {
            //return if fracture is invalid
            //if (fractureIsValid == false && VerifyFracture() == false) return;
            if (fractureIsValid != shouldBeFractured && VerifyFracture() != shouldBeFractured) return;
            if (fractureIsValid == false) return;

            //if (allParts[0].trans != saved_allPartsCol[0].transform)
            //{
            //    Debug.Log("No " + transform.parent.name);
            //    fractureIsValid = false;
            //    VerifyFracture();
            //}

            //sync part colliders with deformed mesh
            if (des_deformedParts[1 - des_deformedPartsIndex].Count > 0 || gpuMeshRequest_do == true) UpdateGpuMeshReadback();
        }

        /// <summary>
        /// Runs on drawGizmos in editor and on fixedUpdate at runtime
        /// </summary>
        private void GlobalUpdate_fixed()
        {
            //sync with gpu, wantToSyncFracRendData is set to false inside SyncFracRendData()
            if (wantToSyncFracRendData == true)
            {
                SyncFracRendData();
            }

            //update modified parents
            if (parentsThatNeedsUpdating.Count > 0)
            {
                foreach (int parentI in parentsThatNeedsUpdating) UpdateParentData(parentI);
                if (gpuIsReady == true) buf_structs_parentI.SetData(jCDW_job.partsParentI.AsArray());
                parentsThatNeedsUpdating.Clear();
            }
        }

        /// <summary>
        /// Call to dispose variabels from gpu and cpu
        /// </summary>
        public void ClearUsedGpuAndCpuMemory()
        {
            //fracture is never valid when disposing
            fractureIsValid = false;
            gpuIsReady = false;

            //dispose buffers
            if (buf_desWeights != null)
            {
                buf_desWeights.Release();
                buf_desWeights.Dispose();
            }

            if (buf_meshData != null)
            {
                buf_meshData.Release();
                buf_meshData.Dispose();
            }

            if (buf_fr_boneWeightsCurrent != null)
            {
                buf_fr_boneWeightsCurrent.Release();
                buf_fr_boneWeightsCurrent.Dispose();
            }

            if (buf_boneBindsLToW != null)
            {
                buf_boneBindsLToW.Release();
                buf_boneBindsLToW.Dispose();
            }

            if (buf_structs_parentI != null)
            {
                buf_structs_parentI.Release();
                buf_structs_parentI.Dispose();
            }

            if (buf_structs_posL != null)
            {
                buf_structs_posL.Release();
                buf_structs_posL.Dispose();
            }

            if (buf_structs_posLPrev != null)
            {
                buf_structs_posLPrev.Release();
                buf_structs_posLPrev.Dispose();
            }

            if (buf_verNors != null)
            {
                buf_verNors.Release();
                buf_verNors.Dispose();
            }

            //dispose getTransformData job
            GetTransformData_end();//Make sure the job aint running
            if (jGTD_hasMoved.IsCreated == true) jGTD_hasMoved.Dispose();
            if (jGTD_fracBoneTrans.isCreated == true) jGTD_fracBoneTrans.Dispose();
            if (jGTD_job.fracBonesLToW.IsCreated == true) jGTD_job.fracBonesLToW.Dispose();
            if (jGTD_job.fracBonesPosW.IsCreated == true) jGTD_job.fracBonesPosW.Dispose();
            if (jGTD_job.fracBonesLocValue.IsCreated == true) jGTD_job.fracBonesLocValue.Dispose();

            //disepose computeDestruction job
            ComputeDestruction_end();//Make sure the job aint running
            if (jCDW_job.structPosL.IsCreated == true) jCDW_job.structPosL.Dispose();
            if (jCDW_job.partsParentI.IsCreated == true) jCDW_job.partsParentI.Dispose();
            if (jCDW_job.kinematicPartIndexes.IsCreated == true) jCDW_job.kinematicPartIndexes.Dispose();
            if (jCDW_job.desSources.IsCreated == true) jCDW_job.desSources.Dispose();
            if (jCDW_job.fStructs.IsCreated == true) jCDW_job.fStructs.Dispose();
            if (jCDW_job.desProps.IsCreated == true) jCDW_job.desProps.Dispose();
            if (jCDW_job.boneBindsLToW.IsCreated == true) jCDW_job.boneBindsLToW.Dispose();
            if (jCDW_job.partsToBreak.IsCreated == true) jCDW_job.partsToBreak.Dispose();
            if (jCDW_job.newParentsData.IsCreated == true) jCDW_job.newParentsData.Dispose();
            if (jCDW_job.partsNewParentI.IsCreated == true) jCDW_job.partsNewParentI.Dispose();
        }

        private int cpKernelId_ComputeSkinDef = -1;
        private ComputeShader computeDestructionSolver;

        #endregion MainUpdateFunctions





        #region GetTransformData

        private TransformAccessArray jGTD_fracBoneTrans = new();

        /// <summary>
        /// The matrix each fracRend bone had the previous frame (localToWorld)(Is multiplied with bindposes)(Threaded, written to on mainthread in GetTransformData_end())
        /// </summary>
        //private NativeArray<Matrix4x4> jCDW_job.bonesLToW;
        private JobHandle jGTD_handle;
        private GetTransformData_work jGTD_job;
        private NativeQueue<short> jGTD_hasMoved;

        private bool jGTD_jobIsActive = false;

        private void GetTransformData_start()
        {
            if (jGTD_jobIsActive == true) return;
            if (jGTD_fracBoneTrans.isCreated == false || jGTD_fracBoneTrans.length != fr_bones.Count
                || jCDW_job.boneBindsLToW == null || jCDW_job.boneBindsLToW.Length != fr_bones.Count || jGTD_hasMoved.IsCreated == false) GetTransformData_setup();

            //Run the job
            jGTD_handle = jGTD_job.Schedule(jGTD_fracBoneTrans);
            jGTD_jobIsActive = true;

            void GetTransformData_setup()
            {
                //Assign variabels used in GetTransformData job
                if (jGTD_fracBoneTrans.isCreated == false) jGTD_fracBoneTrans = new(fr_bones.ToArray());
                jCDW_job.boneBindsLToW = new NativeArray<Matrix4x4>(jGTD_fracBoneTrans.length, Allocator.Persistent);
                buf_boneBindsLToW = new ComputeBuffer(jGTD_fracBoneTrans.length, 16 * sizeof(float));

                if (jGTD_hasMoved.IsCreated == false) jGTD_hasMoved = new NativeQueue<short>(Allocator.Persistent);

                jGTD_job = new GetTransformData_work()
                {
                    fracBonesLToW = new NativeArray<Matrix4x4>(jGTD_fracBoneTrans.length, Allocator.Persistent),
                    fracBonesLocValue = new NativeArray<float>(jGTD_fracBoneTrans.length, Allocator.Persistent),
                    fracBonesPosW = new NativeArray<Vector3>(jGTD_fracBoneTrans.length, Allocator.Persistent),
                    hasMoved = jGTD_hasMoved.AsParallelWriter()
                };
            }
        }

        private int fracRendDividedVerCount = 1;
        private bool wantToApplyDeformation = false;

        private void GetTransformData_end()
        {
            if (jGTD_jobIsActive == false) return;

            //finish the job
            jGTD_jobIsActive = false;
            jGTD_handle.Complete();

            if (jGTD_hasMoved.IsCreated == false)
            {
                //if (fractureIsValid == false) return;

                //make sure the job has ben setup
                GetTransformData_start();
                GetTransformData_end();
                return;
            }

            if (jGTD_hasMoved.Count > 0)
            {
                //When a bone has moved
                //Copy updated localToWorld matrixes to allFracBonesLToW and gpu
                while (jGTD_hasMoved.TryDequeue(out short boneI) == true)
                {
                    jCDW_job.boneBindsLToW[boneI] = jGTD_job.fracBonesLToW[boneI] * fr_bindPoses[boneI];
                }

                jGTD_hasMoved.Clear();

                buf_boneBindsLToW.SetData(jCDW_job.boneBindsLToW);

                //Update fracRend bounds
                if (fracRend == null) return;
                Vector3 min = Vector3.one * 694200;
                Vector3 max = Vector3.one * -694200;
                foreach (Vector3 vec in jGTD_job.fracBonesPosW)
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
                    min = min + (-2.0f * partMaxExtent * Vector3.one),
                    max = max + (2.0f * partMaxExtent * Vector3.one)
                };

                //apply skinning
                ApplySkinAndDef();
            }
            else if (wantToApplyDeformation == true) ApplySkinAndDef();
        }

        [BurstCompile]
        private struct GetTransformData_work : IJobParallelForTransform
        {
            public NativeArray<Matrix4x4> fracBonesLToW;
            public NativeArray<float> fracBonesLocValue;
            public NativeArray<Vector3> fracBonesPosW;

            /// <summary>
            /// Contrains the indexes of the fracBones that has moved
            /// </summary>
            public NativeQueue<short>.ParallelWriter hasMoved;

            public void Execute(int index, TransformAccess transform)
            {
                //If fracRend bone trans has moved, add to hasMoved queue
                float newLocValue = transform.position.sqrMagnitude + FractureHelperFunc.QuaternionSqrMagnitude(transform.rotation);
                if (newLocValue - fracBonesLocValue[index] != 0.0f)//Should it be more sensitive?
                {
                    //get fracRend bone lToW matrix and its world pos
                    fracBonesLToW[index] = transform.localToWorldMatrix;
                    fracBonesLocValue[index] = newLocValue;
                    fracBonesPosW[index] = transform.position;
                    hasMoved.Enqueue((short)index);
                }
            }
        }

        #endregion GetTransformData




        #region ComputeDestruction

        /// <summary>
        ///Contains all parts that has been deformed. (Since we use async readback, we toggle between using [0] and [1], if its currently readingback)
        /// </summary>
        private HashSet<short>[] des_deformedParts = new HashSet<short>[2];
        private byte des_deformedPartsIndex = 0;
        private AsyncGPUReadbackRequest gpuMeshRequest;
        private List<Rigidbody> jCDW_bodies = new();

        private struct GpuMeshVertex
        {
            public Vector3 pos;
            public Vector3 nor;
        }

        /// <summary>
        /// Set to true to request gpuMesh as soon as possible
        /// </summary>
        private bool gpuMeshRequest_do = false;
        private bool jCDW_jobIsActive = false;

        private void ComputeDestruction_start()
        {
#if UNITY_EDITOR
            if (Application.isPlaying == false)
            {
                Debug.LogError("Computing destruction while in editmode is not supported");
                return;
            }
#endif

            if (jCDW_jobIsActive == true || destructionSources.Count == 0) return;

            //set job destruction sources
            int desSLenght = jCDW_job.desSources.Length;
            int desOCount = destructionSources.Count;
            if (desSLenght < desOCount)
            {
                if (jCDW_job.desSources.IsCreated == true) jCDW_job.desSources.Dispose();
                desSLenght = desOCount;
                jCDW_job.desSources = new NativeArray<DestructionSource>(desSLenght, Allocator.Persistent);
                FractureHelperFunc.SetListLenght(ref jCDW_bodies, desSLenght);
            }

            int[] deskeys = destructionSources.Keys.ToArray();

            for (int i = 0; i < desSLenght; i++)
            {
                //dispose previous impPoints to prevent memory leak
                if (i >= desOCount)
                {
                    var source = jCDW_job.desSources[i];
                    source.impForceTotal = -1;
                    jCDW_job.desSources[i] = source;
                    jCDW_bodies[i] = null;
                }
                else
                {
                    jCDW_job.desSources[i] = destructionSources[deskeys[i]];
                    jCDW_bodies[i] = destructionBodies[deskeys[i]];
                }
            }

            destructionSources.Clear();
            destructionBodies.Clear();
            destructionPairs.Clear();//The nativeArrays will be disposed later by the worker

            //update bone matrixes
            jCDW_job.bonesLToW = jGTD_job.fracBonesLToW.AsReadOnly();

            //run the job
            jCDW_jobIsActive = true;

            //Debug.Log(jCDW_job.partsParentI[0]);
            jCDW_handle = jCDW_job.Schedule();
        }

        private void ComputeDestruction_end()
        {
            if (jCDW_jobIsActive == false) return;

            //complete the job
            jCDW_jobIsActive = false;
            jCDW_handle.Complete();
            FixReadIssue(jCDW_job.partsParentI[0]);//This somehow fixes a weird bug. Dont believe me? Try removing it and you get errors when trying to destroy objects
            static void FixReadIssue(int someth)
            {

            }

            FixReadIssueOther(jCDW_job.fStructs[0]);
            static void FixReadIssueOther(FracStruct somertyh)
            {

            }

            //apply destruction result
            wantToApplyDeformation = true;
            for (short i = 0; i < allParts.Count; i++)
            {
                des_deformedParts[des_deformedPartsIndex].Add(i);
            }

            //create new parents and set their velocity
            NativeArray<byte> newParentKeys = jCDW_job.newParentsData.GetKeyArray(Allocator.Temp);
            Dictionary<byte, int> newParentIToParentI = new(newParentKeys.Length);

            for (int i = 0; i < newParentKeys.Length; i++)
            {
                DesNewParentData newPD = jCDW_job.newParentsData[newParentKeys[i]];
                if (newPD.newPartCount >= 0)
                {
                    newPD.sourceParentI = CreateNewParent(null);
                    newParentIToParentI.Add(newParentKeys[i], newPD.sourceParentI);
                }

                Debug.Log(newPD.sourceParentI);
                if (allParents[newPD.sourceParentI].parentRb != null) allParents[newPD.sourceParentI].parentRb.AddForceAtPosition(newPD.velocity, newPD.velPos, ForceMode.VelocityChange);
            }
            
            //set parts to their new parents
            if (newParentIToParentI.Count > 0)
            {
                int partCount = allParts.Count;
                for (short partI = 0; partI < partCount; partI++)
                {
                    byte newPKey = jCDW_job.partsNewParentI[partI];
                    if (newPKey == 0) continue;
            
                    if (jCDW_job.partsToBreak.ContainsKey(partI) == true) Debug.Log(partI);
                    SetPartParent(partI, newParentIToParentI[newPKey]);
                }
            }

            //break parts that should break
            foreach (DesPartToBreak pBreak in jCDW_job.partsToBreak.GetValueArray(Allocator.Temp))
            {
                SetPartParent((short)pBreak.partI, -1, pBreak.velTarget);
            }
        }

        public ComputeDestruction_work jCDW_job;
        private JobHandle jCDW_handle;

        //[BurstCompile]
        public struct ComputeDestruction_work : IJob
        {
            //solve destruction, to fix later, nearby must move the same amount as X - dis to X * dir to X dot
            public NativeList<Vector3> structPosL;

            /// <summary>
            /// The parent index each part has
            /// </summary>
            public NativeList<int> partsParentI;

            /// <summary>
            /// The number of parts each parent has
            /// </summary>
            public NativeList<short> parentPartCount;

            /// <summary>
            /// Contains all part indexes that are kinematic
            /// </summary>
            public NativeHashSet<int> kinematicPartIndexes;
            public NativeArray<DestructionSource> desSources;    
            public NativeList<FracStruct> fStructs;
            public NativeArray<DestructionMaterial.DesProperties> desProps;
            public NativeArray<Matrix4x4>.ReadOnly bonesLToW;
            public NativeHashMap<int, DesPartToBreak> partsToBreak;
            public NativeHashMap<byte, DesNewParentData> newParentsData;
            public NativeArray<byte> partsNewParentI;
            public OptMainPhysicsType optMainPhyType;

            /// <summary>
            /// The local to world matrix for every bone (Parts bones + skinned bones), skinned bones are first
            /// </summary>
            public NativeArray<Matrix4x4> boneBindsLToW;
            public int partBoneOffset;

            public unsafe void Execute()
            {
                //Since its useless and you cant access fields from localLocal functions, I have to assign it like this
                var _desSources = desSources;
                var _partsParentI = partsParentI;
                var _fStructs = fStructs;
                var _desProps = desProps;
                int partCount = structPosL.Length;
                var _KinParts = kinematicPartIndexes;
                var _partsToBreak = partsToBreak;
                var _newParentsData = newParentsData;
                var _parentPartCount = parentPartCount;
                var _partsNewParentI = partsNewParentI;
                var _optMainPhyType = optMainPhyType;

                //Allocate global used variabels
                _partsToBreak.Clear();
                _newParentsData.Clear();
                NativeArray<float> partsMoveMass = new(partCount, Allocator.Temp); //The resistance each part can make to movement
                NativeHashSet<int> partsThatWillBreak = new(8, Allocator.Temp);
                float allTotImpForces = 0.0f;

                //get all parts world position
                NativeArray<Vector3> partsWPos = new(partCount, Allocator.Temp);

                for (int partI = 0; partI < partCount; partI++)
                {
                    partsWPos[partI] = boneBindsLToW[partI + partBoneOffset].MultiplyPoint3x4(structPosL[partI]);
                }

                //Loop all sources and compute them
                for (int sourceI = 0; sourceI < desSources.Length; sourceI++)
                {
                    CalcSource(sourceI);
                }

                //get the velocity to give all parts and if need any new parents
                CalcChunks();

                //Return destruction result
                //Transform parts world position back to local
                for (int partI = 0; partI < partCount; partI++)
                {
                    structPosL[partI] = boneBindsLToW[partI + partBoneOffset].inverse.MultiplyPoint3x4(partsWPos[partI]);
                }

                void CalcChunks()
                {
                    //declare native containers and clear
                    int breakCount = _partsToBreak.Count;
                    if (breakCount == 0) return;

                    NativeHashSet<int> p_breakSources = new(breakCount, Allocator.Temp);
                    NativeArray<int> p_parts = new(partCount - breakCount, Allocator.Temp); 
                    NativeHashSet<int> p_partsUsed = new(p_parts.Length, Allocator.Temp);
                    NativeArray<int> toBreakKeys = _partsToBreak.GetKeyArray(Allocator.Temp);
                    NativeHashSet<int> kinSourceParentsI = new(2, Allocator.Temp);
                    FractureHelperFunc.SetWholeNativeArray<byte>(ref _partsNewParentI, 0);
                    byte nextNewParentI = 0;
                    float totUsedForce = 0.0f;

                    if (_optMainPhyType == OptMainPhysicsType.alwaysKinematic) goto SKIPGETNEWPARENTS;

                    for (int brI = 0; brI < breakCount; brI++)
                    {
                        //get the spreadPart
                        DesPartToBreak pBreak = _partsToBreak[toBreakKeys[brI]];
                        FracStruct fPart = _fStructs[pBreak.partI];
                        int parentI = _partsParentI[pBreak.partI];
                        if (parentI < 0) continue;

                        int nPI = -1;
                        float p_totMass = 0.0f;

                        for (int nI = 0; nI < fPart.neighbourPartI_lenght; nI++)
                        {
                            nPI = fPart.neighbourPartI[nI];
                            if (_partsToBreak.ContainsKey(nPI) == true || _partsParentI[nPI] != parentI || p_partsUsed.Add(nPI) == false)
                            {
                                nPI = -1;
                                continue;
                            }

                            p_parts[0] = nPI;
                            p_totMass += _desProps[_fStructs[nPI].desMatI].mass;
                            break;
                        }

                        if (nPI < 0) continue;
                        nextNewParentI++;
                        _partsNewParentI[nPI] = nextNewParentI;
                        brI--;//If breaking caused a new parent, we must loop it again since it may have caused even more new parents

                        //get all parts that are connected to spreadPart
                        int ppCount = 1;
                        int avgBreakSourceLayerI = 0;
                        bool isKin = false;
                        p_breakSources.Clear();

                        for (int i = 0; i < ppCount; i++)
                        {
                            fPart = _fStructs[p_parts[i]];

                            for (int nI = 0; nI < fPart.neighbourPartI_lenght; nI++)
                            {
                                nPI = fPart.neighbourPartI[nI];
                                if (_partsParentI[nPI] != parentI) continue;

                                if (_partsToBreak.ContainsKey(nPI) == true)
                                {
                                    if (p_breakSources.Add(nPI) == false) continue;
                                    avgBreakSourceLayerI += _partsToBreak[nPI].layerI;
                                    continue;
                                }

                                if (p_partsUsed.Add(nPI) == false) continue;
                                if (_KinParts.Contains(nPI) == true) isKin = true;

                                _partsNewParentI[nPI] = nextNewParentI;
                                p_parts[ppCount] = nPI;
                                p_totMass += _desProps[_fStructs[nPI].desMatI].mass;
                                ppCount++;
                            }
                        }

                        //ignore if kinematic
                        if (isKin == true)
                        {
                            for (int ppI = 0; ppI < ppCount; ppI++)
                            {
                                _partsNewParentI[p_parts[ppI]] = 0;
                            }

                            kinSourceParentsI.Add(parentI);
                            nextNewParentI--;
                            continue;
                        }

                        //get break dir and break force
                        int resistanceLayer = Mathf.CeilToInt(avgBreakSourceLayerI / (float)p_breakSources.Count) + 1;
                        float breakForce = 0.0f;
                        Vector3 breakVel = Vector3.zero;
                        Vector3 breakPos = Vector3.zero;
                        int usedBreakCount = 0;
                        float avgBreakForceLeft = 0.0f;

                        foreach (int bsPI in p_breakSources)
                        {
                            pBreak = _partsToBreak[bsPI];
                            if (pBreak.layerI > resistanceLayer)
                            {
                                breakForce -= pBreak.velForce;
                                continue;
                            }

                            breakForce += pBreak.velForce;
                            breakVel += pBreak.velTarget;
                            breakPos += partsWPos[bsPI];
                            avgBreakForceLeft += pBreak.forceLeft;
                            usedBreakCount++;
                        }

                        if (breakForce < 0.0f)
                        {
                            breakForce = 0.0f; //In theory it should be impossible for it to be <0.0f
                            if (usedBreakCount == 0) continue;
                        }

                        avgBreakForceLeft /= usedBreakCount;
                        breakVel /= usedBreakCount;

                        //If newParent is too small, just break those parts
                        if (ppCount < FracGlobalSettings.minParentPartCount)
                        {
                            for (int ppI = 0; ppI < ppCount; ppI++)
                            {
                                int partI = p_parts[ppI];
                                _partsNewParentI[partI] = 0;

                                _partsToBreak[partI] = new()
                                {
                                    layerI = resistanceLayer - 1,
                                    velForce = breakForce / usedBreakCount,
                                    velTarget = breakVel,
                                    partI = partI,
                                    forceLeft = avgBreakForceLeft
                                };
                            }

                            nextNewParentI--;
                            continue;
                        }

                        //needs new parent, so add one
                        breakForce /= breakCount;
                        breakPos /= usedBreakCount;
                        totUsedForce += breakForce;

                        _newParentsData.Add(nextNewParentI, new()
                        {
                            force = breakForce,
                            velocity = breakVel * Mathf.Clamp01(breakForce / (p_totMass * breakVel.magnitude)),
                            velPos = breakPos,
                            sourceParentI = (short)parentI,
                            newPartCount = ppCount,
                            forceLeft = breakForce - (p_totMass * breakVel.magnitude)
                        });
                    }

                    SKIPGETNEWPARENTS:

                    //get the velocity broken parts should get
                    breakCount = _partsToBreak.Count;
                    if (toBreakKeys.Length != breakCount) toBreakKeys = _partsToBreak.GetKeyArray(Allocator.Temp);
                    float breakForceLeft = (allTotImpForces - totUsedForce) / breakCount;
                    if (breakForceLeft < 0.0f)
                    {
                        breakForceLeft = 0.0f;
                    }

                    for (int brI = 0; brI < breakCount; brI++)
                    {
                        int partI = toBreakKeys[brI];
                        DesPartToBreak pBreak = _partsToBreak[partI];
                        float forceRequired = (pBreak.velTarget.magnitude * _desProps[_fStructs[pBreak.partI].desMatI].mass);
                        pBreak.velTarget *= Mathf.Clamp01(pBreak.forceLeft / breakCount / forceRequired);
                        //pBreak.forceLeft = breakForceLeft - forceRequired;
                        pBreak.forceLeft -= forceRequired;
                        _partsToBreak[partI] = pBreak;
                    }

                    //get what parent to keep (Keep the one with most parts as it is the slowest to set)
                    int newPCount = _newParentsData.Count;
                    NativeArray<byte> newPKeys = _newParentsData.GetKeyArray(Allocator.Temp);
                    NativeHashSet<int> usedSourcePI = new(newPCount, Allocator.Temp);

                    for (int newPI = 0; newPI < newPCount; newPI++)
                    {
                        //Get the best newParent with the most source parent parts
                        byte best_nParentI = newPKeys[newPI];
                        int sParentI = _newParentsData[best_nParentI].sourceParentI;
                        if (usedSourcePI.Add(sParentI) == false || kinSourceParentsI.Contains(sParentI) == true) continue;

                        int best_newPartCount = _newParentsData[best_nParentI].newPartCount;

                        for (int newPII = newPI + 1; newPII < newPCount; newPII++)
                        {
                            byte nParentI = newPKeys[newPII];
                            if (_newParentsData[nParentI].sourceParentI != sParentI) continue;

                            int newPartCount = _newParentsData[nParentI].newPartCount;
                            if (newPartCount <= best_newPartCount) continue;

                            best_nParentI = nParentI;
                            best_newPartCount = newPartCount;
                        }

                        //Keep the best newParent
                        DesNewParentData newPD = _newParentsData[best_nParentI];
                        newPD.newPartCount = -1;
                        _newParentsData[best_nParentI] = newPD;

                        for (int partI = 0; partI < partCount; partI++)
                        {
                            if (_partsNewParentI[partI] == best_nParentI) _partsNewParentI[partI] = 0;
                        }
                    }
                }

                unsafe void CalcSource(int sourceI)
                {
                    //get the source
                    DestructionSource desSource = _desSources[sourceI];
                    if (desSource.impForceTotal <= 0.0f || desSource.parentI < 0) return;
                    allTotImpForces += desSource.impForceTotal;

                    //reset nativeArrays
                    FractureHelperFunc.SetWholeNativeArray(ref partsMoveMass, 0.0f);//To prevent devision by zero

                    //add impact forces to forceCons
                    NativeArray<DestructionPoint> desPoints = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<DestructionPoint>(
                                        desSource.desPoints_ptr, desSource.desPoints_lenght, Allocator.Temp);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref desPoints, AtomicSafetyHandle.GetTempMemoryHandle());
#endif

                    //get all impacts that has higher force than the highest impact force * 0.9f
                    NativeArray<int> orderIToPartI = new(partCount, Allocator.Temp);//Index 0 is a impact part
                    NativeArray<int> partIToLayerI = new(partCount, Allocator.Temp);//The layer each part has

                    float maxForce = 0.0f;
                    foreach (var point in desPoints)
                    {
                        if (point.force > maxForce) maxForce = point.force;
                    }

                    maxForce *= 0.9f;
                    int nextOrderI = 0;
                    int nextLayerI = 1;//We must start at 1 since defualt is 0 and we must know if it has been assigned
                    DestructionPoint desPoint;
                    float totForceOgklk = 0.0f;

                    for (int impI = 0; impI < desPoints.Length; impI++)
                    {
                        desPoint = desPoints[impI];
                        if (_partsToBreak.ContainsKey(desPoint.partI) == true) continue;

                        if (desPoint.force > maxForce)
                        {
                            if (partIToLayerI[desPoint.partI] != 0) continue;

                            orderIToPartI[nextOrderI] = desPoint.partI;
                            nextOrderI++;

                            partIToLayerI[desPoint.partI] = nextLayerI;
                            totForceOgklk += desPoint.force;
                        }
                    }

                    //Get each part distance to any 90% impact
                    int oI = 0;

                    while (oI < nextOrderI)
                    {
                        int innerLoopStop = nextOrderI;
                        nextLayerI++;

                        while (oI < innerLoopStop)
                        {

                            FracStruct fPart = _fStructs[orderIToPartI[oI]];
                            oI++;
                            
                            for (int nI = 0; nI < fPart.neighbourPartI_lenght; nI++)
                            {
                                int npI = fPart.neighbourPartI[nI];

                                if (partIToLayerI[npI] != 0 || _partsParentI[npI] != desSource.parentI || _partsToBreak.ContainsKey(npI) == true) continue;
                                partIToLayerI[npI] = nextLayerI;
                                orderIToPartI[nextOrderI] = npI;
                                nextOrderI++;
                            }
                        }
                    }

                    //get the mass each part would need to "push"
                    int usedStartCount = Mathf.NextPowerOfTwo(partCount / nextLayerI);
                    NativeHashSet<int> usedPI = new(partCount, Allocator.Temp);
                    NativeList<int> usedTPI = new(usedStartCount, Allocator.Temp);
                    NativeHashSet<int> usedNPI = new(usedStartCount, Allocator.Temp);
                    float velDis = desSource.impVel.magnitude;
                    Vector3 velDir = desSource.impVel.normalized;
                    bool alwaysKin = (desSource.parentI == 0 && _optMainPhyType == OptMainPhysicsType.orginalIsKinematic) || _optMainPhyType == OptMainPhysicsType.alwaysKinematic;

                    for (oI = partCount - 1; oI >= 0; oI--)
                    {
                        int partI = orderIToPartI[oI];
                       
                        if (usedPI.Contains(partI) == true) continue;

                        int layerI = partIToLayerI[partI];
                        if (layerI == 0) continue;

                        int npI;
                        float resMass = 0.0f;
                        float totTransCap = 0.0f;
                        usedTPI.Clear();
                        usedNPI.Clear();
                        usedTPI.Add(partI);

                        for (int uI = 0; uI < usedTPI.Length; uI++)
                        {
                            partI = usedTPI[uI];
                            FracStruct fPart = _fStructs[partI];
                            DestructionMaterial.DesProperties desProp = _desProps[fPart.desMatI];
                            resMass += _KinParts.Contains(partI) == false && alwaysKin == false ? desProp.mass : desProp.stenght;
                            totTransCap += GetPartActualTransCap(ref fPart, ref desProp);

                            for (int nI = 0; nI < fPart.neighbourPartI_lenght; nI++)
                            {
                                npI = fPart.neighbourPartI[nI];

                                if (partIToLayerI[npI] < layerI) continue;
                                if (partIToLayerI[npI] > layerI)
                                {
                                    //partI will "push" npI
                                    if (usedNPI.Add(npI) == false) continue;
                                    resMass += partsMoveMass[npI];
                                    continue;
                                }

                                if (usedPI.Add(npI) == false) continue;
                                usedTPI.Add(npI);
                            }
                        }

                        foreach (int pI in usedTPI)
                        {
                            FracStruct fPart = _fStructs[pI];
                            DestructionMaterial.DesProperties desProp = _desProps[fPart.desMatI];
                            float pTransCap = GetPartActualTransCap(ref fPart, ref desProp);
                            partsMoveMass[pI] = resMass * (pTransCap / totTransCap);

                            //get if part should break
                            if (_KinParts.Contains(pI) == true) continue;

                            Vector3 transDir = Vector3.zero;
                            Vector3 partWPos = partsWPos[pI];
                            byte usedNeighbourCount = 0;
                            layerI = partIToLayerI[pI];

                            for (int nI = 0; nI < fPart.neighbourPartI_lenght; nI++)
                            {
                                int nPI = fPart.neighbourPartI[nI];
                                if (partIToLayerI[nPI] <= layerI) continue;

                                transDir += (partsWPos[nPI] - partWPos).normalized;
                                usedNeighbourCount++;
                            }

                            if (usedNeighbourCount == 0) continue;

                            //float forceRequired = totForceOgklk * Mathf.Clamp01((pTransCap / totTransCap) * (usedTPI.Length / 2.0f));
                            float forceRequired = totForceOgklk * Mathf.Clamp01((pTransCap / totTransCap) * Mathf.Max(1.0f, usedTPI.Length / 2.0f));
                            forceRequired = Mathf.Min((velDis * partsMoveMass[pI]) + (forceRequired - (forceRequired * Mathf.Clamp01(desProp.chockResistance * layerI))), forceRequired);
                            forceRequired -= forceRequired * Mathf.Clamp01((layerI - 1) * desProp.falloff);

                            //transDir /= usedNeighbourCount;
                            transDir.Normalize();//Maybe we should use the best dir instead of avg?
                            pTransCap *= Mathf.Clamp01(Mathf.Abs(Vector3.Dot(velDir, transDir)) + FracGlobalSettings.transDirInfluenceReduction);



                            if (pTransCap <= forceRequired)
                            {
                                partsMoveMass[pI] *= pTransCap / forceRequired;
                                fPart.maxTransportUsed = 1.0f;

                                _partsToBreak[pI] = new()
                                {
                                    partI = pI,
                                    velForce = pTransCap,
                                    velTarget = velDir * velDis,
                                    layerI = layerI,
                                    forceLeft = forceRequired - pTransCap
                                };
                            }
                            else
                            {
                                fPart.maxTransportUsed = Mathf.Max(forceRequired / pTransCap, fPart.maxTransportUsed);
                            }

                            _fStructs[pI] = fPart;
                        }
                    }
                }

                float GetPartActualTransCap(ref FracStruct _fPart, ref DestructionMaterial.DesProperties _desProp)
                {
                    return _desProp.stenght - (_desProp.stenght * _fPart.maxTransportUsed * _desProp.damageAccumulation);
                }
            }
        }

        public struct DesPartToBreak
        {
            public int partI;
            public Vector3 velTarget;
            public float velForce;
            public int layerI;
            public float forceLeft;
        }

        public struct DesNewParentData
        {
            public Vector3 velocity;
            public Vector3 velPos;
            public float force;
            public short sourceParentI;
            public int newPartCount;
            public float forceLeft;
        }

        private void ApplySkinAndDef()
        {
            //return if not ready
            if (gpuIsReady == false) return;

            //compute skinning and deformation on gpu
            if (wantToApplyDeformation == true) buf_structs_posL.SetData(jCDW_job.structPosL.AsArray());
            computeDestructionSolver.SetMatrix("fracRendWToL", fracRend.worldToLocalMatrix);
            computeDestructionSolver.Dispatch(cpKernelId_ComputeSkinDef, fracRendDividedVerCount, 1, 1);

            if (wantToApplyDeformation == true)
            {
                buf_structs_posLPrev.SetData(jCDW_job.structPosL.AsArray());//We could also always store it as array and resize it when needed, depends on how fast AsArray() is
                if (des_deformedParts[des_deformedPartsIndex].Count > 0) gpuMeshRequest_do = true;
                wantToApplyDeformation = false;
            }
        }

        Matrix4x4[] gpuMeshBonesLToW;
        GpuMeshVertex[] gpuMeshVertexData;

        private void UpdateGpuMeshReadback()
        {
            //get mesh data from readback
            byte oppositeI = (byte)(1 - des_deformedPartsIndex);

            if (gpuMeshRequest.done == true)
            {
                if (gpuMeshRequest.hasError == false)
                {

                    //gpu readback is done, get the result
                    NativeArray<GpuMeshVertex> newMD = gpuMeshRequest.GetData<GpuMeshVertex>();

                    if (gpuMeshVertexData.Length != newMD.Length) gpuMeshVertexData = new GpuMeshVertex[newMD.Length];
                    newMD.CopyTo(gpuMeshVertexData);
                }
                else if (gpuMeshVertexData.Length != fr_verticesL.Count)
                {
                    //for some reason readback result is invalid, just request again
                    RequestMeshFromGpu();
                    return;
                }

                //Update colliders from new mesh
                byte maxLoops = FracGlobalSettings.maxColliderUpdatesPerFrame;
                while (maxLoops > 0 && des_deformedParts.Length > 0)
                {
                    maxLoops--;
                    short partI = des_deformedParts[oppositeI].FirstOrDefault();
                    if (des_deformedParts[oppositeI].Remove(partI) == false) break;

                    Matrix4x4 partLToW = gpuMeshBonesLToW[partI + partBoneOffset].inverse;
                    Matrix4x4 rendWToL = fracRend.localToWorldMatrix;
                    Vector3[] partPossL = new Vector3[allParts[partI].partMeshVerts.Count];
                    short pI = 0;

                    foreach (int vI in allParts[partI].partMeshVerts)
                    {
                        partPossL[pI] = partLToW.MultiplyPoint3x4(rendWToL.MultiplyPoint3x4(gpuMeshVertexData[vI].pos));
                        pI++;
                    }

                    FractureHelperFunc.SetColliderFromFromPoints(saved_allPartsCol[partI], partPossL, ref partMaxExtent);
                }
            }

            //If all colliders from previous request has been updated and more colliders needs updating, do request
            if (gpuMeshRequest_do == true && des_deformedParts[oppositeI].Count == 0)
            {
                RequestMeshFromGpu();
                des_deformedPartsIndex = oppositeI;
                gpuMeshRequest_do = false;
            }

            void RequestMeshFromGpu()
            {
                gpuMeshRequest = AsyncGPUReadback.Request(buf_verNors);
                gpuMeshRequest.forcePlayerLoopUpdate = true;

                //We need to store all colliders matrix since they may move durring gpu->cpu transfer. The matrixes they had at request seems to always stay valid
                if (gpuMeshBonesLToW.Length != jGTD_job.fracBonesLToW.Length) gpuMeshBonesLToW = new Matrix4x4[jGTD_job.fracBonesLToW.Length];
                jGTD_job.fracBonesLToW.CopyTo(gpuMeshBonesLToW);
            }
        }

        /// <summary>
        /// The rb that caused the impact, null if caused by self or misc, always contains the same keys as destructionSources
        /// </summary>
        private Dictionary<int, Rigidbody> destructionBodies;
        private Dictionary<int, NativeArray<DestructionPoint>> destructionPairs;
        private ConcurrentDictionary<int, DestructionSource> destructionSources;//For some weird reason it does not work correctly if I replace this with a Dictionary

        public unsafe struct DestructionSource
        {
            /// <summary>
            /// The velocity that can be applied at most to parts that breaks and their parent, the opposite of this should be applied to rbSource(If any)(If == vector.zero, it will be like a chock wave)
            /// </summary>
            public Vector3 impVel;

            /// <summary>
            /// The total force applied to this object (Should be equal to all impPoints_force added togehter)
            /// </summary>
            public float impForceTotal;

            public void* desPoints_ptr;
            public int desPoints_lenght;

            /// <summary>
            /// The index of the parent all parts in this source has
            /// </summary>
            public int parentI;
        }

        public struct DestructionPoint
        {
            public float force;
            public int partI;
        }

        /// <summary>
        /// Applies force as damage to the object the next physics frame
        /// </summary>
        /// <param name="impactPoints">The nativeArray must have a Persistent allocator, it will be disposed by the destruction system. DO NOT DISPOSE IT ANYWHERE ELSE</param>
        /// <param name="sourceRb">The rb that caused the impact, null if caused by self or misc</param>
        /// <param name="impactId">Used to identify different impact sources, if == 0 a unique id will be generated</param>
        /// <param name="canOverwrite">If impactId already exists, true == overwrite existing source, false == merge with existing source</param>
        public unsafe void RegisterImpact(DestructionSource impactData, NativeArray<DestructionPoint> impactPoints, Rigidbody sourceRb, int impactId = 0, bool canOverwrite = false)
        {
            impactData.desPoints_ptr = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(impactPoints);
            impactData.desPoints_lenght = impactPoints.Length;

            lock (destructionPairs)
            {
                if (impactId == 0) impactId = destructionSources.Count;
                if (destructionSources.TryAdd(impactId, impactData) == false)
                {
                    if (canOverwrite == true) destructionSources.TryUpdate(impactId, impactData, impactData);
                    else
                    {
                        //merge the sources if cant overwrite
                        destructionSources.TryGetValue(impactId, out DestructionSource impD);
                        destructionPairs.TryGetValue(impactId, out NativeArray<DestructionPoint> impP);

                        //impP can sometimes be [writeOnly] and [readOnly] but im never declaring anything writeOnly. Spent hours debugging and still no idea
                        int ogLenght = impP.Length;
                        impP.ResizeArray(impP.Length + impactPoints.Length);
                        impactPoints.CopyTo(impP.GetSubArray(ogLenght, impactPoints.Length));


                        float tempTotalForce = impD.impForceTotal + impactData.impForceTotal;

                        if (impactData.impForceTotal > impD.impForceTotal)
                        {
                            impD.impForceTotal = impactData.impForceTotal;
                            impD.impVel = impactData.impVel;
                            destructionBodies[impactId] = sourceRb;//Should always be the same but we update it anyways just in case
                        }

                        for (int i = 0; i < impP.Length; i++)
                        {
                            DestructionPoint desP = impP[i];
                            desP.force /= tempTotalForce;
                            desP.force *= impD.impForceTotal;
                            impP[i] = desP;
                        }

                        impD.desPoints_ptr = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(impP);//We must update the pointer since we created a new instance
                        impD.desPoints_lenght = impP.Length;
                        destructionSources.TryUpdate(impactId, impD, impD);
                        destructionPairs[impactId] = impP;
                    }
                }
                else
                {
                    destructionPairs.TryAdd(impactId, impactPoints);//In theory we dont need to store impactPoints anywhere, since its Persistant and we dispose it by a pointer (No, we need it for merging)
                    destructionBodies.TryAdd(impactId, sourceRb);
                }
            }
        }

        #endregion ComputeDestruction




        #region InternalFractureData

        /// <summary>
        /// All parts the destruction system is made out off
        /// </summary>
        [System.NonSerialized] public List<FracPart> allParts = new();

        /// <summary>
        /// All parents. All connected fracParts share the same fracParent
        /// </summary>
        public List<FracParent> allParents = new();

        /// <summary>
        /// Used to compute destruction, the position of the struct in its part localspace
        /// </summary>
        //[System.NonSerialized] public NativeList<Vector3> structs_posL = new();

        /// <summary>
        /// Used to compute destruction, the parent part X has
        /// </summary>
        //[System.NonSerialized] public NativeList<int> structs_parentI = new();

        /// <summary>
        /// Contains the index of all parents that should be updated the next frame
        /// </summary>
        private HashSet<int> parentsThatNeedsUpdating = new();

        /// <summary>
        /// What parent, position... all parts had when it was added to the system
        /// </summary>
        [SerializeField] private List<FracPartDefualt> partsDefualtData = new();

        /// <summary>
        /// Contains all part indexes that is always kinematic (Kinematic overlap or kinematic groupData)
        /// </summary>
        //[System.NonSerialized] public NativeHashSet<int> partsKinematicStatus = new();

        /// <summary>
        /// The global handler, must always exists if it is fractured
        /// </summary>
        [SerializeField] private FractureGlobalHandler globalHandler;

        [System.Serializable]
        public class IntList
        {
            public List<int> intList = new();

            public static IntList[] FromIntArray(List<int>[] intArray)
            {
                IntList[] resultList = new IntList[intArray.Length];

                for (int i = 0; i < intArray.Length; i++)
                {
                    resultList[i] = new IntList();
                    resultList[i].intList.AddRange(intArray[i]);
                }

                return resultList;
            }
        }

        #endregion InternalFractureData

        #region HelperFunctions

        /// <summary>
        /// Returns true if a nearby kinematic collider exists, nearPartIndexes will contain the index of all nearby parts unless justWantKinematic is true
        /// </summary>
        /// <param name="centerCol">All colliders that are overlaping with this one count as nearby</param>
        public bool GetNearbyFracColliders(Collider centerCol, GenerationQuality checkQuality, out List<short> nearPartIndexes, bool justWantKinematic = false)
        {
            //do box overlaps to get all nearby colliders
            PhysicsScene phyScene = centerCol.gameObject.scene.GetPhysicsScene();
            Collider[] lapCols = new Collider[20];

            int lapCount = phyScene.OverlapBox(centerCol.bounds.center,
                        centerCol.bounds.extents * 1.05f,
                        lapCols,
                        Quaternion.identity,
                        Physics.AllLayers,
                        QueryTriggerInteraction.Ignore);

            //Get nearby parts and kinematic objects from nearby colliders
            bool partIsKin = false;
            nearPartIndexes = new();

            for (int i = 0; i < lapCount; i++)
            {
                if (lapCols[i] == centerCol) continue;

                if (checkQuality == GenerationQuality.high)
                {
                    if (Physics.ComputePenetration(//Accurate overlaps (It will still depend on collider type, 
                                                   //is that fine or do I need to use the actuall meshes to get overlaps for parts?)
                        centerCol,
                        Vector3.MoveTowards(centerCol.transform.position,
                        lapCols[i].transform.position, FracGlobalSettings.worldScale * 0.001f),
                        centerCol.transform.rotation,
                        lapCols[i],
                        lapCols[i].transform.position,
                        lapCols[i].transform.rotation,
                        out _, out _) == false) continue;
                }

                short partI = TryGetPartIndexFromTrans(lapCols[i].transform);

                if (partI < 0)
                {
                    //near col is not a part, get if its kinematic
                    if (partIsKin == true) continue;

                    partIsKin = lapCols[i].attachedRigidbody == null || lapCols[i].attachedRigidbody.isKinematic == true;
                    if (partIsKin == true && justWantKinematic == true) return true;

                    continue;
                }

                //near col is part
                nearPartIndexes.Add(partI);
            }

            return partIsKin;
        }

#if UNITY_EDITOR
        //debug stopwatch
        private System.Diagnostics.Stopwatch stopwatch = new();

        public void Debug_toggleTimer(string note = "")
        {
            if (stopwatch.IsRunning == false)
            {
                stopwatch.Restart();
            }
            else
            {
                stopwatch.Stop();
                Debug.Log(note + " time: " + stopwatch.Elapsed.TotalMilliseconds + "ms");
            }
        }
#endif

        /// <summary>
        /// Returns the index of the part that uses the given transform. returns -1 if no part uses the given trans
        /// </summary>
        public short TryGetPartIndexFromTrans(Transform trans)
        {
            for (short partI = 0; partI < saved_allPartsCol.Count; partI++)
            {
                if (fr_bones[partI + partBoneOffset] == trans) return partI;
            }

            return -1;

            ////The part index is stored in the transform name
            //Match match = Regex.Match(trans.name, @"Part\((\d+)\)");
            //
            //if (match.Success == true && int.TryParse(match.Groups[1].Value, out int partId) == true) return partId;
            //
            //return -1;
        }

        /// <summary>
        /// Contains fracRend vertics in worldspace skinned from fr_verticsL (Call SkinVertexIndexes() to skin some)
        /// </summary>
        private List<Vector3> skinnedVerticsW = new();
        private BoneWeight mBake_weight;
        private Matrix4x4 mBake_bm0;
        private Matrix4x4 mBake_bm1;
        private Matrix4x4 mBake_bm2;
        private Matrix4x4 mBake_bm3;
        private Matrix4x4 mBake_vms = new();

        /// <summary>
        /// Skins the fracRend vertics used by the given part and assigns them to skinnedVerticsW (Make sure SkinVertexIndexes_prepare() has been called after any changes)
        /// </summary>
        private void SkinPartVertics(int partI)
        {
            foreach (int vI in allParts[partI].partMeshVerts)
            {
                //Storing all mBake_vms in a array and only updating them when a bone has moved may be worth it??
                mBake_weight = fr_boneWeightsCurrent[vI];
                mBake_bm0 = jCDW_job.boneBindsLToW[mBake_weight.boneIndex0];
                mBake_bm1 = jCDW_job.boneBindsLToW[mBake_weight.boneIndex1];
                mBake_bm2 = jCDW_job.boneBindsLToW[mBake_weight.boneIndex2];
                mBake_bm3 = jCDW_job.boneBindsLToW[mBake_weight.boneIndex3];

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

                skinnedVerticsW[vI] = mBake_vms.MultiplyPoint3x4(fr_verticesL[vI]);
            }
        }

        /// <summary>
        /// Returns the inside version of the given material, returns insideMat_fallback if inside version does not exists or always at runtime (Returned mat can be null)
        /// </summary>
        private Material TryGetInsideMatFromMat(Material mat)
        {
            //get if inside material exists
            if (mat == null) return null;
            Material insideMat = null;

#if UNITY_EDITOR
            if (Application.isPlaying == false) //Just to make runtime edit behave more like build
            {
                string path = AssetDatabase.GetAssetPath(mat);
                if (path == null || path.Length < 3) return null;

                int lastIndex = path.LastIndexOf('/') + 1;
                path = (lastIndex > 0 ? path[..lastIndex] : path) + mat.name + insideMat_nameAddition + ".mat";

                insideMat = (Material)AssetDatabase.LoadAssetAtPath(path, typeof(Material));
            }

            if (insideMat != null) return insideMat;
#endif
            return insideMat_fallback;
        }

        /// <summary>
        /// The prefab type it had when the fracture was generated (0 if no prefab, 1 if prefab instance, 2 if prefab asset)
        /// </summary>
        public byte fracPrefabType = 0;

        /// <summary>
        /// Returns 0 if no prefab, 1 if prefab instance, 2 if prefab asset (Will always return 0 at runtime)
        /// </summary>
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

        /// <summary>
        /// Returns the rough force that partI can "consume" if X collided with partI at the given velocity.
        /// In other words, rougly how much force partI can take before it either breaks or gets pushed away
        /// </summary>
        public float GuessMaxForceConsume(Vector3 velocity, short partI, float bouncyness = 0.0f)
        {
            int parentI = jCDW_job.partsParentI[partI];
            DestructionMaterial desMat = GetDesMatFromIntId(allParts[partI].groupIdInt);
            float velSpeed = velocity.magnitude;
            if (parentI < 0)
            {
                //if no parent, it cant break so it has infinit stenght
                if (jCDW_job.kinematicPartIndexes.Contains(partI) == true) return float.MaxValue;

                velSpeed *= desMat.desProps.mass;
                velSpeed += velSpeed * bouncyness * FracGlobalSettings.bouncynessEnergyConsumption;
                return velSpeed + (velSpeed * desMat.desProps.falloff);
            }

            float impForce = allParents[parentI].parentKinematic > 0 ? float.MaxValue : velSpeed * (allParents[parentI].parentMass - desMat.desProps.mass);

            float totalTransportCap = desMat.desProps.stenght * jCDW_job.fStructs[partI].neighbourPartI_lenght;
            if (impForce > totalTransportCap) impForce = totalTransportCap;//We currently do not consider transportMaxDamage, we probably wanna do to that

            int parentPartCount = allParents[parentI].partIndexes.Count;
            impForce += impForce * (allParents[parentI].totalStiffness / parentPartCount);
            impForce += velSpeed * desMat.desProps.mass;
            impForce += impForce * bouncyness * FracGlobalSettings.bouncynessEnergyConsumption;
            return impForce + (impForce * (allParents[parentI].totalTransportCoEfficiency / parentPartCount));
        }

        /// <summary>
        /// Returns the rough force that partI can apply to X if partI would collide with X at the given velocity
        /// </summary>
        public float GuessMaxForceApplied(Vector3 velocity, short partI, float bouncyness = 0.0f)
        {
            //Fix later, These needs rethinking since we have redesigned how destruction works
            int parentI = jCDW_job.partsParentI[partI];
            DestructionMaterial desMat = GetDesMatFromIntId(allParts[partI].groupIdInt);
            float velSpeed = velocity.magnitude;
            if (parentI < 0)
            {
                //if no parent, it cant break so it has infinit stenght
                if (jCDW_job.kinematicPartIndexes.Contains(partI) == true) return float.MaxValue;

                velSpeed *= desMat.desProps.mass;
                velSpeed -= velSpeed * bouncyness * FracGlobalSettings.bouncynessEnergyConsumption;
                return velSpeed - (velSpeed * desMat.desProps.falloff);
            }

            float impForce = allParents[parentI].parentKinematic > 0 ? float.MaxValue : velSpeed * (allParents[parentI].parentMass - desMat.desProps.mass);

            int parentPartCount = allParents[parentI].partIndexes.Count;
            impForce -= impForce * (allParents[parentI].totalStiffness / parentPartCount);

            float totalTransportCap = desMat.desProps.stenght * jCDW_job.fStructs[partI].neighbourPartI_lenght;
            if (impForce > totalTransportCap) impForce = totalTransportCap;//We currently do not consider transportMaxDamage, we probably wanna do to that

            impForce += velSpeed * desMat.desProps.mass;
            impForce -= impForce * bouncyness * FracGlobalSettings.bouncynessEnergyConsumption;
            return impForce - (impForce * (allParents[parentI].totalTransportCoEfficiency / parentPartCount));
        }

        /// <summary>
        /// The lowest TransportCapacity any part has
        /// </summary>
        private float lowestTransportCapacity = 0.0f;

        /// <summary>
        /// Returns true if applying the given force on partI is likely to cause any noticiable destruction on the object
        /// </summary>
        public bool GuessIfForceCanCauseBreaking(float force, int partI, float bouncyness = 0.0f)
        {
            //fix later, we have changed what stiffness does
            int parentI = jCDW_job.partsParentI[partI];
            int parentPartCount = allParents[parentI].partIndexes.Count;
            FracStruct fPart = jCDW_job.fStructs[partI];
            DestructionMaterial.DesProperties desProp = destructionMaterials[fPart.desMatI].desProps;

            float transCap = desProp.stenght - (desProp.stenght * fPart.maxTransportUsed * desProp.damageAccumulation);
            transCap *= Mathf.Clamp01(0.25f + FracGlobalSettings.transDirInfluenceReduction); 
            force -= force * bouncyness * FracGlobalSettings.bouncynessEnergyConsumption;

            return force > transCap;
        }
#endregion HelperFunctions
    }
}