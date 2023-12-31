// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Arc.Visceral;
using Microsoft.CodeAnalysis;

#pragma warning disable SA1202 // Elements should be ordered by access
#pragma warning disable SA1204 // Static elements should appear before instance elements
#pragma warning disable SA1602 // Enumeration items should be documented

namespace Netsphere.Generator;

public enum DeclarationCondition
{
    NotDeclared, // Not declared
    ImplicitlyDeclared, // declared (implicitly)
    ExplicitlyDeclared, // declared (explicitly interface)
}

[Flags]
public enum NetsphereObjectFlag
{
    Configured = 1 << 0,
    RelationConfigured = 1 << 1,
    Checked = 1 << 2,

    NetServiceInterface = 1 << 10, // NetServiceInterface
    NetServiceObject = 1 << 11, // NetServiceObject
    HasDefaultConstructor = 1 << 12, // Has default constructor
}

public class NetsphereObject : VisceralObjectBase<NetsphereObject>
{
    public NetsphereObject()
    {
    }

    public new NetsphereBody Body => (NetsphereBody)((VisceralObjectBase<NetsphereObject>)this).Body;

    public NetsphereObjectFlag ObjectFlag { get; private set; }

    public NetServiceObjectAttributeMock? NetServiceObjectAttribute { get; private set; }

    public NetServiceInterfaceAttributeMock? NetServiceInterfaceAttribute { get; private set; }

    public int LoaderNumber { get; private set; } = -1;

    public List<NetsphereObject>? Children { get; private set; } // The opposite of ContainingObject

    public List<NetsphereObject>? ConstructedObjects { get; private set; } // The opposite of ConstructedFrom

    public VisceralIdentifier Identifier { get; private set; } = VisceralIdentifier.Default;

    public int GenericsNumber { get; private set; }

    public string GenericsNumberString => this.GenericsNumber > 1 ? this.GenericsNumber.ToString() : string.Empty;

    public NetsphereObject? Implementation { get; private set; } // For NetServiceInterface; NetsphereObject that implements this net service interface.

    public List<NetsphereObject>? ServiceInterfaces { get; private set; } // For NetServiceObjectAttribute; Net service interfaces implemented by this net service object.

    // public NetsphereObject? NetServiceBase { get; private set; } // For NetServiceObjectAttribute; Net service base implemented by this net service object.

    public ServiceFilterGroup? ClassFilters { get; private set; } // For NetServiceObjectAttribute; Service filters.

    public Dictionary<string, ServiceFilterGroup>? MethodToFilter { get; private set; } // For NetServiceObjectAttribute; Method full name to Service filters.

    public Dictionary<uint, ServiceMethod>? ServiceMethods { get; private set; } // For NetServiceInterface; Methods included in this net service interface.

    public string ClassName { get; set; } = string.Empty;

    public Arc.Visceral.NullableAnnotation NullableAnnotationIfReferenceType
    {
        get
        {
            if (this.TypeObject?.Kind.IsReferenceType() == true)
            {
                if (this.symbol is IFieldSymbol fs)
                {
                    return (Arc.Visceral.NullableAnnotation)fs.NullableAnnotation;
                }
                else if (this.symbol is IPropertySymbol ps)
                {
                    return (Arc.Visceral.NullableAnnotation)ps.NullableAnnotation;
                }
            }

            return Arc.Visceral.NullableAnnotation.None;
        }
    }

    public string QuestionMarkIfReferenceType
    {
        get
        {
            if (this.Kind.IsReferenceType())
            {
                return "?";
            }
            else
            {
                return string.Empty;
            }
        }
    }

