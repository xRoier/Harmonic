using Autofac;

namespace SharpRtmp.Hosting;

public interface IStartup
{
    void ConfigureServices(ContainerBuilder builder);
}