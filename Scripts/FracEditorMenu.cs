#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.EditorCoroutines.Editor;
using UnityEngine.Assertions.Must;

namespace zombDestruction
{
    public class FracEditorMenu : Editor
    {
        [MenuItem("Tools/Destruction/Fracture All")]
        private static void FractureAll()
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(FractureArray(GameObject.FindObjectsOfType<DestructableObject>(false)));
        }

        [MenuItem("Tools/Destruction/Fracture Selected")]
        private static void FractureSelected()
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(FractureArray(GetSelectedFractures().ToArray()));
        }

        [MenuItem("Tools/Destruction/Remove All Fractures")]
        private static void RemoveAllFractures()
        {
            RemoveFracturesArray(GameObject.FindObjectsOfType<DestructableObject>(false));
        }

        [MenuItem("Tools/Destruction/Remove Selected Fractures")]
        private static void RemoveSelectedFractures()
        {
            RemoveFracturesArray(GetSelectedFractures().ToArray());
        }

        /// <summary>
        /// Returns a list containing all fractures the user have currently selected in the editor
        /// </summary>
        private static List<DestructableObject> GetSelectedFractures()
        {
            if (Selection.activeTransform == null)
            {
                Debug.LogError("No object selected");
                return new();
            }

            Transform[] transs = Selection.transforms;
            List<DestructableObject> selectedFracs = new();
            for (int i = 0; i < transs.Length; i += 1)
            {
                if (transs[i].TryGetComponent(out DestructableObject fracT) == true) selectedFracs.Add(fracT);
            }

            return selectedFracs;
        }

        [MenuItem("Tools/Destruction/SetSelectedAsCopySource")]
        private static void SetSelectedAsCopySource()
        {
            if (Selection.activeTransform == null)
            {
                Debug.LogError("No object selected");
                return;
            }

            if (Selection.activeTransform.TryGetComponent<DestructableObject>(out var fThis) == false)
            {
                Debug.LogError("The last selected object is not destructable");
                return;
            }

            DestructionHandler desHandler = DestructionHandler.TryGetDestructionHandler(fThis.gameObject);
            if (desHandler == null) return;

            desHandler.desCopySource = fThis;
            Debug.Log(fThis.transform.name + " is set as copy source!");
        }

        [MenuItem("Tools/Destruction/SetSelectedFromCopySource")]
        private static void SetSelectedFromCopySource()
        {
            if (Selection.activeGameObject == null)
            {
                Debug.LogError("No object selected");
                return;
            }

            DestructionHandler desHandler = DestructionHandler.TryGetDestructionHandler(Selection.activeGameObject);
            if (desHandler == null) return;
            if (desHandler.desCopySource == null)
            {
                Debug.LogError("No copy source has been set!");
                return;
            }

            var fThis = desHandler.desCopySource;

            if (fThis.destructionMaterials.Count > (fThis.fractureIsValid == true ? 1 : 0))
                Debug.Log("Only the defualt destruction material is copied!");

            int copyCount = 0;

            foreach (Transform trans in Selection.transforms)
            {
                if (trans == fThis.transform) continue;
                if (trans.TryGetComponent<DestructableObject>(out var frac) == false) continue;
                if (frac.fractureIsValid == true)
                {
                    Debug.Log("Copied fracture properties to " + trans.name + " while it was fractured, I recommend you regenerate the fracture!");
                }

                copyCount++;
                frac.CopyFracturePropertiesFrom(fThis);

                if (frac.GetFracturePrefabType() == 1)
                {
                    string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(frac.gameObject);
                    PrefabUtility.ApplyObjectOverride(frac, prefabPath, InteractionMode.AutomatedAction);
                }

                EditorUtility.SetDirty(frac);
            }

            Debug.Log("Copied fracture properties from " + fThis.transform.name + " to " + copyCount + " other fracture object(s)");
        }

        /// <summary>
        /// Fractures all objects in the given arrayz
        /// </summary>
        /// <param name="fractureQuality">0 = low, 1 = medium, 2 = high (above 2 = defualt)</param>
        /// <returns></returns>
        public static IEnumerator FractureArray(DestructableObject[] toFracture, byte fractureQuality = 69, bool logProgress = true)
        {
            if (toFracture.Length == 0 || FracHelpFunc.AskEditorIfCanSave(false) == false) yield break;

            int fracturedCount = 0;
            HashSet<FracSaveAsset> usedAssets = new();

            foreach (DestructableObject frac in toFracture)
            {
                if (frac == null || frac.enabled == false || usedAssets.Add(frac.saveAsset) == false)
                {
                    fracturedCount++;
                    if (fracturedCount >= toFracture.Length) Debug.Log("Fracture Progress: " + fracturedCount + "/" + toFracture.Length + " (Done)");
                    continue;
                }

                DestructableObject.GenerationQuality ogQuality = frac.generationQuality;

                if (fractureQuality <= 2) frac.generationQuality = (DestructableObject.GenerationQuality)fractureQuality;
                frac.Gen_fractureObject(false, false);

                frac.generationQuality = ogQuality;

                fracturedCount++;

                if (logProgress == true)
                {
                    if (fracturedCount >= toFracture.Length) Debug.Log("Fracture Progress: " + fracturedCount + "/" + toFracture.Length + " (Done)");
                    else Debug.Log("Fracture Progress: " + fracturedCount + "/" + toFracture.Length);
                }

                yield return new WaitForSecondsRealtime(0.01f);
            }
        }

        /// <summary>
        /// Removes all fractures in the given array
        /// </summary>
        public static void RemoveFracturesArray(DestructableObject[] toRemove, bool logProgress = true)
        {
            if (toRemove.Length == 0 || FracHelpFunc.AskEditorIfCanSave(true) == false) return;

            HashSet<FracSaveAsset> usedAssets = new();

            foreach (DestructableObject frac in toRemove)
            {
                if (frac == null || frac.enabled == false || usedAssets.Add(frac.saveAsset) == false)
                {
                    continue;
                }

                frac.Gen_removeFracture(false, false);
            }

            if (logProgress == true) Debug.Log("Removed " + toRemove.Length + " fractures");
        }

        [MenuItem("CONTEXT/SkinnedMeshRenderer/Save Mesh...")]
        public static void SaveMeshInPlace(MenuCommand menuCommand)
        {
            SkinnedMeshRenderer mf = menuCommand.context as SkinnedMeshRenderer;
            Mesh m = mf.sharedMesh;
            SaveMesh(m, m.name, true, false);
        }

        public static void SaveMesh(Mesh mesh, string name, bool makeNewInstance, bool optimizeMesh)
        {
            string path = EditorUtility.SaveFilePanel("Save Separate Mesh Asset", "Assets/", name, "asset");
            if (string.IsNullOrEmpty(path)) return;

            path = FileUtil.GetProjectRelativePath(path);

            Mesh meshToSave = (makeNewInstance) ? Object.Instantiate(mesh) as Mesh : mesh;

            if (optimizeMesh)
                MeshUtility.Optimize(meshToSave);

            AssetDatabase.CreateAsset(meshToSave, path);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