    public void Configure()
    {
        if (this.ObjectFlag.HasFlag(NetsphereObjectFlag.Configured))
        {
            return;
        }

        this.ObjectFlag |= NetsphereObjectFlag.Configured;

        if (this.AllAttributes.FirstOrDefault(x => x.FullName == NetServiceObjectAttributeMock.FullName) is { } objectAttribute)
        {// NetServiceObjectAttribute
            try
            {
                this.NetServiceObjectAttribute = NetServiceObjectAttributeMock.FromArray(objectAttribute.ConstructorArguments, objectAttribute.NamedArguments);
                this.NetServiceObjectAttribute.Location = objectAttribute.Location;
                this.ObjectFlag |= NetsphereObjectFlag.NetServiceObject;
            }
            catch (InvalidCastException)
            {
                this.Body.AddDiagnostic(NetsphereBody.Error_AttributePropertyError, objectAttribute.Location);
            }
        }
        else if (TryGetNetServiceInterfaceAttribute(this))
        {// NetServiceInterfaceAttribute
        }
        else
        {
            return;
        }

        // Generic type is not supported.
        if (this.Generics_Kind != VisceralGenericsKind.NotGeneric)
        {
            this.Body.AddDiagnostic(NetsphereBody.Error_GenericType, this.Location);
            return;
        }

        // Must be derived from INetService
        if (!this.AllInterfaces.Any(x => x == INetService.FullName))
        {
            this.Body.AddDiagnostic(NetsphereBody.Error_INetService, this.Location);
            return;
        }

        // Used keywords
        this.Identifier = new VisceralIdentifier("__gen_ns_identifier__");
        foreach (var x in this.AllMembers.Where(a => a.ContainingObject == this))
        {
            this.Identifier.Add(x.SimpleName);
        }

        if (this.NetServiceInterfaceAttribute != null)
        {// NetServiceInterface
            if (this.Body.IdToNetInterface.TryGetValue(this.NetServiceInterfaceAttribute.ServiceId, out var obj))
            {
                this.Body.AddDiagnostic(NetsphereBody.Error_DuplicateServiceId, this.NetServiceInterfaceAttribute.Location, this.NetServiceInterfaceAttribute.ServiceId);
                this.Body.AddDiagnostic(NetsphereBody.Error_DuplicateServiceId, obj.NetServiceInterfaceAttribute!.Location, obj.NetServiceInterfaceAttribute!.ServiceId);
            }
            else
            {
                this.Body.IdToNetInterface.Add(this.NetServiceInterfaceAttribute.ServiceId, this);
            }
        }
        else if (this.NetServiceObjectAttribute != null)
        {// NetServiceObject
            var accessibility = this.AccessibilityName;
            if (accessibility != "public" && accessibility != "internal")
            {
                this.Body.AddDiagnostic(NetsphereBody.Error_Accessibility, this.Location);
                return;
            }

            this.ServiceInterfaces = new();
            foreach (var x in this.AllInterfaceObjects)
            {
                if (x.AllInterfaces.Any(x => x == INetService.FullName))
                {
                    if (x.NetServiceInterfaceAttribute == null)
                    {
                        if (!TryGetNetServiceInterfaceAttribute(x))
                        {
                            continue;
                        }

                        x.Check();
                    }

                    this.ServiceInterfaces.Add(x);
                }
            }

            if (this.ServiceInterfaces.Count == 0)
            {
                return;
            }

            this.ConfigureNetBase();
            this.ConfigureServiceFilters();

            this.Body.NetObjects.Add(this);
        }

        static bool TryGetNetServiceInterfaceAttribute(NetsphereObject obj)
        {
            if (obj.AllAttributes.FirstOrDefault(x => x.FullName == NetServiceInterfaceAttributeMock.FullName) is { } interfaceAttribute)
            {// NetServiceInterfaceAttribute
                try
                {
                    obj.NetServiceInterfaceAttribute = NetServiceInterfaceAttributeMock.FromArray(interfaceAttribute.ConstructorArguments, interfaceAttribute.NamedArguments);
                    obj.NetServiceInterfaceAttribute.Location = interfaceAttribute.Location;
                    obj.ObjectFlag |= NetsphereObjectFlag.NetServiceInterface;

                    // Service ID
                    if (obj.NetServiceInterfaceAttribute.ServiceId == 0)
                    {
                        obj.NetServiceInterfaceAttribute.ServiceId = (uint)Arc.Crypto.FarmHash.Hash64(obj.FullName);
                    }

                    return true;
                }
                catch (InvalidCastException)
                {
                    obj.Body.AddDiagnostic(NetsphereBody.Error_AttributePropertyError, interfaceAttribute.Location);
                    return false;
                }
            }

            return false;
        }
    }

