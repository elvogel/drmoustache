namespace DrMoustache.Core.Frontmatter;

/// <param name="Source">The provider source to read records from.</param>
/// <param name="Delivery">Whether records are rendered one-per-document or
/// collated into one.</param>
/// <param name="Output">The output path template.</param>
/// <param name="OnCollision">Required for <see cref="DeliveryMode.Fanout"/>,
/// null for <see cref="DeliveryMode.Collate"/>.</param>
public sealed record RenderConfig(
    string Source,
    DeliveryMode Delivery,
    string Output,
    CollisionPolicy? OnCollision);
