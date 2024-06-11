using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class destructionVisualMesh : MonoBehaviour
{
    [SerializeField] private Mesh visualMesh = null;

    /// <summary>
    /// Returns the visual color array from rend, returns null if no valid virtual color array is found
    /// </summary>
    public static Color[] TryGetVisualVertexColors(Renderer rend, int requiredLenght)
    {
        if (rend == null) return null;
        if (rend.TryGetComponent(out destructionVisualMesh visMesh) == false) return null;
        if (visMesh.visualMesh == null) return null;
        Color[] visColors = visMesh.visualMesh.colors;
        if (visColors.Length != requiredLenght)
        {
            Debug.LogError("Unable to use visualMesh from " + rend.transform.name + " since its color array lenght does not math the real mesh color array lenght");
            return null;
        }

        return visColors;
    }
}