    public void ConfigureServiceFilters()
    {
        var classFilters = ServiceFilter.CreateFromObject(this) ?? new ServiceFilter();
        classFilters.Sort();
        this.ClassFilters = new ServiceFilterGroup(this, classFilters);
        this.ClassFilters.CheckAndPrepare();

        this.MethodToFilter ??= new();
        foreach (var x in this.GetMembers(VisceralTarget.Method))
        {
            var methodFilters = ServiceFilter.CreateFromObject(x);
            if (methodFilters != null)
            {
                methodFilters.Sort();
                var filterGroup = new ServiceFilterGroup(this, methodFilters);
                filterGroup.CheckAndPrepare();
                this.MethodToFilter[x.FullName] = filterGroup;
            }
        }
    }

    public void ConfigureNetBase()
    {
        /*var baseObject = this.BaseObject;
        while (baseObject != null)
        {
            if (baseObject.Generics_IsGeneric)
            {// Generic
                if (baseObject.OriginalDefinition?.FullName == NetsphereBody.NetServiceBaseFullName2)
                {
                    this.NetServiceBase = baseObject;
                    return;
                }
            }
            else
            {// Not generic
                if (baseObject.FullName == NetsphereBody.NetServiceBaseFullName)
                {
                    this.NetServiceBase = baseObject;
                    return;
                }
            }

            baseObject = baseObject.BaseObject;
        }*/
    }

    public void ConfigureRelation()
    {// Create an object tree.
        if (this.ObjectFlag.HasFlag(NetsphereObjectFlag.RelationConfigured))
        {
            return;
        }

        this.ObjectFlag |= NetsphereObjectFlag.RelationConfigured;

        if (!this.Kind.IsType())
        {// Not type
            return;
        }

        var cf = this.OriginalDefinition;
        if (cf == null)
        {
            return;
        }
        else if (cf != this)
        {
            cf.ConfigureRelation();
        }

        if (cf.ContainingObject == null)
        {// Root object
            List<NetsphereObject>? list;
            if (!this.Body.Namespaces.TryGetValue(this.Namespace, out list))
            {// Create a new namespace.
                list = new();
                this.Body.Namespaces[this.Namespace] = list;
            }

            if (!list.Contains(cf))
            {
                list.Add(cf);
            }
        }
        else
        {// Child object
            var parent = cf.ContainingObject;
            parent.ConfigureRelation();
            if (parent.Children == null)
            {
                parent.Children = new();
            }

            if (!parent.Children.Contains(cf))
            {
                parent.Children.Add(cf);
            }
        }

        if (cf.ConstructedObjects == null)
        {
            cf.ConstructedObjects = new();
        }

        if (!cf.ConstructedObjects.Contains(this))
        {
            cf.ConstructedObjects.Add(this);
            this.GenericsNumber = cf.ConstructedObjects.Count;
        }
    }

    public bool CheckKeyword(string keyword, Location? location = null)
    {
        if (!this.Identifier.Add(keyword))
        {
            this.Body.AddDiagnostic(NetsphereBody.Error_KeywordUsed, location ?? Location.None, this.SimpleName, keyword);
            return false;
        }

        return true;
    }

    public void Check()
    {
        if (this.ObjectFlag.HasFlag(NetsphereObjectFlag.Checked))
        {
            return;
        }

        this.ObjectFlag |= NetsphereObjectFlag.Checked;

        if (this.NetServiceObjectAttribute != null)
        {// NetServiceObject
            this.ClassName = NetsphereBody.BackendClassName + Arc.Crypto.FarmHash.Hash32(this.FullName).ToString("x");

            if (this.ServiceInterfaces != null)
            {
                foreach (var x in this.ServiceInterfaces)
                {
                    if (x.NetServiceInterfaceAttribute != null)
                    {
                        if (this.Body.IdToNetObject.TryGetValue(x.NetServiceInterfaceAttribute.ServiceId, out var obj))
                        {
                            var serviceInterface = x.ToString();
                            this.Body.AddDiagnostic(NetsphereBody.Error_DuplicateServiceObject, obj.Location, serviceInterface);
                            this.Body.AddDiagnostic(NetsphereBody.Error_DuplicateServiceObject, this.Location, serviceInterface);
                        }
                        else
                        {
                            this.Body.IdToNetObject.Add(x.NetServiceInterfaceAttribute.ServiceId, this);
                        }
                    }
                }
            }

            foreach (var x in this.GetMembers(VisceralTarget.Method))
            {
                if (x.Method_IsConstructor && x.ContainingObject == this)
                {// Constructor
                    if (x.Method_Parameters.Length == 0)
                    {
                        this.ObjectFlag |= NetsphereObjectFlag.HasDefaultConstructor;
                        break;
                    }
                }
            }
        }
        else if (this.NetServiceInterfaceAttribute != null)
        {// NetServiceInterface
            this.ClassName = NetsphereBody.FrontendClassName + this.NetServiceInterfaceAttribute.ServiceId.ToString("x");

            foreach (var x in this.GetMembers(VisceralTarget.Method))
            {
                AddMethod(this, x);
            }

            foreach (var @interface in this.AllInterfaceObjects)
            {
                foreach (var x in @interface.GetMembers(VisceralTarget.Method).Where(y => y.ContainingObject == @interface))
                {
                    AddMethod(this, x);
                }
            }
        }

        static void AddMethod(NetsphereObject obj, NetsphereObject method)
        {
            var serviceMethod = ServiceMethod.Create(obj, method);
            if (serviceMethod == null)
            {
                return;
            }

            // Add
            obj.ServiceMethods ??= new();
            if (obj.ServiceMethods.TryGetValue(serviceMethod.MethodId, out var s))
            {// Duplicated
                obj.Body.AddDiagnostic(NetsphereBody.Error_DuplicateServiceMethod, s.Location, serviceMethod.MethodId);
                obj.Body.AddDiagnostic(NetsphereBody.Error_DuplicateServiceMethod, serviceMethod.Location, serviceMethod.MethodId);
            }
            else
            {
                obj.ServiceMethods.Add(serviceMethod.MethodId, serviceMethod);
            }
        }
    }

