using UdonSharp;
using UnityEngine;

namespace K13A.UdonStatic.Examples
{
    [AddComponentMenu("K13A/UdonStatic/Counter Example")]
    public class UdonStaticCounterExample : UdonSharpBehaviour
    {
        private static int Counter = 0;
        private static float ElapsedSeconds = 0f;
        private static bool Paused = false;

        public int writeCounterValue;
        public bool writePaused;

        public int visibleCounter;
        public float visibleElapsedSeconds;
        public bool visiblePaused;

        private void Update()
        {
            Paused = writePaused;

            if (!Paused)
            {
                Counter++;
                ElapsedSeconds = ElapsedSeconds + Time.deltaTime;
            }

            visibleCounter = Counter;
            visibleElapsedSeconds = ElapsedSeconds;
            visiblePaused = Paused;
        }

        public override void Interact()
        {
            UdonStaticCounterExample.Counter = writeCounterValue;
        }
    }
}
