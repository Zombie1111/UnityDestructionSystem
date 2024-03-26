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
using Unity.Mathematics;
using UnityEditor.SceneManagement;
using UnityEngine.Jobs;
using Unity.Collections;
using Unity.Jobs;
using System.Diagnostics;
using UnityEngine.Rendering;
using UnityEditor.ShaderKeywordFilter;
using System.Net.NetworkInformation;
using UnityEditor.ShaderGraph;
using TreeEditor;
using Unity.VisualScripting;
using UnityEngine.Experimental.AI;
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
        }

        public void CopyFracturePropertiesFrom(FractureThis from)
        {
            worldScale = from.worldScale;
            fractureCount = from.fractureCount;
            dynamicFractureCount = from.dynamicFractureCount;
            seed = from.seed;
            maxFractureAttempts = from.maxFractureAttempts;
            generationQuality = from.generationQuality;
            colliderType = from.colliderType;
            phyMainOptions = from.phyMainOptions;
            phyPartsOptions = from.phyPartsOptions;
            insideMat_fallback = from.insideMat_fallback;
            insideMat_nameAddition = from.insideMat_nameAddition;
            physicsMat = from.physicsMat;
        }
#endif

        //fracture settings
        [Header("Fracture")]
        public FractureSaveAsset saveAsset = null;
        [SerializeField] private FractureSavedState saveState = null;
        [SerializeField] private float worldScale = 1.0f;
        [SerializeField] private int fractureCount = 15;
        [SerializeField] private bool dynamicFractureCount = true;
        [Tooltip("If < 1.0f, controls how random the angle of the cuts are. If >= 1.0f, voronoi is used")]
        [SerializeField][Range(0.0f, 1.0f)] private float randomness = 1.0f;
        [SerializeField] private int seed = -1;
        [SerializeField] private byte maxFractureAttempts = 20;
        public GenerationQuality generationQuality = GenerationQuality.normal;
        [SerializeField] private FractureRemesh remeshing = FractureRemesh.defualt;

        [Space(10)]
        [Header("Physics")]
        [SerializeField] private ColliderType colliderType = ColliderType.mesh;
        [SerializeField] private OptPhysicsMain phyMainOptions = new();
        [SerializeField] private OptPhysicsParts phyPartsOptions = new();

        [Space(10)]
        [Header("Destruction")]

        [Space(10)]
        [Header("Mesh")]

        [Space(10)]
        [Header("Material")]
        [SerializeField] private Material insideMat_fallback = null;
        [SerializeField] private string insideMat_nameAddition = "_inside";
        [SerializeField] private PhysicMaterial physicsMat = null;

        [Space(10)]
        [Header("Advanced")]
        [SerializeField] private bool splitDisconnectedFaces = false;
        [SerializeField] private bool useGroupIds = false;
#if UNITY_EDITOR
        [SerializeField] private int visualizedGroupId = -1;
