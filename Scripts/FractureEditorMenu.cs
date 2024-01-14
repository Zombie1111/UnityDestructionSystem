#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.EditorCoroutines.Editor;
using Unity.VisualScripting.FullSerializer;

namespace Zombie1111_uDestruction
{
    public class FractureEditorMenu : Editor
    {
        [MenuItem("Tools/Fracture/Fracture All")]
        private static void FractureAll()
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(FractureAll(69, GameObject.FindObjectsOfType<FractureThis>(false)));
        }

        [MenuItem("Tools/Fracture/Fracture Selected")]
        private static void FractureSelected()
        {
            if (Selection.activeTransform == null)
            {
                Debug.LogError("No object selected");
                return;
            }

            Transform[] transs = Selection.transforms;
            FractureThis[] toFrac = new FractureThis[transs.Length];
            for (int i = 0; i < toFrac.Length; i += 1)
            {
                toFrac[i] = transs[i].GetComponent<FractureThis>();
            }

            EditorCoroutineUtility.StartCoroutineOwnerless(FractureAll(69, toFrac));
        }

        [MenuItem("Tools/Fracture/Remove All Fractures")]
        private static void RemoveAllFractures()
        {
            int count = 0;
            foreach (FractureThis frac in GameObject.FindObjectsOfType<FractureThis>())
            {
                frac.Gen_loadAndMaybeSaveOgData(false);
                count++;
            }

            Debug.Log("Removed " + count + " fractures");
        }

        [MenuItem("Tools/Fracture/Remove Selected Fractures")]
        private static void RemoveSelectedFractures()
        {
            if (Selection.activeTransform == null)
            {
                Debug.LogError("No object selected");
                return;
            }

            int count = 0;
            Transform[] transs = Selection.transforms;
            for (int i = 0; i < transs.Length; i += 1)
            {
                if (transs[i].TryGetComponent<FractureThis>(out var toFrac) == false) continue;
                toFrac.Gen_loadAndMaybeSaveOgData(false);
                count++;
            }

            Debug.Log("Removed " + count + " fractures");
        }

        private string ff;

        [MenuItem("Tools/Fracture/CopyPropertiesFromLastSelected")]
        private static void CopyPropertiesFromLastSelected()
        {
            if (Selection.activeTransform == null)
            {
                Debug.LogError("No object selected");
                return;
            }

            FractureThis fThis = Selection.activeTransform.GetComponent<FractureThis>();
            if (fThis == null)
            {
                Debug.LogError("The last selected object is not a fracture");
                return;
            }

            int copyCount = 0;

            foreach (Transform trans in Selection.transforms)
            {
                if (trans == fThis.transform) continue;
                if (trans.TryGetComponent<FractureThis>(out var frac) == false) continue;
                copyCount++;

                frac.CopyFracturePropertiesFrom(fThis);
            }

            //Debug.Log(Selection.gameObjects);
            Debug.Log("Copied fracture properties from " + fThis.transform.name + " to " + copyCount + " other fracture objects");
        }

        /// <summary>
        /// Fractures all meshes with the given fracture quality
        /// </summary>
        /// <param name="fractureQuality">0 = low, 1 = medium, 2 = high (above 2 = defualt)</param>
        /// <returns></returns>
        public static IEnumerator FractureAll(byte fractureQuality, FractureThis[] toFracture)
        {
            int fracturedCount = 0;

            foreach (FractureThis frac in toFracture)
            {
                if (frac == null) continue;
                if (frac.enabled == false)
                {
                    fracturedCount += 1;
                    continue;
                }

                FractureThis.GenerationQuality ogQuality = frac.generationQuality;

                if (fractureQuality <= 2) frac.generationQuality = (FractureThis.GenerationQuality)fractureQuality;
                frac.Gen_fractureObject();

                frac.generationQuality = ogQuality;

                fracturedCount += 1;
                if (fracturedCount >= toFracture.Length) Debug.Log("Fracture Progress: " + fracturedCount + "/" + toFracture.Length + " (Done)");
                else Debug.Log("Fracture Progress: " + fracturedCount + "/" + toFracture.Length);

                yield return new WaitForSecondsRealtime(0.01f);
            }
        }
    }
}
#endif
