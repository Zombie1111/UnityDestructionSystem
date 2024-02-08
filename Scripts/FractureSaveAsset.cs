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
    //[CreateAssetMenu(fileName = "fractureSaveAsset", menuName = "Fracture Save Asset", order = 101)]
    public class FractureSaveAsset : ScriptableObject
    {
#if UNITY_EDITOR
        /// <summary>
        /// Creates a new saveAsset in the folder that is currently selected in the editor project tab (returns the newly saveAsset, null if no valid folder selected)
        /// Editor only
        /// </summary>
        [MenuItem("Tools/Fracture/CreateSaveAssetInSelectedFolder")]
        public static FractureSaveAsset TryCreateSaveAssetInSelectedFolder()
        {
            UnityEngine.Object[] selection = Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.Assets);

            if (selection.Length == 1)
            {
                string selectedFolderPath = AssetDatabase.GetAssetPath(selection[0]);
                if (AssetDatabase.IsValidFolder(selectedFolderPath))
                {
                    //Create the asset and save it
                    selectedFolderPath += "/fracSaveAsset.asset";
                    selectedFolderPath = AssetDatabase.GenerateUniqueAssetPath(selectedFolderPath);

                    ScriptableObject fracSaveAsset = ScriptableObject.CreateInstance<FractureSaveAsset>();
                    AssetDatabase.CreateAsset((FractureSaveAsset)fracSaveAsset, selectedFolderPath);
                    return (FractureSaveAsset)fracSaveAsset;
                }
            }

            Debug.LogError("Please select a folder in the Project Tab the saveAsset should be created in");
            return null;
        }
#endif

        public FractureSaveData fracSavedData = new();

        [System.Serializable]
        public class FractureSaveData
        {
            public int id = -1;

            public FractureThis.IntList[] saved_verticsLinkedThreaded = new FractureThis.IntList[0];
            public float[] saved_partsOgResistanceThreaded = new float[0];
            public FractureThis.FracParts[] saved_allParts = new FractureThis.FracParts[0];
            public bool[] saved_kinematicPartStatus = new bool[0];
            public int[] saved_verticsPartThreaded = new int[0];
            public BoneWeight[] saved_boneWe_broken = new BoneWeight[0];

            //Only used if prefab because unity is useless and cant save meshes inside prefabs
            public BoneWeight[] sMesh_boneWeights = null;
            public Matrix4x4[] sMesh_bindposes = null;
            public Vector3[] sMesh_vertics = null;
            public int[] sMesh_triangels = null;
            public Vector2[] sMesh_uvs = null;
            public VecArray[] sMesh_colsVers = null;
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
            //save the data
            fracSavedData.id += 1;
            fracSavedData.saved_kinematicPartStatus = saveFrom.kinematicPartStatus.ToArray();
            fracSavedData.saved_verticsLinkedThreaded = saveFrom.verticsLinkedThreaded.ToArray();
            fracSavedData.saved_allParts = saveFrom.allParts.ToArray();
            fracSavedData.saved_partsOgResistanceThreaded = saveFrom.allPartsResistance.ToArray();
            fracSavedData.saved_verticsPartThreaded = saveFrom.verticsPartThreaded.ToArray();
            fracSavedData.saved_boneWe_broken = saveFrom.boneWe_broken.ToArray();

            //save mesh stuff if prefab
            if (saveFrom.GetFracturePrefabType() > 0)
            {
                fracSavedData.sMesh_boneWeights = saveFrom.fracRend.sharedMesh.boneWeights;
                fracSavedData.sMesh_bindposes = saveFrom.fracRend.sharedMesh.bindposes;
                fracSavedData.sMesh_vertics = saveFrom.fracRend.sharedMesh.vertices;
                fracSavedData.sMesh_triangels = saveFrom.fracRend.sharedMesh.triangles;
                fracSavedData.sMesh_uvs = saveFrom.fracRend.sharedMesh.uv;

                if (saveFrom.allParts[0].col is MeshCollider)
                {
                    //if mesh colliders save the them too
                    fracSavedData.sMesh_colsVers = new VecArray[saveFrom.allParts.Length];
                    for (int i = 0; i < saveFrom.allParts.Length; i += 1)
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
                fracSavedData.sMesh_boneWeights = null;
                fracSavedData.sMesh_bindposes = null;
                fracSavedData.sMesh_vertics = null;
                fracSavedData.sMesh_triangels = null;
                fracSavedData.sMesh_uvs = null;
                fracSavedData.sMesh_colsVers = null;
            }

            //because ScriptableObject cannot save actual components we save it on FractureThis
            saveFrom.saved_allPartsCol = new Collider[saveFrom.allParts.Length];
            for (int i = 0; i < saveFrom.allParts.Length; i += 1)
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
                || (loadTo.fracRend.sharedMesh != null && loadTo.fracRend.sharedMesh.vertexCount != fracSavedData.saved_verticsPartThreaded.Length))
            {
                return false;
            }

            //Apply saved data to the provided FractureThis instance
            loadTo.kinematicPartStatus = fracSavedData.saved_kinematicPartStatus.ToArray();
            loadTo.verticsLinkedThreaded = fracSavedData.saved_verticsLinkedThreaded.ToArray();
            loadTo.allParts = fracSavedData.saved_allParts.ToArray();
            loadTo.allPartsResistance = fracSavedData.saved_partsOgResistanceThreaded.ToArray();
            loadTo.verticsPartThreaded = fracSavedData.saved_verticsPartThreaded.ToArray();
            loadTo.boneWe_broken = fracSavedData.saved_boneWe_broken.ToArray();

            //Restore saved colliders to the allParts array
            for (int i = 0; i < loadTo.allParts.Length; i++)
            {
                loadTo.allParts[i].col = loadTo.saved_allPartsCol[i];
                loadTo.allParts[i].trans = loadTo.saved_allPartsCol[i].transform;
            }

            //load meshes if was prefab
            if (fracSavedData.sMesh_vertics == null || fracSavedData.sMesh_vertics.Length == 0) return true;

            loadTo.fracRend.sharedMesh = new()
            {
                vertices = fracSavedData.sMesh_vertics,
                triangles = fracSavedData.sMesh_triangels,
                bindposes = fracSavedData.sMesh_bindposes,
                boneWeights = fracSavedData.sMesh_boneWeights,
                uv = fracSavedData.sMesh_uvs
            };

            loadTo.fracRend.sharedMesh.RecalculateBounds();
            loadTo.fracRend.sharedMesh.RecalculateNormals();
            loadTo.fracRend.sharedMesh.RecalculateTangents();

            if (fracSavedData.sMesh_colsVers == null || fracSavedData.sMesh_colsVers.Length == 0) return true;
            for (int i = 0; i < loadTo.allParts.Length; i += 1)
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

            ////always show next id
            //SerializedProperty nextFId = serializedObject.FindProperty("nextFracId");
            //EditorGUILayout.PropertyField(nextFId, true);

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

                //serializedObject.ApplyModifiedProperties(); // Apply changes to the serialized object
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
