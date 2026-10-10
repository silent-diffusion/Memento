namespace Memento.Video.Encoding;

/// <summary>An H.264 encoder Media Foundation offers.</summary>
/// <param name="Name">Its friendly name ("NVIDIA H.264 Encoder MFT", "H264 Encoder MFT").</param>
/// <param name="IsHardware">On a graphics card (NVENC, Quick Sync, VCN/VCE).</param>
/// <param name="AdapterLuid">The adapter it belongs to (hardware only).</param>
/// <param name="VendorId">"VEN_10DE" and the like (hardware only).</param>
public sealed record H264EncoderInfo(string Name, bool IsHardware, long? AdapterLuid, string? VendorId);
