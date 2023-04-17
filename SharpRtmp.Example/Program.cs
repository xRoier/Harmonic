using SharpRtmp.Hosting;
using System.Net;
using SharpRtmp.Example;

var server = new RtmpServerBuilder()
    .UseStartup<Startup>()
    .UseWebSocket(c =>
    {
        c.BindEndPoint = new IPEndPoint(IPAddress.Parse("0.0.0.0"), 8080);
    })
    .Build();
await server.StartAsync();