#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.EditorCoroutines.Editor;

namespace Zombie1111_uDestruction
{
    public class FractureEditorMenu : Editor
    {
        [MenuItem("Tools/Fracture/Fracture All Low")]
        private static void FractureAll_low()
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(FractureAll(0));
        }

        [MenuItem("Tools/Fracture/Fracture All Medium")]
        private static void FractureAll_medium()
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(FractureAll(1));
        }

        [MenuItem("Tools/Fracture/Fracture All High")]
        private static void FractureAll_high()
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(FractureAll(2));
        }

        [MenuItem("Tools/Fracture/Remove All Fractures")]
        private static void RemoveAllFractures()
        {
            foreach (FractureThis frac in GameObject.FindObjectsOfType<FractureThis>())
            {
                frac.Gen_loadAndMaybeSaveOgData(false);
            }
        }

        /// <summary>
        /// Fractures all meshes with the given fracture quality
        /// </summary>
        /// <param name="fractureQuality">0 = low, 1 = medium, 2 = high</param>
        /// <returns></returns>
        public static IEnumerator FractureAll(byte fractureQuality)
        {
            FractureThis[] allFracs = GameObject.FindObjectsOfType<FractureThis>();
            int fracturedCount = 0;

            foreach (FractureThis frac in allFracs)
            {
                FractureThis.GenerationQuality ogQuality = frac.generationQuality;

                frac.generationQuality = (FractureThis.GenerationQuality)fractureQuality;
                frac.Gen_fractureObject();

                frac.generationQuality = ogQuality;

                fracturedCount += 1;
                Debug.Log("Fracture Progress: " + fracturedCount + "/" + allFracs.Length);

                yield return new WaitForSecondsRealtime(0.01f);
            }
        }
    }
}
#endif
