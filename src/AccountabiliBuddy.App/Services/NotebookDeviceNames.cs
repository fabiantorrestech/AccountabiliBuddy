namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The last known name of each other device, by id, for the Archive's "from &lt;device&gt;" on a note (AR1b, ADR 87). Sources, the first that
/// knows an id wins: the devices of the group now (their file cards), the names the received task histories carry, then the removal list
/// (a removed device's notes stay on the others, and its name is remembered there). An id no source knows reads "another device".
/// </summary>
public static class NotebookDeviceNames
{
    public static Dictionary<Guid, string> Of(
        IEnumerable<(Guid Id, string Name)> current, IEnumerable<(Guid Id, string Name)> fromHistory, IEnumerable<(Guid Id, string Name)> removed)
    {
        return current.Concat(fromHistory).Concat(removed)
            .Where(device => !string.IsNullOrWhiteSpace(device.Name))
            .GroupBy(device => device.Id)
            .ToDictionary(group => group.Key, group => group.First().Name);
    }
}
