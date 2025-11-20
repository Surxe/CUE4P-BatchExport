using CUE4Parse.UE4.Versions;
using BatchExport.Enums;
using CUE4Parse.UE4.Assets.Exports.Texture;

namespace BatchExport
{
    public struct ExporterOptions
    {
        public ELodFormat LodFormat { get; set; }
        public EMeshFormat MeshFormat { get; set; }
        public ENaniteMeshFormat NaniteMeshFormat { get; set; }
        public EAnimFormat AnimFormat { get; set; }
        public EMaterialFormat MaterialFormat { get; set; }
        public ETextureFormat TextureFormat { get; set; }
        public EFileCompressionFormat CompressionFormat { get; set; }
        public ETexturePlatform Platform { get; set; }
        public ESocketFormat SocketFormat { get; set; }
        public bool ExportMorphTargets { get; set; }
        public bool ExportMaterials { get; set; }
        public bool ExportHdrTexturesAsHdr { get; set; }
        public bool ExportSVG { get; set; }

        public ExporterOptions()
        {
            LodFormat = ELodFormat.FirstLod;
            MeshFormat = EMeshFormat.UEFormat;
            NaniteMeshFormat = ENaniteMeshFormat.OnlyNaniteLOD;
            AnimFormat = EAnimFormat.UEFormat;
            MaterialFormat = EMaterialFormat.AllLayersNoRef;
            TextureFormat = ETextureFormat.Png;
            CompressionFormat = EFileCompressionFormat.None;
            Platform = ETexturePlatform.DesktopMobile;
            SocketFormat = ESocketFormat.Bone;
            ExportMorphTargets = true;
            ExportMaterials = true;
            ExportHdrTexturesAsHdr = true;
            ExportSVG = false;
        }
    }
}