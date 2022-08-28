using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using UnityEditor;
using UnityEngine.Profiling;

public partial class DeferredCameraRenderer
{

    ScriptableRenderContext context;
    Camera camera;
    const string bufferName = "Render Camera";
    CommandBuffer buffer = new CommandBuffer
    {
        name = bufferName
    };
    CullingResults cullingResults;
    static ShaderTagId shaderTagId = new ShaderTagId("gbuffer");

    RenderTexture gdepth;                                               // depth attachment
    RenderTexture[] gbuffers = new RenderTexture[4];                    // color attachments 
    RenderTargetIdentifier gdepthID; 
    RenderTargetIdentifier[] gbufferID = new RenderTargetIdentifier[4]; // tex ID 

    // IBL 贴图
    public Cubemap diffuseIBL;
    public Cubemap specularIBL;
    public Texture brdfLut;

    public DeferredCameraRenderer(){
        QualitySettings.vSyncCount = 0;     // 关闭垂直同步
        Application.targetFrameRate = 60;   // 帧率

        gdepth  = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.Depth, RenderTextureReadWrite.Linear);
        gbuffers[0] = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        gbuffers[1] = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB2101010, RenderTextureReadWrite.Linear);
        gbuffers[2] = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB64, RenderTextureReadWrite.Linear);
        gbuffers[3] = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);

        gdepthID = gdepth;
        for(int i=0; i<4; i++)
            gbufferID[i] = gbuffers[i];
    }

    public void Render(ScriptableRenderContext context, Camera camera)
    {
        this.context = context;
        this.camera = camera;

        //  gbuffer 
        Shader.SetGlobalTexture("_gdepth", gdepth);
        for(int i=0; i<4; i++) 
            Shader.SetGlobalTexture("_GT"+i, gbuffers[i]);

        PrepareBuffer();
        PrepareForSceneWindow();
        if (!Cull())
        {
            return;
        }

        GbufferPass(context, camera);
        LightPass(context,camera);
        // Setup();
        // DrawVisibleGeometry();
        // DrawUnsupportedShaders();
        DrawGizmos();
        Submit();
    }

    void DrawVisibleGeometry()
    {
        var sortingSettings = new SortingSettings(camera);
        var drawingSettings = new DrawingSettings(shaderTagId, sortingSettings);
        // var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
        var filteringSettings = FilteringSettings.defaultValue;

        context.DrawRenderers(
            cullingResults, ref drawingSettings, ref filteringSettings
        );

        context.DrawSkybox(camera);

        sortingSettings.criteria = SortingCriteria.CommonTransparent;
        drawingSettings.sortingSettings = sortingSettings;
        filteringSettings.renderQueueRange = RenderQueueRange.transparent;

        context.DrawRenderers(
            cullingResults, ref drawingSettings, ref filteringSettings
        );
    }
    
    void GbufferPass(ScriptableRenderContext context, Camera camera)
    {
        Profiler.BeginSample("gbufferDraw");

        context.SetupCameraProperties(camera);
        CommandBuffer cmd = new CommandBuffer();
        cmd.name = "gbuffer";
        
        // 清屏
        cmd.SetRenderTarget(gbufferID, gdepth);
        cmd.ClearRenderTarget(true, true, Color.clear);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        // 剔除
        camera.TryGetCullingParameters(out var cullingParameters);
        var cullingResults = context.Cull(ref cullingParameters);

        // config settings
        ShaderTagId shaderTagId = new ShaderTagId("gbuffer");   // 使用 LightMode 为 gbuffer 的 shader
        SortingSettings sortingSettings = new SortingSettings(camera);
        DrawingSettings drawingSettings = new DrawingSettings(shaderTagId, sortingSettings);
        FilteringSettings filteringSettings = FilteringSettings.defaultValue;

        // 绘制一般几何体
        context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
        context.Submit();

        Profiler.EndSample();
    }
    void LightPass(ScriptableRenderContext context, Camera camera)
    {// 光照 Pass : 计算 PBR 光照并且存储到 lightPassTex 纹理
        // 使用 Blit
        CommandBuffer cmd = new CommandBuffer();
        cmd.name = "lightpass";

        Material mat = new Material(Shader.Find("Custom RP/lightpass"));
        cmd.Blit(gbufferID[0], BuiltinRenderTextureType.CameraTarget, mat);
        context.ExecuteCommandBuffer(cmd);
    }

    void Setup()
    {
        context.SetupCameraProperties(camera);
        // CameraClearFlags flags = camera.clearFlags;
        // buffer.ClearRenderTarget(
        //     flags <= CameraClearFlags.Depth,
        //     flags == CameraClearFlags.Color,
        //     flags == CameraClearFlags.Color ?
		// 		camera.backgroundColor.linear : Color.clear
        // );
        buffer.ClearRenderTarget(true, true, Color.clear);
        buffer.BeginSample(SampleName);
        ExecuteBuffer();
    }
    
    void Submit()
    {
        buffer.EndSample(SampleName);
        ExecuteBuffer();
        context.Submit();
    }
    void ExecuteBuffer()
    {
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }
    bool Cull()
    {
        if (camera.TryGetCullingParameters(out ScriptableCullingParameters p))
        {
            cullingResults = context.Cull(ref p);
            return true;
        }
        return false;
    }
}