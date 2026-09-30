using UnityEngine;

namespace GIS
{

    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshRenderer))]
    public class BuildingFacadeOverride : MonoBehaviour
    {
        public bool hasWindows = true;

        public bool hasDoor = true;

        static readonly int s_WindowsEnabled = Shader.PropertyToID("_WindowsEnabled");
        static readonly int s_DoorEnabled    = Shader.PropertyToID("_DoorEnabled");

        MaterialPropertyBlock _mpb;

        void OnEnable()  { Apply(); }
        void OnValidate(){ Apply(); }

        void Apply()
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr == null) return;

            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            mr.GetPropertyBlock(_mpb);
            _mpb.SetFloat(s_WindowsEnabled, hasWindows ? 1f : 0f);
            _mpb.SetFloat(s_DoorEnabled,    hasDoor    ? 1f : 0f);
            mr.SetPropertyBlock(_mpb);
        }

        void OnDestroy()
        {

            var mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.SetPropertyBlock(null);
        }
    }
}
