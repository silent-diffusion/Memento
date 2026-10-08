namespace Memento.Core.Engines;

/// <summary>Looks processes up by id (a snapshot taken once per attribution).</summary>
public interface IProcessDirectory
{
    /// <summary>The process, or <c>null</c> when it has exited or cannot be read.</summary>
    ProcessEntry? Find(int pid);
}
