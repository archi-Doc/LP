using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NetsphereTest;

public class TestServerContext : ServerContext
{
}

public class TestCallContext : CallContext<TestServerContext>
{
    public static new TestCallContext Current => (TestCallContext)CallContext.Current;

    public TestCallContext()
    {
    }
}

[NetServiceInterface]
public interface ICustomService : INetService
{
    public NetTask Test();
}

[NetServiceInterface]
public interface ICustomService2 : INetService
{
    public NetTask Test();
}

[NetServiceObject]
[NetServiceFilter(typeof(CustomFilter), Order = 0)]
public class CustomService : ICustomService, ICustomService2
{
    [NetServiceFilter(typeof(CustomFilter), Arguments = new object[] { 1, 2, new string?[] { "te" }, 3 })]
    async NetTask ICustomService.Test()
    {
        var serverContext = TestCallContext.Current;
    }

    [NetServiceFilter(typeof(CustomFilter), Arguments = new object[] { 9, })]

    public async NetTask Test()
    {
        var serverContext = TestCallContext.Current;
    }
}

public class CustomFilter : IServiceFilter
{
    public async Task Invoke(CallContext context, Func<CallContext, Task> invoker)
    {
        if (context is not TestCallContext testContext)
        {
            throw new NetException(NetResult.NoCallContext);
        }

        await invoker(context).ConfigureAwait(false);
    }
    /*public async Task Invoke(CallContext context, Func<CallContext, Task> invoker)
    {
        if (context is not TestCallContext testContext)
        {
            throw new NetException(NetResult.NoCallContext);
        }

        await invoker(context).ConfigureAwait(false);
    }*/

    public void SetArguments(object[] args)
    {
    }
}
