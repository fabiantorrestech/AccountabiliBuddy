namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>A sync method that can carry the rekey file (ADR 54) to the devices that stay: the folder, Drive and the relay.</summary>
public interface IRekeyPublisher
{
    /// <summary>
    /// Writes the rekey file where this method keeps the group's files, under the OLD key's group (call before the key
    /// is switched).
    /// </summary>
    /// <param name="sealedBytes">The rekey file, sealed with the old key.</param>
    /// <param name="fileName">Its name, from <c>RekeyCodec.FileNameFor</c>.</param>
    /// <returns>Null when this method isn't in use; true when the file was written; false when writing failed.</returns>
    Task<bool?> PublishRekeyAsync(byte[] sealedBytes, string fileName);

    /// <summary>
    /// The rekey files this method holds under any of <paramref name="fileNames"/> (the names other devices would write, from
    /// <c>RekeyCodec.FileNameFor</c>), to find a removal made at the same time. Empty when none, or the method isn't in use or can't be reached.
    /// </summary>
    Task<IReadOnlyList<byte[]>> FindRekeysAsync(IReadOnlyCollection<string> fileNames);
}