    internal void GenerateFrontend(ScopingStringBuilder ssb, GeneratorInformation info)
    {
        using (var cls = ssb.ScopeBrace($"private class {this.ClassName} : {this.FullName}")) // {this.AccessibilityName}
        {
            /*ssb.AppendLine("public NetResult Result => this.result;");
            ssb.AppendLine();
            ssb.AppendLine("private NetResult result;");
            ssb.AppendLine();*/

            using (var ctr = ssb.ScopeBrace($"public {this.ClassName}(ClientConnection clientConnection)"))
            {
                // ssb.AppendLine("this.result = default;");
                ssb.AppendLine("this.ClientConnection = clientConnection;");
            }

            ssb.AppendLine();
            ssb.AppendLine("public ClientConnection ClientConnection { get; }");

            if (this.ServiceMethods != null)
            {
                foreach (var x in this.ServiceMethods.Values)
                {
                    ssb.AppendLine();
                    this.GenerateFrontend_Method(ssb, info, x);
                }
            }
        }
    }

    internal void GenerateFrontend_Method(ScopingStringBuilder ssb, GeneratorInformation info, ServiceMethod method)
    {
        var genericString = method.ReturnObject == null ? string.Empty : $"<{method.ReturnObject.FullNameWithNullable}>";
        var taskString = $"NetTask{genericString}";
        var returnTypeIsNetResult = method.ReturnObject?.FullName == NetsphereBody.NetResultFullName;
        var deserializeString = method.ReturnObject == null ? "NetResult" : method.ReturnObject.FullNameWithNullable;

        using (var scopeMethod = ssb.ScopeBrace($"public {taskString} {method.SimpleName}({method.GetParameters()})"))
        {
            ssb.AppendLine($"return new {taskString}(Core());");
            ssb.AppendLine();

            using (var scopeCore = ssb.ScopeBrace($"async Task<ServiceResponse{genericString}> Core()"))
            {
                if (method.ParameterType == ServiceMethod.Type.ByteArray)
                {
                    ssb.AppendLine($"var owner = new {ServiceMethod.MemoryOwnerName}(a1);");
                }
                else if (method.ParameterType == ServiceMethod.Type.MemoryOwner)
                {
                    ssb.AppendLine("var owner = a1.IncrementAndShare();");
                }
                else if (method.ParameterType == ServiceMethod.Type.ReadOnlyMemoryOwner)
                {
                    ssb.AppendLine("var owner = a1.IncrementAndShare().AsMemory();");
                }
                else if (method.ParameterLength == 0)
                {
                    ssb.AppendLine($"var owner = {ServiceMethod.MemoryOwnerName}.Empty;");
                }
                else
                {
                    using (var scopeSerialize = ssb.ScopeBrace($"if (!Netsphere.Block.BlockService.TrySerialize({method.GetParameterNames(NetsphereBody.ArgumentName)}, out var owner))"))
                    {
                        AppendReturn("NetResult.SerializationError");
                    }
                }

                ssb.AppendLine();
                ssb.AppendLine($"var response = await this.ClientConnection.SendAndReceiveService(owner, {method.IdString}).ConfigureAwait(false);");
                ssb.AppendLine("owner.Return();");
                using (var scopeNoNetService = ssb.ScopeBrace("if (response.Result == NetResult.Success && response.Value.IsEmpty)"))
                {
                    AppendReturn("(NetResult)response.DataId");
                }

                using (var scopeNotSuccess = ssb.ScopeBrace("else if (response.Result != NetResult.Success)"))
                {
                    AppendReturn("response.Result");
                }

                ssb.AppendLine();
                if (method.ReturnType == ServiceMethod.Type.ByteArray)
                {
                    ssb.AppendLine("var result = response.Value.Memory.ToArray();");
                    ssb.AppendLine("response.Value.Return();");
                }
                else if (method.ReturnType == ServiceMethod.Type.MemoryOwner)
                {
                    ssb.AppendLine("var result = response.Value;");
                }
                else if (method.ReturnType == ServiceMethod.Type.ReadOnlyMemoryOwner)
                {
                    ssb.AppendLine("var result = response.Value.AsReadOnly();");
                }
                else
                {
                    using (var scopeDeserialize = ssb.ScopeBrace($"if (!Tinyhand.TinyhandSerializer.TryDeserialize<{deserializeString}>(response.Value.Memory.Span, out var result))"))
                    {
                        AppendReturn("NetResult.DeserializationError");
                    }

                    ssb.AppendLine();
                    ssb.AppendLine("response.Value.Return();");
                }

                if (method.ReturnObject == null)
                {
                    ssb.AppendLine($"return default;");
                }
                else
                {
                    ssb.AppendLine($"return new(result);");
                }
            }
        }

        void AppendReturn(string netResult)
        {
            if (method.ReturnObject == null)
            {
                ssb.AppendLine($"return new({netResult});");
            }
            else
            {
                if (returnTypeIsNetResult)
                {
                    ssb.AppendLine($"return new({netResult}, {netResult});");
                }
                else
                {
                    ssb.AppendLine($"return new(default!, {netResult});");
                }
            }
        }
    }

