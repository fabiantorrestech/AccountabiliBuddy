using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.Core.Ports;

/// <summary>Loads and saves the user's configuration (profiles, schedule, settings).</summary>
public interface IProfileStore
{
    /// <returns>The saved document, or <see cref="ProfileDocument.CreateDefault"/> if nothing is saved yet.</returns>
    /// <exception cref="UnsupportedSchemaVersionException">Saved by a newer app version.</exception>
    Task<ProfileDocument> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(ProfileDocument document, CancellationToken cancellationToken = default);
}
