using DomainMap.Descriptors.Mappings.ExistingTarget;

namespace DomainMap.Descriptors.Mappings.UserMappings;

/// <summary>
/// A <see cref="IUserMapping"/> which is also a <see cref="IExistingTargetMapping"/>.
/// </summary>
public interface IExistingTargetUserMapping : IUserMapping, IExistingTargetMapping;