    internal void GenerateBackend(ScopingStringBuilder ssb, GeneratorInformation info)
    {
        using (var cls = ssb.ScopeBrace($"private class {this.ClassName}"))
        {
            this.GenerateBackend_CreateImpl(ssb, info);

            if (this.ServiceInterfaces != null)
            {
                foreach (var x in this.ServiceInterfaces)
                {
                    this.GenerateBackend_Interface(ssb, info, x);
                }
            }

            ssb.AppendLine();
            ssb.AppendLine($"private {this.FullName} impl;");

            // Service filters
            this.ClassFilters?.GenerateDefinition(ssb);
            if (this.MethodToFilter != null)
            {
                foreach (var x in this.MethodToFilter.Values)
                {
                    x.GenerateDefinition(ssb);
                }
            }
        }
    }

    internal void GenerateBackend_CreateImpl(ScopingStringBuilder ssb, GeneratorInformation info)
    {
        using (var scopeMethod = ssb.ScopeBrace($"private static object CreateImpl(ConnectionContext connectionContext)"))
        {
            ssb.AppendLine($"var impl = connectionContext.ServiceProvider?.GetService(typeof({this.FullName})) as {this.FullName};");
            using (var scopeIf = ssb.ScopeBrace($"if (impl == null)"))
            {
                if (this.ObjectFlag.HasFlag(NetsphereObjectFlag.HasDefaultConstructor))
                {
                    ssb.AppendLine($"impl = new {this.FullName}();");
                }
                else
                {
                    ssb.AppendLine($"throw new InvalidOperationException($\"Could not create an instance of the net service {{typeof({this.FullName}).ToString()}}.\");");
                }
            }

            ssb.AppendLine();

            // Service filters
            ServiceFilterGroup.GenerateInitialize(ssb, "this", "connectionContext.ServiceProvider", this.ClassFilters?.Items);

            ssb.AppendLine("return impl;");
        }
    }

