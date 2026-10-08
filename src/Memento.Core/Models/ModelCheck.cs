namespace Memento.Core.Models;

/// <summary>What <see cref="IModelManager.VerifyAsync"/> found for an installed model.</summary>
public enum ModelCheck
{
    /// <summary>The file matches the catalog's SHA-256.</summary>
    Verified,

    /// <summary>The model is unknown or not installed.</summary>
    NotInstalled,

    /// <summary>The file did not match; it was set aside as <c>&lt;file&gt;.corrupt-&lt;time&gt;</c> and the model reads as not installed.</summary>
    Damaged,
}
