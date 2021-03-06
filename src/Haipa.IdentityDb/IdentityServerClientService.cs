﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.Configuration;
using AutoMapper.QueryableExtensions;
using Haipa.IdentityDb;
using IdentityServer4.EntityFramework.Mappers;
using IdentityServer4.Models;

using Entities = IdentityServer4.EntityFramework.Entities;

namespace Haipa.Modules.Identity.Services
{
    public class IdentityServerClientService : IIdentityServerClientService
    {
        private readonly IClientRepository _repository;

        private static readonly IMapper Mapper;
        private static readonly MapperConfiguration ProjectionMapperConfiguration;

        static IdentityServerClientService()
        {
            //this mapper is used for update mapping (Identity Server Mapper is not access-able for us)
            var mapperConfiguration = new MapperConfiguration(cfg => { cfg.AddProfile<ClientMapperProfile>(); });

            //projection will not work with identity server profile, but by using CovertUsing we can use to default mapper from identity server
            ProjectionMapperConfiguration = new MapperConfiguration(cfg => cfg.CreateMap<Entities.Client,Client>()
                    .ConvertUsing(src => src.ToModel()));
           
            Mapper = mapperConfiguration.CreateMapper();

        }

        public IdentityServerClientService(IClientRepository repository)
        {
            _repository = repository;
        }

        public IQueryable<Client> QueryClients()
        {
            return _repository.QueryClients().ProjectTo<Client>(ProjectionMapperConfiguration);
        }

        public async Task<Client> GetClient(string clientId)
        {
            var (id, _) = await _repository.GetClientIdAsync(clientId);
            return id.GetValueOrDefault(0) == 0 ? null : (await _repository.GetClientAsync(id.Value)).ToModel();
        }

        public async Task AddClient(Client client)
        {
            await _repository.AddClientAsync(client.ToEntity());
            await _repository.SaveAllChangesAsync();
        }

        public async Task AddClients(IEnumerable<Client> clients)
        {
            await _repository.AddClientsAsync(clients.Select(x => x.ToEntity()));
            await _repository.SaveAllChangesAsync();
        }

        public async Task DeleteClient(Client client)
        {
            var trackedClient = await _repository.GetTrackedClientAsync(client.ClientId);

            if (trackedClient == null)
                return;

            await _repository.RemoveClientAsync(trackedClient);
            await _repository.SaveAllChangesAsync();
        }


        public async Task UpdateClient(Client client)
        {
            var trackedClient = await _repository.GetTrackedClientAsync(client.ClientId);

            if (trackedClient == null)
                return;

            _repository.RemoveClientRelations(trackedClient);
            Mapper.Map(client, trackedClient);

            await _repository.UpdateClientAsync(trackedClient);
            await _repository.SaveAllChangesAsync();
        }

    }

}