    internal void GenerateBackend_Interface(ScopingStringBuilder ssb, GeneratorInformation info, NetsphereObject serviceInterface)
    {
        if (serviceInterface.ServiceMethods != null)
        {
            foreach (var x in serviceInterface.ServiceMethods.Values)
            {
                ssb.AppendLine();
                this.GenerateBackend_Method(ssb, info, serviceInterface, x);
            }
        }

        ssb.AppendLine();
        this.GenerateBackend_ServiceInfo(ssb, info, serviceInterface);
    }

    internal ServiceFilterGroup? GetServiceFilter(NetsphereObject serviceInterface, ServiceMethod method)
    {
        if (this.MethodToFilter == null)
        {
            return null;
        }

        var explicitName = this.FullName + "." + serviceInterface.FullName + "." + method.LocalName;
        if (this.MethodToFilter.TryGetValue(explicitName, out var serviceFilter))
        {
            return serviceFilter;
        }

        var name = this.FullName + "." + method.LocalName;
        if (this.MethodToFilter.TryGetValue(name, out var serviceFilter2))
        {
            return serviceFilter2;
        }

        return null;
    }

    internal void GenerateBackend_Method(ScopingStringBuilder ssb, GeneratorInformation info, NetsphereObject serviceInterface, ServiceMethod method)
    {
        using (var scopeMethod = ssb.ScopeBrace($"private static async Task {method.MethodString}(object obj, TransmissionContext c0)"))
        {
            var methodFilters = this.GetServiceFilter(serviceInterface, method);
            var filters = ServiceFilterGroup.FromClassAndMethod(this.ClassFilters, methodFilters);

            var code = "Core(obj, c0)";
            var previousAsync = true;
            if (filters != null)
            {
                ssb.AppendLine($"var b = ({this.ClassName})obj;");
                ServiceFilterGroup.GenerateInitialize(ssb, "b", "c0.ConnectionContext.ServiceProvider", methodFilters?.Items);
                ssb.AppendLine();

                var sb = new StringBuilder();
                var n = 1;
                for (var i = filters.Length - 1; i >= 0; i--, n++)
                {
                    var item = filters[i];
                    if (i != filters.Length)
                    {
                        var filterType = item.CallContextObject == null ? string.Empty : $"({item.CallContextObject.FullName})";
                        if (item.IsAsync == previousAsync)
                        {
                            code = $"b.{item.Identifier}!.{NetsphereBody.ServiceFilterInvokeName}({filterType}c0, c{n} => {code})";
                        }
                        else if (item.IsAsync)
                        {
                            code = $"b.{item.Identifier}!.{NetsphereBody.ServiceFilterInvokeName}({filterType}c0, async c{n} => {code})";
                        }
                        else
                        {
                            code = $"b.{item.Identifier}!.{NetsphereBody.ServiceFilterInvokeName}({filterType}c0, c{n} => {code}.Wait())";
                        }

                        previousAsync = item.IsAsync;
                    }
                }
            }

            if (previousAsync)
            {
                ssb.AppendLine($"await {code}.ConfigureAwait(false);");
            }
            else
            {
                ssb.AppendLine($"{code};");
            }

            ssb.AppendLine();

            using (var scopeCore = ssb.ScopeBrace("static async Task Core(object obj, TransmissionContext context)"))
            {
                // using (var scopeTry = ssb.ScopeBrace("try"))
                {
                    this.GenerateBackend_MethodCore(ssb, info, serviceInterface, method);
                }

                /*using (var scopeCatch = ssb.ScopeBrace("catch (NetException ne)"))
                {
                    ssb.AppendLine("context.RentData.Return();");
                    ssb.AppendLine($"context.RentData = {ServiceMethod.MemoryOwnerName}.Empty;");
                    ssb.AppendLine("context.Result = ne.Result;");
                }*/
            }
        }
    }

