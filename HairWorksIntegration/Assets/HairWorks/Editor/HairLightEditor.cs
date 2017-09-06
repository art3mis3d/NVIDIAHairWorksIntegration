using System;
using UnityEditor;
using UnityEngine;

namespace GameWorks
{
    [CustomEditor(typeof(HairLight))]
    public class HairLightEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
        }
    }

}
