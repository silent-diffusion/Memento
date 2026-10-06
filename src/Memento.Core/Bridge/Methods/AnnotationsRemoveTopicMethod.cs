using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>annotations.removeTopic</c>.</summary>
public sealed class AnnotationsRemoveTopicMethod(ProjectService projects) : BridgeMethod<TopicIdParams, TopicsResult>
{
    public override string Name => BridgeMethodNames.AnnotationsRemoveTopic;

    public override JsonTypeInfo<TopicIdParams> ParamsTypeInfo => BridgeJsonContext.Default.TopicIdParams;

    public override JsonTypeInfo<TopicsResult> ResultTypeInfo => BridgeJsonContext.Default.TopicsResult;

    public override async Task<TopicsResult> InvokeAsync(TopicIdParams parameters, CancellationToken cancellationToken)
    {
        return new TopicsResult(await projects.RemoveTopicAsync(parameters.RecordingId, parameters.TopicId, cancellationToken));
    }
}
