namespace StadiaPass.Domain.Common;

public abstract class Entity(Guid id) : IEquatable<Entity>
{
    protected Entity() : this(Guid.CreateVersion7())
    {
    }

    public Guid Id { get; private init; } = id;

    public bool Equals(Entity? other) =>
        other is not null && other.GetType() == GetType() && other.Id == Id;

    public override bool Equals(object? obj) => obj is Entity entity && Equals(entity);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
