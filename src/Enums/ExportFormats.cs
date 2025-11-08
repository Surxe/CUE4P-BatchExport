namespace BatchExport.Enums
{
    public enum ELodFormat
    {
        FirstLod,
        AllLods
    }

    public enum EMeshFormat 
    {
        ActorX,
        UEFormat,
        GLB,
        ASCII_STL,
        BINARY_STL
    }

    public enum ENaniteMeshFormat
    {
        OnlyNaniteLOD,
        OnlyInterimLOD,
        BothLODs
    }

    public enum EAnimFormat
    {
        ActorX,
        UEFormat
    }

    public enum EPoseFormat
    {
        UEFormat
    }

    public enum EMaterialFormat
    {
        AllLayersNoRef,
        FirstLayer
    }

    public enum ETextureFormat
    {
        Png,
        Jpeg,
        Tga,
        Bmp,
        Dds,
        Hdr
    }

    public enum EFileCompressionFormat
    {
        None,
        Zip,
        SevenZip
    }

    public enum ESocketFormat
    {
        None,
        Bone
    }
}