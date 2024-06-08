using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using Debug = UnityEngine.Debug;
using System;
using Component = UnityEngine.Component;
using System.Data;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Jobs;
using Unity.Burst;
using UnityEngine.Rendering;
using System.Collections.Concurrent;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.SceneManagement;
using UnityEngine.Assertions.Must;


#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Zombie1111_uDestruction
{
    [ExecuteInEditMode]
    [DefaultExecutionOrder(0)]
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
                "fracRend", "fr_bones", "partBoneOffset", "isRealSkinnedM", "allParents", "globalHandler", "fracPrefabType"
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
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("repairSpeed"), true);

                        EditorGUILayout.Space();
                        EditorGUILayout.HelpBox("Modifying destructionMaterials at runtime may cause issues and should only be used to temporarly test different values", MessageType.Warning);
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("destructionMaterials"), true);
                    }

                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox("Properties cannot be edited while fractured", MessageType.Info);

                    GUI.enabled = false;
                    DrawPropertiesExcluding(serializedObject, new string[]
                    { "m_Script",
                        "debugMode",
                        Application.isPlaying == true ? "destructionMaterials" : "",
                        Application.isPlaying == true ? "repairSpeed" : ""
                    });

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
        [SerializeField] private float repairSpeed = 10.0f;

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
                        if (FracHelpFunc.Gd_isIdInColor(gId, verCols[vI]) == true)
                        {
                            Gizmos.DrawCube(vers[vI], drawBoxSize);
                            continue;
                        }
                    }
                }
            }

        skipDrawGroups:

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

        private void OnValidate()
        {
            if (eOnly_ignoreNextDraw == true) return;

            //for temporarly testing destruction material properties
            if (Application.isPlaying == true && jCDW_jobIsActive == false)
            {
                int desPropI = 0;
                foreach (DestructionMaterial.DesProperties desProp in destructionMaterials.Select(desMat => desMat.desProps))
                {
                    jCDW_job.desProps[desPropI] = desProp;
                    desPropI++;
                }

                int bendPropI = 0;
                foreach (DestructionMaterial.BendProperties bendProp in destructionMaterials.Select(desMat => desMat.bendProps))
                {
                    jCDW_job.bendProps[bendPropI] = bendProp;
                    bendPropI++;
                }

                buf_bendProperties.SetData(jCDW_job.bendProps);
            }
        }


#endif

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
            normal = 1,
            high = 2,
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
            public float massMultiplier = 0.5f;
        }

        [System.Serializable]
        private class OptPhysicsParts
        {
            public OptPartPhysicsType partPhysicsType = OptPartPhysicsType.rigidbody_medium;
            public bool useGravity = true;
            public float massMultiplier = 4.0f;
            public float drag = 0.0f;
            public float angularDrag = 0.05f;
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
            orginalIsManuall,
            manuall
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

            //Update defualt parent, There is always 1 parent when loading
            fracInstanceId = this.GetInstanceID();
            for (int i = 0; i < allParents[0].parentRbs.Count; i++)
            {
                if (allParents[0].parentRbs[i].rb == null)
                {
                    Debug.LogError("A rigidbody used by " + transform.name + " has been destroyed, this is not allowed!");
                    return false;
                }

                allParents[0].parentRbs[i].rbId = allParents[0].parentRbs[i].rb.GetInstanceID();
                allParents[0].parentRbs[i].rbIsKinByDefualt = allParents[0].parentRbs[i].rb.isKinematic;
                //(Not needed) allParents[0].parentRb.maxDepenetrationVelocity = FracGlobalSettings.desRbMaxDepenetrationVelocity;
            }

            MarkParentAsModified(0);

            //update renderer
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
                        || FracHelpFunc.GetIfTransformIsAnyParent(transform, ogD.comp.transform) == false) continue;

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

            if (parentTemplate != null) DestroyImmediate(parentTemplate);

            //set debug mode to none, since debugMode can freeze if huge fracture its safer to have it always defualt to none to prevent "softlock"
#if UNITY_EDITOR
            debugMode = DebugMode.none;
#endif

            //clear saved variabels
            repairIsSetup = false;
            saved_allPartsCol = new();
            allParts = new();
            allParents = new();
            partsLocalParentPath = new();
            localPathToRbIndex = new();
            parentsThatNeedsUpdating = new();
            syncFR_modifiedPartsI = new();
            ClearUsedGpuAndCpuMemory();
            jCDW_job = new()
            {
                structPosL = new NativeList<Vector3>(Allocator.Persistent),
                partsParentI = new NativeList<int>(Allocator.Persistent),
                parentPartCount = new NativeList<short>(Allocator.Persistent),
                kinematicPartIndexes = new NativeHashSet<int>(0, Allocator.Persistent),
                fStructs = new NativeList<FracStruct>(Allocator.Persistent),
                partIToDesMatI = new NativeList<int>(Allocator.Persistent)
            };

            //remove defualt desMaterial override
            if (destructionMaterials != null && destructionMaterials.Count > 0
                && (destructionMaterials[0].affectedGroupIndexes == null || destructionMaterials[0].affectedGroupIndexes.Count == 0)
                && destructionMaterials[0].desProps.stenght == defualtDestructionMaterial.stenght
                && destructionMaterials[0].objLayerBroken == defualtDestructionMaterial.objLayerBroken
                && destructionMaterials[0].bendProps.bendyness == defualtDestructionMaterial.bendyness)
            {
                destructionMaterials.RemoveAt(0);
            }

            //return if dont save og
#if UNITY_EDITOR
            if (didChangeAnything == true) EditorUtility.SetDirty(objToUse);
