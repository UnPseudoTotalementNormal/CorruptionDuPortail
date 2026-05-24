using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Produit une version floutée (gaussien) du rendu de la caméra Base — qui contient
/// déjà 3D + canvases World Space (cartes) — dans la texture globale _BackgroundBlurSource.
/// Le shader Custom/UI_Blur (sur le voile) l'échantillonne directement.
///
/// Pipeline : downsample → gaussien séparable H/V (itéré) → RT persistante basse-réso.
/// Le downsample + l'upscale bilinéaire + le gaussien donnent un vrai flou doux, pas
/// une simple copie. Régler avec downsample / iterations / spread.
///
/// Ne tourne qu'en Game / Scene View. Purement additive : n'altère pas l'image affichée.
/// Le voile (InstructionCanvas) est en Screen Space - Overlay → composité APRÈS cette
/// capture → jamais dans _BackgroundBlurSource → aucun feedback.
/// </summary>
[DisallowMultipleRendererFeature]
public class BackgroundCaptureFeature : ScriptableRendererFeature
{
    internal static readonly int BlurSourceId = Shader.PropertyToID("_BackgroundBlurSource");
    static readonly int SpreadId = Shader.PropertyToID("_Spread");
    const string k_TexName = "_BackgroundBlurSource";

    [Tooltip("Diviseur de résolution du flou. Plus haut = plus flou et moins cher.")]
    [SerializeField, Range(1, 8)] int downsample = 4;

    [Tooltip("Nombre de passes gaussiennes H+V. Plus = plus lisse/large.")]
    [SerializeField, Range(1, 6)] int iterations = 2;

    [Tooltip("Écart entre les taps du gaussien.")]
    [SerializeField, Range(0.5f, 4f)] float spread = 1.5f;

    Material blurMaterial;
    BackgroundCapturePass pass;

    public override void Create()
    {
        var shader = Shader.Find("Hidden/BackgroundBlur");
        if (shader != null)
            blurMaterial = CoreUtils.CreateEngineMaterial(shader);

        pass = new BackgroundCapturePass
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents
        };

        // Force une texture couleur intermédiaire (sinon backbuffer non échantillonnable
        // en Deferred + IntermediateTextureMode.Auto).
        pass.ConfigureInput(ScriptableRenderPassInput.Color);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var camType = renderingData.cameraData.cameraType;
        if (camType != CameraType.Game && camType != CameraType.SceneView)
            return;
        if (blurMaterial == null)
            return;

        pass.Setup(blurMaterial, Mathf.Max(1, downsample), Mathf.Max(1, iterations), Mathf.Max(0.5f, spread));
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        pass?.Cleanup();
        CoreUtils.Destroy(blurMaterial);
        blurMaterial = null;
    }

    // -------------------------------------------------------------------------

    class BackgroundCapturePass : ScriptableRenderPass
    {
        RTHandle captureRT;
        Material blur;
        int downsample;
        int iterations;
        float spread;

        public void Setup(Material material, int downsample, int iterations, float spread)
        {
            this.blur = material;
            this.downsample = downsample;
            this.iterations = iterations;
            this.spread = spread;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData   = frameData.Get<UniversalCameraData>();

            if (resourceData.isActiveTargetBackBuffer)
                return;

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid() || blur == null)
                return;

            var camDesc = cameraData.cameraTargetDescriptor;
            int w = Mathf.Max(1, camDesc.width  / downsample);
            int h = Mathf.Max(1, camDesc.height / downsample);

            // RT finale persistante (basse réso) exposée en globale. Persistante = survit
            // entre frames, valide quand l'UI Overlay l'échantillonne.
            var finalDesc = camDesc;
            finalDesc.width           = w;
            finalDesc.height          = h;
            finalDesc.depthBufferBits = 0;
            finalDesc.msaaSamples     = 1;
            RenderingUtils.ReAllocateHandleIfNeeded(ref captureRT, finalDesc, FilterMode.Bilinear,
                TextureWrapMode.Clamp, name: k_TexName);
            Shader.SetGlobalTexture(BlurSourceId, captureRT);

            blur.SetFloat(SpreadId, spread);

            // Buffers transitoires (ping-pong) à la même résolution.
            TextureDesc td = renderGraph.GetTextureDesc(source);
            td.width           = w;
            td.height          = h;
            td.depthBufferBits = DepthBits.None;
            td.msaaSamples     = MSAASamples.None;
            td.clearBuffer     = false;
            td.filterMode      = FilterMode.Bilinear;
            td.wrapMode        = TextureWrapMode.Clamp;
            td.name = "BlurPing";
            TextureHandle a = renderGraph.CreateTexture(td);
            td.name = "BlurPong";
            TextureHandle b = renderGraph.CreateTexture(td);

            TextureHandle dst = renderGraph.ImportTexture(captureRT);

            // Downsample du color buffer dans a.
            renderGraph.AddBlitPass(source, a, Vector2.one, Vector2.zero, passName: "Blur Downsample");

            // Gaussien séparable itéré : H (a->b) puis V (b->a), dernière V vers dst.
            for (int i = 0; i < iterations; i++)
            {
                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(a, b, blur, 0), "Blur H");

                bool last = i == iterations - 1;
                TextureHandle vDest = last ? dst : a;
                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(b, vDest, blur, 1), "Blur V");
            }
        }

        public void Cleanup()
        {
            captureRT?.Release();
            captureRT = null;
        }
    }
}
