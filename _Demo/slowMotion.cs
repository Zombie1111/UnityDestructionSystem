using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class slowMotion : MonoBehaviour
{
    // Update is called once per frame
    void Update()
    {
        // Get mouse wheel input
        float scrollWheelInput = Input.GetAxis("Mouse ScrollWheel") * Mathf.Lerp(0.032f, 1.6f, Time.timeScale);

        // Adjust speed based on mouse wheel input
        Time.timeScale = Mathf.Clamp(Time.timeScale + scrollWheelInput, 0.0f, 1.0f);
    }
}
