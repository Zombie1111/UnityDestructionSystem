using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace zombDestruction
{
    public class DestructionCallback : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private bool includeChildren = true;
        [Tooltip("Should parts that are disconnected from the main body but connected with other parts count as broken?")]
        [SerializeField] private bool countDisconnectedChunksAsBroken = true;

        /// <summary>
        /// Contains all stuff to invoke, you can subscribe to callbackEvents[X].OnDestructionCallback to recieve more data on invoke than the defualt unityEvents
        /// </summary>
        [Space()]
        public List<FracCallbackEvent> callbackEvents = new();

        [Space()]
        [Header("Stats")]
        public float currentBrokenPercentage = 0.0f;
        public int brokenPartCount = 0;
        private HashSet<int> includedParts = new();
        private int orginalPartCount = 0;
        private DestructableObject fracSource;

        private void Start()
        {
            //Get fracture
            fracSource = DestructableObject.TryGetValidDestructableObjectInParent(transform);
            if (fracSource == null) return;

            //Reset old
            currentBrokenPercentage = 0.0f;
            brokenPartCount = 0;
            orginalPartCount = 0;
            includedParts.Clear();

            //Get all parts to include in damage count test
            int partCount = fracSource.allParts.Count;

            for (int partI = 0; partI < partCount; partI++)
            {
                if (includeChildren == false)
                {
                    if (fracSource.allPartsCol[partI].transform.parent != transform) continue;
                }
                else if (fracSource.allPartsCol[partI].GetComponentInParent<DestructionCallback>() == null) continue;

                includedParts.Add(partI);
                orginalPartCount++;
            }

            if (orginalPartCount == 0)
            {
                Debug.LogError(transform.name + " does not have any parts to include");
                return;
            }

            //Add to event
            fracSource.OnPartParentChanged += OnPartParentChanged;
        }

        private void OnDestroy()
        {
            //Remove from event
            if (fracSource != null) fracSource.OnPartParentChanged -= OnPartParentChanged;
        }

        private bool IEnumeratorIsStarted = false;

        private IEnumerator OnBrokenPercentageChanged()
        {
            yield return new WaitForEndOfFrame();

            IEnumeratorIsStarted = false;
            currentBrokenPercentage = brokenPartCount / (float)orginalPartCount;

            foreach (var cbEvent in callbackEvents)
            {
                float change = currentBrokenPercentage - cbEvent.lastInvokeValue;
                if (cbEvent.onlyMessure == FracCallbackEvent.BrokenMessureType.PositiveChange)
                {
                    if (change < 0.0f)
                    {
                        cbEvent.lastInvokeValue = currentBrokenPercentage;
                        if (cbEvent.resetInvokedStatusWhenOppositeChange == true) cbEvent.hasBeenInvoked = false;
                        continue;
                    }

                    if (currentBrokenPercentage > cbEvent.alwaysInvokeIfOnMessuredSideOf)
                    {
                        cbEvent.TryInvokeEvents(change, currentBrokenPercentage);
                        continue;
                    }
                }

                if (cbEvent.onlyMessure == FracCallbackEvent.BrokenMessureType.NegativeChange)
                {
                    if (change > 0.0f)
                    {
                        cbEvent.lastInvokeValue = currentBrokenPercentage;
                        if (cbEvent.resetInvokedStatusWhenOppositeChange == true) cbEvent.hasBeenInvoked = false;
                        continue;
                    }

                    if (currentBrokenPercentage < cbEvent.alwaysInvokeIfOnMessuredSideOf)
                    {
                        cbEvent.TryInvokeEvents(change, currentBrokenPercentage);
                        continue;
                    }
                }

                if (Mathf.Abs(change) >= cbEvent.invokeWhenBrokenPercentageHasChangedBy)
                {
                    cbEvent.TryInvokeEvents(change, currentBrokenPercentage);
                    continue;
                }
            }
        }

        private void OnPartParentChanged(int partI, int oldParentI, int newParentI)
        {
            if (includedParts.Contains(partI) == false) return;

            bool wasBroken = GetIfParentICountAsBroken(oldParentI);
            bool isBroken = GetIfParentICountAsBroken(newParentI);
            if (wasBroken == isBroken) return;

            brokenPartCount += isBroken == true ? 1 : -1;

            if (IEnumeratorIsStarted == false)
            {
                IEnumeratorIsStarted = true;
                StartCoroutine(OnBrokenPercentageChanged());
            }

            bool GetIfParentICountAsBroken(int parentI)
            {
                if (parentI != 0)
                {
                    if (parentI < 0 || countDisconnectedChunksAsBroken == true)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

#if UNITY_EDITOR
        //########################Custom Editor######################################
        [CustomEditor(typeof(DestructionCallback))]
        public class YourScriptEditor : Editor
        {
            public override void OnInspectorGUI()
            {
                serializedObject.Update();

                DestructionCallback yourScript = (DestructionCallback)target;

                EditorGUILayout.Space();

                DrawPropertiesExcluding(serializedObject, "m_Script", "currentBrokenPercentage", "brokenPartCount");
                GUI.enabled = false;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("currentBrokenPercentage"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("brokenPartCount"), true);
                GUI.enabled = true;

                //Apply changes
                serializedObject.ApplyModifiedProperties();
            }
        }
#endif
    }
}
