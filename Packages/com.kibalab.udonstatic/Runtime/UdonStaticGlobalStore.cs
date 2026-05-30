using UdonSharp;
using UnityEngine;

namespace K13A.UdonStatic.Runtime
{
    [AddComponentMenu("K13A/UdonStatic/Global Store")]
    public class UdonStaticGlobalStore : UdonSharpBehaviour
    {
        [HideInInspector]
        public string[] IntKeys = new string[0];
        [HideInInspector]
        public int[] IntData = new int[0];
        [HideInInspector]
        public string[] FloatKeys = new string[0];
        [HideInInspector]
        public float[] FloatData = new float[0];
        [HideInInspector]
        public string[] BoolKeys = new string[0];
        [HideInInspector]
        public bool[] BoolData = new bool[0];
        [HideInInspector]
        public string[] StringKeys = new string[0];
        [HideInInspector]
        public string[] StringData = new string[0];
        [HideInInspector]
        public string[] LongKeys = new string[0];
        [HideInInspector]
        public long[] LongData = new long[0];
        [HideInInspector]
        public string[] DoubleKeys = new string[0];
        [HideInInspector]
        public double[] DoubleData = new double[0];
        [HideInInspector]
        public string[] Vector2Keys = new string[0];
        [HideInInspector]
        public Vector2[] Vector2Data = new Vector2[0];
        [HideInInspector]
        public string[] Vector3Keys = new string[0];
        [HideInInspector]
        public Vector3[] Vector3Data = new Vector3[0];
        [HideInInspector]
        public string[] QuaternionKeys = new string[0];
        [HideInInspector]
        public Quaternion[] QuaternionData = new Quaternion[0];
        [HideInInspector]
        public string[] ColorKeys = new string[0];
        [HideInInspector]
        public Color[] ColorData = new Color[0];
        [HideInInspector]
        public string[] GameObjectKeys = new string[0];
        [HideInInspector]
        public GameObject[] GameObjectData = new GameObject[0];
        [HideInInspector]
        public string[] TransformKeys = new string[0];
        [HideInInspector]
        public Transform[] TransformData = new Transform[0];
        [HideInInspector]
        public string[] ObjectKeys = new string[0];
        [HideInInspector]
        public UnityEngine.Object[] ObjectData = new UnityEngine.Object[0];
    }
}
