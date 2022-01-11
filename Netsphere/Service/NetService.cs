// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Netsphere;

public class NetService
{
    public delegate NetTask<ByteArrayPool.MemoryOwner> ServiceDelegate(object instance, ByteArrayPool.MemoryOwner received);

    public delegate INetService CreateFrontendDelegate(ClientTerminal clientTerminal);

    public delegate object CreateServerDelegate(IServiceProvider? serviceProvider);

    public class ServiceInfo
    {
        public ServiceInfo(uint serviceId, CreateServerDelegate createServer)
        {
            this.ServiceId = serviceId;
            this.CreateServer = createServer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddMethod(ServiceMethod serviceMethod) => this.serviceMethods.TryAdd(serviceMethod.Id, serviceMethod);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetMethod(ulong id, [MaybeNullWhen(false)] out ServiceMethod serviceMethod) => this.serviceMethods.TryGetValue(id, out serviceMethod);

        public uint ServiceId { get; }

        public CreateServerDelegate CreateServer { get; }

        private Dictionary<ulong, ServiceMethod> serviceMethods = new();
    }

    public class ServiceMethod
    {
        public ServiceMethod(ulong id, ServiceDelegate process)
        {
            this.Id = id;
            this.Process = process;
        }

        // public ServiceInfo ServiceInfo { get; }

        public ulong Id { get; }

        public object? ServerInstance { get; private set; }

        public ServiceDelegate Process { get; }

        public ServiceMethod CloneWithInstance(object serverInstance)
        {
            var serviceMethod = new ServiceMethod(this.Id, this.Process);
            serviceMethod.ServerInstance = serverInstance;
            return serviceMethod;
        }
    }

    public NetService(IServiceProvider? serviceProvider = null)
    {
        this.serviceProvider = serviceProvider;
    }

    public async Task Process(ServerTerminal serverTerminal, NetReceivedData received)
    {
        if (!this.idToServiceMethod.TryGetValue(received.DataId, out var serviceMethod))
        {
            // Get ServiceInfo.
            var serviceId = (uint)(received.DataId >> 32);
            if (!StaticNetService.TryGetServiceInfo(serviceId, out var serviceInfo))
            {
                goto SendEmpty;
            }

            // Get ServiceMethod.
            if (!serviceInfo.TryGetMethod(received.DataId, out serviceMethod))
            {
                goto SendEmpty;
            }

            // Get ServiceInstance.
            if (!this.idToInstance.TryGetValue(serviceId, out var serverInstance))
            {
                serverInstance = serviceInfo.CreateServer(this.serviceProvider);
                this.idToInstance.TryAdd(serviceId, serverInstance);
            }

            serviceMethod = serviceMethod.CloneWithInstance(serverInstance);
        }

        var sendOwner = await serviceMethod.Process(serviceMethod.ServerInstance!, received.Received);
        await serverTerminal.SendServiceAsync(serviceMethod.Id, sendOwner); // .ContinueWith(x => { sendOwner.Return(); });
        sendOwner.Return();
        return;

SendEmpty:
        await serverTerminal.SendServiceAsync(received.DataId, ByteArrayPool.MemoryOwner.Empty);
    }

    private IServiceProvider? serviceProvider;
    private Dictionary<ulong, ServiceMethod> idToServiceMethod = new();
    private Dictionary<uint, object> idToInstance = new();
}
