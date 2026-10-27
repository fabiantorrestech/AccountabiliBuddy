namespace AccountabiliBuddy.Relay;

/// <summary>What a bearer token's hash means for a group: nothing stored yet, the owner, or someone else.</summary>
internal enum GroupAccess
{
    NoGroup,
    Owner,
    NotOwner,
}
