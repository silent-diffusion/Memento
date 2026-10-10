namespace Memento.Video.Encoding;

/// <summary>Media Foundation and codec-API keys that Vortice does not name (values from the Windows SDK headers).</summary>
internal static class MediaFoundationGuids
{
    public static readonly Guid VideoEncoderCategory = new("F79EAC7D-E545-4387-BDEE-D647D7BDE42A");
    public static readonly Guid VideoProcessorCategory = new("302EA3FC-AA5F-47F9-9F7A-C2188BB16302");

    /// <summary><c>MFT_ENUM_ADAPTER_LUID</c>: the graphics adapter a hardware MFT belongs to.</summary>
    public static readonly Guid MftEnumAdapterLuid = new("1D39518C-E220-4DA8-A07F-BA172552D6B1");

    /// <summary><c>MF_SA_D3D11_BINDFLAGS</c> and <c>MF_SA_D3D11_USAGE</c> for the sample allocator.</summary>
    public static readonly Guid SampleAllocatorBindFlags = new("EACF97AD-065C-4408-BEE3-FDCBFD128BE2");
    public static readonly Guid SampleAllocatorUsage = new("E85FE442-2CA3-486E-A9C7-109DDA609880");

    public static readonly Guid CodecApiRateControlMode = new("1C0608E9-370C-4710-8A58-CB6181C42423");
    public static readonly Guid CodecApiMeanBitRate = new("F7222374-2144-4815-B550-A37F8E12EE52");
    public static readonly Guid CodecApiMaxBitRate = new("9651EAE4-39B9-4EBF-85EF-D7F444EC7465");
    public static readonly Guid CodecApiGopSize = new("95F31B26-95A4-41AA-9303-246A7FC6EEF1");
    public static readonly Guid CodecApiBPictureCount = new("8D390AAC-DC5C-4200-B57F-814D04BABAB2");
    public static readonly Guid CodecApiLowLatencyMode = new("9C27891A-ED7A-40E1-88E8-B22727A024EE");

    /// <summary><c>eAVEncCommonRateControlMode_PeakConstrainedVBR</c>.</summary>
    public const uint RateControlPeakConstrainedVbr = 1;

    /// <summary><c>eAVEncH264VProfile_High</c>.</summary>
    public const uint H264ProfileHigh = 100;

    public const int MfESampleAllocatorEmpty = unchecked((int)0xC00D4A3E);
    public const int MfENoSampleTimestamp = unchecked((int)0xC00D36E8);
}
