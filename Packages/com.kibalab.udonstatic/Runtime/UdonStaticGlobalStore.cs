using UdonSharp;
using UnityEngine;

namespace K13A.UdonStatic.Runtime
{
    [AddComponentMenu("K13A/UdonStatic/Global Store")]
    public class UdonStaticGlobalStore : UdonSharpBehaviour
    {
        public string[] IntKeys = new string[0];
        public int[] IntData = new int[0];
        public string[] FloatKeys = new string[0];
        public float[] FloatData = new float[0];
        public string[] BoolKeys = new string[0];
        public bool[] BoolData = new bool[0];
        public string[] StringKeys = new string[0];
        public string[] StringData = new string[0];
        public string[] LongKeys = new string[0];
        public long[] LongData = new long[0];
        public string[] DoubleKeys = new string[0];
        public double[] DoubleData = new double[0];
        public string[] Vector2Keys = new string[0];
        public Vector2[] Vector2Data = new Vector2[0];
        public string[] Vector3Keys = new string[0];
        public Vector3[] Vector3Data = new Vector3[0];
        public string[] QuaternionKeys = new string[0];
        public Quaternion[] QuaternionData = new Quaternion[0];
        public string[] ColorKeys = new string[0];
        public Color[] ColorData = new Color[0];
        public string[] GameObjectKeys = new string[0];
        public GameObject[] GameObjectData = new GameObject[0];
        public string[] TransformKeys = new string[0];
        public Transform[] TransformData = new Transform[0];
        public string[] ObjectKeys = new string[0];
        public UnityEngine.Object[] ObjectData = new UnityEngine.Object[0];
    }
}
