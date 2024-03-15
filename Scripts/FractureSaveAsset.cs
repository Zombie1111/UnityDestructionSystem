using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JetBrains.Annotations;
using Unity.VisualScripting.FullSerializer;
using System.Linq;
using UnityEngine.SceneManagement;
using Microsoft.Win32.SafeHandles;


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

            public FractureThis.IntList[] saved_verticsLinkedThreaded = new FractureThis.IntList[0];
            public float[] saved_partsOgResistanceThreaded = new float[0];
            public FractureThis.FracPart[] saved_allParts = new FractureThis.FracPart[0];
            public HashSet<int> saved_kinematicPartsStatus = new();
            public int saved_rendVertexCount = -1;

            //Only used if prefab because unity is useless and cant save meshes inside prefabs
            public SavableMesh saved_rendMesh = new();
            public VecArray[] sMesh_colsVers = null;
        }

        [System.Serializable]
        public class SavableMesh
        {
            public BoneWeight[] sMesh_boneWeights = null;
            public Matrix4x4[] sMesh_bindposes = null;
            public Vector3[] sMesh_vertics = null;
            public int[] sMesh_triangels = null;
            public Vector2[] sMesh_uvs = null;

            /// <summary>
            /// Returns the saved data as a mesh
            /// </summary>
            /// <returns></returns>
            public Mesh ToMesh()
            {
                Mesh newM = new()
                {
                    vertices = sMesh_vertics,
                    triangles = sMesh_triangels,
                    bindposes = sMesh_bindposes,
                    boneWeights = sMesh_boneWeights,
                    uv = sMesh_uvs
                };

                newM.RecalculateBounds();
                newM.RecalculateNormals();
                newM.RecalculateTangents();

                return newM;
            }

            /// <summary>
            /// Saves the given mesh
            /// </summary>
            public void FromMesh(Mesh mesh)
            {
                sMesh_boneWeights = mesh.boneWeights;
                sMesh_bindposes = mesh.bindposes;
                sMesh_vertics = mesh.vertices;
                sMesh_triangels = mesh.triangles;
                sMesh_uvs = mesh.uv;
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
                return sMesh_vertics != null && sMesh_vertics.Length > 0;
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
            if (saveFrom.fracRend == null || saveFrom.fracRend.sharedMesh == null) return -1; //cant save if no fracture

            //save the data
            fracSavedData.id += 1;
            fracSavedData.saved_kinematicPartsStatus = saveFrom.partsKinematicStatus.ToHashSet();
            fracSavedData.saved_verticsLinkedThreaded = saveFrom.verticsLinkedThreaded.ToArray();
            fracSavedData.saved_allParts = saveFrom.allParts.ToArray();
            fracSavedData.saved_rendVertexCount = saveFrom.fracRend.sharedMesh.vertexCount;

            //save mesh stuff if prefab
            if (saveFrom.GetFracturePrefabType() > 0)
            {
                fracSavedData.saved_rendMesh.FromMesh(saveFrom.fracRend.sharedMesh);

                if (saveFrom.allParts[0].col is MeshCollider)
                {
                    //if mesh colliders save the them too
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
                || (loadTo.fracRend.sharedMesh != null && loadTo.fracRend.sharedMesh.vertexCount != fracSavedData.saved_rendVertexCount))
            {
                return false;
            }

            //Apply saved data to the provided FractureThis instance
            loadTo.partsKinematicStatus = fracSavedData.saved_kinematicPartsStatus.ToHashSet();
            loadTo.verticsLinkedThreaded = fracSavedData.saved_verticsLinkedThreaded.ToArray();
            loadTo.allParts = fracSavedData.saved_allParts.ToList();

            //Restore saved colliders to the allParts array
            for (int i = 0; i < loadTo.allParts.Count; i++)
            {
                loadTo.allParts[i].col = loadTo.saved_allPartsCol[i];
                loadTo.allParts[i].trans = loadTo.saved_allPartsCol[i].transform;
            }

            //load meshes if was prefab
            if (fracSavedData.saved_rendMesh.IsValidMeshSaved() == false) return true;

            loadTo.fracRend.sharedMesh = fracSavedData.saved_rendMesh.ToMesh();

            if (fracSavedData.sMesh_colsVers == null || fracSavedData.sMesh_colsVers.Length == 0) return true;
            for (int i = 0; i < loadTo.allParts.Count; i += 1)
            {
                ((MeshCollider)loadTo.allParts[i].col).sharedMesh = new()
                {
                    vertices = fracSavedData.sMesh_colsVers[i].vectors
                };
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
