using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor.Experimental.VFX;
using UnityEngine.Experimental.VFX;

namespace UnityEditor.VFX
{
    abstract class VFXAbstractParticleOutput : VFXContext
    {
        public enum BlendMode
        {
            Additive,
            Alpha,
            Masked,
            AlphaPremultiplied,
            Opaque,
        }

        public enum FlipbookMode
        {
            Off,
            Flipbook,
            FlipbookBlend,
        }

        public enum ZWriteMode
        {
            Default,
            Off,
            On
        }
        public enum CullMode
        {
            Default,
            Front,
            Back,
            Off
        }

        public enum ZTestMode
        {
            Default,
            Less,
            Greater,
            LEqual,
            GEqual,
            Equal,
            NotEqual,
            Always
        }

        public enum SortMode
        {
            Auto,
            Off,
            On
        }

        [VFXSetting, SerializeField, Header("Render States")]
        protected BlendMode blendMode = BlendMode.Alpha;

        [VFXSetting(VFXSettingAttribute.VisibleFlags.InInspector), SerializeField]
        protected CullMode cullMode = CullMode.Default;

        [VFXSetting(VFXSettingAttribute.VisibleFlags.InInspector), SerializeField]
        protected ZWriteMode zWriteMode = ZWriteMode.Default;

        [VFXSetting(VFXSettingAttribute.VisibleFlags.InInspector), SerializeField]
        protected ZTestMode zTestMode = ZTestMode.Default;

        [VFXSetting(VFXSettingAttribute.VisibleFlags.InInspector), SerializeField, Header("Particle Options")]
        protected bool uvScaleAndBias = false;

        [VFXSetting, SerializeField]
        protected FlipbookMode flipbookMode;

        [VFXSetting, SerializeField]
        protected bool useSoftParticle = false;

        [VFXSetting(VFXSettingAttribute.VisibleFlags.InInspector), SerializeField, Header("Rendering Options")]
        protected int sortPriority = 0;

        [VFXSetting(VFXSettingAttribute.VisibleFlags.InInspector), SerializeField]
        protected SortMode sort = SortMode.Auto;

        [VFXSetting(VFXSettingAttribute.VisibleFlags.InInspector), SerializeField]
        protected bool indirectDraw = false;

        [VFXSetting(VFXSettingAttribute.VisibleFlags.InInspector), SerializeField]
        protected bool preRefraction = false;

        public bool HasIndirectDraw()   { return indirectDraw || HasSorting(); }
        public bool HasSorting()        { return sort == SortMode.On || (sort == SortMode.Auto && (blendMode == BlendMode.Alpha || blendMode == BlendMode.AlphaPremultiplied)); }
        public bool NeedsDeadListCount() { return HasIndirectDraw() && (taskType == VFXTaskType.ParticleQuadOutput || taskType == VFXTaskType.ParticleHexahedronOutput); } // Should take the capacity into account to avoid false positive

        protected VFXAbstractParticleOutput() : base(VFXContextType.kOutput, VFXDataType.kParticle, VFXDataType.kNone) {}

        public override bool codeGeneratorCompute { get { return false; } }

        public virtual bool supportsFlipbooks { get { return false; } }
        public virtual bool supportsUVScaleBias { get { return false; } }

        public virtual CullMode defaultCullMode { get { return CullMode.Off; } }
        public virtual ZTestMode defaultZTestMode { get { return ZTestMode.LEqual; } }


        public virtual bool supportSoftParticles { get { return useSoftParticle && (blendMode != BlendMode.Opaque && blendMode != BlendMode.Masked); } }

