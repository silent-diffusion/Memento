using Memento.Core.Models;

namespace Memento.Core.Tests.Fakes;

/// <summary>A catalog with the real ids and 4-byte files, so tests can "install" a model by writing it.</summary>
internal static class TestCatalogs
{
    // SHA-256 of the four zero bytes Install writes.
    private const string Zero = "df3f619804a92fdb4057192dc43dd748ea778adc52bc498ce80524c014b81119";

    public static readonly ModelCatalog Tiny = ModelCatalog.Parse($$"""
        { "schemaVersion": 1, "models": [
          { "id": "whisper-large-v3-turbo", "engine": "whisper", "kind": "transcription", "name": "Large v3 Turbo", "description": "d", "fileName": "ggml-large-v3-turbo.bin", "sizeBytes": 4, "sha256": "{{Zero}}", "url": "https://huggingface.co/t", "license": "MIT", "runsOn": "gpu", "minVramBytes": 2684354560, "recommendedFor": "gpu", "accuracyNote": "Most accurate" },
          { "id": "whisper-medium", "engine": "whisper", "kind": "transcription", "name": "Medium", "description": "d", "fileName": "ggml-medium.bin", "sizeBytes": 4, "sha256": "{{Zero}}", "url": "https://huggingface.co/m", "license": "MIT", "runsOn": "gpu", "minVramBytes": 2147483648, "accuracyNote": "Very accurate" },
          { "id": "whisper-small", "engine": "whisper", "kind": "transcription", "name": "Small", "description": "d", "fileName": "ggml-small.bin", "sizeBytes": 4, "sha256": "{{Zero}}", "url": "https://huggingface.co/s", "license": "MIT", "runsOn": "either", "minVramBytes": 1073741824, "recommendedFor": "cpu", "accuracyNote": "Fast on CPU" },
          { "id": "pyannote-segmentation-3-0", "engine": "sherpa-onnx", "kind": "speakers", "role": "segmentation", "name": "Segmentation", "description": "d", "fileName": "seg.onnx", "sizeBytes": 4, "sha256": "{{Zero}}", "url": "https://huggingface.co/p", "license": "MIT", "runsOn": "cpu", "recommendedFor": "any", "accuracyNote": "Required" },
          { "id": "nemo-titanet-small", "engine": "sherpa-onnx", "kind": "speakers", "role": "embedding", "name": "TitaNet", "description": "d", "fileName": "emb.onnx", "sizeBytes": 4, "sha256": "{{Zero}}", "url": "https://huggingface.co/e", "license": "CC-BY-4.0", "runsOn": "cpu", "recommendedFor": "any", "accuracyNote": "Most accurate" }
        ] }
        """);

    /// <summary>Writes the model files and verifies them, so the host's model manager sees them as installed.</summary>
    public static void Install(BridgeTestHost host, params string[] ids)
    {
        foreach (var id in ids)
        {
            var entry = Tiny.Find(id)!;
            var path = host.Models.PathOf(entry);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[entry.SizeBytes]);
            if (!host.Models.VerifyAsync(id, CancellationToken.None).GetAwaiter().GetResult())
            {
                throw new InvalidOperationException($"The test model {id} did not verify.");
            }
        }
    }
}
