using UnityEngine;

/// <summary>
/// Builds a UV resize/crop matrix and creates a resize material for a resize mapping between source and target textures.
/// </summary>
[ExecuteAlways]
public class ResizeTextureController
{
    private Material _resizeMaterial;
    private Texture _sourceTexture;

    private RenderTexture _targetTexture;

    private float _targetWidth;
    private float _targetHeight;
    
    private const string SHADER_NAME = "Hidden/LiveKit/Resize";

    private CropMode _cropMode;  // How to fit src → target

    public enum CropMode
    {
        FillCrop,   // Scale to fill, crop the excess (e.g. 16:9 → 1:1 crops sides)
        Letterbox,  // Scale to fit,  add black bars
    }

    public ResizeTextureController(Texture sourceTexture, float targetWidth, float targetHeight, CropMode cropMode = CropMode.Letterbox)
    {
        var shader = Shader.Find(SHADER_NAME);
        _resizeMaterial = new Material(shader);
        _sourceTexture = sourceTexture;
        _targetWidth = targetWidth;
        _targetHeight = targetHeight;
        _cropMode = cropMode;

        _targetTexture = new RenderTexture((int)_targetWidth, (int)_targetHeight, 0, RenderTextureFormat.ARGB32);

        float srcAspect = (float)_sourceTexture.width / _sourceTexture.height;
        float targetAspect = (float)_targetTexture.width / _targetTexture.height;
        Matrix4x4 m = BuildCropMatrix(srcAspect, targetAspect, _cropMode);
        _resizeMaterial.SetMatrix("_ResizeMatrix", m);
    }

    public RenderTexture GetTargetTexture()
    {
        return _targetTexture;
    }

    /// <summary>
    /// Resize the source texture into the target texture based on the controllers set crop mode.
    /// </summary>
    public void Resize()
    {        
        Graphics.Blit(_sourceTexture, _targetTexture, _resizeMaterial);
    }

    public void Dispose()
    {
        if (_targetTexture != null)
        {
            _targetTexture.Release();
            Object.Destroy(_targetTexture);
        }
    
        if (_resizeMaterial != null) 
            Object.Destroy(_resizeMaterial);
    }

    /// <summary>
    /// Returns a UV-space 4×4 matrix that maps [0,1]² destination UVs
    /// into the correct sub-region of the source texture.
    ///
    /// Convention: multiply as  uvTransformed = M * float4(uv, 0, 1)
    /// </summary>
    public static Matrix4x4 BuildCropMatrix(float srcAspect, float dstAspect,
                                             CropMode mode = CropMode.FillCrop)
    {
        float scaleX, scaleY, offsetX, offsetY;

        // Ratio of source aspect to destination aspect
        float ratio =  srcAspect / dstAspect;  // >1 → source is wider than target

        if (mode == CropMode.FillCrop)
        {
            if (ratio > 1f)
            {
                // Source is wider → crop left/right
                scaleX  = 1f / ratio;   // < 1 → narrows the UV window horizontally
                scaleY  = 1f;
                offsetX = (1f - scaleX) * 0.5f;
                offsetY = 0f;
            }
            else
            {
                // Source is taller → crop top/bottom
                scaleX  = 1f;
                scaleY  = ratio;        // < 1 → narrows the UV window vertically
                offsetX = 0f;
                offsetY = (1f - scaleY) * 0.5f;
            }
        }
        else // Letterbox
        {
            if (ratio > 1f)
            {
                // Source is wider → pillarbox
                scaleX  = 1f;
                scaleY  = ratio;
                offsetX = 0f;
                offsetY = (1f - scaleY) * 0.5f;
            }
            else
            {
                // Source is taller → letterbox
                scaleX  = 1f / ratio;
                scaleY  = 1f;
                offsetX = (1f - scaleX) * 0.5f;
                offsetY = 0f;
            }
        }

        // Build a 2D affine transform embedded in a 4×4 matrix:
        //  | scaleX   0      0  offsetX |
        //  |   0    scaleY   0  offsetY |
        //  |   0      0      1     0    |
        //  |   0      0      0     1    |
        var m = Matrix4x4.identity;
        m.m00 = scaleX;
        m.m11 = scaleY;
        m.m03 = offsetX;
        m.m13 = offsetY;
        return m;
    }
}