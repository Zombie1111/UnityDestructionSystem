using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Player_sway : MonoBehaviour
{
    [Header("Sway Settings")]
    [SerializeField] private float swaySmooth = 4.0f;
    [SerializeField] private float bobSpeed = 3.0f;
    [SerializeField] private float bobAmount = 0.02f;
    [SerializeField] private float bobYAmount = 0.04f;

    [Header("Rotation Settings")]
    [SerializeField] private float rotationAmount = 3.0f;
    [SerializeField] private float maxRotationAmount = 10.0f;
    [SerializeField] private float rotationSmooth = 5.0f;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private float swayX, swayY;
    private float bobTimer = 0.0f;

    //my varibels
    [System.NonSerialized] public float movementSpeed = 0.0f;
    [System.NonSerialized] public float movementSpeedY = 0.0f;
    [System.NonSerialized] public float mouseX = 0.0f;
    [System.NonSerialized] public float mouseY = 0.0f;
    [System.NonSerialized] public Vector3 positionVelocity = Vector3.zero;

    void Start()
    {
        initialPosition = transform.localPosition;
        initialRotation = transform.localRotation;
    }

    void Update()
    {
        // Apply bobbing effect to the weapon
        float bobX = Mathf.Sin(bobTimer) * bobAmount * movementSpeed;
        float bobY = Mathf.Sin(bobTimer * 2) * bobYAmount * movementSpeedY;
        bobTimer += Time.deltaTime * bobSpeed * movementSpeed;
        Vector3 posV = positionVelocity * bobAmount;
        Vector3 bobPosition = new Vector3(initialPosition.x + bobX + posV.x, initialPosition.y + bobY + posV.y, initialPosition.z + posV.z);

        // Smoothly move the weapon based on player movement and bobbing effect
        Vector3 finalPosition = new Vector3(bobPosition.x, bobPosition.y, bobPosition.z);
        transform.localPosition = Vector3.Lerp(transform.localPosition, finalPosition, Time.deltaTime * swaySmooth);

        //Quaternion maxRotBySpeed = Quaternion.Euler(initialRotation.x + (mouseY / Time.deltaTime * rotationAmount), initialRotation.y + (mouseX / Time.deltaTime * rotationAmount), initialRotation.z);
        transform.localEulerAngles += new Vector3(mouseY * rotationAmount * 2.0f, mouseX * rotationAmount, initialRotation.z);
        transform.localRotation = FN_clampRotation(Quaternion.Slerp(transform.localRotation, initialRotation, rotationSmooth * Quaternion.Angle(transform.localRotation, initialRotation) * Time.deltaTime), initialRotation, maxRotationAmount);
    }

    private Quaternion FN_clampRotation(Quaternion rotationA, Quaternion rotationB, float maxRotation)
    {
        //Quaternion relativeRotation = Quaternion.Inverse(rotationB) * rotationA;
        float angleDifference = Quaternion.Angle(rotationB, rotationA);
        if (angleDifference <= maxRotation) return rotationA;
        return Quaternion.RotateTowards(rotationA, rotationB, angleDifference - maxRotation);
    }
}
