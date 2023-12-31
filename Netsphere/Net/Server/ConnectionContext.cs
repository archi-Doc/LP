// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace Netsphere.Server;

public class ConnectionContext
{
    public delegate Task ServiceDelegate(object instance, TransmissionContext transmissionContext);

    public delegate INetService CreateFrontendDelegate(ClientConnection clientConnection);

    public delegate object CreateImplDelegate(ConnectionContext connectionContext);

    public class ServiceInfo
    {
        public ServiceInfo(uint serviceId, CreateImplDelegate createImpl)
        {
            this.ServiceId = serviceId;
            this.CreateImplDelegate = createImpl;
        }

        public void AddMethod(ServiceMethod serviceMethod) => this.serviceMethods.TryAdd(serviceMethod.Id, serviceMethod);

        public bool TryGetMethod(ulong id, [MaybeNullWhen(false)] out ServiceMethod serviceMethod) => this.serviceMethods.TryGetValue(id, out serviceMethod);

        public uint ServiceId { get; }

        public CreateImplDelegate CreateImplDelegate { get; }

        private Dictionary<ulong, ServiceMethod> serviceMethods = new();
    }

    public record class ServiceMethod
    {
        public ServiceMethod(ulong id, ServiceDelegate process)
        {
            this.Id = id;
            this.Invoke = process;
        }

        public ulong Id { get; }

        public object? ImplInstance { get; init; }

        public ServiceDelegate Invoke { get; }
    }

    public ConnectionContext(IServiceProvider? serviceProvider, ServerConnection serverConnection)
    {
        this.ServiceProvider = serviceProvider;
        this.NetTerminal = serverConnection.ConnectionTerminal.NetTerminal;
        this.ServerConnection = serverConnection;
    }

    #region FieldAndProperty

    public IServiceProvider? ServiceProvider { get; }

    public NetTerminal NetTerminal { get; }

    public ServerConnection ServerConnection { get; }

    public Func<CallContext> NewCallContext { get; internal set; } = default!;

    private object syncObject = new();
    private Dictionary<ulong, ServiceMethod> idToServiceMethod = new();
    private Dictionary<uint, object> idToImpl = new();

    #endregion

    public virtual bool InvokeCustom(TransmissionContext transmissionContext)
    {
        return false;
    }

    public void InvokeSync(TransmissionContext transmissionContext)
    {// transmissionContext.Return();
        if (transmissionContext.DataKind == 0)
        {// Block (Responder)
            if (this.NetTerminal.NetControl.TryGetResponder(transmissionContext.DataId, out var responder))
            {
                responder.Respond(transmissionContext);
            }
            else
            {
                transmissionContext.Return();
                return;
            }
        }
        else if (transmissionContext.DataKind == 1)
        {// RPC
            Task.Run(() => this.InvokeRPC(transmissionContext));
            return;
        }

        if (!this.InvokeCustom(transmissionContext))
        {
            transmissionContext.Return();
        }
    }

    public async Task InvokeRPC(TransmissionContext transmissionContext)
    {// Thread-safe
        ServiceMethod? serviceMethod;
        lock (this.syncObject)
        {
            if (!this.idToServiceMethod.TryGetValue(transmissionContext.DataId, out serviceMethod))
            {
                // Get ServiceInfo.
                var serviceId = (uint)(transmissionContext.DataId >> 32);
                if (!StaticNetService.TryGetServiceInfo(serviceId, out var serviceInfo))
                {
                    goto SendNoNetService;
                }

                // Get ServiceMethod.
                if (!serviceInfo.TryGetMethod(transmissionContext.DataId, out serviceMethod))
                {
                    goto SendNoNetService;
                }

                // Get Impl instance.
                if (!this.idToImpl.TryGetValue(serviceId, out var implInstance))
                {
                    try
                    {
                        implInstance = serviceInfo.CreateImplDelegate(this);
                    }
                    catch
                    {
                        goto SendNoNetService;
                    }

                    this.idToImpl.TryAdd(serviceId, implInstance);
                }

                serviceMethod = serviceMethod with { ImplInstance = implInstance, };
                this.idToServiceMethod.TryAdd(transmissionContext.DataId, serviceMethod);
            }
        }

        TransmissionContext.AsyncLocal.Value = transmissionContext;
        try
        {
            await serviceMethod.Invoke(serviceMethod.ImplInstance!, transmissionContext).ConfigureAwait(false);
            try
            {
                var result = NetResult.Success; // context.Result
                if (result == NetResult.Success)
                {// Success
                    transmissionContext.SendAndForget(transmissionContext.Owner, (ulong)result);
                }
                else
                {// Failure
                    transmissionContext.SendAndForget(ByteArrayPool.MemoryOwner.Empty, (ulong)result);
                }
            }
            catch
            {
            }
        }
        catch (NetException netException)
        {// NetException
            transmissionContext.SendAndForget(ByteArrayPool.MemoryOwner.Empty, (ulong)netException.Result);
        }
        catch
        {// Unknown exception
            transmissionContext.SendAndForget(ByteArrayPool.MemoryOwner.Empty, (ulong)NetResult.UnknownException);
        }
        finally
        {
            transmissionContext.Return();
        }

        return;

SendNoNetService:
        transmissionContext.SendAndForget(ByteArrayPool.MemoryOwner.Empty, (ulong)NetResult.NoNetService);
        transmissionContext.Return();
        return;
    }
}