        protected virtual IEnumerable<VFXNamedExpression> CollectGPUExpressions(IEnumerable<VFXNamedExpression> slotExpressions)
        {
            if (blendMode == BlendMode.Masked)
                yield return slotExpressions.First(o => o.name == "alphaThreshold");

            if (supportSoftParticles)
            {
                var softParticleFade = slotExpressions.First(o => o.name == "softParticlesFadeDistance");
                var invSoftParticleFade = (VFXValue.Constant(1.0f) / softParticleFade.exp);
                yield return new VFXNamedExpression(invSoftParticleFade, "invSoftParticlesFadeDistance");
            }

            if (flipbookMode != FlipbookMode.Off)
            {
                var flipBookSizeExp = slotExpressions.First(o => o.name == "flipBookSize");
                yield return flipBookSizeExp;
                yield return new VFXNamedExpression(VFXValue.Constant(Vector2.one) / flipBookSizeExp.exp, "invFlipBookSize");
            }

            if (supportsUVScaleBias && uvScaleAndBias)
            {
                yield return slotExpressions.First(o => o.name == "uvScale");
                yield return slotExpressions.First(o => o.name == "uvBias");
            }
        }

        public override VFXExpressionMapper GetExpressionMapper(VFXDeviceTarget target)
        {
            if (target == VFXDeviceTarget.GPU)
            {
                var gpuMapper = VFXExpressionMapper.FromBlocks(activeChildrenWithImplicit);
                gpuMapper.AddExpressions(CollectGPUExpressions(GetExpressionsFromSlots(this)), -1);
                return gpuMapper;
            }
            return new VFXExpressionMapper();
        }

        protected override IEnumerable<VFXPropertyWithValue> inputProperties
        {
            get
            {
                foreach (var property in PropertiesFromType(GetInputPropertiesTypeName()))
                    yield return property;

                if (flipbookMode != FlipbookMode.Off)
                    yield return new VFXPropertyWithValue(new VFXProperty(typeof(Vector2), "flipBookSize"), new Vector2(4, 4));
                if (blendMode == BlendMode.Masked)
                    yield return new VFXPropertyWithValue(new VFXProperty(typeof(float), "alphaThreshold", VFXPropertyAttribute.Create(new RangeAttribute(0.0f, 1.0f))), 0.5f);
                if (supportSoftParticles)
                    yield return new VFXPropertyWithValue(new VFXProperty(typeof(float), "softParticlesFadeDistance", VFXPropertyAttribute.Create(new MinAttribute(0.0f))), 1.0f);
                if (supportsUVScaleBias && uvScaleAndBias)
                {
                    yield return new VFXPropertyWithValue(new VFXProperty(typeof(Vector2), "uvScale"), Vector2.one);
                    yield return new VFXPropertyWithValue(new VFXProperty(typeof(Vector2), "uvBias"), Vector2.zero);
                }
            }
        }

        public override IEnumerable<string> additionalDefines
        {
            get
            {
                if (blendMode == BlendMode.Opaque || blendMode == BlendMode.Masked)
                    yield return "IS_OPAQUE_PARTICLE";
                if (blendMode == BlendMode.Masked)
                    yield return "USE_ALPHA_TEST";
                if (supportSoftParticles)
                    yield return "USE_SOFT_PARTICLE";

                VisualEffectResource asset = GetResource();
                if (asset != null)
                {
                    var settings = asset.rendererSettings;
                    if (settings.motionVectorGenerationMode == MotionVectorGenerationMode.Object)
                        yield return "USE_MOTION_VECTORS_PASS";
                    if (settings.shadowCastingMode != ShadowCastingMode.Off)
                        yield return "USE_CAST_SHADOWS_PASS";
                }

                if (HasIndirectDraw())
                    yield return "VFX_HAS_INDIRECT_DRAW";

                if (uvScaleAndBias && supportsUVScaleBias)
                    yield return "USE_UV_SCALE_BIAS";

                if (flipbookMode != FlipbookMode.Off)
                {
                    yield return "USE_FLIPBOOK";
                    if (flipbookMode == FlipbookMode.FlipbookBlend)
                        yield return "USE_FLIPBOOK_INTERPOLATION";
                }

                if (NeedsDeadListCount())
                    yield return "USE_DEAD_LIST_COUNT";
            }
        }

