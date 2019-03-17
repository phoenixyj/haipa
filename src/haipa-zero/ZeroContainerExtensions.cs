﻿using System.Linq;
using Haipa.Modules.Abstractions;
using Haipa.Modules.Api;
using Haipa.Modules.Controller;
using Haipa.Modules.VmHostAgent;
using Haipa.Rebus;
using Haipa.StateDb;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Rebus.Persistence.InMem;
using Rebus.Transport.InMem;
using SimpleInjector;

namespace Haipa.Runtime.Zero
{
    internal static class ZeroContainerExtensions
    {
        public static void Bootstrap(this Container container, string[] args)
        {
            var modules = new[]
            {
                typeof(ApiModule),
                typeof(VmHostAgentModule),
                typeof(ControllerModule),
            };

            container.Collection.Register<IModule>(
                modules.Select(t => Lifestyle.Singleton.CreateRegistration(t, container)));

            container
                .HostAspNetCore(args)
                .UseInMemoryBus()
                .UseInMemoryDb();
        }

        public static Container HostAspNetCore(this Container container, string[] args)
        {
            container.RegisterInstance<IWebModuleHostBuilderFactory>(
                new PassThroughWebHostBuilderFactory(
                    () => WebHost.CreateDefaultBuilder(args).UseUrls("http://localhost:62189")
                        .ConfigureLogging(lc=>lc.SetMinimumLevel(LogLevel.Trace))));
            return container;
        }

        public static Container UseInMemoryBus(this Container container)
        {
            container.RegisterInstance(new InMemNetwork(true));
            container.RegisterInstance(new InMemorySubscriberStore());
            container.Register<IRebusTransportConfigurer, InMemoryTransportConfigurer>();
            container.Register<IRebusSagasConfigurer, InMemorySagasConfigurer>();
            container.Register<IRebusSubscriptionConfigurer, InMemorySubscriptionConfigurer>();
            container.Register<IRebusTimeoutConfigurer, InMemoryTimeoutConfigurer>(); return container;
        }

        public static Container UseInMemoryDb(this Container container)
        {
            container.RegisterInstance(new InMemoryDatabaseRoot());
            container.Register<IDbContextConfigurer<StateStoreContext>, InMemoryStateStoreContextConfigurer>();

            return container;
        }
    }
}
