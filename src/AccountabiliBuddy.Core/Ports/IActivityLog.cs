using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Ports;

/// <summary>Local usage log and label store. Feeds V2 suggestions and classifier training.</summary>
public interface IActivityLog
{
    Task RecordSegmentAsync(ActivitySegment segment, CancellationToken cancellationToken = default);

    Task RecordLabelAsync(ActivityLabel label, CancellationToken cancellationToken = default);
}
