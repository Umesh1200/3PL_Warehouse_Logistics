using Mapster;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;

namespace Warehouse3PL.Application.Common.Mappings;

public static class MapsterConfig
{
    public static void RegisterMappings()
    {
        TypeAdapterConfig<Client, ClientDto>.NewConfig()
            .Map(dest => dest.ActiveUsersCount, src => src.Users.Count(u => !u.IsDeleted))
            .Map(dest => dest.Contacts, src => src.Contacts);

        TypeAdapterConfig<ClientContact, ClientContactDto>.NewConfig();

        TypeAdapterConfig<User, UserDto>.NewConfig()
            .Map(dest => dest.CompanyName, src => src.Client != null ? src.Client.CompanyName : null)
            .Map(dest => dest.Roles, src => src.UserRoles.Select(ur => ur.Role.RoleName).ToList());

        TypeAdapterConfig<Role, RoleDto>.NewConfig()
            .Map(dest => dest.Permissions, src => src.RolePermissions);

        TypeAdapterConfig<RolePermission, RolePermissionDto>.NewConfig();

        TypeAdapterConfig<Facility, FacilityDto>.NewConfig();
    }
}

