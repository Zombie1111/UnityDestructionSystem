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
    [CreateAssetMenu(fileName = "fractureSaveAsset", menuName = "Fracture Save Asset", order = 101)]
    public class FractureSaveAsset : ScriptableObject
    {
        /// <summary>
        /// Contains saved data for all fractures
        /// </summary>
        //public Dictionary<int, FractureSaveData> fracturesSavedData = new();
        public int nextFracId = 0;
        public List<FractureSaveData> fracturesSavedData = new();

        [System.Serializable]
        public class FractureSaveData
        {
            public int id = -69420;

            public FractureThis.IntList[] saved_verticsLinkedThreaded = new FractureThis.IntList[0];
            public float[] saved_partsOgResistanceThreaded = new float[0];
            public FractureThis.FracParts[] saved_allParts = new FractureThis.FracParts[0];
            public bool[] saved_kinematicPartStatus = new bool[0];
            public int[] saved_verticsPartThreaded = new int[0];
            public BoneWeight[] saved_boneWe_broken = new BoneWeight[0];
        }

        /// <summary>
        /// Saves data from the given script and returns the id it saved to
        /// </summary>
        /// <param name="saveFrom"></param>
        /// <param name="fracId">If >= 0, overwrites the data at the given id</param>
        /// <returns></returns>
        public int Save(FractureThis saveFrom, int fracId = -1)
        {
            //create new save slot if needed
            //if (fracturesSavedData.ContainsKey(fracId) == false)
            int idIndex = fracturesSavedData.FindIndex(fData => fData.id == fracId);
            if (idIndex < 0)
            {
                nextFracId += 1;
                fracId = nextFracId;
                idIndex = fracturesSavedData.Count;
                fracturesSavedData.Add(new() { id = fracId });
            }

            //save the data
            fracturesSavedData[idIndex].saved_kinematicPartStatus = saveFrom.kinematicPartStatus.ToArray();
            fracturesSavedData[idIndex].saved_verticsLinkedThreaded = saveFrom.verticsLinkedThreaded.ToArray();
            fracturesSavedData[idIndex].saved_allParts = saveFrom.allParts.ToArray();
            fracturesSavedData[idIndex].saved_partsOgResistanceThreaded = saveFrom.partsOgResistanceThreaded.ToArray();
            fracturesSavedData[idIndex].saved_verticsPartThreaded = saveFrom.verticsPartThreaded.ToArray();
            fracturesSavedData[idIndex].saved_boneWe_broken = saveFrom.boneWe_broken.ToArray();

            //because ScriptableObject cannot save actual components we save it on FractureThis
            saveFrom.saved_allPartsCol = new Collider[saveFrom.allParts.Length];
            for (int i = 0; i < saveFrom.allParts.Length; i += 1)
            {
                saveFrom.saved_allPartsCol[i] = saveFrom.allParts[i].col;
            }

            Debug.Log("saved " + idIndex + " id " + fracId);

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            EditorUtility.SetDirty(saveFrom);
#endif

            return fracId;
        }

        /// <summary>
        /// Loads the data saved at the given id to the provided FractureThis
        /// </summary>
        /// <param name="loadTo"></param>
        /// <param name="fracId"></param>
        public void Load(FractureThis loadTo, int fracId)
        {
            //Check if the specified fracId exists in the saved data
            int idIndex = fracturesSavedData.FindIndex(fData => fData.id == fracId);

            if (idIndex >= 0)
            {
                Debug.Log("loaded " + idIndex + " id " + fracId);

                //Apply saved data to the provided FractureThis instance
                loadTo.kinematicPartStatus = fracturesSavedData[idIndex].saved_kinematicPartStatus.ToArray();
                loadTo.verticsLinkedThreaded = fracturesSavedData[idIndex].saved_verticsLinkedThreaded.ToArray();
                loadTo.allParts = fracturesSavedData[idIndex].saved_allParts.ToArray();
                loadTo.partsOgResistanceThreaded = fracturesSavedData[idIndex].saved_partsOgResistanceThreaded.ToArray();
                loadTo.verticsPartThreaded = fracturesSavedData[idIndex].saved_verticsPartThreaded.ToArray();
                loadTo.boneWe_broken = fracturesSavedData[idIndex].saved_boneWe_broken.ToArray();

                //Restore saved colliders to the allParts array
                for (int i = 0; i < loadTo.allParts.Length; i++)
                {
                    loadTo.allParts[i].col = loadTo.saved_allPartsCol[i];
                    loadTo.allParts[i].trans = loadTo.saved_allPartsCol[i].transform;
                }
            }
        }

        /// <summary>
        /// Removes the data saved for the given id
        /// </summary>
        /// <param name="fracId"></param>
        public void RemoveSavedData(FractureThis removeFrom, int fracId)
        {
            removeFrom.saved_allPartsCol = new Collider[0];
            int idIndex = fracturesSavedData.FindIndex(fData => fData.id == fracId);
            if (idIndex >= 0) fracturesSavedData.RemoveAt(idIndex);

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            EditorUtility.SetDirty(removeFrom);
#endif
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

            //always show next id
            SerializedProperty nextFId = serializedObject.FindProperty("nextFracId");
            EditorGUILayout.PropertyField(nextFId, true);

            // Show the button to toggle the float variable
            if (GUILayout.Button("Show Fracture Data (MAY FREEZE UNITY!)"))
            {
                showFloatVariable = !showFloatVariable;
            }

            if (showFloatVariable)
            {
                // Show the variables
                serializedObject.Update(); // Ensure serialized object is up to date

                SerializedProperty fracSavedData = serializedObject.FindProperty("fracturesSavedData");
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