    internal void GenerateBackend_MethodCore(ScopingStringBuilder ssb, GeneratorInformation info, NetsphereObject serviceInterface, ServiceMethod method)
    {
        if (method.ParameterType == ServiceMethod.Type.ByteArray)
        {// byte[]
            ssb.AppendLine("var value = context.Owner.Memory.ToArray();");
        }
        else if (method.ParameterType == ServiceMethod.Type.MemoryOwner)
        {// ByteArrayPool.MemoryOwner
            ssb.AppendLine("var value = context.Owner;");
        }
        else if (method.ParameterType == ServiceMethod.Type.ReadOnlyMemoryOwner)
        {// ByteArrayPool.ReadOnlyMemoryOwner
            ssb.AppendLine("var value = context.Owner.AsReadOnly();");
        }
        else if (method.ParameterLength == 0)
        {// No parameter
            ssb.AppendLine($"var owner = {ServiceMethod.MemoryOwnerName}.Empty;");
        }
        else
        {
            using (var scopeDeserialize = ssb.ScopeBrace($"if (!Netsphere.Block.BlockService.TryDeserialize<{method.GetParameterTypes()}>(context.Owner, out var value))"))
            {
                ssb.AppendLine("context.Result = NetResult.DeserializationError;");
                ssb.AppendLine("return;");
            }
        }

        ssb.AppendLine();

        // Backend
        // ssb.AppendLine($"var backend = (({this.ClassName})obj).impl;");

        // Set ServerContext
        /*if (this.NetServiceBase != null)
        {
            if (this.NetServiceBase.Generics_IsGeneric)
            {
                ssb.AppendLine($"(({this.NetServiceBase.FullName})backend).Context = ({this.NetServiceBase.Generics_Arguments[0].FullName})context!;");
            }
            else
            {
                ssb.AppendLine($"(({this.NetServiceBase.FullName})backend).Context = (ServerContext)context!;");
            }
        }*/

        var prefix = string.Empty;
        if (method.ReturnObject != null)
        {
            prefix = "var result = ";
        }

        // task
        // ssb.AppendLine($"var task = (({serviceInterface.FullName})backend).{method.SimpleName}({method.GetTupleNames("value")});");
        ssb.AppendLine($"{prefix}await (({serviceInterface.FullName})(({this.ClassName})obj).impl).{method.SimpleName}({method.GetTupleNames("value")}).ValueAsync.ConfigureAwait(false);");
        // ssb.AppendLine($"{prefix}await task.ValueAsync.ConfigureAwait(false);");

        /*if (method.ReturnObject == null)
        {
            ssb.AppendLine("var result = NetResult.Success;");
        }*/

        ssb.AppendLine("context.Owner.Return();");
        if (method.ReturnObject == null)
        {// NetTask
            ssb.AppendLine($"context.Owner = {ServiceMethod.MemoryOwnerName}.Empty;");
        }
        else if (method.ReturnType == ServiceMethod.Type.ByteArray)
        {// byte[] result;
            ssb.AppendLine($"context.Owner = result != null ? new {ServiceMethod.MemoryOwnerName}(result) : default;");
        }
        else if (method.ReturnType == ServiceMethod.Type.MemoryOwner)
        {// ByteArrayPool.MemoryOwner result;
            ssb.AppendLine("context.Owner = result;");
        }
        else if (method.ReturnType == ServiceMethod.Type.ReadOnlyMemoryOwner)
        {// ByteArrayPool.ReadOnlyMemoryOwner result;
            ssb.AppendLine("context.Owner = result.AsMemory();");
        }
        else
        {// Other
            using (var scopeSerialize = ssb.ScopeBrace($"if (Netsphere.Block.BlockService.TrySerialize(result, out var owner2))"))
            {
                ssb.AppendLine("context.Owner = owner2;");
            }

            using (var scopeElse = ssb.ScopeBrace("else"))
            {
                ssb.AppendLine("context.Owner = default;");
                ssb.AppendLine("context.Result = NetResult.SerializationError;");
            }
        }

        // ssb.AppendLine("context.Result = NetResult.Success;");
    }

    internal void GenerateBackend_ServiceInfo(ScopingStringBuilder ssb, GeneratorInformation info, NetsphereObject serviceInterface)
    {
        var serviceIdString = serviceInterface.NetServiceInterfaceAttribute!.ServiceId.ToString("x");
        using (var scopeMethod = ssb.ScopeBrace($"public static ConnectionContext.ServiceInfo ServiceInfo_{serviceIdString}()"))
        {
            ssb.AppendLine($"var si = new ConnectionContext.ServiceInfo(0x{serviceIdString}u, CreateImpl);");
            if (serviceInterface.ServiceMethods != null)
            {
                foreach (var x in serviceInterface.ServiceMethods.Values)
                {
                    ssb.AppendLine($"si.AddMethod(new ConnectionContext.ServiceMethod({x.IdString}, {x.MethodString}));");
                }
            }

            ssb.AppendLine("return si;");
        }
    }
}
