using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/Custom Deferred Render Pipeline")]
public class CustomDeferredRenderPipelineAsset : RenderPipelineAsset
{
    public Cubemap diffuseIBL;
    public Cubemap specularIBL;
    public Texture brdfLut;
    
    protected override RenderPipeline CreatePipeline()
    {
        CustomDeferredRenderPipeline dfpipeline= new CustomDeferredRenderPipeline();

        dfpipeline.diffuseIBL = diffuseIBL;
        dfpipeline.specularIBL = specularIBL;
        dfpipeline.brdfLut = brdfLut;
        
        return dfpipeline;
    }    
}