#endif
        /// <summary>
        /// Contains all groups that has overrides
        /// </summary>
        [SerializeField] private List<GroupIdData> groupDataOverrides = new();

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
        [SerializeField][Tooltip("Only works at runtime")] private DebugMode debugMode = DebugMode.none;

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
            //Do not run while playing
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
                Gen_removeFracture(false, false, false);
                return;
            }

            GlobalUpdate();
        }

        private void OnDrawGizmosSelected()
        {
            //draw selected group id
            if (visualizedGroupId < 0 || useGroupIds == false || fracRend != null)
            {
                if (visualizedGroupId >= 0 && useGroupIds == true) Debug.LogError("Cannot visualize groupId while object is fractured");
                visualizedGroupId = -1;
            }
            else
            {
                List<FracSource> fracMeshes = Gen_getMeshesToFracture(gameObject, out _, true, worldScale * 0.0001f);
                visualizedGroupId = Mathf.Min(visualizedGroupId, md_verGroupIds.Length - 1);
                if (fracMeshes == null || visualizedGroupId < 0) goto skipDrawGroups;

                Color[] verCols;
                Vector3[] vers;
                List<float> gId = md_verGroupIds[visualizedGroupId];
                Vector3 drawBoxSize = 0.05f * worldScale * Vector3.one;
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

            //visualize fracture structure
            if (debugMode == DebugMode.showStructure)
            {
                Gizmos.color = Color.red;
                Vector3 sPos;
                bool prevWasKind = true;

                for (int structI = 0; structI < desStructure.Count; structI++)
                {
                    sPos = allParts[desStructure[structI].refPartI].trans.localToWorldMatrix.MultiplyPoint(desStructure[structI].posL);
                    if (partsKinematicStatus.Contains(desStructure[structI].refPartI) == true)
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

                    foreach (int nStructI in desStructure[structI].neighbourStructs)
                    {
                        Gizmos.DrawLine(allParts[desStructure[nStructI].refPartI].trans.localToWorldMatrix.MultiplyPoint(desStructure[nStructI].posL),
                            sPos);
                        //Gizmos.DrawLine(allParts[structI].trans.position, allParts[nStructI].trans.position);
                    }
                }
            }

            //visualize fracture bones
            if (debugMode == DebugMode.showBones)
            {
                Gizmos.color = Color.blue;

                Transform[] tBones = fracRend.bones;
                for (int i = 0; i < tBones.Length - 1; i += 1)
                {
                    Gizmos.DrawLine(tBones[i].position, tBones[i + 1].position);
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

        public enum SelfCollisionMode
        {
            always,
            mainOnly,
            never
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

        private enum SelfCollideWithRule
        {
            ignoreNone = 0,
            ignoreSource = 3,
            ignoreSourceAndNeighbours = 4,
            ignoreNeighbours = 2,
            ignoreAll = 1
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

        private class SkinSourceData
        {
            public Matrix4x4[] bindPoses;
            public Transform[] bones;
            public Transform rendTrans;
            public Transform rootBone;
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
                SyncFracRendData(); //Rend must be synced before saving since fr_[] variabels are "saved" inside the renderer
                saved_fracId = saveAsset.Save(this);

                //save scene (Since the user agreed to saving before fracturing we can just save without asking)
#if UNITY_EDITOR
                if (Application.isPlaying == false) EditorSceneManager.SaveOpenScenes();
#endif
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

            SyncFracRendData();
        }

        /// <summary>
        /// Restores all components on the previous saved objToUse, returns true if anything changed
        /// </summary>
        /// <param name="doSave">If true, objToUse og data will be saved</param>
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

            //load/restore og object
            saved_fracId = -1;
            GameObject objToUse = gameObject;
            bool didChangeAnything = false;

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
                if (ogData.hadRend == true && fracRend != null && fracRend.transform == transform)
                {
                    didChangeAnything = true;

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
                    didChangeAnything = true;

                    DestroyImmediate(fracRend);
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

            //clear saved variabels
            saved_allPartsCol = new Collider[0];
            allParts = new();
            allParents = new();
            partsDefualtData = new();
            partsKinematicStatus = new();
            parentsThatNeedsUpdating = new();
            desStructure = new();
            syncFR_modifiedPartsI = new();

            if (didChangeAnything == true) EditorUtility.SetDirty(objToUse);
            if (doSave == false) return didChangeAnything;

            //save og object
            //save og renderer
            didChangeAnything = true;
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
            if (rend != null) CopyRendProperties(rend, fracRend, new Material[0], true);

            //save og components
            bool newBoolValue;

            foreach (Component comp in objToUse.GetComponentsInChildren<Component>())
            {
                if (comp == fracRend) continue;

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
            /// The total number of kinematic parts that uses this parent
            /// </summary>
            public int parentKinematic;
        }

        [System.Serializable]
        public class FracWeight
        {
            /// <summary>
            /// The index of every struct in the weight
            /// </summary>
            public List<int> structsI;

            /// <summary>
            /// The weight of each structI (Total = 1.0f)
            /// </summary>
            public List<float> weights;
        }

        [System.Serializable]
        public class FracStruct
        {
            /// <summary>
            /// The position of this struct in refPartI localspace
            /// </summary>
            public Vector3 posL;

            /// <summary>
            /// The index of all other structs this struct is connected with
            /// </summary>
            public List<int> neighbourStructs;

            /// <summary>
            /// The index of the part this struct uses (Currently not needed since partIndex always equal structIndex)
            /// </summary>
            public int refPartI;
        }

        [System.Serializable]
        public class FracPart
        {
            /// <summary>
            /// The part collider
            /// </summary>
            public Collider col;

            /// <summary>
            /// All desStructures that uses this part (Currently not needed since partIndex always equal structIndex)
            /// </summary>
            public List<int> desStructIndexes;

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

            public int parentIndex = -69420;

            /// <summary>
            /// The part trans
            /// </summary>
            public Transform trans;

            /// <summary>
            /// The vertex indexes in fracRend.sharedmesh this part uses
            /// </summary>
            public List<int> partMeshVerts = new();
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
            //}
            finally
            {
                //Always clear the progressbar
                EditorUtility.ClearProgressBar();
            }

            bool Execute()
            {
                //make sure we can save the scene later, save scene
                if (askForSavePermission == true && Gen_askIfCanSave(false) == false) return false;

                //verify if we can continue with fracturing here or on prefab
                if (UpdateProgressBar("Verifying objects") == false) return CancelFracturing();
                if (Gen_checkIfContinueWithFrac(out bool didFracOther, isPrefabAsset) == false) return didFracOther;

                //restore orginal data
                Gen_loadAndMaybeSaveOgData(false);

                //Get the meshes to fracture
                if (UpdateProgressBar("Getting meshes") == false) return CancelFracturing();
                float worldScaleDis = worldScale * 0.0001f;

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
                if (UpdateProgressBar("Saving") == false) return CancelFracturing();
                SaveOrLoadAsset(true);

                //log result when done, log when done
                if (fracRend.bones.Length > 500) Debug.LogWarning(transform.name + " has " + fracRend.bones.Length + " bones (skinnedMeshRenderers seems to have a limitation of ~500 bones before it breaks)");
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
        private GroupIdData groupDataDefualt = new();

        /// <summary>
        /// Contains all group ids that exists in the meshes to fracture, only assigned durring fracturing
        /// </summary>
        private List<float>[] md_verGroupIds = new List<float>[0];

        [System.Serializable]
        public class GroupIdData
        {
            public List<int> affectedGroupIndexes = new();
            public PhysicMaterial phyMat;
            public bool isKinematic = false;
            public float mass;
            public byte objLayerDefualt;
            public byte objLayerBroken;
        }

        /// <summary>
        /// assigns groupDataDefualt and groupIntIdToGroupIndex from <defualtSettings> and groupDataOverrides+md_verGroupIds
        /// </summary>
        private void SetupGroupData()
        {
            //set defualt group data
            groupDataDefualt = new()
            {
                affectedGroupIndexes = null,
                phyMat = physicsMat
            };

            //assign groupIntIdToGroupIndex with proper keys and values
            groupIntIdToGroupIndex.Clear();

            for (int i = 0; i < groupDataOverrides.Count; i++)
            {
                foreach (int groupI in groupDataOverrides[i].affectedGroupIndexes)
                {
                    if (groupI < 0 || groupI >= md_verGroupIds.Length) continue;

                    groupIntIdToGroupIndex.Add(FractureHelperFunc.Gd_getIntIdFromId(md_verGroupIds[groupI]), i);
                }
            }
        }

        //fracRend mesh will be set from all fr_[] variabels when synced, they should only be modified by destructionSystem
        [System.NonSerialized] public List<Vector3> fr_verticesL;
        [System.NonSerialized] public List<Vector3> fr_normalsL;
        [System.NonSerialized] public List<Vector2> fr_uvs;
        [System.NonSerialized] public List<BoneWeight> fr_boneWeights;
        [System.NonSerialized] public List<BoneWeight> fr_boneWeightsSkin;
        [System.NonSerialized] public List<Material> fr_materials;
        [System.NonSerialized] public List<List<int>> fr_subTris;
        [System.NonSerialized] public List<Transform> fr_bones;
        [System.NonSerialized] public List<Matrix4x4> fr_bindPoses;

        /// <summary>
        /// The desStructs each vertex in fracRend.sharedmesh uses
        /// </summary>
        [System.NonSerialized] public List<FracWeight> fr_fracWeights;

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
            fracRend.sharedMesh = new();
            fracRend.sharedMaterials = new Material[0];
            fracRend.rootBone = transform;

            //set meshData lists
            fr_verticesL = new();
            fr_normalsL = new();
            fr_uvs = new();
            fr_boneWeights = new();
            fr_boneWeightsSkin = new();
            fr_materials = new();
            fr_subTris = new();
            fr_bones = new();
            fr_bindPoses = new();
            fr_fracWeights = new();

            //add data from source
            if (skinRendSource != null)
            {
                fr_bones.AddRange(skinRendSource.bones);
                fr_bindPoses.AddRange(skinRendSource.bindPoses);
            }

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

        /// <summary>
        /// Call to assign fracRend with data from fr_[] and sync with gpu (RequestSyncFracRendData() should be used instead to prevent SyncFracRendData() from being called many times a frame)
        /// </summary>
        private void SyncFracRendData()
        {
            wantToSyncFracRendData = false;
            float worldScaleDis = worldScale * 0.00001f;

            //set the renderer
            fracRend.SetSharedMaterials(fr_materials);
            fracRend.bones = fr_bones.ToArray();
            fracRend.sharedMesh.SetVertices(fr_verticesL);
            fracRend.sharedMesh.SetNormals(fr_normalsL);
            fracRend.sharedMesh.SetUVs(0, fr_uvs);
            fracRend.sharedMesh.bindposes = fr_bindPoses.ToArray();
            fracRend.sharedMesh.boneWeights = isRealSkinnedM == false ? fr_boneWeights.ToArray() : fr_boneWeightsSkin.ToArray();

            //set rend submeshes
            fracRend.sharedMesh.subMeshCount = fr_subTris.Count;

            for (int subI = 0; subI < fr_subTris.Count; subI++)
            {
                fracRend.sharedMesh.SetTriangles(fr_subTris[subI], subI);
            }

            //update modified parts
            if (syncFR_modifiedPartsI.Count > 0) SetModifiedParts();

            //sync data with gpu


            void SetModifiedParts()
            {
                //Set the desStruct weight for every vertex used by the modified parts
                //get world position of every struct
                Vector3[] structsWPos = new Vector3[allParts.Count];
                for (int partI = 0; partI < allParts.Count; partI++)
                {
                    structsWPos[partI] = GetStructWorldPosition(partI);
                }

                //get the weight for every vertex in every part
                UpdateFracWorldSpaceMesh(true);//Could be made much faster by only updating the vertices for the modified parts
                Vector3[] frWVers = fracWorldSpaceMesh.vertices;

                Debug_toggleTimer();

                HashSet<int> usedVers = new();
                
                foreach (int partI in syncFR_modifiedPartsI)
                {
                    usedVers.Clear();

                    foreach (int vI in allParts[partI].partMeshVerts)
                    {
                        if (usedVers.Contains(vI) == true) continue;

                        SetFracWeightFromPartAndV(partI, vI);

                        //All vers in this part that share the ~same pos must always have the same weight 
                        foreach (int vII in allParts[partI].partMeshVerts)
                        {
                            if (vII == vI) continue;
                        
                            if ((frWVers[vI] - frWVers[vII]).sqrMagnitude < worldScaleDis)
                            {
                                usedVers.Add(vII);
                                fr_fracWeights[vII] = fr_fracWeights[vI];
                            }
                        }
                    }
                }

                //Combine weights for vertics that share the same pos


                Debug_toggleTimer();

                syncFR_modifiedPartsI.Clear();

                void SetFracWeightFromPartAndV(int partI, int vI)
                {
                    //reset weights
                    if (fr_fracWeights[vI] == null) fr_fracWeights[vI] = new(); //it does not make since why this is null, lazy fix
                    fr_fracWeights[vI].structsI = new();
                    fr_fracWeights[vI].weights = new();
                    float totalDis = 0.0f;

                    //add weights from each nearby struct (Get weights from distance to ver from struct)
                    AddWeightFromStruct(partI);

                    foreach (int nearPI in desStructure[partI].neighbourStructs)
                    {
                        AddWeightFromStruct(nearPI);
                    }

                    //Nomilize weights so total weight = 1.0f
                    float totalWeight = 0.0f;//currently only used for debug
                    for (int wI = 0; wI < fr_fracWeights[vI].weights.Count; wI++)
                    {
                        fr_fracWeights[vI].weights[wI] /= totalDis;
                        totalWeight += fr_fracWeights[vI].weights[wI];
                    }

                    if (Mathf.Approximately(totalWeight, 1.0f) == false) Debug.Log(totalWeight);

                    void AddWeightFromStruct(int structI)
                    {
                        fr_fracWeights[vI].structsI.Add(structI);
                        fr_fracWeights[vI].weights.Add((structsWPos[structI] - frWVers[vI]).magnitude);
                        totalDis += fr_fracWeights[vI].weights[^1];
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
            fObj.col = Gen_createPartCollider(pTrans, fObj.meshW, GetGroupIdDataFromIntId(FractureHelperFunc.Gd_getIntIdFromId(fMesh.groupId)).phyMat);

            //get inside vers before we modify fObj.mesh
            HashSet<int> insideVers = FractureHelperFunc.GetAllVersInSubMesh(fObj.meshW, 1);

            //get fObject materials
            FractureHelperFunc.GetMostSimilarTris(fObj.meshW, fMesh.sourceM.meshW, out int[] nVersBestSVer, out int[] nTrisBestSTri, worldScale);
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
        private GroupIdData GetGroupIdDataFromIntId(int intId)
        {
            if (groupIntIdToGroupIndex.TryGetValue(intId, out int groupI) == true) return groupDataOverrides[groupI];
            return groupDataDefualt;
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

            //get what groupData the part should use
            GroupIdData partGroupD = null;

            //create fracPart and assign variabels
            FracPart newPart = new()
            {
                groupId = fObj.groupId,
                groupLinks = fObj.groupLinks,
                parentIndex = -69420,
                col = fObj.col,
                trans = fObj.col.transform,
                partMeshVerts = new(),
                desStructIndexes = new()
                //neighbourParts = new()
            };

            int newPartI = allParts.Count;
            newPart.trans.name = newPart.trans.name.Replace("unusedPart", newPartI.ToString());
            allParts.Add(newPart);

            //create fracStruct
            int partMainNeighbour = -1;

            SetPartNeighboursAndConnections();

            //add part mesh
            Matrix4x4 rendWtoL = fracRend.transform.worldToLocalMatrix;
            int newBoneI = fr_bones.Count;
            int partVerCount = fObj.meshW.vertexCount;
            int newVerOffset = fr_verticesL.Count;
            int newVerEndI = newVerOffset + partVerCount;

            for (int nvI = newVerOffset; nvI < newVerEndI; nvI++)
            {
                newPart.partMeshVerts.Add(nvI);
            }

            fr_bones.Add(fObj.col.transform);
            fr_bindPoses.Add(fObj.col.transform.worldToLocalMatrix * fracRend.transform.localToWorldMatrix);
            fr_verticesL.AddRange(FractureHelperFunc.ConvertPositionsWithMatrix(fObj.meshW.vertices, rendWtoL));
            fr_normalsL.AddRange(FractureHelperFunc.ConvertDirectionsWithMatrix(fObj.meshW.normals, rendWtoL));
            fr_uvs.AddRange(fObj.meshW.uv);
            fr_fracWeights.AddRange(new FracWeight[partVerCount]); //fr_fracWeights will be assigned later in SyncFracRendData
                                                                   //but we still want to add here to prevent potential out of bounds error

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
            BoneWeight newBoneWe = new() { boneIndex0 = newBoneI, weight0 = 1.0f };

            for (int i = 0; i < partVerCount; i++)
            {
                fr_boneWeights.Add(newBoneWe);
            }

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

            RequestSyncFracRendData(newPartI);
            return true;

            void SetPartNeighboursAndConnections()
            {
                //do box overlaps to get all nearby colliders
                PhysicsScene phyScene = fObj.col.gameObject.scene.GetPhysicsScene();
                Collider[] lapCols = new Collider[20];

                int lapCount = phyScene.OverlapBox(fObj.col.bounds.center,
                            fObj.col.bounds.extents * 1.05f,
                            lapCols,
                            Quaternion.identity,
                            Physics.AllLayers,
                            QueryTriggerInteraction.Ignore);

                //Get nearby parts and kinematic objects from nearby colliders
                bool partIsKin = false;
                List<int> nearPartIndexes = new();

                for (int i = 0; i < lapCount; i++)
                {
                    if (lapCols[i] == fObj.col) continue;

                    if (generationQuality == GenerationQuality.high)
                    {
                        if (Physics.ComputePenetration(//Accurate overlaps (It will still depend on collider type,
                                                       //is that fine or do I need to use the actuall meshes to get overlaps for parts?)
                            fObj.col,
                            Vector3.MoveTowards(fObj.col.transform.position,
                            lapCols[i].transform.position, worldScale * 0.001f),
                            fObj.col.transform.rotation,
                            lapCols[i],
                            lapCols[i].transform.position,
                            lapCols[i].transform.rotation,
                            out _, out _) == false) continue;
                    }

                    int partI = TryGetPartIndexFromTrans(lapCols[i].transform);

                    if (partI < 0)
                    {
                        //near col is not a part
                        if (phyMainOptions.mainPhysicsType != OptMainPhysicsType.overlappingIsKinematic || partIsKin == true) continue;

                        partIsKin = lapCols[i].attachedRigidbody == null || lapCols[i].attachedRigidbody.isKinematic == true;

                        continue;
                    }

                    //near col is part
                    nearPartIndexes.Add(partI);
                }

                //get the groupData the part should use
                if (newPart.groupId == null && nearPartIndexes.Count > 0) newPart.groupId = allParts[nearPartIndexes[0]].groupId;
                newPart.groupIdInt = FractureHelperFunc.Gd_getIntIdFromId(newPart.groupId);
                partGroupD = GetGroupIdDataFromIntId(newPart.groupIdInt);

                //set part parent
                if (newPartParentI <= 0 && nearPartIndexes.Count > 0) newPartParentI = allParts[nearPartIndexes[0]].parentIndex;
                if (newPartParentI < 0) newPartParentI = CreateNewParent(null);
                SetPartParent(newPartI, newPartParentI);

                //add part to kinematic list if needed
                if (partIsKin == true || partGroupD.isKinematic == true) SetPartKinematicStatus(newPartI, true);

                //get what nearPartIndexes is actually valid neighbours and create structure for the new part
                if (newPartI != desStructure.Count)
                    Debug.LogError(transform.name + " destructionStructure or parts is invalid! (part = " + newPartI + " struct = " + desStructure.Count + ")");

                desStructure.Add(new()
                {
                    posL = allParts[newPartI].trans.worldToLocalMatrix.MultiplyPoint(GetPartWorldPosition(newPartI)),
                    refPartI = newPartI,
                    neighbourStructs = new()
                });

                foreach (int nearPartI in nearPartIndexes)
                {
                    //ignore if this neighbour part is invalid
                    if (newPart.parentIndex != allParts[nearPartI].parentIndex
                        || FractureHelperFunc.Gd_isPartLinkedWithPart(newPart, allParts[nearPartI]) == false) continue;

                    //add neighbours to newPart struct and neighbour part struct
                    desStructure[nearPartI].neighbourStructs.Add(newPartI);
                    desStructure[newPartI].neighbourStructs.Add(newPartI);

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
            if (partsKinematicStatus.Contains(partI) == newKinematicStatus) return;

            //add/remove from kinematic parts list
            if (newKinematicStatus == true) partsKinematicStatus.Add(partI);
            else partsKinematicStatus.Remove(partI);

            //update the parent the part uses
            int parentI = allParts[partI].parentIndex;

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
        private void CreateAndAddFObjectsFromFMeshes(List<FracMesh> fMeshesW, int newPartsParentI = 0)
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
        /// Sets the given parts parent to newParentI
        /// </summary>
        private void SetPartParent(int partI, int newParentI)
        {
            if (allParts[partI].parentIndex == newParentI) return;

            //get part groupData
            GroupIdData partGData = GetGroupIdDataFromIntId(allParts[partI].groupIdInt);

            //remove part from previous parent
            if (allParts[partI].parentIndex >= 0)
            {
                allParents[allParts[partI].parentIndex].partIndexes.Remove(partI);
                allParents[allParts[partI].parentIndex].parentMass -= partGData.mass * phyMainOptions.massMultiplier;
                if (partsKinematicStatus.Contains(partI) == true) allParents[allParts[partI].parentIndex].parentKinematic--;

                MarkParentAsModified(allParts[partI].parentIndex);
            }
            else FromNoParentToParent();

            allParts[partI].parentIndex = newParentI;

            //if want to remove parent
            if (newParentI < 0)
            {
                FromParentToNoParent();
                return;
            }

            //if want to set parent
            allParts[partI].trans.SetParent(newParentI > 0 || partsDefualtData.Count == 0 ? allParents[newParentI].parentTrans : partsDefualtData[partI].defParent);
            allParents[newParentI].partIndexes.Add(partI);
            allParents[newParentI].parentMass += partGData.mass * phyMainOptions.massMultiplier;
            if (partsKinematicStatus.Contains(partI) == true) allParents[allParts[partI].parentIndex].parentKinematic++;
            MarkParentAsModified(newParentI);

            void FromParentToNoParent()
            {
                //set transform
                allParts[partI].trans.SetParent(transform);
                allParts[partI].trans.gameObject.layer = partGData.objLayerBroken;

                //add and set rigidbody
                Rigidbody newRb = allParts[partI].trans.gameObject.AddComponent<Rigidbody>();

                newRb.mass = partGData.mass;
                newRb.interpolation = phyPartsOptions.interpolate;//Do we really need these properties to be configurable?
                newRb.drag = phyPartsOptions.drag;
                newRb.angularDrag = phyPartsOptions.angularDrag;
                newRb.freezeRotation = phyPartsOptions.canRotate;
                newRb.useGravity = phyPartsOptions.useGravity;
            }

            void FromNoParentToParent()
            {
                //note that this also runs once for newly created objects
                //set transform
                allParts[partI].trans.gameObject.layer = partGData.objLayerDefualt;

                //remove rigidbody
                if (allParts[partI].parentIndex == -1) Destroy(allParts[partI].col.attachedRigidbody);
            }
        }

        /// <summary>
        /// Creates a new parent and returns its index (transToUse will be used as parent if it aint null)
        /// </summary>
        public int CreateNewParent(Transform transToUse = null)
        {
            //if empty&&valid parent exists, reuse it since we are not allowed to destroy unused parents.
            int newParentI = -1;

            if (transToUse == null)
            {
                for (int i = 1; i < allParents.Count; i++)
                {
                    if (allParents[i].partIndexes.Count == 0 && allParents[i].parentTrans != null && allParents[i].parentTrans.parent == transform &&
                        (allParents[i].parentRb != null || phyMainOptions.mainPhysicsType == OptMainPhysicsType.alwaysKinematic))
                    {
                        newParentI = i;
                        allParents[newParentI].parentTrans.gameObject.SetActive(true);
                    }
                }
            }

            //if empty&&valid parent did not exist, create new parent object
            if (newParentI < 0)
            {
                newParentI = allParents.Count;
                Rigidbody parentRb = null;

                if (transToUse == null)
                {
                    transToUse = new GameObject("Parent(" + newParentI + ")_" + transform.name).transform;
                    transToUse.gameObject.layer = gameObject.layer;
                    transToUse.gameObject.tag = gameObject.tag;
                    transToUse.position = transform.position;//just to make orgin somewhat relevant
                    transToUse.SetParent(transform);
                    if (phyMainOptions.mainPhysicsType != OptMainPhysicsType.alwaysKinematic) parentRb = transToUse.gameObject.AddComponent<Rigidbody>();
                }
                else transToUse.GetComponent<Rigidbody>();

                allParents.Add(new() {
                    parentTrans = transToUse,
                    parentRb = parentRb,
                    partIndexes = new(),
                    parentMass = 0.0f });
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
            //update parent rigidbody
            if (allParents[parentI].parentRb != null)
            {
                allParents[parentI].parentRb.mass = allParents[parentI].parentMass;
                allParents[parentI].parentRb.isKinematic = allParents[parentI].parentKinematic > 0;
            }
        }

        /// <summary>
        /// Returns the center world position of the given part (center of the part collider)
        /// </summary>
        private Vector3 GetPartWorldPosition(int partI)
        {
            return allParts[partI].trans.position;
        }

        private Vector3 GetStructWorldPosition(int structI)
        {
            return allParts[structI].trans.localToWorldMatrix.MultiplyPoint(desStructure[structI].posL);
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
            if (GetFracturePrefabType() == 1)
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
                FractureHelperFunc.ConvertPositionsWithMatrix(partWVers, partTrans.worldToLocalMatrix));

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

# if UNITY_EDITOR
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

                byte loopMaxAttempts = maxFractureAttempts;
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
                            newMeshesTemp[^1] = FractureHelperFunc.MakeMeshConvex(newMeshesTemp[^1], false, worldScale);
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

                    if (Vector3.Distance(skinnedR.transform.position, transform.position) > worldScale * 0.01f)
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

            //convert all meshes to world space
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
                    meshesToFrac.Add(new() {
                        meshW = splittedMeshes[ii].meshW,
                        sRend = meshesToFrac[i].sRend,
                        mGroupId = splittedMeshes[ii].mGroupId,
                        mMats = splittedMeshes[ii].mMats,
                        trisSubMeshI = FractureHelperFunc.GetTrisSubMeshI(splittedMeshes[ii].meshW) });
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
                    || saveState.preS_toFracData.worldScale != worldScale
                    || saveState.preS_toFracData.totalVerCount != totalVerCount
                    || FractureHelperFunc.AreBoundsArrayEqual(saveState.preS_toFracData.toFracRendBounds, fRendBounds) == false)
                {
                    //if has saveState but mayor stuff has changed, return false and clear savedPrefracture
                    saveState.ClearSavedPrefracture();

                    saveState.SavePrefracture(new() {
                        dynamicFractureCount = dynamicFractureCount,
                        worldScale = worldScale,
                        seed = seed,
                        fractureCount = fractureCount,
                        generationQuality = generationQuality,
                        md_verGroupIds = mdVersList,
                        randomness = randomness,
                        remeshing = remeshing,
                        toFracRendBounds = fRendBounds,
                        totalVerCount = totalVerCount });

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
        [SerializeField] private bool isRealSkinnedM;

        /// <summary>
        /// This is true if the destructionSystem is valid and running (Runtime only)
        /// </summary>
        [System.NonSerialized] public bool fractureIsValid = false;

        private void Awake()
        {
            //verify fracture
            fractureIsValid = false;
            if (fracRend == null) return;
            if (globalHandler == null && VerifyGlobalHandler() == false)
            {
                Debug.LogError(transform.name + " globalHandler is null, destruction will not work (Make sure a active FractureGlobalHandler script exists in all scenes)");
                return;
            }

            //load from save aset
            SaveOrLoadAsset(false);

            //setup collider instanceid references
            globalHandler.AddReferencesFromFracture(this);

            //load compute shaders
            computeDestructionSolver = Resources.Load<ComputeShader>("ComputeDestructionSolver");
            if (computeDestructionSolver == null)
            {
                Debug.LogError("Expected destructionSolver compute shader to exist at path 'UltimateFracture/Resources/ComputeDestructionSolver.compute', have you deleted it?");
                return;
            }

            cpKernelId_SolveDestructionStep = computeDestructionSolver.FindKernel("SolveDestructionStep");

            //sync with gpu
            SyncFracRendData();
            fractureIsValid = true;
        }

        private void Update()
        {
            //return if fracture is invalid
            if (fractureIsValid == false) return;

            GlobalUpdate();
        }

        /// <summary>
        /// Runs once every frame at runtime and in editor
        /// </summary>
        private void GlobalUpdate()
        {
            //update modified parents
            if (parentsThatNeedsUpdating.Count > 0)
            {
                foreach (int parentI in parentsThatNeedsUpdating) UpdateParentData(parentI);
                parentsThatNeedsUpdating.Clear();
            }

            //sync with gpu, wantToSyncFracRendData is set to false inside SyncFracRendData()
            if (wantToSyncFracRendData == true)
            {
                SyncFracRendData();
            }
        }

        private int cpKernelId_SolveDestructionStep;
        [SerializeField] private ComputeShader computeDestructionSolver;

        #endregion MainUpdateFunctions

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
        /// The structure used to compute destruction
        /// </summary>
        [System.NonSerialized] public List<FracStruct> desStructure = new();

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
        [System.NonSerialized] public HashSet<int> partsKinematicStatus = new();

        /// <summary>
        /// The renderer used to render the fractured mesh (always skinned)
        /// </summary>
        public SkinnedMeshRenderer fracRend = null;

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
        public int TryGetPartIndexFromTrans(Transform trans)
        {
            for (int i = 0; i < allParts.Count; i++)
            {
                if (allParts[i].trans == trans) return i;
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
        /// The mesh used by fracRend in worldspace, call UpdateFracWorldSpaceMesh() to update
        /// </summary>
        private Mesh fracWorldSpaceMesh = null;

        /// <summary>
        /// Assigns fracWorldSpaceMesh with the mesh used by the fracRend but in worldSpace
        /// </summary>
        private void UpdateFracWorldSpaceMesh(bool verticsOnly = false)
        {
            if (fracWorldSpaceMesh == null) fracWorldSpaceMesh = new();

            fracRend.BakeMesh(fracWorldSpaceMesh, true);
            if (verticsOnly == false) FractureHelperFunc.ConvertMeshWithMatrix(ref fracWorldSpaceMesh, fracRend.localToWorldMatrix);
            else fracWorldSpaceMesh.SetVertices(FractureHelperFunc.ConvertPositionsWithMatrix(fracWorldSpaceMesh.vertices, fracRend.localToWorldMatrix));
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
    }
}