#endif
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
            public List<FracRb> parentRbs;
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

            [System.Serializable]
            public class FracRb
            {
                public Rigidbody rb;
                public int rbId;
                public float rbDesMass;
                public int rbKinCount;
                public int rbPartCount;
                public bool rbIsKinByDefualt;
                public bool rbIsKin;
            }
        }

        [System.Serializable]
        public unsafe struct FracStruct
        {
            /// <summary>
            /// The destruction material index this part uses
            /// </summary>
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
            public List<int> partColVerts = new();

            /// <summary>
            /// The index of all other structs this struct is connected with
            /// </summary>
            //public List<short> neighbourStructs;
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

            if (SystemInfo.supportsComputeShaders == false)
            {
                Debug.LogError("The current device does not support ComputeShaders, destruction wont work!");
                RemoveCorrupt();
                return false;
            }

            if (SystemInfo.supportsAsyncGPUReadback == false)
            {
                Debug.LogWarning("The current device does not support AsyncGPUReadback, destruction can be expected to cause noticeable fps drops!");
            }

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
#if UNITY_EDITOR
                //Always clear the progressbar
                EditorUtility.ClearProgressBar();
#endif
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
                List<FracMesh> partMeshesW = Gen_fractureMeshes(meshesToFracW, fractureCount, dynamicFractureCount, worldScaleDis, seed);
                if (partMeshesW == null) return CancelFracturing();

                //save orginal data (save as late as possible)
                if (UpdateProgressBar("Saving orginal objects") == false) return CancelFracturing();
                Gen_loadAndMaybeSaveOgData(true);

                //setup fracture renderer
                if (UpdateProgressBar("Creating renderer") == false) return CancelFracturing();
                Gen_setupFracRend(meshesToFracW, skinRendSource);

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
                if (UpdateProgressBar("Verifying") == false) return CancelFracturing();

                if (GetConnectedPartCount(0, out int missingPart) != allParts.Count) Debug.LogWarning("Not all parts in " + transform.name + " are connected with each other, part " + missingPart + " is not connected");
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

            unsafe int GetConnectedPartCount(int partI, out int missingPart)
            {
                int partCount = allParts.Count;
                List<int> partsToSearch = new(partCount) { partI };
                HashSet<int> searchedParts = new(partCount) { partI };

                for (int i = 0; i < partsToSearch.Count; i++)
                {
                    FracStruct fPart = jCDW_job.fStructs[partsToSearch[i]];

                    for (int nI = 0; nI < fPart.neighbourPartI_lenght; nI++)
                    {
                        int nPI = fPart.neighbourPartI[nI];
                        if (searchedParts.Add(nPI) == false) continue;
                        partsToSearch.Add(nPI);
                    }
                }

                missingPart = -1;
                for (int pI = 0; pI <= partCount; pI++)
                {
                    if (searchedParts.Contains(pI) == true) continue;

                    missingPart = pI;
                    break;
                }

                return searchedParts.Count;
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

#pragma warning disable CS0162 // Unreachable code detected
                return true;
#pragma warning restore CS0162 // Unreachable code detected
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

            /// <summary>
            /// The parent the object should have when the part is not broken
            /// </summary>
            public Transform defualtParent;
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
#if UNITY_2023_1_OR_NEWER
            public PhysicsMaterial phyMat;
#else
            public PhysicMaterial phyMat;
#endif
            public bool isKinematic = false;
            public byte objLayerDefualt = 0;
            public byte objLayerBroken = 0;
            public DesProperties desProps = new()
            {
                mass = 0.1f,
                stenght = 40.0f,
                falloff = 0.2f,
                chockResistance = 0.4f,
                damageAccumulation = 0.5f,
            };

            public BendProperties bendProps = new()
            {
                bendyness = 0.25f,
                bendStrenght = 120.0f,
                bendFalloff = 80.0f,
                bendPower = 0.02f
            };

            [System.Serializable]
            public struct DesProperties
            {
                public float mass;
                public float stenght;
                public float falloff;
                public float chockResistance;

                /// <summary>
                /// How much less X can transport depending on how much force it has recieved at most. (actualTransportCapacity = transportCapacity - (maxForceRecieved * transportMaxDamage)) 
                /// </summary>
                public float damageAccumulation;
            }

            [System.Serializable]
            public struct BendProperties
            {
                public float bendyness;
                public float bendStrenght;
                public float bendFalloff;
                public float bendPower;
            }
        }

        [System.Serializable]
        private class DefualtDesMatOptions
        {
#if UNITY_2023_1_OR_NEWER
            public PhysicsMaterial phyMat;
#else
            public PhysicMaterial phyMat;
#endif
            public float mass = 0.1f;
            public byte objLayerBroken = 0;
            public float stenght = 40.0f;
            public float falloff = 0.2f;
            public float chockResistance = 0.4f;
            public float damageAccumulation = 0.5f;
            public float bendyness = 0.25f;
            public float bendStrenght = 120.0f;
            public float bendFalloff = 80.0f;
            public float bendPower = 0.02f;
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
                    stenght = defualtDestructionMaterial.stenght,
                    falloff = defualtDestructionMaterial.falloff,
                    chockResistance = defualtDestructionMaterial.chockResistance,
                    damageAccumulation = defualtDestructionMaterial.damageAccumulation,
                },
                bendProps = new()
                {
                    bendyness = defualtDestructionMaterial.bendyness,
                    bendStrenght = defualtDestructionMaterial.bendStrenght,
                    bendPower = defualtDestructionMaterial.bendPower,
                    bendFalloff = defualtDestructionMaterial.bendFalloff
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

                    groupIntIdToGroupIndex.Add(FracHelpFunc.Gd_getIntIdFromId(md_verGroupIds[groupI]), overrideI);
                }
            }
        }

        //fracRend mesh will be set from all fr_[] variabels when synced, they should only be modified by destructionSystem
        public MeshFilter fracFilter;
        public MeshRenderer fracRend;

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
        /// The Gameobject to clone when creating a new parent
        /// </summary>
        [SerializeField] private GameObject parentTemplate = null;

        /// <summary>
        /// Sets fracRend defualt values so its ready to get parts added to it, returns true if valid fracRend
        /// </summary>
        private bool Gen_setupFracRend(List<FracSource> fracSources, SkinSourceData skinRendSource = null)
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
            Transform bestChild = null;
 
            if (skinRendSource != null) bestChild = skinRendSource.rootBone;
            else
            {
                int bestCount = -1;

                for (int childI = 0; childI < transform.childCount; childI++)
                {
                    Transform thisChild = transform.GetChild(childI);
                    if (FracHelpFunc.TransformHasUniformScale(thisChild) == false) continue;

                    int thisCount = 0;

                    foreach (FracSource source in fracSources)
                    {
                        if (source.sRend == null || FracHelpFunc.GetIfTransformIsAnyParent(thisChild, source.sRend.transform) == false) continue;
                        thisCount++;
                    }

                    if (thisCount <= bestCount) continue;

                    bestCount = thisCount;
                    bestChild = thisChild;
                }
            }

            if (bestChild == null || bestChild == transform)
            {
                Debug.LogError(transform.name + " does not have a valid transform to be used as defualt parent");
                return false;
            }

            //Create parent template
            parentTemplate = new GameObject(transform.name + "_parent");
            parentTemplate.transform.SetParent(transform);
            CreateTemplateIteration(bestChild, parentTemplate.transform);
            List<Rigidbody> rbOrder = new();
            parentTemplate.GetComponentsInChildren<Rigidbody>(rbOrder);

            foreach (Transform child in parentTemplate.GetComponentsInChildren<Transform>())
            {
                Rigidbody rb = child.GetComponentInParent<Rigidbody>();
                if (rb == null) continue;
                localPathToRbIndex.Add(FracHelpFunc.EncodeHierarchyPath(child, parentTemplate.transform), rbOrder.IndexOf(rb));

                if (rb.collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic || rb.collisionDetectionMode == CollisionDetectionMode.Continuous)
                    Debug.LogWarning(child.name + " has a rigidbody with " + rb.collisionDetectionMode + " collisionDetectionMode," +
                        " the destruction system is more stable with Discrete or ContinuousSpeculative");

                if (rb.interpolation != RigidbodyInterpolation.None)
                {
                    Debug.LogWarning(child.name + " had a rigidbody with interpolation, interpolation is not supported by the destruction system and it was disabled");
                    rb.interpolation = RigidbodyInterpolation.None;
                }
            }

            parentTemplate.SetActive(false);
            CreateNewParent(bestChild);
            

            return true;

            static void CreateTemplateIteration(Transform sourceTrans, Transform templateTrans)
            {
                if (sourceTrans.TryGetComponent(out Rigidbody rb) == true) FracHelpFunc.CopyRigidbody(rb, templateTrans.gameObject);
                bool keepRest = false;

                for (int i = sourceTrans.childCount - 1; i >= 0; i--)
                {
                    Transform child = sourceTrans.GetChild(i);
                    if (keepRest == false && (child.GetComponentInChildren<Renderer>(true) == false || child.gameObject.activeInHierarchy == false)) continue;

                    keepRest = true;
                    Transform newTrans = new GameObject(child.name + "_parentChild" + i).transform;
                    newTrans.SetParent(templateTrans);
                    newTrans.SetAsFirstSibling();
                    newTrans.localScale = child.localScale;
                    newTrans.SetPositionAndRotation(child.position, child.rotation);

                    CreateTemplateIteration(child, newTrans);
                }
            }
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

        private ComputeBuffer buf_boneBindsLToW;
        private ComputeBuffer buf_meshData;
        private ComputeBuffer buf_fr_boneWeightsCurrent;
        private ComputeBuffer buf_partIToParentI;
        private ComputeBuffer buf_bendProperties;
        private ComputeBuffer buf_partIToBendPropI;
        private ComputeBuffer buf_defPoints;
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
            public int verToPartI;
        };

        /// <summary>
        /// Call to assign fracRend with data from fr_[] and sync with gpu (RequestSyncFracRendData() should be used instead to prevent SyncFracRendData() from being called many times a frame)
        /// </summary>
        private unsafe void SyncFracRendData()
        {
            gpuIsReady = false;
            wantToSyncFracRendData = false;

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
            fracFilter.sharedMesh.SetVertices(fr_verticesL, 0, fr_verticesL.Count, MeshUpdateFlags.DontValidateIndices);
            fracFilter.sharedMesh.SetNormals(fr_normalsL, 0, fr_normalsL.Count, MeshUpdateFlags.DontValidateIndices);
            fracFilter.sharedMesh.SetUVs(0, fr_uvs, 0, fr_uvs.Count, MeshUpdateFlags.DontValidateIndices);
            FracHelpFunc.SetListLenght(ref skinnedVerticsW, fr_verticesL.Count);

            //set rend submeshes
            fracFilter.sharedMesh.subMeshCount = fr_subTris.Count;

            for (int subI = 0; subI < fr_subTris.Count; subI++)
            {
                fracFilter.sharedMesh.SetTriangles(fr_subTris[subI], subI);
            }

            //fracFilter.sharedMesh.RecalculateNormals();
            fracFilter.sharedMesh.RecalculateTangents(MeshUpdateFlags.DontValidateIndices);
            fracFilter.sharedMesh.RecalculateUVDistributionMetric(0);

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
                    cpKernelId_RestoreSkinDef = computeDestructionSolver.FindKernel("RestoreSkinDef");
                }

                //sync vertics, normals, verToPartI and fracWeightsI with gpu
                if (fr_verticesL.Count > 0 && fr_normalsL.Count > 0)
                {
                    int verCount = fr_verticesL.Count;

                    MeshData[] newMD = new MeshData[verCount];
                    for (int vI = 0; vI < verCount; vI++)
                    {
                        newMD[vI] = new()
                        {
                            vertexL = fr_verticesL[vI],
                            normalL = fr_normalsL[vI],
                            verToPartI = fr_verToPartI[vI],
                        };
                    }

                    buf_meshData = new ComputeBuffer(verCount,
                       (sizeof(float) * 6) + (sizeof(int)));

                    bufR_og_frMeshData = new ComputeBuffer(verCount,
                       (sizeof(float) * 6) + (sizeof(int)));

                    buf_meshData.SetData(newMD);
                    bufR_og_frMeshData.SetData(newMD);//Optimization, only update the new added vertics (Needed if wanna support runtime creation)
                    computeDestructionSolver.SetBuffer(cpKernelId_ComputeSkinDef, "fr_meshData", buf_meshData);
                    computeDestructionSolver.SetBuffer(cpKernelId_RestoreSkinDef, "fr_meshData", buf_meshData);
                    computeDestructionSolver.SetBuffer(cpKernelId_RestoreSkinDef, "og_frMeshData", bufR_og_frMeshData);
                }

                //part related buffers only needs updating if parts has been modified
                if (fr_boneWeightsCurrent != null && fr_boneWeightsCurrent.Count > 0
                    && (syncFR_modifiedPartsI.Count > 0 || buf_partIToParentI == null))
                {
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
                        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
                        new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, 1),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 1)
                    );

                    buf_verNors = mesh.GetVertexBuffer(index: 0);
                    computeDestructionSolver.SetBuffer(cpKernelId_ComputeSkinDef, ShaderIDs.verNors, buf_verNors);
                    fracFilter.sharedMesh = mesh;

                    //sync structs pos and parents
                    buf_partIToParentI = new ComputeBuffer(jCDW_job.partsParentI.Length,
                        sizeof(int));

                    buf_partIToParentI.SetData(jCDW_job.partsParentI.AsArray());
                    computeDestructionSolver.SetBuffer(cpKernelId_ComputeSkinDef, "partIToParentI", buf_partIToParentI);
                    computeDestructionSolver.SetInt("partBoneOffset", partBoneOffset);

                    //sync deformation stuff
                    ComputeDestruction_setupGpu();
                }

                computeDestructionSolver.SetInt("defPointsLenght", 0);

                gpuIsReady = true;
                wantToApplySkinning = true;
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
            Transform sourceTrans = fMesh.sourceM.sRend.transform;

            pTrans.gameObject.layer = sourceTrans.gameObject.layer;
            pTrans.gameObject.tag = sourceTrans.gameObject.tag;
            fObj.defualtParent = GetDefualtParent(sourceTrans);
            pTrans.SetPositionAndRotation(
                FracHelpFunc.GetGeometricCenterOfPositions(fMesh.meshW.vertices),
                fObj.defualtParent != null ? fObj.defualtParent.rotation : sourceTrans.rotation);

            fObj.meshW = fMesh.meshW;
            fObj.col = Gen_createPartCollider(pTrans, fObj.meshW, GetDesMatFromIntId(FracHelpFunc.Gd_getIntIdFromId(fMesh.groupId)).phyMat);

            //get inside vers before we modify fObj.mesh
            HashSet<int> insideVers = FracHelpFunc.GetAllVersInSubMesh(fObj.meshW, 1);

            //get fObject materials
            FracHelpFunc.GetMostSimilarTris(fObj.meshW, fMesh.sourceM.meshW, out int[] nVersBestSVer, out int[] nTrisBestSTri, FracGlobalSettings.worldScale);
            List<int> nSubSSubI = FracHelpFunc.SetMeshFromOther(ref fObj.meshW, fMesh.sourceM.meshW, nVersBestSVer, nTrisBestSTri, fMesh.sourceM.trisSubMeshI, false);

            fObj.mMaterials = new();
            for (int nsI = 0; nsI < nSubSSubI.Count; nsI++)
            {
                fObj.mMaterials.Add(fMesh.sourceM.mMats[nSubSSubI[nsI]]);
            }

            if (matToInsideMat != null) FracHelpFunc.SetMeshInsideMats(ref fObj.meshW, ref fObj.mMaterials, insideVers, matToInsideMat); ;

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
                FracHelpFunc.GetLinksFromColors(usedSColors, ref gLinks);
                fObj.groupLinks = gLinks;
            }
            else fObj.groupLinks = new();

            fObj.groupId = fMesh.groupId;

            //return the new fObject
            return fObj;

            Transform GetDefualtParent(Transform trans)
            {
                while (trans != null && FracHelpFunc.TransformHasUniformScale(trans) == false)
                {
                    trans = trans.parent;
                }

                if (trans == null || FracHelpFunc.GetIfTransformIsAnyParent(allParents[0].parentTrans, trans) == false) return null;
                return trans;
            }
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
                partColVerts = new(),
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

            //partsDefualtParentTrans.Add(fObj.defualtParent);
            allParts.Add(newPart);
            fr_bones.Add(newTrans);
            saved_allPartsCol.Add(fObj.col);

            //create new fracStruct, the fracStruct will be added to jCDW_job.fStructs later in SetPartNeighboursAndConnections();
            FracStruct newStruct = new()
            {
                neighbourPartI_lenght = 0,
                maxTransportUsed = 0.0f
            };

            //get part connection
            int partMainNeighbour = -1;
            SetPartNeighboursAndConnections();

            //get part collider vertics
            Vector3[] fObjMeshWVerts = fObj.meshW.vertices;
            int partVerCount = fObj.meshW.vertexCount;
            int newVerOffset = fr_verticesL.Count;
            int newVerEndI = newVerOffset + partVerCount;
            int pVI = 0;
            float worldScaleDis = FracGlobalSettings.worldScale * 0.0001f;
            worldScaleDis *= worldScaleDis;

            HashSet<int> usedPartVers = new();

            for (int nvI = newVerOffset; nvI < newVerEndI; nvI++)
            {
                bool isNewVerPos = true;

                foreach (int uPVI in usedPartVers)
                {
                    if ((fObjMeshWVerts[pVI] - fObjMeshWVerts[uPVI]).sqrMagnitude > worldScaleDis) continue;
                
                    isNewVerPos = false;
                    break;
                }

                if (isNewVerPos == false)
                {
                    pVI++;
                    continue;
                }

                usedPartVers.Add(pVI);
                newPart.partColVerts.Add(nvI);
                pVI++;
            }

            //add part mesh to fracture renderer
            Matrix4x4 rendWtoL = fracRend.transform.worldToLocalMatrix;
            int newBoneI = fr_bones.Count - 1;

            fr_bindPoses.Add(fObj.col.transform.worldToLocalMatrix * fracRend.transform.localToWorldMatrix);
            jCDW_job.structPosL.Add((fr_bones[newPartI + partBoneOffset].localToWorldMatrix * fr_bindPoses[newPartI + partBoneOffset]).inverse.MultiplyPoint3x4(GetPartWorldPosition(newPartI)));
            fr_verticesL.AddRange(FracHelpFunc.ConvertPositionsWithMatrix(fObjMeshWVerts, rendWtoL));
            fr_normalsL.AddRange(FracHelpFunc.ConvertDirectionsWithMatrix(fObj.meshW.normals, rendWtoL));
            fr_verToPartI.AddRange(Enumerable.Repeat((int)newPartI, fObj.meshW.vertexCount));
            fr_uvs.AddRange(fObj.meshW.uv);

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
                    else backupBoneWe = fr_boneWeightsSkin[allParts[partMainNeighbour].partColVerts[0]];

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
                newPart.groupIdInt = FracHelpFunc.Gd_getIntIdFromId(newPart.groupId);
                //newStruct.desMatI = GetDesMatIndexFromIntId(newPart.groupIdInt);
                jCDW_job.partIToDesMatI.Add(GetDesMatIndexFromIntId(newPart.groupIdInt));
                jCDW_job.fStructs.Add(newStruct);
                partGroupD = GetDesMatFromIntId(newPart.groupIdInt);

                //set part parent
                jCDW_job.partsParentI.Add(-6969);
                if (newPartParentI <= 0 && nearPartIndexes.Count > 0) newPartParentI = jCDW_job.partsParentI[nearPartIndexes[0]];
                if (newPartParentI < 0) newPartParentI = CreateNewParent(null);

                partsLocalParentPath.Add(FracHelpFunc.EncodeHierarchyPath(fObj.defualtParent, allParents[newPartParentI].parentTrans));
                SetPartParent(newPartI, newPartParentI);

                //add part to kinematic list if needed
                if ((partIsKin == true && (FracGlobalSettings.recalculateKinematicPartsOnLoad == 0 || (FracGlobalSettings.recalculateKinematicPartsOnLoad == 1 && GetFracturePrefabType() == 0))) 
                    || partGroupD.isKinematic == true) SetPartKinematicStatus(newPartI, true);

                //get what nearPartIndexes is actually valid neighbours and create structure for the new part
                foreach (short nearPartI in nearPartIndexes)
                {
                    //ignore if this neighbour part is invalid
                    if (jCDW_job.partsParentI[newPartI] != jCDW_job.partsParentI[nearPartI]
                        || FracHelpFunc.Gd_isPartLinkedWithPart(newPart, allParts[nearPartI]) == false) continue;

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
                if (newKinematicStatus == false)
                {
                    allParents[parentI].parentKinematic--;
                    if (localPathToRbIndex.TryGetValue(partsLocalParentPath[partI], out int rbI) == true) allParents[parentI].parentRbs[rbI].rbKinCount--;
                }
                else
                {
                    allParents[parentI].parentKinematic++;
                    if (localPathToRbIndex.TryGetValue(partsLocalParentPath[partI], out int rbI) == true) allParents[parentI].parentRbs[rbI].rbKinCount++;
                }

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

        public delegate void Event_OnPartParentChanged(int partI, int oldParentI, int newParentI);
        public event Event_OnPartParentChanged OnPartParentChanged;

        /// <summary>
        /// Sets the given parts parent to newParentI, if -1 the part will become lose
        /// </summary>
        private void SetPartParent(int partI, int newParentI, Vector3 losePartVelocity = default)
        {
            if (repairIsSetup == true && jGTD_job.repair_partIWannaRestore[partI] == 1) return;//Not allowed to set parent of parts that are currently being repaired

            int oldParentI = jCDW_job.partsParentI[partI];
            if (oldParentI == newParentI) return;

            //Verify part count
            if (jCDW_job.parentPartCount.Length != allParents.Count) RecreateParentPartCount();

            //get part groupData
            if (localPathToRbIndex.TryGetValue(partsLocalParentPath[partI], out int localRbI) == false) localRbI = -1;
            int partBoneI = partI + partBoneOffset;
            DestructionMaterial partDesMat = GetDesMatFromIntId(allParts[partI].groupIdInt);

            //remove part from previous parent
            if (oldParentI >= 0)
            {
                jCDW_job.parentPartCount[oldParentI]--;
                allParents[oldParentI].partIndexes.RemoveSwapBack(partI);
                allParents[oldParentI].parentMass -= partDesMat.desProps.mass;
                allParents[oldParentI].totalStiffness -= partDesMat.bendProps.bendyness;
                allParents[oldParentI].totalTransportCoEfficiency -= partDesMat.desProps.falloff;
                FracParent.FracRb ofRb;
                if (localRbI >= 0)
                {
                    ofRb = allParents[oldParentI].parentRbs[localRbI];
                    ofRb.rbDesMass -= partDesMat.desProps.mass;
                    ofRb.rbPartCount--;
                }
                else ofRb = null;

                if (jCDW_job.kinematicPartIndexes.Contains(partI) == true)
                {
                    allParents[oldParentI].parentKinematic--;
                    if (ofRb != null) ofRb.rbKinCount--;
                }

                MarkParentAsModified(oldParentI);
            }
            else FromNoParentToParent();

            jCDW_job.partsParentI[partI] = newParentI;

            //if want to remove parent
            if (newParentI < 0)
            {
                FromParentToNoParent();
                OnPartParentChanged?.Invoke(partI, oldParentI, newParentI);
                return;
            }

            //if want to change parent
            fr_bones[partBoneI].SetParent(FracHelpFunc.DecodeHierarchyPath(allParents[newParentI].parentTrans, partsLocalParentPath[partI]));
            jCDW_job.parentPartCount[newParentI]++;
            allParents[newParentI].partIndexes.Add(partI);
            allParents[newParentI].parentMass += partDesMat.desProps.mass;
            allParents[newParentI].totalStiffness += partDesMat.bendProps.bendyness;
            allParents[newParentI].totalTransportCoEfficiency += partDesMat.desProps.falloff;
            FracParent.FracRb fRb;
            if (localRbI >= 0)
            {
                fRb = allParents[newParentI].parentRbs[localRbI];
                fRb.rbDesMass += partDesMat.desProps.mass;
                fRb.rbPartCount++;
            }
            else fRb = null;

            if (jCDW_job.kinematicPartIndexes.Contains(partI) == true)
            {
                allParents[newParentI].parentKinematic++;
                if (fRb != null) fRb.rbKinCount++;
            }

            MarkParentAsModified(newParentI);

            OnPartParentChanged?.Invoke(partI, oldParentI, newParentI);

            void FromParentToNoParent()
            {
                //set transform
                fr_bones[partBoneI].SetParent(transform);
                fr_bones[partBoneI].gameObject.layer = partDesMat.objLayerBroken;
                saved_allPartsCol[partI].hasModifiableContacts = false;
                //saved_allPartsCol[partI].enabled = false;//"lag" bug is caused by parts collision

                //set physics
                //add and set rigidbody, create rigidbody for parts
                Rigidbody newRb = fr_bones[partBoneI].gameObject.AddComponent<Rigidbody>();

                FracHelpFunc.SetRbMass(ref newRb, partDesMat.desProps.mass * phyPartsOptions.massMultiplier);

#if UNITY_2023_1_OR_NEWER
                newRb.linearDamping = phyPartsOptions.drag;
                newRb.angularDamping = phyPartsOptions.angularDrag;
                newRb.linearVelocity = losePartVelocity * 0.5f;
#else
                newRb.drag = phyPartsOptions.drag;
                newRb.angularDrag = phyPartsOptions.angularDrag;
                newRb.velocity = losePartVelocity * 0.5f;
#endif
                newRb.useGravity = phyPartsOptions.useGravity;
                fr_bones[partBoneI].position = fr_bones[partBoneI].position + (losePartVelocity * Time.fixedDeltaTime);
                //newRb.gameObject.SetActive(false);//debug remove later
            }

            void FromNoParentToParent()
            {
                //note that this also runs once for newly created objects
                //set transform
                fr_bones[partBoneI].gameObject.layer = partDesMat.objLayerDefualt;
                saved_allPartsCol[partI].hasModifiableContacts = true;
                //remove rigidbody
                if (saved_allPartsCol[partI].attachedRigidbody != null) Destroy(saved_allPartsCol[partI].attachedRigidbody);
            }
        }

        private void RealizeDestructionJoints()
        {
            foreach (var desJoint in allParents[0].parentTrans.GetComponentsInChildren<destructionJoint>())
            {
                desJoint.SetupJoints(this);
            }
        }

        /// <summary>
        /// Recreates the jCDW_job.parentPartCount from allParents list
        /// </summary>
        private void RecreateParentPartCount()
        {
            Debug.LogWarning(transform.name + " jCDW_job.parentPartCount had to be recreated for some reason, " + jCDW_job.parentPartCount.Length + " " + allParents.Count);
            if (jCDW_job.parentPartCount.IsCreated == true) jCDW_job.parentPartCount.Dispose();
            int parentCount = allParents.Count;

            jCDW_job.parentPartCount = new NativeList<short>(parentCount, Allocator.Persistent);
            for (int i = 0; i < parentCount; i++)
            {
                jCDW_job.parentPartCount[i] = (short)allParents[i].partIndexes.Count;
            }
        }

        /// <summary>
        /// Creates a new parent and returns its index (transToUse will be used as parent if it aint null)
        /// </summary>
        public short CreateNewParent(Transform transToUse = null, int referenceParentI = -1)
        {
            //if empty&&valid parent exists, reuse it since we are not allowed to destroy unused parents.
            short newParentI = -1;

            if (transToUse == null)
            {
                for (short parentI = 1; parentI < allParents.Count; parentI++)
                {
                    if (allParents[parentI].partIndexes.Count == 0 && parentsThatNeedsUpdating.Contains(parentI) == false
                        && allParents[parentI].parentTrans != null && allParents[parentI].parentTrans.parent == transform && parentI != referenceParentI)
                    {
                        allParents[parentI].parentTrans.gameObject.SetActive(true);
                        if (referenceParentI >= 0) FracHelpFunc.MatchChildTransforms(allParents[parentI].parentTrans, allParents[referenceParentI].parentTrans);
                        newParentI = parentI;
                        break;
                    }
                }
            }

            //if empty&&valid parent did not exist, create new parent object
            if (newParentI < 0)
            {
                newParentI = (short)allParents.Count;

                if (transToUse == null)
                {
                    transToUse = GameObject.Instantiate(parentTemplate, transform, true).transform;
#if UNITY_EDITOR
                    transToUse.name += newParentI;
#endif
                    transToUse.gameObject.SetActive(true); 
                    if (referenceParentI >= 0) FracHelpFunc.MatchChildTransforms(transToUse, allParents[referenceParentI].parentTrans);
                }

                //Get rigidbodies
                List<FracParent.FracRb> fRbs = new();
                foreach (Rigidbody rb in transToUse.GetComponentsInChildren<Rigidbody>())
                {
                    fRbs.Add(new()
                    {
                        rb = rb,
                        rbDesMass = 0.0f,
                        rbId = rb.GetInstanceID(),
                        rbKinCount = 0
                    });
                }

                allParents.Add(new()
                {
                    parentTrans = transToUse,
                    parentRbs = fRbs,
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

            allParents[parentI].parentTrans.gameObject.SetActive(true);//Is it worth checking if its already enabled?

            //if main parent should be kinematic make sure it always is
            if (allParents[parentI].parentKinematic <= 0)
            {
                if (phyMainOptions.mainPhysicsType == OptMainPhysicsType.overlappingIsKinematic && parentI == 0 && Application.isPlaying == true)
                {
                    //overlappingIsKinematic and this is base parent, it cant every be dynamic so we must move all of its children to new parent
                    int newParentI = CreateNewParent(null, parentI);

                    for (int i = allParents[parentI].parentRbs.Count - 1; i >= 0; i--)
                    {
                        allParents[newParentI].parentRbs[i].rb.velocity = allParents[parentI].parentRbs[i].rb.velocity;
                        allParents[newParentI].parentRbs[i].rb.angularVelocity = allParents[parentI].parentRbs[i].rb.angularVelocity;
                    }

                    for (int i = allParents[parentI].partIndexes.Count - 1; i >= 0; i--)
                    {
                        SetPartParent(allParents[parentI].partIndexes[i], newParentI, Vector3.zero);
                    }

                    return;
                }
            }

            //update parent rigidbody
            foreach (var fRb in allParents[parentI].parentRbs)
            {
                float newRbMass = fRb.rbDesMass
                     * (fRb.rbPartCount * phyMainOptions.massMultiplier > 1.0f ? phyMainOptions.massMultiplier : 1.0f);
                FracHelpFunc.SetRbMass(ref fRb.rb, newRbMass);

                if (fRb.rbPartCount <= 0) fRb.rbIsKin = true;
                else if (phyMainOptions.mainPhysicsType == OptMainPhysicsType.overlappingIsKinematic
                    || (phyMainOptions.mainPhysicsType == OptMainPhysicsType.orginalIsManuall && parentI != 0)) fRb.rbIsKin = fRb.rbKinCount > 0; 
                else fRb.rbIsKin = fRb.rbKinCount > 0 ? true : fRb.rbIsKinByDefualt;

                fRb.rb.isKinematic = fRb.rbIsKin;

                globalHandler.OnAddOrUpdateRb(fRb.rb, fRb.rbDesMass);
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

            if (SystemInfo.supportsComputeShaders == false)
            {
                Debug.LogError("The current device does not support ComputeShaders, destruction wont work!");
                return false;
            }

            return true;
        }

        private Collider Gen_createPartCollider(Transform partTrans, Mesh partColMeshW,
#if UNITY_2023_1_OR_NEWER
            PhysicsMaterial
#else
            PhysicMaterial
#endif
            phyMat)
        {
            //This is the only place we add new colliders to the parts in
            //(We do also add colliders in the copyColliders function but since it copies all collider properties it does not really matter)
            partColMeshW = FracHelpFunc.MergeVerticesInMesh(Instantiate(partColMeshW));
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

            partTrans.position = FracHelpFunc.GetGeometricCenterOfPositions(partWVers);

            FracHelpFunc.SetColliderFromFromPoints(
                newCol,
                FracHelpFunc.ConvertPositionsWithMatrix(partWVers, partTrans.worldToLocalMatrix), ref partMaxExtent);

            newCol.sharedMaterial = phyMat;
            newCol.hasModifiableContacts = true; //This must always be true for all fracture colliders
            return newCol;
        }


        private bool mustConfirmHighCount = true;

        /// <summary>
        /// Returns all mesh chunks that was generated from the meshesToFracture list
        /// </summary>
        private List<FracMesh> Gen_fractureMeshes(List<FracSource> meshesToFrac, int totalChunkCount, bool dynamicChunkCount, float worldScaleDis = 0.0001f, int seed = -1)
        {
            //prefracture
            if (saveState != null && saveState.preS_fracedMeshes != null) return new(saveState.preS_fracedMeshes.ToFracMesh());

            //get random seed
            int nextOgMeshId = 0;

            //get per mesh scale, so each mesh to frac get ~equally sized chunks
            Mesh[] meshes = meshesToFrac.Select(meshData => meshData.meshW).ToArray();
            float[] meshScales = FracHelpFunc.GetPerMeshScale(meshes, generationQuality == GenerationQuality.high);
            Bounds meshBounds = FracHelpFunc.GetCompositeMeshBounds(meshes);

            if (dynamicChunkCount == true) totalChunkCount = Mathf.CeilToInt(totalChunkCount * FracHelpFunc.GetBoundingBoxVolume(meshBounds));

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

                Gen_fractureMesh(meshesToFrac[i], ref fracedMeshes, Mathf.CeilToInt(totalChunkCount * meshScales[i]));

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
                Mesh meshToF = FracHelpFunc.MergeSubMeshes(meshToFrac.meshW);

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
                        if (FracHelpFunc.IsMeshValid(newMeshesTemp[^1], true, worldScaleDis) == false) //is true better?
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
                            newMeshesTemp[^1] = FracHelpFunc.MakeMeshConvex(newMeshesTemp[^1], false, FracGlobalSettings.worldScale);
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
            foreach (Renderer rend in obj.GetComponentsInChildren<Renderer>(true))
            {
                if (rend.gameObject.activeInHierarchy == false) continue;

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
                    if (FracHelpFunc.GetIfTransformIsAnyParent(transform, skinnedR.rootBone) == false)
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

                if (FracHelpFunc.IsMeshValid(newToFrac.meshW, false, worldScaleDis) == false) continue; //continue if mesh is invalid

                newToFrac.sRend = rend;
                meshesToFrac.Add(newToFrac);
            }

            //transform all meshes to worldSpace
            HashSet<List<float>> newGroupIds = new();
            for (int i = 0; i < meshesToFrac.Count; i++)
            {
                FracHelpFunc.ConvertMeshWithMatrix(ref meshesToFrac[i].meshW, meshesToFrac[i].sRend.localToWorldMatrix);
                if (FracHelpFunc.IsMeshValid(meshesToFrac[i].meshW, true, worldScaleDis) == false)
                {
                    //remove from toFrac if the mesh does not have a valid hull
                    meshesToFrac.RemoveAtSwapBack(i);
                    i--;
                    continue;
                }

                FracHelpFunc.Gd_getIdsFromColors(meshesToFrac[i].meshW.colors, ref newGroupIds);
            }

            //Return if no valid mesh
            if (meshesToFrac.Count == 0)
            {
                Debug.LogError("There are no valid mesh in " + transform.name + " or any of its children");
                return null;
            }

            //setup group ids
            md_verGroupIds = newGroupIds.ToArray();
            Array.Sort(md_verGroupIds, new FracHelpFunc.HashSetComparer());
            SetupGroupData();

            //return early if we only want raw
            if (getRawOnly == true)
            {
                return meshesToFrac;
            }

            //Debug fix later, splitDisconnectedFaces causes textures/uvs to break (SplitMeshInTwo() seems to be cause of issue!?)
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
                for (int ii = 0; ii < splittedMeshes.Count; ii++)
                {
                    meshesToFrac.Add(new()
                    {
                        meshW = splittedMeshes[ii].meshW,
                        sRend = meshesToFrac[i].sRend,
                        mGroupId = splittedMeshes[ii].mGroupId,
                        mMats = splittedMeshes[ii].mMats,
                        trisSubMeshI = FracHelpFunc.GetTrisSubMeshI(splittedMeshes[ii].meshW)
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
                    || FracHelpFunc.AreBoundsArrayEqual(saveState.preS_toFracData.toFracRendBounds, fRendBounds) == false)
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
            if (useGroupIds == false && splitDisconnectedFaces == false) return new() { meshToSplit };

            int[] tris = meshToSplit.meshW.triangles;
            Vector3[] vers = meshToSplit.meshW.vertices;
            Color[] cols = meshToSplit.meshW.colors;
            int trisL = tris.Length;
            int[] trisIds = new int[trisL / 3];
            NativeArray<int> triId = new(2, Allocator.Temp);
            Dictionary<int, List<float>> triIdToGroupId = new();
            if (cols.Length != vers.Length) cols = new Color[vers.Length];

            for (int tI = 0; tI < trisL; tI += 3)
            {
                if (trisIds[tI / 3] != 0) continue;
                HashSet<int> conTris = splitDisconnectedFaces == false ? FracHelpFunc.GetAllTriangels(trisL) :
                    FracHelpFunc.GetConnectedTriangels(vers, tris, tI, worldScaleDis);

                foreach (int ctI in conTris)
                {
                    if (trisIds[ctI / 3] != 0) continue;

                    List<float> groupId = FracHelpFunc.Gd_getIdFromColor(cols[tris[ctI]]);
                    triId[0] = tI;
                    triId[1] = FracHelpFunc.Gd_getIntIdFromId(groupId);
                    int tId = FracHelpFuncBurst.GetHashFromInts(ref triId);
                    if (triIdToGroupId.ContainsKey(tId) == false)
                    {
                        triIdToGroupId.Add(tId, groupId);
                    }

                    foreach (int ltI in FracHelpFunc.Gd_getSomeTriangelsInId(cols, tris, groupId, conTris))
                    {
                        trisIds[ltI / 3] = tId;
                    }
                }
            }

            return FracHelpFunc.SplitMeshByTrisIds(meshToSplit, trisIds, triIdToGroupId);

            //while (maxLoops > 0)
            //{
            //    maxLoops--;
            //    if (meshToSplit.meshW.vertexCount < 4) break;
            //
            //    verCols = meshToSplit.meshW.colors;
            //    bool useVerCols = verCols.Length == meshToSplit.meshW.vertexCount;
            //
            //    if (splitDisconnectedFaces == false && (useGroupIds == false || useVerCols == false))
            //    {
            //        splittedMeshes.Add(new() { meshW = meshToSplit.meshW, mGroupId = null, mMats = meshToSplit.sRend.sharedMaterials.ToList() });
            //
            //        return splittedMeshes;
            //    }
            //
            //    tempG = useVerCols == true ? FractureHelperFunc.Gd_getIdFromColor(verCols[0]) : null;
            //    HashSet<int> vertsToSplit;
            //    
            //    if (splitDisconnectedFaces == true)
            //    {
            //        vertsToSplit = FractureHelperFunc.GetConnectedVertics(meshToSplit.meshW, 0, worldScaleDis);
            //        if (useVerCols == true) vertsToSplit = FractureHelperFunc.Gd_getSomeVerticesInId(verCols, tempG, vertsToSplit);
            //    }
            //    else
            //    {
            //        vertsToSplit = FractureHelperFunc.Gd_getAllVerticesInId(verCols, tempG);
            //    }
            //
            //    //Only meshW, mMats and sRend is assigned in meshToSplit
            //    tempM = FractureHelperFunc.SplitMeshInTwo(vertsToSplit, meshToSplit, doBones);
            //
            //    if (tempM == null) return null;
            //
            //    if (tempM[0].meshW.vertexCount >= 4) splittedMeshes.Add(new() { meshW = tempM[0].meshW, mGroupId = tempG, mMats = tempM[0].mMats });
            //    meshToSplit = tempM[1];
            //}
            //
            //if (meshToSplit.meshW.vertexCount >= 4) splittedMeshes.Add(meshToSplit);
            //
            //return splittedMeshes;
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
            ComputeDestruction_setup();

            //setup gpu readback
            gpuMeshVertexData = new GpuMeshVertex[allParts.Count];
            gpuMeshBonesLToW = new Matrix4x4[fr_bones.Count];

            //Add destruction joints
            RealizeDestructionJoints();
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

        //only for debug
        private void LateUpdate()
        {
            if (Input.GetKey(KeyCode.R) == true && allParents.Count > 0 && (allParents[0].parentRbs.Count == 0 || allParents[0].parentRbs[0].rb == null || allParents[0].parentRbs[0].rb.isKinematic == true))
            {
                int partI = GetMostDeformedPartInParent(0, out _);
                if (partI >= 0)
                {
                    RequestRepairPart(partI);
                }

                if (GetIfBrokenPartExists() == true)
                {
                    partI = GetNextBrokenPart();
                    if (partI >= 0)
                    {
                        RequestRepairPart(partI);
                    }
                }
            }
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
            CheckForUpdatingAgain:
            if (parentsThatNeedsUpdating.Count > 0)
            {
                HashSet<int> parentsThatNeedsUpdatingCopy = new(parentsThatNeedsUpdating);//We must clone it since stuff may be added inside UpdateParentData code
                parentsThatNeedsUpdating.Clear();
                foreach (int parentI in parentsThatNeedsUpdatingCopy) UpdateParentData(parentI);
                goto CheckForUpdatingAgain;//If stuff was added inside loop, its probably better if they get updated instantly
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

            if (buf_partIToParentI != null)
            {
                buf_partIToParentI.Release();
                buf_partIToParentI.Dispose();
            }

            if (buf_partIToBendPropI != null)
            {
                buf_partIToBendPropI.Release();
                buf_partIToBendPropI.Dispose();
            }

            if (buf_bendProperties != null)
            {
                buf_bendProperties.Release();
                buf_bendProperties.Dispose();
            }

            if (buf_verNors != null)
            {
                buf_verNors.Release();
                buf_verNors.Dispose();
            }

            if (buf_defPoints != null)
            {
                buf_defPoints.Release();
                buf_defPoints.Dispose();
            }

            //dispose getTransformData job
            GetTransformData_end();//Make sure the job aint running
            ComputeDestruction_end();//Make sure the job aint running
            if (jGTD_hasMoved.IsCreated == true) jGTD_hasMoved.Dispose();
            if (jGTD_fracBoneTrans.isCreated == true) jGTD_fracBoneTrans.Dispose();
            if (jGTD_job.fracBonesLToW.IsCreated == true) jGTD_job.fracBonesLToW.Dispose();
            if (jGTD_job.fracBonesPosW.IsCreated == true) jGTD_job.fracBonesPosW.Dispose();
            if (jGTD_job.fracBonesLocValue.IsCreated == true) jGTD_job.fracBonesLocValue.Dispose();
            if (jGTD_job.repair_partIWannaRestore.IsCreated == true) jGTD_job.repair_partIWannaRestore.Dispose();
            if (jGTD_job.repair_partsDefualtLoc.IsCreated == true) jGTD_job.repair_partsDefualtLoc.Dispose();

            //disepose computeDestruction job
            if (jCDW_job.structPosL.IsCreated == true) jCDW_job.structPosL.Dispose();
            if (jCDW_job.partsParentI.IsCreated == true) jCDW_job.partsParentI.Dispose();
            if (jCDW_job.kinematicPartIndexes.IsCreated == true) jCDW_job.kinematicPartIndexes.Dispose();
            if (jCDW_job.desSources.IsCreated == true) jCDW_job.desSources.Dispose();
            if (jCDW_job.fStructs.IsCreated == true) jCDW_job.fStructs.Dispose();
            if (jCDW_job.partIToDesMatI.IsCreated == true) jCDW_job.partIToDesMatI.Dispose();
            if (jCDW_job.desProps.IsCreated == true) jCDW_job.desProps.Dispose();
            if (jCDW_job.bendProps.IsCreated == true) jCDW_job.bendProps.Dispose();
            if (jCDW_job.boneBindsLToW.IsCreated == true) jCDW_job.boneBindsLToW.Dispose();
            if (jCDW_job.partsToBreak.IsCreated == true) jCDW_job.partsToBreak.Dispose();
            if (jCDW_job.newParentsData.IsCreated == true) jCDW_job.newParentsData.Dispose();
            if (jCDW_job.partsNewParentI.IsCreated == true) jCDW_job.partsNewParentI.Dispose();
            if (jCDW_job.defOffsetW.IsCreated == true) jCDW_job.defOffsetW.Dispose();
            if (jCDW_job.defPoints.IsCreated == true) jCDW_job.defPoints.Dispose();
            if (jCDW_job.deformedPartsI.IsCreated == true) jCDW_job.deformedPartsI.Dispose();
        }

        private int cpKernelId_ComputeSkinDef = -1;
        private int cpKernelId_RestoreSkinDef = -1;
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

            //run repair kernel
#if UNITY_EDITOR
            if (Application.isPlaying == true)
#endif
                RepairSys_update();

            //Run the job
            jGTD_job.repairSpeedDelta = repairSpeed * Time.fixedDeltaTime;
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

                RepairSys_setup();
            }
        }

        private int fracRendDividedVerCount = 1;
        private bool wantToApplyDeformation = false;
        private bool wantToApplySkinning = false;

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
            else if (wantToApplySkinning == true) ApplySkinAndDef();
        }

#if !FRAC_NO_BURST
        [BurstCompile]
#endif
        private struct GetTransformData_work : IJobParallelForTransform
        {
            public NativeArray<Matrix4x4> fracBonesLToW;
            public NativeArray<float> fracBonesLocValue;
            public NativeArray<Vector3> fracBonesPosW;

            /// <summary>
            /// Contrains the indexes of the fracBones that has moved
            /// </summary>
            public NativeQueue<short>.ParallelWriter hasMoved;

            //Repair system variabels
            [NativeDisableParallelForRestriction] public NativeArray<LocationData> repair_partsDefualtLoc;
            [NativeDisableParallelForRestriction] public NativeArray<int> repair_partIWannaRestore;
            public int partBoneOffset;

            /// <summary>
            /// The repair speed, should be multiplied with deltaTime
            /// </summary>
            public float repairSpeedDelta;

            public void Execute(int index, TransformAccess transform)
            {
                //repair system restore transform positions
                int partI = index - partBoneOffset;
                if (partI >= 0 && repair_partIWannaRestore[partI] == 1)
                {
                    LocationData locD = repair_partsDefualtLoc[partI];

                    transform.localPosition = FracHelpFunc.Vec3LerpMin(transform.localPosition, locD.pos, repairSpeedDelta, repairSpeedDelta * 1.25f, out bool doneA);
                    transform.localRotation = FracHelpFunc.QuatLerpMin(transform.localRotation, locD.rot, repairSpeedDelta, repairSpeedDelta * 87.5f, out bool doneB);

                    if (doneA == true && doneB == true) repair_partIWannaRestore[partI] = 0;
                }

                //If fracRend bone trans has moved, add to hasMoved queue
                float newLocValue = transform.worldToLocalMatrix.GetHashCode();
                if (newLocValue - fracBonesLocValue[index] != 0.0f)
                {
                    //get fracRend bone lToW matrix and its world pos
                    fracBonesLToW[index] = transform.localToWorldMatrix;
                    fracBonesLocValue[index] = newLocValue;
                    fracBonesPosW[index] = transform.position;
                    hasMoved.Enqueue((short)index);
                }
            }
        }

        private struct LocationData
        {
            public Vector3 pos;
            public Quaternion rot;
        }

        #endregion GetTransformData




        #region ComputeDestruction

        /// <summary>
        ///Contains all parts that has been deformed. (Since we use async readback, we toggle between using [0] and [1], if its currently readingback)
        /// </summary>
        private HashSet<int>[] des_deformedParts = new HashSet<int>[2];
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

        private void ComputeDestruction_setup()
        {
            //setup job
            jCDW_job.desSources = new NativeArray<DestructionSource>(0, Allocator.Persistent);
            jCDW_job.desProps = destructionMaterials.Select(desMat => desMat.desProps).ToList().ToNativeArray(Allocator.Persistent);
            jCDW_job.bendProps = destructionMaterials.Select(desMat => desMat.bendProps).ToList().ToNativeArray(Allocator.Persistent);
            jCDW_job.partBoneOffset = partBoneOffset;
            jCDW_job.partsToBreak = new NativeHashMap<int, DesPartToBreak>(8, Allocator.Persistent);
            jCDW_job.newParentsData = new NativeHashMap<byte, DesNewParentData>(4, Allocator.Persistent);
            jCDW_job.partsNewParentI = new NativeArray<byte>(allParts.Count, Allocator.Persistent);
            jCDW_job.defOffsetW = new NativeArray<Vector3>(allParts.Count, Allocator.Persistent);
            jCDW_job.optMainPhyType = phyMainOptions.mainPhysicsType;
            jCDW_job.defPoints = new NativeList<DefPoint>(8, Allocator.Persistent);
            jCDW_job.defBendForce = new(0.0f, Allocator.Persistent);
            jCDW_job.deformedPartsI = new(Allocator.Persistent);
            jCDW_job.partMaxExtent = partMaxExtent;

            //setup register destruction/impacts
            destructionSources = new();
            destructionBodies = new();
            destructionPairs = new();

            //setup compute buffers
            ComputeDestruction_setupGpu();
        }

        private void ComputeDestruction_setupGpu()
        {
            if (jCDW_job.bendProps.IsCreated == true)
            {
                buf_bendProperties = new ComputeBuffer(Mathf.Max(1, jCDW_job.bendProps.Length), sizeof(float) * 4);
                buf_partIToBendPropI = new ComputeBuffer(allParts.Count, sizeof(int));
                buf_defPoints = new ComputeBuffer(8, sizeof(float) * 7 + sizeof(int) * 2);

                buf_bendProperties.SetData(jCDW_job.bendProps);
                buf_partIToBendPropI.SetData(jCDW_job.partIToDesMatI.AsArray());
                buf_defPoints.SetData(jCDW_job.defPoints.AsArray());
            }
            else
            {
                buf_bendProperties = new ComputeBuffer(1, sizeof(float) * 4);
                buf_partIToBendPropI = new ComputeBuffer(1, sizeof(int));
                buf_defPoints = new ComputeBuffer(1, sizeof(float) * 7 + sizeof(int) * 2);
            }

            computeDestructionSolver.SetBuffer(cpKernelId_ComputeSkinDef, "bendProperties", buf_bendProperties);
            computeDestructionSolver.SetBuffer(cpKernelId_ComputeSkinDef, "partIToBendPropI", buf_partIToBendPropI);
            computeDestructionSolver.SetBuffer(cpKernelId_ComputeSkinDef, "defPoints", buf_defPoints);
        }

        private RaycastHit[] rayHits = new RaycastHit[2];

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

            //set job destruction sources and destruction points distance to wall
            int desSLenght = jCDW_job.desSources.Length;
            int desOCount = destructionSources.Count;
            if (desSLenght < desOCount)
            {
                if (jCDW_job.desSources.IsCreated == true) jCDW_job.desSources.Dispose();
                desSLenght = desOCount;
                jCDW_job.desSources = new NativeArray<DestructionSource>(desSLenght, Allocator.Persistent);
                FracHelpFunc.SetListLenght(ref jCDW_bodies, desSLenght);
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
                    var source = destructionSources[deskeys[i]];
                    jCDW_job.desSources[i] = source;
                    jCDW_bodies[i] = destructionBodies[deskeys[i]];

                    //get destruction points distance to wall
#pragma warning disable CS0162
                    if (FracGlobalSettings.doDeformationCollision == false) continue;

                    var desPairs = destructionPairs[deskeys[i]];
                    Vector3 impDir = source.impVel.normalized;
                    float impDis = source.impVel.magnitude + partMaxExtent;

                    for (int desPI = 0; desPI < desPairs.Length; desPI++)
                    {
                        var desP = desPairs[desPI];
                        desP.disToWall = 69420.0f;
                        int hitCount = Physics.RaycastNonAlloc(GetStructWorldPosition(desP.partI), impDir, rayHits, impDis, globalHandler.groundLayers, QueryTriggerInteraction.Ignore);
                        if (hitCount == 0) continue;
                        foreach (RaycastHit nHit in rayHits)
                        {
                            var fracD = globalHandler.TryGetFracPartFromColInstanceId(nHit.colliderInstanceID);
                            if (fracD != null && fracD.fracThis == this) continue;

                            float hDis = Math.Max(nHit.distance - (partMaxExtent * 0.5f), 0.001f);
                            if (hDis < desP.disToWall) desP.disToWall = hDis; 
                        }

                        desPairs[desPI] = desP;
                    }
#pragma warning restore CS0162
                }
            }

            destructionSources.Clear();
            destructionBodies.Clear();
            destructionPairs.Clear();//The nativeArrays will be disposed later by the worker

            //update bone matrixes
            jCDW_job.bonesLToW = jGTD_job.fracBonesLToW.AsReadOnly();
            jCDW_job.fixedDeltaTime = Time.fixedDeltaTime;

            //run the job
            jCDW_jobIsActive = true;

            //Debug.Log(jCDW_job.partsParentI[0]);
            jCDW_handle = jCDW_job.Schedule();
        }

        private class RbAddForceData
        {
            public int parentI;
            public Vector3 vel;
            public Vector3 velPos;
        }

        private void ComputeDestruction_end()
        {
            if (jCDW_jobIsActive == false) return;

            //complete the job
            jCDW_jobIsActive = false;
            jCDW_handle.Complete();
            FixReadIssue(jCDW_job.partsParentI[0]);//This somehow fixes a weird bug. Dont believe me? Try removing it and you get errors when trying to break objects
            FixReadIssue(jCDW_job.partIToDesMatI[0]);

            static void FixReadIssue(int someth)
            {

            }

            FixReadIssueOther(jCDW_job.fStructs[0]);
            static void FixReadIssueOther(FracStruct somertyh)
            {

            }

            //apply destruction result
            //send deformation points to gpu
            wantToApplyDeformation = true;
            wantToApplySkinning = true;

#pragma warning disable CS0162
            if (FracGlobalSettings.maxColliderUpdatesPerFrame > 0)
            {
                while (jCDW_job.deformedPartsI.TryDequeue(out int partI) == true)
                {
                    des_deformedParts[des_deformedPartsIndex].Add(partI);
                }
            }
            else jCDW_job.deformedPartsI.Clear();
#pragma warning restore CS0162

            if (buf_defPoints.IsValid() == false || buf_defPoints.count < jCDW_job.defPoints.Capacity)
            {
                buf_defPoints = new ComputeBuffer(jCDW_job.defPoints.Capacity, sizeof(float) * 7 + sizeof(int) * 2);
                computeDestructionSolver.SetBuffer(cpKernelId_ComputeSkinDef, "defPoints", buf_defPoints);
            }

            //defPoints can somehow sometimes contain nan values, lazy fix
            for (int i = jCDW_job.defPoints.Length - 1; i >= 0; i--)
            {
                var defP = jCDW_job.defPoints[i];
                if (FracHelpFunc.IsVectorValid(defP.defVel) == true && FracHelpFunc.IsVectorValid(defP.defPos) == true) continue;

                jCDW_job.defPoints.RemoveAtSwapBack(i);
            }

            buf_defPoints.SetData(jCDW_job.defPoints.AsArray());
            computeDestructionSolver.SetInt("defPointsLenght", jCDW_job.defPoints.Length);
            computeDestructionSolver.SetFloat("defBendForce", jCDW_job.defBendForce.Value);

            //create new parents
            NativeArray<byte> newParentKeys = jCDW_job.newParentsData.GetKeyArray(Allocator.Temp);
            Dictionary<byte, int> newParentIToParentI = new(newParentKeys.Length);
            HashSet<RbAddForceData> forcesToAdd = new();

            for (int i = 0; i < newParentKeys.Length; i++)
            {
                DesNewParentData newPD = jCDW_job.newParentsData[newParentKeys[i]];
                if (newPD.newPartCount >= 0)
                {
                    newPD.sourceParentI = CreateNewParent(null, newPD.sourceParentI);
                    newParentIToParentI.Add(newParentKeys[i], newPD.sourceParentI);
                }

                if (allParents[newPD.sourceParentI].parentRbs.Count == 0) continue;

                forcesToAdd.Add(new()//We cant add the force directly here since we must set its children first for it to work properly
                {
                    parentI = newPD.sourceParentI,
                    vel = newPD.velocity,
                    velPos = newPD.velPos
                });
            }

            //break parts that should break
            foreach (DesPartToBreak pBreak in jCDW_job.partsToBreak.GetValueArray(Allocator.Temp))
            {
                //Debug.Log(pBreak.velTarget.magnitude + " " + transform.name + " " + pBreak.partI);
                SetPartParent((short)pBreak.partI, -1, pBreak.velTarget);
                //SetPartParent((short)pBreak.partI, -1, Vector3.zero);
            }

            //Set compute buffer parents, we wanna update them before we set new parents
            if (gpuIsReady == true) buf_partIToParentI.SetData(jCDW_job.partsParentI.AsArray());

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

            //add velocity to parents
            foreach (RbAddForceData forceToAdd in forcesToAdd)
            {
                forceToAdd.vel /= allParents[forceToAdd.parentI].parentRbs.Count;

                foreach (var fRb in allParents[forceToAdd.parentI].parentRbs)
                {
                    fRb.rb.AddForceAtPosition(forceToAdd.vel, forceToAdd.velPos, ForceMode.VelocityChange);
                }
            }

            ////add force to impact source rb
            //for (int i = 0; i < jCDW_bodies.Count; i++)
            //{
            //    if (jCDW_bodies[i] == null) continue;
            //
            //    Debug.Log(jCDW_bodies[i].transform.name + " " + jCDW_job.desSources[i].impVel + " " + transform.name);
            //    //jCDW_bodies[i].velocity = Vector3.MoveTowards(jCDW_bodies[i].velocity, Vector3.zero, jCDW_job.desSources[i].impVel.magnitude);
            //    jCDW_bodies[i].AddForceAtPosition(jCDW_job.desSources[i].impVel, jCDW_job.desSources[i].avgImpPos, ForceMode.VelocityChange);
            //}
        }

        public ComputeDestruction_work jCDW_job;
        private JobHandle jCDW_handle;

#if !FRAC_NO_BURST
        [BurstCompile]
#endif
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
            public NativeList<int> partIToDesMatI;
            public NativeArray<DestructionMaterial.DesProperties> desProps;
            public NativeArray<DestructionMaterial.BendProperties> bendProps;
            public NativeArray<Matrix4x4>.ReadOnly bonesLToW;
            public NativeHashMap<int, DesPartToBreak> partsToBreak;
            public NativeHashMap<byte, DesNewParentData> newParentsData;
            public NativeArray<byte> partsNewParentI;

            /// <summary>
            /// How much part X has been deformed in worldspace
            /// </summary>
            public NativeArray<Vector3> defOffsetW;
            public NativeList<DefPoint> defPoints;
            public NativeReference<float> defBendForce;
            public NativeQueue<int> deformedPartsI;
            public OptMainPhysicsType optMainPhyType;

            /// <summary>
            /// The local to world matrix for every bone (Parts bones + skinned bones), skinned bones are first
            /// </summary>
            public NativeArray<Matrix4x4> boneBindsLToW;
            public int partBoneOffset;
            public float fixedDeltaTime;
            public float partMaxExtent;

            public unsafe void Execute()
            {
                //Since its useless and you cant access fields from localLocal functions, I have to assign it like this
                var _desSources = desSources;
                var _partsParentI = partsParentI;
                var _fStructs = fStructs;
                var _desProps = desProps;
                var _bendProps = bendProps;
                int partCount = structPosL.Length;
                var _KinParts = kinematicPartIndexes;
                var _partsToBreak = partsToBreak;
                var _newParentsData = newParentsData;
                var _parentPartCount = parentPartCount;
                var _partsNewParentI = partsNewParentI;
                var _optMainPhyType = optMainPhyType;
                var _fixedDeltaTime = fixedDeltaTime;
                var _partIToDesMatI = partIToDesMatI;
                var _defPoints = defPoints;
                var _deformedPartsI = deformedPartsI;
                var _defOffsetW = defOffsetW;
                var _partMaxExtent = partMaxExtent;

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
                NativeHashMap<int, byte> impPartIToImpSourceI = new(8, Allocator.Temp);
                _defPoints.Clear();
                //NativeList<Vector3> allImpPossW = new(8, Allocator.Temp);
                //NativeList<Vector3> allImpDirsW = new(8, Allocator.Temp);

                for (byte sourceI = 0; sourceI < desSources.Length; sourceI++)
                {
                    CalcSource(sourceI);
                }

                //get the velocity to give all parts and if need any new parents
                CalcChunks();

                //deformation
                float bendForceOg;
                CalcDeformation();
                defBendForce.Value = bendForceOg;

                //Return destruction result
                //Transform parts world position back to local
                for (int partI = 0; partI < partCount; partI++)
                {
                    structPosL[partI] = boneBindsLToW[partI + partBoneOffset].inverse.MultiplyPoint3x4(partsWPos[partI]);
                }

                void CalcDeformation()
                {
                    //Deformation should be simple, just offset all
                    //get max bend amount
                    int offsetPointCount = _defPoints.Length;
                    bendForceOg = allTotImpForces / offsetPointCount;
                    //Debug.Log(bendForceOg + " " + offsetPointCount);
                    for (int opI = 0; opI < offsetPointCount; opI++)
                    {
                        DefPoint defPoint = _defPoints[opI];
                        Vector3 oPos = defPoint.defPos;
                        Vector3 oVel = defPoint.defVel / offsetPointCount;//This should be clamped to wall hit, max deformation and kinematic parts

                        int propI = _partIToDesMatI[defPoint.partI];
                        oVel = Vector3.MoveTowards(oVel, Vector3.zero, _defOffsetW[defPoint.partI].magnitude * (1.0f - _desProps[propI].damageAccumulation));
                        float maxDef = Math.Min(defPoint.disToWall > 0.0f ? defPoint.disToWall : 69420.0f, GetDisToKin(ref oPos, defPoint.parentI));

                        //At maxDef bendForceOg should be 0

                        int parentI = defPoint.parentI;
                        float oDis = oVel.magnitude;
                        if (oDis > maxDef)
                        {
                            oDis = maxDef;
                            oVel = oVel.normalized * maxDef;
                        }

                        if (oDis <= 0.0f) oDis = 0.0001f;

                        for (int pI = 0; pI < partCount; pI++)
                        {
                            if (_partsParentI[pI] != parentI || _partsToBreak.ContainsKey(pI) == true) continue;

                            DestructionMaterial.BendProperties bendProp = _bendProps[_partIToDesMatI[pI]];

                            float bendStrenght = bendProp.bendStrenght;
                            float bendForce = bendForceOg > bendStrenght ? bendStrenght : bendForceOg;

                            float falloffX = (partsWPos[pI] - oPos).magnitude * bendProp.bendFalloff;
                            falloffX += falloffX * (falloffX * bendProp.bendPower);
                            if (falloffX * 0.5f > bendForce) continue;

                            FracStruct fPart = _fStructs[pI];
                            if (fPart.maxTransportUsed <= 0.0f) fPart.maxTransportUsed = 0.0001f;
                            _fStructs[pI] = fPart;
#pragma warning disable CS0162
                            if (FracGlobalSettings.sensitiveColliderSync == true) _deformedPartsI.Enqueue(pI);


                            if (falloffX > bendForce) continue;

                            if (FracGlobalSettings.sensitiveColliderSync == false) _deformedPartsI.Enqueue(pI);
#pragma warning restore CS0162
                            Vector3 newOffset = bendProp.bendyness * Mathf.Clamp01((bendForce - falloffX) / (bendStrenght * oDis)) * oVel;
                            partsWPos[pI] += newOffset;
                            _defOffsetW[pI] += newOffset;
                        }

                        defPoint.defVel = oVel;
                        _defPoints[opI] = defPoint;
                    }
                }

                float GetDisToKin(ref Vector3 pos, int parentI)
                {
#pragma warning disable CS0162
                    if (FracGlobalSettings.preventKinematicDeformation == false) return 69420.0f;

                    if (parentI != 0) return 69420.0f; //KinematicParts should always have defualt parent!?

                    float minDis = 69420.0f;
                    foreach (int partI in _KinParts)
                    {
                        float dis = (pos - partsWPos[partI]).magnitude - (_partMaxExtent * 1.5f);
                        if (dis < minDis) minDis = dis;
                    }

                    return Math.Max(minDis, 0.001f);
#pragma warning restore CS0162
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
                    FracHelpFunc.SetWholeNativeArray<byte>(ref _partsNewParentI, 0);
                    byte nextNewParentI = 0;
                    float totUsedForce = 0.0f;

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
                            p_totMass += _desProps[_partIToDesMatI[nPI]].mass;
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
                                p_totMass += _desProps[_partIToDesMatI[nPI]].mass;
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
                                Debug.Log("reduced breakForce");
                                breakForce -= pBreak.velForce;
                                continue;
                            }

                            breakForce += pBreak.velForce;
                            breakVel += pBreak.velTarget;
                            breakPos += partsWPos[bsPI];
                            avgBreakForceLeft += pBreak.forceLeft;
                            usedBreakCount++;
                        }

                        if (usedBreakCount == 0) continue;

                        if (breakForce < 0.0f)
                        {
                            breakForce = 0.0f; //In theory it should be impossible for it to be <0.0f
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
                        float forceRequired = (pBreak.velTarget.magnitude * _desProps[_partIToDesMatI[pBreak.partI]].mass);
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
                        if (_newParentsData.TryGetValue(best_nParentI, out DesNewParentData newPData) == false) continue;
                        int sParentI = newPData.sourceParentI;
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
                        _newParentsData.Remove(best_nParentI);
                        //DesNewParentData newPD = _newParentsData[best_nParentI];
                        //newPD.newPartCount = -1;
                        //_newParentsData[best_nParentI] = newPD;

                        for (int partI = 0; partI < partCount; partI++)
                        {
                            if (_partsNewParentI[partI] == best_nParentI) _partsNewParentI[partI] = 0;
                        }
                    }

                    ////Get how much velocity impact source rb should keep, if we enable this again note that _desSources is used after
                    //NativeArray<float> impSourceForces = new(_desSources.Length, Allocator.Temp);
                    //
                    //foreach (var keyValue in impPartIToImpSourceI)
                    //{
                    //    int partI = keyValue.Key;
                    //    if (_partsToBreak.TryGetValue(partI, out DesPartToBreak pBreak) == true)
                    //    {
                    //        impSourceForces[keyValue.Value] += pBreak.forceLeft;
                    //    }
                    //    else if (_partsNewParentI[partI] > 0) impSourceForces[keyValue.Value] += _newParentsData[_partsNewParentI[partI]].forceLeft;
                    //}
                    //
                    //for (byte sourceI = 0; sourceI < _desSources.Length; sourceI++)
                    //{
                    //    DestructionSource desSource = _desSources[sourceI];
                    //    if (desSource.impForceTotal <= 0.0f && desSource.sourceRbVirtualMass > 0.0f) continue;
                    //
                    //    desSource.impVel = -desSource.impVel.normalized * (Mathf.Min(impSourceForces[sourceI], desSource.impForceTotal) / desSource.sourceRbVirtualMass);
                    //    _desSources[sourceI] = desSource;
                    //}
                }

                unsafe void CalcSource(byte sourceI)
                {
                    //get the source
                    DestructionSource desSource = _desSources[sourceI];
                    if (desSource.impForceTotal <= 0.0f || desSource.parentI < 0) return;
                    //allTotImpForces += desSource.impForceTotal;

                    //reset nativeArrays
                    FracHelpFunc.SetWholeNativeArray(ref partsMoveMass, 0.0f);//To prevent devision by zero

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
                    Vector3 velDir = desSource.impVel.normalized;
                    NativeArray<Vector3> partsVelDir = new(partCount, Allocator.Temp);

                    for (int impI = 0; impI < desPoints.Length; impI++)
                    {
                        desPoint = desPoints[impI];
                        if (_partsToBreak.ContainsKey(desPoint.partI) == true) continue;

                        if (desPoint.force > maxForce)
                        {
                            if (desPoint.partI >= partCount || desPoint.partI < 0) continue;//This should never be able to be true but it sometimes is, so lazy "fix"
                            if (partIToLayerI[desPoint.partI] != 0) continue;

                            orderIToPartI[nextOrderI] = desPoint.partI;
                            nextOrderI++;

                            impPartIToImpSourceI.TryAdd(desPoint.partI, sourceI);
                            partsVelDir[desPoint.partI] = velDir;
                            partIToLayerI[desPoint.partI] = nextLayerI;
                            totForceOgklk += desPoint.force;

                            _defPoints.Add(new()
                            {
                                defPos = desPoint.impPosW,
                                defVel = desSource.impVel,
                                parentI = desSource.parentI,
                                disToWall = desPoint.disToWall,
                                partI = desPoint.partI,
                            });

                            desSource.avgImpPos += desPoint.impPosW;
                        }
                    }

                    desSource.avgImpPos /= nextOrderI;
                    _desSources[sourceI] = desSource;
                    allTotImpForces += totForceOgklk;

                    //Get each part distance to any 90% impact
                    int oI = 0;

                    while (oI < nextOrderI)
                    {
                        int innerLoopStop = nextOrderI;
                        nextLayerI++;

                        while (oI < innerLoopStop)
                        {
                            int partI = orderIToPartI[oI];
                            FracStruct fPart = _fStructs[partI];
                            Vector3 partVelDir = partsVelDir[partI].normalized;
                            Vector3 partWPos = partsWPos[partI];
                            oI++;
                            
                            for (int nI = 0; nI < fPart.neighbourPartI_lenght; nI++)
                            {
                                int npI = fPart.neighbourPartI[nI];

                                if (partIToLayerI[npI] != 0 || _partsParentI[npI] != desSource.parentI || _partsToBreak.ContainsKey(npI) == true) continue;

                                partsVelDir[npI] += (partVelDir * 2.0f) + (partsWPos[npI] - partWPos).normalized;
                                partIToLayerI[npI] = nextLayerI;
                                orderIToPartI[nextOrderI] = npI;
                                nextOrderI++;
                            }

                            partsVelDir[partI] = partVelDir;
                        }
                    }

                    //get the mass each part would need to "push"
                    int usedStartCount = Mathf.NextPowerOfTwo(partCount / nextLayerI);
                    NativeHashSet<int> usedPI = new(partCount, Allocator.Temp);
                    NativeList<int> usedTPI = new(usedStartCount, Allocator.Temp);
                    NativeHashSet<int> usedNPI = new(usedStartCount, Allocator.Temp);
                    float velDis = desSource.impVel.magnitude;
                    //bool alwaysKin = (desSource.parentI == 0 && _optMainPhyType == OptMainPhysicsType.orginalIsKinematic) || _optMainPhyType == OptMainPhysicsType.alwaysKinematic;
                    //We currently dont know if a part is kinematic if orginalIsManuall or Manuall is selected,
                    //I currently pretend that they are always dynamic, should rarely if ever cause a problem.

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
                            DestructionMaterial.DesProperties desProp = _desProps[_partIToDesMatI[partI]];
                            //resMass += _KinParts.Contains(partI) == false && alwaysKin == false ? desProp.mass : desProp.stenght;
                            resMass += _KinParts.Contains(partI) == false ? desProp.mass : desProp.stenght;
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
                            DestructionMaterial.DesProperties desProp = _desProps[_partIToDesMatI[partI]];
                            float pTransCap = GetPartActualTransCap(ref fPart, ref desProp);
                            partsMoveMass[pI] = resMass * (pTransCap / totTransCap);

                            //get if part should break
                            if (FracGlobalSettings.kinematicPartsCanBreak == false && _KinParts.Contains(pI) == true) continue;

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

                            float forceRequired = totForceOgklk * Mathf.Clamp01((pTransCap / totTransCap) * Mathf.Max(1.0f, usedTPI.Length / 2.0f));
                            //float forceRequired = totForceOgklk * Mathf.Clamp01((pTransCap / totTransCap));
                            forceRequired = Mathf.Min((velDis * partsMoveMass[pI]) + (forceRequired - (forceRequired * Mathf.Clamp01(desProp.chockResistance * layerI))), forceRequired);
                            forceRequired -= forceRequired * Mathf.Clamp01((layerI - 1) * desProp.falloff);

                            //transDir /= usedNeighbourCount;
                            transDir.Normalize();//Maybe we should use the best dir instead of avg?
                            //pTransCap *= Mathf.Clamp01(Mathf.Abs(Vector3.Dot(velDir, transDir)) + FracGlobalSettings.transDirInfluenceReduction);
                            Vector3 partVelDir = partsVelDir[pI];
                            pTransCap *= Mathf.Clamp01(Mathf.Abs(Vector3.Dot(partVelDir, transDir)) + FracGlobalSettings.transDirInfluenceReduction);

                            if (pTransCap <= forceRequired)
                            {
                                partsMoveMass[pI] *= pTransCap / forceRequired;
                                fPart.maxTransportUsed = 1.0f;

                                _partsToBreak[pI] = new()
                                {
                                    partI = pI,
                                    velForce = pTransCap * 0.5f,
                                    //velTarget = (velDir * velDis) + FractureHelperFunc.GetObjectVelocityAtPoint(desSource.parentWToL_prev, desSource.parentLToW_now, partWPos, _fixedDeltaTime),
                                    velTarget = (partVelDir * velDis) + FracHelpFunc.GetObjectVelocityAtPoint(desSource.parentWToL_prev, desSource.parentLToW_now, partWPos, _fixedDeltaTime),
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

        public struct DefPoint
        {
            public Vector3 defPos;
            public Vector3 defVel;
            public float disToWall;
            public int parentI;
            public int partI;
        };

        private void ApplySkinAndDef()
        {
            //return if not ready
            if (gpuIsReady == false) return;

            //compute skinning and deformation on gpu
            if (wantToApplyDeformation == false) computeDestructionSolver.SetInt("defPointsLenght", 0);
            computeDestructionSolver.SetMatrix("fracRendWToL", fracRend.worldToLocalMatrix);
            computeDestructionSolver.Dispatch(cpKernelId_ComputeSkinDef, fracRendDividedVerCount, 1, 1);

            if (wantToApplyDeformation == true || wantToApplySkinning == true)
            {
                if (des_deformedParts[des_deformedPartsIndex].Count > 0) gpuMeshRequest_do = true;
                wantToApplyDeformation = false;
                wantToApplySkinning = false;
            }
        }

        private Matrix4x4[] gpuMeshBonesLToW;
        private GpuMeshVertex[] gpuMeshVertexData;

        private void UpdateGpuMeshReadback()
        {

            //get mesh data from readback
            byte oppositeI = (byte)(1 - des_deformedPartsIndex);
            bool supportAsync = SystemInfo.supportsAsyncGPUReadback;
            //bool supportAsync = false;

            if (gpuMeshRequest.done == true || supportAsync == false)
            {
                if (gpuMeshRequest.hasError == false && supportAsync == true)
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
                    int partI = des_deformedParts[oppositeI].FirstOrDefault();
                    if (des_deformedParts[oppositeI].Remove(partI) == false) break;

                    Matrix4x4 partLToW = gpuMeshBonesLToW[partI + partBoneOffset].inverse;
                    Matrix4x4 rendWToL = fracRend.localToWorldMatrix;
                    Vector3[] partPossL = new Vector3[allParts[partI].partColVerts.Count];
                    short pI = 0;

                    foreach (int vI in allParts[partI].partColVerts)
                    {
                        partPossL[pI] = partLToW.MultiplyPoint3x4(rendWToL.MultiplyPoint3x4(gpuMeshVertexData[vI].pos));
                        pI++;
                    }

                    FracHelpFunc.SetColliderFromFromPoints(saved_allPartsCol[partI], partPossL, ref partMaxExtent);
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
                if (supportAsync == true)
                {
                    gpuMeshRequest = AsyncGPUReadback.Request(buf_verNors);
                    gpuMeshRequest.forcePlayerLoopUpdate = true;
                }
                else
                {
                    if (gpuMeshVertexData.Length != fr_verticesL.Count) gpuMeshVertexData = new GpuMeshVertex[fr_verticesL.Count];
                    buf_verNors.GetData(gpuMeshVertexData);
                }

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
        private readonly object destructionPairsLock = new();
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

            public Matrix4x4 parentLToW_now;
            public Matrix4x4 parentWToL_prev;

            public Vector3 avgImpPos;
        }

        public struct DestructionPoint
        {
            public Vector3 impPosW;
            public float force;
            public int partI;
            public float disToWall;
        }

        /// <summary>
        /// Applies force as damage to the object the next physics frame
        /// </summary>
        /// <param name="impactPoints">The nativeArray must have a Persistent allocator, it will be disposed by the destructableObject. DO NOT DISPOSE IT ANYWHERE ELSE</param>
        /// <param name="sourceRb">The rb that caused the impact, null if caused by self or misc</param>
        /// <param name="impactId">Used to identify different impact sources, if == 0 a unique id will be generated</param>
        /// <param name="canOverwrite">If impactId already exists, true == overwrite existing source, false == merge with existing source</param>
        public unsafe void RegisterImpact(DestructionSource impactData, NativeArray<DestructionPoint> impactPoints, int thisRbJGRVI, Rigidbody sourceRb, int impactId = 0, bool canOverwrite = false)
        {
            //Debug.Log(impactId + " " + impactData.impVel.magnitude + " " + (impactData.impForceTotal / impactPoints.Count()));
            //Get stuff that does not need to be assigned when calling register function
            impactData.desPoints_ptr = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(impactPoints);
            impactData.desPoints_lenght = impactPoints.Length;

            //if (globalHandler.rbInstancIdToJgrvIndex.TryGetValue(thisRbJGRVI, out int rbI) == true)
            if (thisRbJGRVI >= 0)
            {
                var parentRbData = globalHandler.jGRV_job.rb_posData[thisRbJGRVI];
                impactData.parentWToL_prev = parentRbData.rbWToLPrev;//Do we really wanna get these here, we could just get them when starting the job
                impactData.parentLToW_now = parentRbData.rbLToWNow;
            }
            else
            {
                //parent has not been registered yet so get some "fake" matrixs, can happen if you try to break a parent the same frame it was created.
                impactData.parentWToL_prev = Matrix4x4.identity;
                impactData.parentLToW_now = Matrix4x4.identity.inverse;
                //Debug.LogError("The parent rigidbody has not been registered by the globalHandler, unexpected behavior may occure!");
            }

            lock (destructionPairsLock)
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
                    destructionPairs.TryAdd(impactId, impactPoints);
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
        [System.NonSerialized] public List<int> partsLocalParentPath = new();
        [System.NonSerialized] public Dictionary<int, int> localPathToRbIndex = new();

        /// <summary>
        /// Contains all part indexes that is always kinematic (Kinematic overlap or kinematic groupData)
        /// </summary>
        //[System.NonSerialized] public NativeHashSet<int> partsKinematicStatus = new();

        /// <summary>
        /// The global handler, must always exists if it is fractured
        /// </summary>
        [SerializeField] private FractureGlobalHandler globalHandler;

        [System.NonSerialized] public int fracInstanceId;

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





        #region MeshRepairSystem

        private ComputeBuffer bufR_og_frMeshData;
        private ComputeBuffer bufR_partIWannaRestore;
        private bool wannaRunRepairCompute = false;
        private bool repairIsSetup = false;
        //ComputeBuffer bufR_isRepairDone;
        //readonly int[] repair_isGpuRepairDone = new int[1];

        private void RepairSys_setup()
        {
            int partCount = allParts.Count;
            jGTD_job.repair_partIWannaRestore = new(partCount, Allocator.Persistent);
            jGTD_job.repair_partsDefualtLoc = new(partCount, Allocator.Persistent);
            bufR_partIWannaRestore = new ComputeBuffer(partCount,
                sizeof(int));
            
            bufR_partIWannaRestore.SetData(jGTD_job.repair_partIWannaRestore);
            computeDestructionSolver.SetBuffer(cpKernelId_RestoreSkinDef, "partIWannaRestore", bufR_partIWannaRestore);
            repair_ogStructsPosL = new Vector3[partCount];
            //bufR_og_frMeshData is set in syncWithGpu() along with buf_meshData

            for (int partI = 0; partI < partCount; partI++)
            {
                Transform pTrans = saved_allPartsCol[partI].transform;
                jGTD_job.repair_partsDefualtLoc[partI] = new()
                {
                    pos = pTrans.localPosition,
                    rot = pTrans.localRotation
                };

                repair_ogStructsPosL[partI] = jCDW_job.structPosL[partI];
            }

            repairIsSetup = true;
        }

        private void RepairSys_update()
        {
            if (wannaRunRepairCompute == false) return;

            //Run repair compute
            computeDestructionSolver.Dispatch(cpKernelId_RestoreSkinDef, fracRendDividedVerCount, 1, 1);
            wantToApplySkinning = true;
            wannaRunRepairCompute = false;

            if (FracGlobalSettings.maxColliderUpdatesPerFrame > 0)
            {
                foreach (int partI in partsToRepair)
                {
                    des_deformedParts[des_deformedPartsIndex].Add(partI);
                }
            }

            partsToRepair.Clear();
        }

        private HashSet<int> partsToRepair = new();
        private Vector3[] repair_ogStructsPosL;

        /// <summary>
        /// Restores the given part (Resets deformation, damage recieved, position, rotation and parent)
        /// </summary>
        public void RequestRepairPart(int partI)
        {
            //make sure jobs aint running
            GetTransformData_end();
            ComputeDestruction_end();

            //reset part stats
            FracStruct fStruct = jCDW_job.fStructs[partI];
            fStruct.maxTransportUsed = 0.0f;
            jCDW_job.fStructs[partI] = fStruct;
            jCDW_job.structPosL[partI] = repair_ogStructsPosL[partI];
            jCDW_job.defOffsetW[partI] = Vector3.zero;
            partsToRepair.Add(partI);

            //set parent and notify gpu and job about restoring it
            SetPartParent(partI, 0, Vector3.zero);
            jGTD_job.repair_partIWannaRestore[partI] = 1;
            bufR_partIWannaRestore.SetData(jGTD_job.repair_partIWannaRestore, partI, partI, 1);
            wannaRunRepairCompute = true;
        }

        /// <summary>
        /// Returns the next part index that has taken any damage, returns -1 if no damaged part exists (Next means a damaged part that will be connected to a non damaged part) 
        /// </summary>
        public unsafe int GetNextDamagedPartI()
        {
            //make sure jobs aint running
            ComputeDestruction_end();

            if (allParents[0].partIndexes.Count == 0)
            {
                if (jCDW_job.kinematicPartIndexes.Count() == 0) return 0;
                else foreach (int partI in jCDW_job.kinematicPartIndexes)
                    {
                        return partI;
                    }
            }

            foreach (int partI in allParents[0].partIndexes)
            {
                FracStruct fStruct = jCDW_job.fStructs[partI];
                if (fStruct.maxTransportUsed > 0.0f) return partI;

                for (int nI = 0; nI < fStruct.neighbourPartI_lenght; nI++)
                {
                    short nPI = fStruct.neighbourPartI[nI];
                    if (jCDW_job.partsParentI[nPI] != 0) return nPI;
                }
            }

            return -1;
        }

        /// <summary>
        /// Returns the next part index that does not have the defualt parent, returns -1 if no damaged part exists (Next means a damaged part that will be connected to a non damaged part) 
        /// </summary>
        public unsafe int GetNextBrokenPart()
        {
            //make sure jobs aint running
            ComputeDestruction_end();

            if (allParents[0].partIndexes.Count == 0)
            {
                if (jCDW_job.kinematicPartIndexes.Count() == 0) return 0;
                else foreach (int partI in jCDW_job.kinematicPartIndexes)
                    {
                        return partI;
                    }
            }

            foreach (int partI in allParents[0].partIndexes)
            {
                FracStruct fStruct = jCDW_job.fStructs[partI];

                for (int nI = 0; nI < fStruct.neighbourPartI_lenght; nI++)
                {
                    short nPI = fStruct.neighbourPartI[nI];
                    if (jCDW_job.partsParentI[nPI] != 0) return nPI;
                }
            }

            return -1;
        }

        /// <summary>
        /// Returns a part index that has taken any damage, returns -1 if no damaged part exists
        /// </summary>
        public int GetDamagedPartI()
        {
            //make sure jobs aint running
            ComputeDestruction_end();

            int partCount = allParts.Count;
            for (int partI = 0; partI < partCount; partI++)
            {
                if (jCDW_job.partsParentI[partI] != 0) return partI;
                if (jCDW_job.fStructs[partI].maxTransportUsed > 0.0f) return partI;
            }

            return -1;
        }

        /// <summary>
        /// Returns a part index that does not have a parent, returns -1 if no lose part exists
        /// </summary>
        public int GetLosePartI()
        {
            //make sure jobs aint running
            ComputeDestruction_end();

            int partCount = allParts.Count;
            for (int partI = 0; partI < partCount; partI++)
            {
                if (jCDW_job.partsParentI[partI] < 0) return partI;
            }

            return -1;
        }

        /// <summary>
        /// Returns a part that has the given parent that is deformed, returns -1 if no deformed part exists in parent
        /// </summary>
        public int GetDeformedPartInParent(int parentI)
        {
            //make sure jobs aint running
            ComputeDestruction_end();

            foreach (int partI in allParents[parentI].partIndexes)
            {
                if (jCDW_job.fStructs[partI].maxTransportUsed > 0.0f) return partI;
            }

            return -1;
        }

        /// <summary>
        /// Returns the part that has the most deformation and has the given parent, returns -1 if no deformed part exists in parent
        /// </summary>
        public int GetMostDeformedPartInParent(int parentI, out float deformationAmount)
        {
            //make sure jobs aint running
            ComputeDestruction_end();
            float maxDef = 0.0f;
            int maxPartI = -1;

            foreach (int partI in allParents[parentI].partIndexes)
            {
                float def = jCDW_job.fStructs[partI].maxTransportUsed;
                if (def > maxDef)
                {
                    maxDef = def;
                    maxPartI = partI;
                }
            }

            deformationAmount = maxDef;
            return maxPartI;
        }

        /// <summary>
        /// Returns true if any broken part exists (A part that does not have the defualt parent, could be used to avoid calling GetNextDamagedPartI() when it most likely will return -1 anyway)
        /// </summary>
        public bool GetIfBrokenPartExists()
        {
            return allParents[0].partIndexes.Count != allParts.Count;
        }

        #endregion MeshRepairSystem





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
                        lapCols[i].transform.position, FracGlobalSettings.worldScale * 0.01f),
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
            Debug.LogError("SkinPartVertics() is no longer supported because partMeshVerts does not longer exists!");

           //foreach (int vI in allParts[partI].partMeshVerts)
           //{
           //    //Storing all mBake_vms in a array and only updating them when a bone has moved may be worth it??
           //    mBake_weight = fr_boneWeightsCurrent[vI];
           //    mBake_bm0 = jCDW_job.boneBindsLToW[mBake_weight.boneIndex0];
           //    mBake_bm1 = jCDW_job.boneBindsLToW[mBake_weight.boneIndex1];
           //    mBake_bm2 = jCDW_job.boneBindsLToW[mBake_weight.boneIndex2];
           //    mBake_bm3 = jCDW_job.boneBindsLToW[mBake_weight.boneIndex3];
           //
           //    mBake_vms.m00 = mBake_bm0.m00 * mBake_weight.weight0 + mBake_bm1.m00 * mBake_weight.weight1 + mBake_bm2.m00 * mBake_weight.weight2 + mBake_bm3.m00 * mBake_weight.weight3;
           //    mBake_vms.m01 = mBake_bm0.m01 * mBake_weight.weight0 + mBake_bm1.m01 * mBake_weight.weight1 + mBake_bm2.m01 * mBake_weight.weight2 + mBake_bm3.m01 * mBake_weight.weight3;
           //    mBake_vms.m02 = mBake_bm0.m02 * mBake_weight.weight0 + mBake_bm1.m02 * mBake_weight.weight1 + mBake_bm2.m02 * mBake_weight.weight2 + mBake_bm3.m02 * mBake_weight.weight3;
           //    mBake_vms.m03 = mBake_bm0.m03 * mBake_weight.weight0 + mBake_bm1.m03 * mBake_weight.weight1 + mBake_bm2.m03 * mBake_weight.weight2 + mBake_bm3.m03 * mBake_weight.weight3;
           //
           //    mBake_vms.m10 = mBake_bm0.m10 * mBake_weight.weight0 + mBake_bm1.m10 * mBake_weight.weight1 + mBake_bm2.m10 * mBake_weight.weight2 + mBake_bm3.m10 * mBake_weight.weight3;
           //    mBake_vms.m11 = mBake_bm0.m11 * mBake_weight.weight0 + mBake_bm1.m11 * mBake_weight.weight1 + mBake_bm2.m11 * mBake_weight.weight2 + mBake_bm3.m11 * mBake_weight.weight3;
           //    mBake_vms.m12 = mBake_bm0.m12 * mBake_weight.weight0 + mBake_bm1.m12 * mBake_weight.weight1 + mBake_bm2.m12 * mBake_weight.weight2 + mBake_bm3.m12 * mBake_weight.weight3;
           //    mBake_vms.m13 = mBake_bm0.m13 * mBake_weight.weight0 + mBake_bm1.m13 * mBake_weight.weight1 + mBake_bm2.m13 * mBake_weight.weight2 + mBake_bm3.m13 * mBake_weight.weight3;
           //
           //    mBake_vms.m20 = mBake_bm0.m20 * mBake_weight.weight0 + mBake_bm1.m20 * mBake_weight.weight1 + mBake_bm2.m20 * mBake_weight.weight2 + mBake_bm3.m20 * mBake_weight.weight3;
           //    mBake_vms.m21 = mBake_bm0.m21 * mBake_weight.weight0 + mBake_bm1.m21 * mBake_weight.weight1 + mBake_bm2.m21 * mBake_weight.weight2 + mBake_bm3.m21 * mBake_weight.weight3;
           //    mBake_vms.m22 = mBake_bm0.m22 * mBake_weight.weight0 + mBake_bm1.m22 * mBake_weight.weight1 + mBake_bm2.m22 * mBake_weight.weight2 + mBake_bm3.m22 * mBake_weight.weight3;
           //    mBake_vms.m23 = mBake_bm0.m23 * mBake_weight.weight0 + mBake_bm1.m23 * mBake_weight.weight1 + mBake_bm2.m23 * mBake_weight.weight2 + mBake_bm3.m23 * mBake_weight.weight3;
           //
           //    skinnedVerticsW[vI] = mBake_vms.MultiplyPoint3x4(fr_verticesL[vI]);
           //}
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

            //float impForce = allParents[parentI].parentKinematic > 0 ? float.MaxValue : velSpeed * (allParents[parentI].parentMass - desMat.desProps.mass);
            if (localPathToRbIndex.TryGetValue(partsLocalParentPath[partI], out int rbI) == false) rbI = -1;
            float impForce = rbI < 0 || allParents[parentI].parentRbs[rbI].rbIsKin == true ?
                float.MaxValue : velSpeed * (allParents[parentI].parentMass - desMat.desProps.mass);

            float totalTransportCap = desMat.desProps.stenght * jCDW_job.fStructs[partI].neighbourPartI_lenght;
            if (impForce > totalTransportCap) impForce = totalTransportCap;//We currently do not consider transportMaxDamage, we probably wanna do to that

            int parentPartCount = allParents[parentI].partIndexes.Count;
            impForce += impForce * (allParents[parentI].totalStiffness / parentPartCount);
            impForce += velSpeed * desMat.desProps.mass;
            impForce += impForce * bouncyness * FracGlobalSettings.bouncynessEnergyConsumption;
            return impForce + (impForce * (allParents[parentI].totalTransportCoEfficiency / parentPartCount));
        }

        /// <summary>
        /// Returns the rough force that partI can apply to X if partI would collide with X at the given velocity (If transCap > 0.0f, you may wanna clamp the returned float with it)
        /// </summary>
        public float GuessMaxForceApplied(Vector3 velocity, short partI, out float transCap, float bouncyness = 0.0f)
        {
            int parentI = jCDW_job.partsParentI[partI];
            DestructionMaterial.DesProperties desProp = GetDesMatFromIntId(allParts[partI].groupIdInt).desProps;
            float velSpeed = velocity.magnitude;
            if (parentI < 0)
            {
                //if no parent, it cant break so it has infinit stenght
                transCap = 0.0f;
                if (jCDW_job.kinematicPartIndexes.Contains(partI) == true) return float.MaxValue;

                velSpeed *= desProp.mass;
                velSpeed -= velSpeed * bouncyness * FracGlobalSettings.bouncynessEnergyConsumption;
                return velSpeed;
            }

            //float impForce = allParents[parentI].parentKinematic > 0 ? float.MaxValue : (velSpeed * allParents[parentI].parentMass);
            if (localPathToRbIndex.TryGetValue(partsLocalParentPath[partI], out int rbI) == false) rbI = -1;
            float impForce = rbI < 0 || allParents[parentI].parentRbs[rbI].rbIsKin == true ?
                float.MaxValue : (velSpeed * allParents[parentI].parentMass);

            FracStruct fStruct = jCDW_job.fStructs[partI];

            //float transCap = (desProp.stenght - (desProp.stenght * fStruct.maxTransportUsed * desProp.damageAccumulation)) * GetPartConnectionCount(ref fStruct, parentI);
            transCap = (desProp.stenght - (desProp.stenght * fStruct.maxTransportUsed * desProp.damageAccumulation)) + (desProp.stenght * desProp.chockResistance);
            //float transCap = (desProp.stenght - (desProp.stenght * fStruct.maxTransportUsed * desProp.damageAccumulation)) + (desProp.stenght * desProp.chockResistance);
            //transCap /= Mathf.Clamp01(0.25f + FracGlobalSettings.transDirInfluenceReduction);
            //if (impForce > transCap) impForce = transCap;

            impForce -= impForce * bouncyness * FracGlobalSettings.bouncynessEnergyConsumption;
            return impForce;
        }

        /// <summary>
        /// The lowest TransportCapacity any part has
        /// </summary>
        private float lowestTransportCapacity = 0.0f;

        /// <summary>
        /// Returns true if applying the given force on partI is likely to cause any noticiable destruction on the object
        /// </summary>
        public bool GuessIfForceCauseBreaking(float force, int partI, out float transCap, float bouncyness = 0.0f)
        {
            //fix later, we have changed what stiffness does
            DestructionMaterial.DesProperties desProp = destructionMaterials[jCDW_job.partIToDesMatI[partI]].desProps;
            DestructionMaterial.BendProperties bendProp = destructionMaterials[jCDW_job.partIToDesMatI[partI]].bendProps;

            transCap = desProp.stenght - (desProp.stenght * jCDW_job.fStructs[partI].maxTransportUsed * desProp.damageAccumulation);
            //transCap *= Mathf.Clamp01(0.25f + FracGlobalSettings.transDirInfluenceReduction); 
            force -= force * bouncyness * FracGlobalSettings.bouncynessEnergyConsumption;
            transCap -= transCap * bendProp.bendyness;

            return force > transCap;
        }

        /// <summary>
        /// Returns how many neighbours of fStruct has parentI as parent, if two parts has the same parent they are connected
        /// </summary>
        private unsafe int GetPartConnectionCount(ref FracStruct fStruct, int parentI)
        {
            int connectionCount = 0;

            for (int nI = 0; nI < fStruct.neighbourPartI_lenght; nI++)
            {
                if (jCDW_job.partsParentI[fStruct.neighbourPartI[nI]] != parentI) continue;
                connectionCount++;
            }

            return connectionCount;
        }

#endregion HelperFunctions
    }
}