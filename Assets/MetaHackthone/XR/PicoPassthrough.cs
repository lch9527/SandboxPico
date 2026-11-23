using System.Collections;
using UnityEngine;
using Unity.XR.OpenXR.Features.PICOSupport;

namespace PhysicalAssembly.MetaXR
{
    // Present only on the Pico build's portable player prefab.
    public sealed class PicoPassthrough : MonoBehaviour
    {
        bool wanted=true,ready;
        // A mode that draws an opaque world (HandFleet) turns this off to stop paying for the camera feed it covers,
        // then puts back the value it found. Before the layer is up, the request is applied once it is.
        public bool Wanted
        {
            get=>wanted;
            set
            {
                if(wanted==value) return;
                wanted=value;
                if(ready && PassthroughFeature.EnableSeeThroughManual(value)) Debug.Log("METAHACKTHONE_PICO_PASSTHROUGH "+(value?"on":"off"));
            }
        }

        IEnumerator Start()
        {
            if(Application.isEditor) yield break;
            for(int attempt=0;attempt<50;attempt++)
            {
                if(PassthroughFeature.EnableSeeThroughManual(true))
                {
                    ready=true;
                    if(!wanted) PassthroughFeature.EnableSeeThroughManual(false);
                    Debug.Log("METAHACKTHONE_PICO_PASSTHROUGH_READY"+(wanted?"":" (paused)"));
                    yield break;
                }
                yield return new WaitForSeconds(.1f);
            }
            Debug.LogWarning("Pico passthrough unavailable; XR rendering continues.");
        }
    }
}
