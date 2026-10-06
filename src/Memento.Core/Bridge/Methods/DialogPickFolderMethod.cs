using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>dialog.pickFolder</c>: the Windows folder picker. The chosen path is the only path the UI receives.</summary>
public sealed class DialogPickFolderMethod(IFolderPicker picker) : BridgeMethod<PickFolderParams, PickFolderResult>
{
    public override string Name => BridgeMethodNames.DialogPickFolder;

    public override JsonTypeInfo<PickFolderParams> ParamsTypeInfo => BridgeJsonContext.Default.PickFolderParams;

    public override JsonTypeInfo<PickFolderResult> ResultTypeInfo => BridgeJsonContext.Default.PickFolderResult;

    public override async Task<PickFolderResult> InvokeAsync(PickFolderParams parameters, CancellationToken cancellationToken)
    {
        var title = string.IsNullOrWhiteSpace(parameters.Title) ? "Choose a folder" : parameters.Title.Trim();
        if (title.Length > 200)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidParams, "A folder picker title can be at most 200 characters.");
        }

        var initial = parameters.InitialPath is { } path && Path.IsPathFullyQualified(path) ? path : null;
        return new PickFolderResult(await picker.PickAsync(title, initial, cancellationToken));
    }
}
