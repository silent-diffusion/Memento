using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>annotations.addTopic</c>. Adding a label that already exists changes nothing.</summary>
public sealed class AnnotationsAddTopicMethod(ProjectService projects) : BridgeMethod<TopicParams, TopicsResult>
{
    public override string Name => BridgeMethodNames.AnnotationsAddTopic;

    public override JsonTypeInfo<TopicParams> ParamsTypeInfo => BridgeJsonContext.Default.TopicParams;

    public override JsonTypeInfo<TopicsResult> ResultTypeInfo => BridgeJsonContext.Default.TopicsResult;

    public override async Task<TopicsResult> InvokeAsync(TopicParams parameters, CancellationToken cancellationToken)
    {
        return new TopicsResult(await projects.AddTopicAsync(parameters.RecordingId, parameters.Topic, cancellationToken));
    }
}
