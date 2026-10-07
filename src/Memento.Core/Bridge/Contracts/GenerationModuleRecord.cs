namespace Memento.Core.Bridge.Contracts;

/// <summary>One module of a generation record: claims found, verified, dropped, and whether it says "not discussed".</summary>
public sealed record GenerationModuleRecord(string ModuleId, int Claims, int Verified, int Dropped, bool NotDiscussed);
