namespace TToApp.Helpers
{
    using AutoMapper;
    using TToApp.DTOs;
    using TToApp.Model;
    public class MappingProfile: Profile
    {
        public MappingProfile() {

            CreateMap<WarehouseDTO, Warehouse>().ReverseMap();
         
        }
    }
}
