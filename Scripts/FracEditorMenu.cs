#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.EditorCoroutines.Editor;

namespace zombDestruction
{
    public class FracEditorMenu : Editor
    {
        [MenuItem("Tools/Fracture/Fracture All")]
        private static void FractureAll()
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(FractureArray(GameObject.FindObjectsOfType<DestructableObject>(false)));
        }

        [MenuItem("Tools/Fracture/Fracture Selected")]
        private static void FractureSelected()
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(FractureArray(GetSelectedFractures().ToArray()));
        }

        [MenuItem("Tools/Fracture/Remove All Fractures")]
        private static void RemoveAllFractures()
        {
            RemoveFracturesArray(GameObject.FindObjectsOfType<DestructableObject>(false));
        }

        [MenuItem("Tools/Fracture/Remove Selected Fractures")]
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

        [MenuItem("Tools/Fracture/CopyPropertiesFromLastSelected")]
        private static void CopyPropertiesFromLastSelected()
        {
            if (Selection.activeTransform == null)
            {
                Debug.LogError("No object selected");
                return;
            }

            if (Selection.activeTransform.TryGetComponent<DestructableObject>(out var fThis) == false)
            {
                Debug.LogError("The last selected object is not a fracture");
                return;
            }

            int copyCount = 0;

            foreach (Transform trans in Selection.transforms)
            {
                if (trans == fThis.transform) continue;
                if (trans.TryGetComponent<DestructableObject>(out var frac) == false) continue;
                copyCount++;

                frac.CopyFracturePropertiesFrom(fThis);
            }

            //Debug.Log(Selection.gameObjects);
            Debug.Log("Copied fracture properties from " + fThis.transform.name + " to " + copyCount + " other fracture objects");
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
