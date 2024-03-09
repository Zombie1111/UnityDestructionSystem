using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zombie1111_uDestruction;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Zombie1111_uDestruction
{
    public class FractureSavedState : ScriptableObject
    {
#if UNITY_EDITOR

        [System.Serializable]
        public class FloatList
        {
            [SerializeField] public List<float> floatList = new();

            public static FloatList[] FromFloatArray(List<float>[] arrayOfLists)
            {
                FloatList[] resultList = new FloatList[arrayOfLists.Length];

                for (int i = 0; i < arrayOfLists.Length; i++)
                {
                    resultList[i] = new FloatList();
                    if (arrayOfLists[i] == null) resultList[i].floatList = null;
                    else resultList[i].floatList.AddRange(arrayOfLists[i]);
                }

                return resultList;
            }

            public static bool IsTwoArraySame(FloatList[] floatsA, FloatList[] floatsB)
            {
                if (floatsA == null || floatsB == null) return floatsA == null && floatsB == null;
                int fL = floatsA.Length;
                if (fL != floatsB.Length) return false;

                for (int i = 0; i < fL; i++)
                {
                    if (floatsA[i].floatList == null || floatsA[i].floatList.Count == 0)
                    {
                        if (floatsB[i].floatList != null && floatsB[i].floatList.Count > 0) return false;
                        continue;
                    }

                    for (int ii = 0; ii < floatsA[i].floatList.Count; ii++)
                    {
                        if (floatsA[i].floatList[ii] != floatsB[i].floatList[ii]) return false;
                    }
                }

                return true;
            }
        }

        //Editor only, save info about the previous fracture result so we can fracture much faster if no mayor changes has been made
        //save data about meshes to fracture so we can identify if mayor changes has been made or not
        public PreS_toFracData preS_toFracData = null;

        [System.Serializable]
        public class PreS_toFracData
        {
            public FloatList[] md_verGroupIds;
            public int seed;
            public int fractureCount;
            public bool dynamicFractureCount;
            public float randomness;
            public float worldScale;
            public FractureThis.FractureRemesh remeshing;
            public FractureThis.GenerationQuality generationQuality;
            public Bounds[] toFracRendBounds;
            public int totalVerCount;
        }

        public PreS_setupRendData preS_setupRendResult = null;

        [System.Serializable]
        public class PreS_setupRendData
        {
            public FractureSaveAsset.SavableMesh comMesh;
            public FractureThis.IntList[] parts_rendLinkVerIndexes;
            public FractureThis.IntList[] verticsLinkedThreaded;
            public int[] rVersBestOgMeshVer;
            public int[] rTrisBestOgMeshTris;
        }

        public PreS_setupRealSkinData preS_setupRealSkinResult = null;

        [System.Serializable]
        public class PreS_setupRealSkinData
        {
            public Vector3[] parts_positions;
            public Quaternion[] parts_rotations;
        }

        public PreS_fracedMeshesData preS_fracedMeshes = null;

        [System.Serializable]
        public class PreS_fracedMeshesData
        {
            public List<FractureThis.MeshData> fracedMeshes_d;
            public List<FractureSaveAsset.SavableMesh> fracedMeshes_m;

            public List<FractureThis.MeshData> ToMeshData()
            {
                for (int i = 0; i < fracedMeshes_d.Count; i++)
                {
                    fracedMeshes_d[i].mesh = fracedMeshes_m[i].ToMesh();
                }

                return fracedMeshes_d;
            }

            public void FromMeshData(List<FractureThis.MeshData> meshDatas)
            {
                fracedMeshes_d = meshDatas;
                fracedMeshes_m = new();

                for (int i = 0; i < meshDatas.Count; i++)
                {
                    fracedMeshes_m.Add(new());
                    fracedMeshes_m[i].FromMesh(meshDatas[i].mesh);
                }
            }
        }

        /// <summary>
        /// Saves the given variabels that are not null (Editor only)
        /// </summary>
        public void SavePrefracture(
            PreS_toFracData toFracData = null,
            PreS_fracedMeshesData fracedMeshes = null,
            PreS_setupRendData setupRendResult = null,
            PreS_setupRealSkinData setupRealSkin = null)
        {
            //save stuff
            if (toFracData != null) preS_toFracData = toFracData;
            if (fracedMeshes != null) preS_fracedMeshes = fracedMeshes;
            if (setupRendResult != null) preS_setupRendResult = setupRendResult;
            if (setupRealSkin != null) preS_setupRealSkinResult = setupRealSkin;

            //mark dirty
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// Cleares all preS data
        /// </summary>
        public void ClearSavedPrefracture()
        {
            preS_toFracData = null;
            preS_fracedMeshes = null;
            preS_setupRendResult = null;
            preS_setupRealSkinResult = null;
        }

        [MenuItem("Tools/Fracture/CreateSaveStateAsset")]
        public static FractureSavedState CreateSaveStateAsset()
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
            string path = EditorUtility.SaveFilePanel("Create saveState asset", selectedFolderPath, "fracSaveState", "asset");
            if (string.IsNullOrEmpty(path)) return null;

            path = FileUtil.GetProjectRelativePath(path);

            ScriptableObject fracSaveAsset = ScriptableObject.CreateInstance<FractureSavedState>();
            AssetDatabase.CreateAsset((FractureSavedState)fracSaveAsset, path);
            return (FractureSavedState)fracSaveAsset;
        }
#endif
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(FractureSavedState))]
    public class FractureSaveStateEditor : Editor
    {
        private bool showFloatVariable = false;

        public override void OnInspectorGUI()
        {
            FractureSavedState myScript = (FractureSavedState)target;

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
                SerializedProperty fracSavedData = serializedObject.FindProperty("preS_fracedMeshes");
                EditorGUILayout.PropertyField(fracSavedData, true);

                fracSavedData = serializedObject.FindProperty("preS_toFracData");
                EditorGUILayout.PropertyField(fracSavedData, true);


                fracSavedData = serializedObject.FindProperty("preS_setupRendResult");
                EditorGUILayout.PropertyField(fracSavedData, true);


                fracSavedData = serializedObject.FindProperty("preS_setupRealSkinResult");
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