        protected override IEnumerable<string> filteredOutSettings
        {
            get
            {
                if (!supportsFlipbooks)
                    yield return "flipbookMode";

                if (!supportsUVScaleBias)
                    yield return "uvMode";

                if (blendMode == BlendMode.Masked || blendMode == BlendMode.Opaque)
                    yield return "preRefraction";

                if (blendMode == BlendMode.Opaque || blendMode == BlendMode.Masked)
                    yield return "useSoftParticle";
            }
        }

        public override IEnumerable<KeyValuePair<string, VFXShaderWriter>> additionalReplacements
        {
            get
            {
                yield return new KeyValuePair<string, VFXShaderWriter>("${VFXOutputRenderState}", renderState);

                var shaderTags = new VFXShaderWriter();
                if (blendMode == BlendMode.Opaque)
                    shaderTags.Write("Tags { \"Queue\"=\"Geometry\" \"IgnoreProjector\"=\"False\" \"RenderType\"=\"Opaque\" }");
                else if (blendMode == BlendMode.Masked)
                    shaderTags.Write("Tags { \"Queue\"=\"AlphaTest\" \"IgnoreProjector\"=\"False\" \"RenderType\"=\"Opaque\" }");
                else
                {
                    string queueName = preRefraction ? "Geometry+750" : "Transparent"; // TODO Geometry + 750 is currently hardcoded value from HDRP...
                    shaderTags.Write(string.Format("Tags {{ \"Queue\"=\"{0}\" \"IgnoreProjector\"=\"True\" \"RenderType\"=\"Transparent\" }}", queueName));
                }

                yield return new KeyValuePair<string, VFXShaderWriter>("${VFXShaderTags}", shaderTags);
            }
        }

        protected virtual VFXShaderWriter renderState
        {
            get
            {
                var rs = new VFXShaderWriter();

                if (blendMode == BlendMode.Additive)
                    rs.WriteLine("Blend SrcAlpha One");
                else if (blendMode == BlendMode.Alpha)
                    rs.WriteLine("Blend SrcAlpha OneMinusSrcAlpha");
                else if (blendMode == BlendMode.AlphaPremultiplied)
                    rs.WriteLine("Blend One OneMinusSrcAlpha");

                var zTest = zTestMode;
                if (zTest == ZTestMode.Default)
                    zTest = defaultZTestMode;

                switch (zTest)
                {
                    case ZTestMode.Default: rs.WriteLine("ZTest LEqual"); break;
                    case ZTestMode.Always: rs.WriteLine("ZTest Always"); break;
                    case ZTestMode.Equal: rs.WriteLine("ZTest Equal"); break;
                    case ZTestMode.GEqual: rs.WriteLine("ZTest GEqual"); break;
                    case ZTestMode.Greater: rs.WriteLine("ZTest Greater"); break;
                    case ZTestMode.LEqual: rs.WriteLine("ZTest LEqual"); break;
                    case ZTestMode.Less: rs.WriteLine("ZTest Less"); break;
                    case ZTestMode.NotEqual: rs.WriteLine("ZTest NotEqual"); break;
                }

                switch (zWriteMode)
                {
                    case ZWriteMode.Default:
                        if (blendMode == BlendMode.Masked || blendMode == BlendMode.Opaque)
                            rs.WriteLine("ZWrite On");
                        else
                            rs.WriteLine("ZWrite Off");
                        break;
                    case ZWriteMode.On: rs.WriteLine("ZWrite On"); break;
                    case ZWriteMode.Off: rs.WriteLine("ZWrite Off"); break;
                }

                var cull = cullMode;
                if (cull == CullMode.Default)
                    cull = defaultCullMode;

                switch (cull)
                {
                    case CullMode.Default: rs.WriteLine("Cull Off"); break;
                    case CullMode.Front: rs.WriteLine("Cull Front"); break;
                    case CullMode.Back: rs.WriteLine("Cull Back"); break;
                    case CullMode.Off: rs.WriteLine("Cull Off"); break;
                }

                return rs;
            }
        }


        public override IEnumerable<VFXMapping> additionalMappings
        {
            get
            {
                yield return new VFXMapping("sortPriority", sortPriority);
                if (HasIndirectDraw())
                    yield return new VFXMapping("indirectDraw", 1);
            }
        }
    }
}
