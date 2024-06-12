using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JetBrains.Annotations;
using System.Linq;
using Unity.Collections;





#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Zombie1111_uDestruction
{
    public class FractureSaveAsset : ScriptableObject
    {
#if UNITY_EDITOR
        /// <summary>
        /// Creates a new saveAsset in the folder that is currently selected in the editor project tab (returns the newly saveAsset, null if no valid folder selected)
        /// Editor only
        /// </summary>
        [MenuItem("Tools/Fracture/CreateSaveAsset")]
        public static FractureSaveAsset CreateSaveAsset()
        {
            //get current selected folder (To make it open the panel there)
            UnityEngine.Object[] selection = Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.Assets);

            string selectedFolderPath;

            if (selection.Length == 1)
            {
                selectedFolderPath = AssetDatabase.GetAssetPath(selection[0]);
                if (AssetDatabase.IsValidFolder(selectedFolderPath) == false) selectedFolderPath = "Assets/";
            }
            else selectedFolderPath = "Assets/";

            //ask user where to save and create new asset
            string path = EditorUtility.SaveFilePanel("Create save asset", selectedFolderPath, "fracSave", "asset");
            if (string.IsNullOrEmpty(path)) return null;

            path = FileUtil.GetProjectRelativePath(path);

            ScriptableObject fracSaveAsset = ScriptableObject.CreateInstance<FractureSaveAsset>();
            AssetDatabase.CreateAsset((FractureSaveAsset)fracSaveAsset, path);
            return (FractureSaveAsset)fracSaveAsset;
        }
#endif

        public FractureSaveData fracSavedData = new();

        [System.Serializable]
        public class FractureSaveData
        {
            public int id = -1;

            public FractureThis.FracPart[] saved_allParts = new FractureThis.FracPart[0];
            public List<int> saved_kinematicPartsStatus = new();
            public int saved_rendVertexCount = -1;
            public List<Vector3> saved_structs_posL = new();
            public List<int> saved_structs_parentI = new();
            public List<int> saved_fr_verToPartI = new();
            public FractureThis.FracStruct[] saved_fracStructs = new FractureThis.FracStruct[0];
            public List<short> saved_parentPartCount = new();
            public int[] saved_partIToDesMatI = new int[0];

            public int[] saved_partsLocalParentPath = new int[0];
            public int[] saved_localPathToRbIndex_keys = new int[0];
            public int[] saved_localPathToRbIndex_values = new int[0];

            //Only used if prefab because unity is useless and cant save meshes inside prefabs
            public SavableMesh saved_rendMesh = new();
            public VecArray[] sMesh_colsVers = null;
            public List<BoneWeight> saved_nonSkinnedBoneWe = new();
        }

        [System.Serializable]
        public class SavableMesh
        {
            public BoneWeight[] sMesh_boneWeights = null;
            public Matrix4x4[] sMesh_bindposes = null;
            public Vector3[] sMesh_vertics = null;
            public FractureThis.IntList[] sMesh_triangels = null;
            public Vector2[] sMesh_uvs = null;

            /// <summary>
            /// Returns the saved data as a mesh, if ignoreSkin is true boneWeights and bindposes will be ignored
            /// </summary>
            /// <returns></returns>
            public Mesh ToMesh(bool ignoreSkin = false)
            {
                //load basics
                Mesh newM = new()
                {
                    vertices = sMesh_vertics,
                    uv = sMesh_uvs
                };

                if (ignoreSkin == false)
                {
                    newM.bindposes = sMesh_bindposes;
                    newM.boneWeights = sMesh_boneWeights;
                }

                //load submeshes and tris
                newM.subMeshCount = sMesh_triangels.Length;

                for (int sI = 0; sI < sMesh_triangels.Length; sI++)
                {
                    newM.SetTriangles(sMesh_triangels[sI].intList, sI);
                }

                //recalculate everything (We dont save normals since fracMeshes normals are always created from mesh.RecalculateNormals() anyway)
                newM.RecalculateBounds();
                newM.RecalculateNormals();
                newM.RecalculateTangents();

                return newM;
            }

            /// <summary>
            /// Saves the given mesh, if ignoreSkin is true boneWeights and bindposes will be ignored
            /// </summary>
            public void FromMesh(Mesh mesh, bool ignoreSkin = false)
            {
                //save basic
                if (ignoreSkin == false)
                {
                    sMesh_boneWeights = mesh.boneWeights;
                    sMesh_bindposes = mesh.bindposes;
                }

                sMesh_vertics = mesh.vertices;
                sMesh_uvs = mesh.uv;

                //save submeshes and tris
                sMesh_triangels = new FractureThis.IntList[mesh.subMeshCount];

                for (int sI = 0; sI < sMesh_triangels.Length; sI++)
                {
                    sMesh_triangels[sI] = new()
                    {
                        intList = mesh.GetTriangles(sI).ToList()
                    };
                }
            }

            /// <summary>
            /// Sets all saved mesh data to null
            /// </summary>
            public void Clear()
            {
                sMesh_boneWeights = null;
                sMesh_bindposes = null;
                sMesh_vertics = null;
                sMesh_triangels = null;
                sMesh_uvs = null;
            }

            /// <summary>
            /// Returns true if a valid mesh is stored
            /// </summary>
            public bool IsValidMeshSaved()
            {
                return sMesh_vertics != null && sMesh_vertics.Length > 2;
            }
        }

        [System.Serializable]
        public class VecArray
        {
            public Vector3[] vectors = null;
        }

        /// <summary>
        /// Saves data from the given script and returns the id it saved to
        /// </summary>
        /// <param name="saveFrom"></param>
        /// <returns></returns>
        public int Save(FractureThis saveFrom)
        {
            if (saveFrom.fracRend == null || saveFrom.fracFilter == null || saveFrom.fracFilter.sharedMesh == null) return -1; //cant save if no fracture

            //save the data
            fracSavedData.id += 1;
            fracSavedData.saved_kinematicPartsStatus = new();
            foreach (int partI in saveFrom.jCDW_job.kinematicPartIndexes)
            {
                fracSavedData.saved_kinematicPartsStatus.Add(partI);
            }

            fracSavedData.saved_allParts = saveFrom.allParts.ToArray();
            fracSavedData.saved_fracStructs = new FractureThis.FracStruct[saveFrom.jCDW_job.fStructs.Length];
            for (int i = 0; i < fracSavedData.saved_fracStructs.Length; i++)
            {
                fracSavedData.saved_fracStructs[i] = saveFrom.jCDW_job.fStructs[i];
            }

            fracSavedData.saved_partIToDesMatI = new int[saveFrom.jCDW_job.partIToDesMatI.Length];
            for (int i = 0; i < fracSavedData.saved_partIToDesMatI.Length; i++)
            {
                fracSavedData.saved_partIToDesMatI[i] = saveFrom.jCDW_job.partIToDesMatI[i];
            }

            fracSavedData.saved_rendVertexCount = saveFrom.fracFilter.sharedMesh.vertexCount;
            fracSavedData.saved_structs_posL = new();
            foreach (Vector3 pos in saveFrom.jCDW_job.structPosL)
            {
                fracSavedData.saved_structs_posL.Add(pos);
            }

            fracSavedData.saved_structs_parentI = new();
            foreach (int parentI in saveFrom.jCDW_job.partsParentI)
            {
                fracSavedData.saved_structs_parentI.Add(parentI);
            }

            fracSavedData.saved_parentPartCount = new();
            foreach (short partCount in saveFrom.jCDW_job.parentPartCount)
            {
                fracSavedData.saved_parentPartCount.Add(partCount);
            }

            fracSavedData.saved_fr_verToPartI = saveFrom.fr_verToPartI.ToList();

            fracSavedData.saved_partsLocalParentPath = saveFrom.partsLocalParentPath.ToArray();
            FracHelpFunc.DictoraryToArrays<int, int>(saveFrom.localPathToRbIndex, out fracSavedData.saved_localPathToRbIndex_keys, out fracSavedData.saved_localPathToRbIndex_values);

            //save mesh stuff if prefab
            if (saveFrom.fracPrefabType > 0)
            {
                //save fracRend mesh
                fracSavedData.saved_rendMesh.FromMesh(saveFrom.fracFilter.sharedMesh, true);

                //if mesh colliders save the them too
                if (saveFrom.saved_allPartsCol[0] is MeshCollider)
                {
                    fracSavedData.sMesh_colsVers = new VecArray[saveFrom.allParts.Count];
                    for (int i = 0; i < saveFrom.allParts.Count; i += 1)
                    {
                        fracSavedData.sMesh_colsVers[i] = new()
                        {
                            vectors = ((MeshCollider)saveFrom.saved_allPartsCol[i]).sharedMesh.vertices
                        };
                    }
                }
                else fracSavedData.sMesh_colsVers = null;
            }
            else
            {
                //if not prefab clear the arrays
                fracSavedData.saved_rendMesh.Clear();
                fracSavedData.sMesh_colsVers = null;
            }

            //save skin stuff
            fracSavedData.saved_nonSkinnedBoneWe = saveFrom.fr_boneWeights.ToList();
            fracSavedData.saved_rendMesh.sMesh_boneWeights = saveFrom.fr_boneWeightsSkin.ToArray();
            fracSavedData.saved_rendMesh.sMesh_bindposes = saveFrom.fr_bindPoses.ToArray();

            ////because ScriptableObject cannot save actual components we save it on FractureThis
            //saveFrom.saved_allPartsCol = new Collider[saveFrom.allParts.Count];
            //for (int i = 0; i < saveFrom.allParts.Count; i += 1)
            //{
            //    saveFrom.saved_allPartsCol[i] = saveFrom.allParts[i].col;
            //}

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            EditorUtility.SetDirty(saveFrom);
#endif

            return fracSavedData.id;
        }

        /// <summary>
        /// Returns true if was able to load savedData to the given fracture
        /// </summary>
        /// <param name="loadTo"></param>
        public bool Load(FractureThis loadTo)
        {
            //get if can load
            if (loadTo.saved_fracId != fracSavedData.id
                || fracSavedData.saved_allParts.Length != loadTo.saved_allPartsCol.Count
                || loadTo.fracRend == null
                || loadTo.fracRend.transform != loadTo.transform
                || (loadTo.fracFilter.sharedMesh != null && loadTo.fracFilter.sharedMesh.vertexCount != fracSavedData.saved_rendVertexCount))
            {
                return false;
            }

            //Apply saved data to the provided FractureThis instance
            //Load compute destruction job
            loadTo.jCDW_job = new()
            {
                structPosL = fracSavedData.saved_structs_posL.ToNativeList(Allocator.Persistent),
                partsParentI = fracSavedData.saved_structs_parentI.ToNativeList(Allocator.Persistent),
                kinematicPartIndexes = new(fracSavedData.saved_kinematicPartsStatus.Count, Allocator.Persistent),
                fStructs = new(fracSavedData.saved_fracStructs.Length, Allocator.Persistent),
                partIToDesMatI = new(fracSavedData.saved_partIToDesMatI.Length, Allocator.Persistent),
                parentPartCount = fracSavedData.saved_parentPartCount.ToNativeList(Allocator.Persistent) 
            };

            //Load kinematic parts, and maybe recalculate kinematic parts depending on global setting
            foreach (int partI in fracSavedData.saved_kinematicPartsStatus)
            {
                //We dont need to add with SetPartKinematicStatus() since it has also been saved with the parent
                loadTo.jCDW_job.kinematicPartIndexes.Add(partI);
            }

            if (loadTo.phyMainOptions.mainPhysicsType == FractureThis.OptMainPhysicsType.overlappingIsKinematic
                && (FracGlobalSettings.recalculateKinematicPartsOnLoad == 2 || (FracGlobalSettings.recalculateKinematicPartsOnLoad == 1 && loadTo.fracPrefabType > 0)))
            {
                int partCount = loadTo.saved_allPartsCol.Count;
                for (int partI = 0; partI < partCount; partI++)
                {
                    if (loadTo.GetNearbyFracColliders(loadTo.saved_allPartsCol[partI], FractureThis.GenerationQuality.normal, out _, true) == false) continue;

                    loadTo.SetPartKinematicStatus(partI, true);
                }
            }

            //Load part structure
            foreach (FractureThis.FracStruct fStruct in fracSavedData.saved_fracStructs)
            {
                loadTo.jCDW_job.fStructs.Add(fStruct);
            }

            foreach (int desMatI in fracSavedData.saved_partIToDesMatI)
            {
                loadTo.jCDW_job.partIToDesMatI.Add(desMatI);
            }

            loadTo.allParts = fracSavedData.saved_allParts.ToList();

            //load fracRend mesh if was prefab
            if (fracSavedData.saved_rendMesh.IsValidMeshSaved() == true)
            {
                loadTo.fracFilter.sharedMesh = fracSavedData.saved_rendMesh.ToMesh(true);
            }

            //load meshColliders mesh if was prefab
            if (fracSavedData.sMesh_colsVers != null && fracSavedData.sMesh_colsVers.Length > 0)
            {
                for (int i = 0; i < loadTo.allParts.Count; i += 1)
                {
                    ((MeshCollider)loadTo.saved_allPartsCol[i]).sharedMesh = new()
                    {
                        vertices = fracSavedData.sMesh_colsVers[i].vectors
                    };
                }
            }

            //load local paths
            loadTo.partsLocalParentPath = fracSavedData.saved_partsLocalParentPath.ToList();
            loadTo.localPathToRbIndex = FracHelpFunc.CreateDictionaryFromArrays<int, int>(fracSavedData.saved_localPathToRbIndex_keys, fracSavedData.saved_localPathToRbIndex_values);

            //restore fr_[] variabels from fracRend mesh
            Mesh fracRendMesh = loadTo.fracFilter.sharedMesh;
            loadTo.fr_materials = loadTo.fracRend.sharedMaterials.ToList();
            loadTo.fr_verticesL = fracRendMesh.vertices.ToList();
            loadTo.fr_normalsL = fracRendMesh.normals.ToList();
            loadTo.fr_uvs = fracRendMesh.uv.ToList();
#if !FRAC_NO_VERTEXCOLORSUPPORT
            loadTo.fr_colors = fracRendMesh.colors.ToList();
#endif
            loadTo.fr_boneWeights = fracSavedData.saved_nonSkinnedBoneWe.ToList();
            loadTo.fr_boneWeightsSkin = fracSavedData.saved_rendMesh.sMesh_boneWeights.ToList();
            loadTo.fr_bindPoses = fracSavedData.saved_rendMesh.sMesh_bindposes.ToList();
            loadTo.fr_subTris = new();
            loadTo.fr_boneWeightsCurrent = loadTo.isRealSkinnedM == true ? loadTo.fr_boneWeightsSkin.ToList() : loadTo.fr_boneWeights.ToList();
            loadTo.fr_verToPartI = fracSavedData.saved_fr_verToPartI.ToList();

            for (int sI = 0; sI < fracRendMesh.subMeshCount; sI++)
            {
                loadTo.fr_subTris.Add(new());

                foreach (int vI in fracRendMesh.GetTriangles(sI))
                {
                    loadTo.fr_subTris[sI].Add(vI);
                }
            }

            return true;
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(FractureSaveAsset))]
    public class FractureSaveAssetEditor : Editor
    {
        private bool showFloatVariable = false;

        public override void OnInspectorGUI()
        {
            FractureSaveAsset myScript = (FractureSaveAsset)target;

            // Show the button to toggle the float variable
            if (GUILayout.Button("Show Fracture Data (MAY FREEZE UNITY!)"))
            {
                showFloatVariable = !showFloatVariable;
            }

            if (showFloatVariable)
            {
                // Show the variables
                serializedObject.Update(); // Ensure serialized object is up to date

                SerializedProperty fracSavedData = serializedObject.FindProperty("fracSavedData");
                EditorGUILayout.PropertyField(fracSavedData, true);
            }

            // Apply modifications to the asset
            if (GUI.changed)
            {
                EditorUtility.SetDirty(target);
            }
        }
    }
#endif
}
