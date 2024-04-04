using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JetBrains.Annotations;
using Unity.VisualScripting.FullSerializer;
using System.Linq;



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
            public List<FractureThis.FracWeight> saved_desWeights = new();
            public List<int> saved_fracWeightsI = new();
            public List<int> saved_fr_verToPartI = new();

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
            fracSavedData.saved_kinematicPartsStatus = saveFrom.partsKinematicStatus.ToList();
            fracSavedData.saved_allParts = saveFrom.allParts.ToArray();
            fracSavedData.saved_rendVertexCount = saveFrom.fracFilter.sharedMesh.vertexCount;
            fracSavedData.saved_structs_posL = saveFrom.structs_posL.ToList();
            fracSavedData.saved_structs_parentI = saveFrom.structs_parentI.ToList();
            fracSavedData.saved_desWeights = saveFrom.desWeights.ToList();
            fracSavedData.saved_fracWeightsI = saveFrom.fr_fracWeightsI.ToList();
            fracSavedData.saved_fr_verToPartI = saveFrom.fr_verToPartI.ToList();

            //save mesh stuff if prefab
            if (saveFrom.GetFracturePrefabType() > 0)
            {
                //save fracRend mesh
                fracSavedData.saved_rendMesh.FromMesh(saveFrom.fracFilter.sharedMesh, true);

                //if mesh colliders save the them too
                if (saveFrom.allParts[0].col is MeshCollider)
                {
                    fracSavedData.sMesh_colsVers = new VecArray[saveFrom.allParts.Count];
                    for (int i = 0; i < saveFrom.allParts.Count; i += 1)
                    {
                        fracSavedData.sMesh_colsVers[i] = new()
                        {
                            vectors = ((MeshCollider)saveFrom.allParts[i].col).sharedMesh.vertices
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

            //because ScriptableObject cannot save actual components we save it on FractureThis
            saveFrom.saved_allPartsCol = new Collider[saveFrom.allParts.Count];
            for (int i = 0; i < saveFrom.allParts.Count; i += 1)
            {
                saveFrom.saved_allPartsCol[i] = saveFrom.allParts[i].col;
            }

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
                || fracSavedData.saved_allParts.Length != loadTo.saved_allPartsCol.Length
                || loadTo.fracRend == null
                || loadTo.fracRend.transform != loadTo.transform
                || (loadTo.fracFilter.sharedMesh != null && loadTo.fracFilter.sharedMesh.vertexCount != fracSavedData.saved_rendVertexCount))
            {
                return false;
            }

            //Apply saved data to the provided FractureThis instance
            loadTo.partsKinematicStatus = fracSavedData.saved_kinematicPartsStatus.ToHashSet();
            loadTo.structs_posL = fracSavedData.saved_structs_posL.ToList();
            loadTo.structs_parentI = fracSavedData.saved_structs_parentI.ToList();
            loadTo.allParts = fracSavedData.saved_allParts.ToList();
            loadTo.desWeights = fracSavedData.saved_desWeights.ToList();
            loadTo.fr_fracWeightsI = fracSavedData.saved_fracWeightsI.ToList();

            //Restore saved colliders to the allParts array
            for (int i = 0; i < loadTo.allParts.Count; i++)
            {
                loadTo.allParts[i].col = loadTo.saved_allPartsCol[i];
                loadTo.allParts[i].trans = loadTo.saved_allPartsCol[i].transform;
            }

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
                    ((MeshCollider)loadTo.allParts[i].col).sharedMesh = new()
                    {
                        vertices = fracSavedData.sMesh_colsVers[i].vectors
                    };
                }
            }

            //restore fr_[] variabels from fracRend mesh
            Mesh fracRendMesh = loadTo.fracFilter.sharedMesh;
            loadTo.fr_materials = loadTo.fracRend.sharedMaterials.ToList();
            loadTo.fr_verticesL = fracRendMesh.vertices.ToList();
            loadTo.fr_normalsL = fracRendMesh.normals.ToList();
            loadTo.fr_uvs = fracRendMesh.uv.ToList();
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
