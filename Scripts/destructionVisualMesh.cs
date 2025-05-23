using UnityEngine;

namespace zombDestruction
{
    public class DestructionVisualMesh : MonoBehaviour
    {
        [Tooltip("The fractured mesh will use the vertexColors from this mesh for rendering at runtime," +
            " the mesh vertexColor array lenght must be the same as the other source mesh")]
        [SerializeField] private Mesh visualMesh = null;

        /// <summary>
        /// Returns the visual color array from rend, returns null if no valid virtual color array is found
        /// </summary>
        public static Color[] TryGetVisualVertexColors(Renderer rend, int requiredLenght)
        {
            if (rend == null) return null;
            if (rend.TryGetComponent(out DestructionVisualMesh visMesh) == false) return null;
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
}
