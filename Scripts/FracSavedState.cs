using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace zombDestruction
{
    public class FracSavedState : ScriptableObject
    {
#if UNITY_EDITOR
        [MenuItem("Tools/Fracture/CreateSaveStateAsset")]
#endif
        public static FracSavedState CreateSaveStateAsset()
        {
#if UNITY_EDITOR
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

            ScriptableObject fracSaveAsset = ScriptableObject.CreateInstance<FracSavedState>();
            AssetDatabase.CreateAsset((FracSavedState)fracSaveAsset, path);
            return (FracSavedState)fracSaveAsset;
#else
            return null;
#endif
        }

        public SavedPartState[] savedPartStates;
        public SavedParentState[] savedParentStates;
        public DestructableObject.MeshData[] savedVertexStates;

#if !FRAC_NO_VERTEXCOLORSUPPORT && !FRAC_NO_VERTEXCOLORSAVESTATESUPPORT
        public GpuMeshVertex[] savedColorStates;
#endif

        [System.Serializable]
        public class SavedPartState
        {
            public Vector3 transPosL;
            public Quaternion transRotL;
            public int parentIndex;
            public float maxTransportUsed;
            public Vector3 structOffset;
            public Vector3 structPos;
        }

        [System.Serializable]
        public class SavedParentState
        {
            public ParentTransData[] transData;

            [System.Serializable]
            public class ParentTransData
            {
                public int transPath;
                public Vector3 transPosL;
                public Quaternion transRotL;
            }
        }

        public bool Save(DestructableObject saveFrom, bool calledManually = false)
        {   
            //Check if can save
            if (saveFrom.fractureIsValid == false)
            {
                Debug.LogError("Cannot save the state of " + saveFrom.transform.name + " because it has not been fractured!");
                return false;
            }

            saveFrom.GetTransformData_end();
            saveFrom.ComputeDestruction_end();

            //Save part states
            int partCount = saveFrom.allParts.Count;
            savedPartStates = new SavedPartState[partCount];

            for (int pI = 0; pI < partCount; pI++)
            {
                saveFrom.allPartsCol[pI].transform.GetLocalPositionAndRotation(out Vector3 lPos, out Quaternion lRot);

                savedPartStates[pI] = new()
                {
                    parentIndex = saveFrom.jCDW_job.partsParentI[pI],
                    maxTransportUsed = saveFrom.jCDW_job.fStructs[pI].maxTransportUsed,
                    transPosL = lPos,
                    transRotL = lRot,
                    structOffset =
#if UNITY_EDITOR
                    Application.isPlaying == false ? Vector3.zero :
#endif    
                    saveFrom.jCDW_job.defOffsetW[pI],
                    structPos = saveFrom.jCDW_job.structPosL[pI]
                };
            }

            //Save parent states
            int parentCount = saveFrom.allParents.Count;
            savedParentStates = new SavedParentState[parentCount];
            HashSet<int> transPaths = new();
            foreach (int path in saveFrom.partsLocalParentPath)
            {
                transPaths.Add(path);
            }

            for (int pI = 0; pI < parentCount; pI++)
            {
                var transData = new SavedParentState.ParentTransData[transPaths.Count];
                int tI = 0;

                foreach (int path in transPaths)
                {
                    Transform trans = FracHelpFunc.DecodeHierarchyPath(saveFrom.allParents[pI].parentTrans, path);
                    transData[tI] = new()
                    {
                       transPath = path,
                       transPosL = trans.localPosition,
                       transRotL = trans.localRotation
                    };

                    tI++;
                }

                savedParentStates[pI] = new()
                { 
                    transData = transData,
                };
            }

            //Save vertex states
            savedVertexStates = new DestructableObject.MeshData[saveFrom.buf_meshData.count];
            saveFrom.buf_meshData.GetData(savedVertexStates);

#if !FRAC_NO_VERTEXCOLORSUPPORT && !FRAC_NO_VERTEXCOLORSAVESTATESUPPORT
            //Save color states
            savedColorStates = new GpuMeshVertex[saveFrom.buf_gpuMeshVertexs.count];
            saveFrom.buf_gpuMeshVertexs.GetData(savedColorStates);
#endif

#if UNITY_EDITOR
            if (calledManually == true)
            {
                EditorUtility.SetDirty(this);
            }
#endif

            return true;
        }

        public bool Load(DestructableObject loadTo)
        {
            //Check if can load
            if (HasValidSavedState(loadTo) == false) return false;
            loadTo.GetTransformData_end();
            loadTo.ComputeDestruction_end();

            //Load parents
            int parentCount = savedParentStates.Length;

            for (int pI = 0; pI < parentCount; pI++)
            {
                if (loadTo.allParents.Count <= pI) loadTo.CreateNewParent(null, -1);
                Transform parentTrans = loadTo.allParents[pI].parentTrans;

                foreach (var tData in savedParentStates[pI].transData)
                {
                    Transform trans = FracHelpFunc.DecodeHierarchyPath(parentTrans, tData.transPath);
                    trans.SetLocalPositionAndRotation(tData.transPosL, tData.transRotL);
                }
            }

            //Load parts
            int partCount = savedPartStates.Length;

            for (int pI = 0; pI < partCount; pI++)
            {
                var partState = savedPartStates[pI];
                var fPart = loadTo.jCDW_job.fStructs[pI];

                fPart.maxTransportUsed = partState.maxTransportUsed;
                loadTo.jCDW_job.fStructs[pI] = fPart;
                loadTo.jCDW_job.structPosL[pI] = partState.structPos;
                loadTo.jCDW_job.defOffsetW[pI] = partState.structOffset;

                loadTo.SetPartParent(pI, partState.parentIndex);
                loadTo.allPartsCol[pI].transform.SetLocalPositionAndRotation(partState.transPosL, partState.transRotL);
            }

            loadTo.StartCoroutine(LoadPartsPosDelay(loadTo));

            //Load vertics
            loadTo.buf_meshData.SetData(savedVertexStates);
            loadTo.wantToApplySkinning = true;
            if (FracGlobalSettings.maxColliderUpdatesPerFrame > 0)
            {
                for (int pI = 0; pI < partCount; pI++)
                {
                    loadTo.des_deformedParts[loadTo.des_deformedPartsIndex].Add(pI);
                }
            }

#if !FRAC_NO_VERTEXCOLORSUPPORT && !FRAC_NO_VERTEXCOLORSAVESTATESUPPORT
            //Load color states
            loadTo.buf_gpuMeshVertexs.SetData(savedColorStates);
#endif

            //Set interpolation speed
            loadTo.interpolationSpeedActual = 0.0f;
            return true;
        }

        public bool HasValidSavedState(DestructableObject validForThis)
        {
            if (validForThis.fractureIsValid == false)
            {
                Debug.LogError("Cannot load a state to " + validForThis.transform.name + " because it has not been fractured");
                return false;
            }

            if (savedPartStates == null || savedParentStates == null || savedVertexStates == null
#if !FRAC_NO_VERTEXCOLORSUPPORT && !FRAC_NO_VERTEXCOLORSAVESTATESUPPORT
                || savedColorStates == null || savedColorStates.Length == 0
#endif
                || savedPartStates.Length == 0 || savedParentStates.Length == 0 || savedVertexStates.Length == 0)
            {
                Debug.LogError("Cant load " + this.name + " to " + validForThis.transform.name + " because no state has been saved yet");
                return false;
            }

            if (savedPartStates.Length != validForThis.allParts.Count
                || savedVertexStates.Length != validForThis.buf_meshData.count
#if !FRAC_NO_VERTEXCOLORSUPPORT && !FRAC_NO_VERTEXCOLORSAVESTATESUPPORT
                || savedColorStates.Length != validForThis.buf_gpuMeshVertexs.count
#endif
                )
            {
                Debug.LogError("Cant load " + this.name + " to " + validForThis.transform.name + " because it was saved for another fracture");
                return false;
            }

            return true;
        }

        private IEnumerator LoadPartsPosDelay(DestructableObject fracThis)
        {
            yield return new WaitForFixedUpdate();

            int partCount = savedPartStates.Length;

            for (int pI = 0; pI < partCount; pI++)
            {
                var partState = savedPartStates[pI];
                fracThis.allPartsCol[pI].transform.SetLocalPositionAndRotation(partState.transPosL, partState.transRotL);
            }
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(FracSavedState))]
    public class FractureSaveStateEditor : Editor
    {
        private bool showFloatVariable = false;

        public override void OnInspectorGUI()
        {
            FracSavedState myScript = (FracSavedState)target;

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
                SerializedProperty fracSavedData = serializedObject.FindProperty("savedPartStates");
                EditorGUILayout.PropertyField(fracSavedData, true);

                fracSavedData = serializedObject.FindProperty("savedParentStates");
                EditorGUILayout.PropertyField(fracSavedData, true);

                fracSavedData = serializedObject.FindProperty("savedVertexStates");
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


